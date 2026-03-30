using AForge;
using BioLib;
using ZarrNET.Core;
using ZarrNET.Core.Nodes;
using ZarrNET.Core.Helpers;
using ZarrNET.Core.OmeZarr.Metadata;
using BioGTK;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZarrNET;
using ZarrNET.Core;

namespace BioLib
{
    /// <summary>
    /// Bridges BioLib's BioImage to the OmeZarrWriter, exporting a full 5D
    /// image as an OME-Zarr v3 dataset on local disk.
    ///
    /// XY planes are written in tile-sized chunks so that even whole-slide
    /// images never require a full plane to be resident in memory at once.
    /// Peak allocation is bounded to a single tile (tileW × tileH × bytesPerSample).
    ///
    /// RGB-interleaved buffers are deinterleaved into separate C planes so
    /// the output array shape is always (T, C, Z, Y, X) with scalar pixels.
    ///
    /// All resolution levels present in b.Resolutions are written, producing
    /// a proper OME-NGFF multiscale pyramid.
    /// </summary>
    public static class Zarr
    {
        private static int s_debugTileLogs;

        private static void Log(string msg)
        {
            AppLog.Append(msg);
        }

        private static string SampleBytes(byte[] data, int count = 16)
        {
            if (data == null || data.Length == 0)
                return "<empty>";
            int len = Math.Min(count, data.Length);
            return BitConverter.ToString(data, 0, len);
        }

        // =====================================================================
        // Public entry point
        // =====================================================================

        /// <summary>
        /// Saves the full 5D content of a <see cref="BioImage"/> as an
        /// OME-Zarr v3 dataset at <paramref name="outputDir"/>.
        /// All pyramid resolution levels in <c>b.Resolutions</c> are written.
        /// </summary>
        public static void SaveZarr(BioImage b, string outputDir)
        {
            // ------------------------------------------------------------------
            // 1. Resolve dimensions and data type from the BioImage
            // ------------------------------------------------------------------

            // Guard against zero-size dimensions which cause ZarrNET to throw
            // "regionStart[0] = 0 is out of bounds [0, 0)".
            if (b.SizeX <= 0 || b.SizeY <= 0 || b.SizeZ <= 0 || b.SizeC <= 0 || b.SizeT <= 0)
                throw new InvalidOperationException(
                    $"BioImage has invalid dimensions: X={b.SizeX} Y={b.SizeY} Z={b.SizeZ} C={b.SizeC} T={b.SizeT}. " +
                    "Ensure the image is fully loaded before saving as Zarr.");

            int bitsPerSample = DetermineSourceBitsPerSample(b);

            var bytesPerSample = bitsPerSample / 8;
            var zarrDataType   = MapDataType(bitsPerSample);
            var totalChannels  = b.SizeC;
            s_debugTileLogs = 0;

            Log($"[SaveZarr] file={b.Filename} out={outputDir} size={b.SizeX}x{b.SizeY} zct={b.SizeZ}/{b.SizeC}/{b.SizeT} " +
                $"bits={bitsPerSample} bytesPerSample={bytesPerSample} srcPixFmt={DetermineSourcePixelFormat(b)} " +
                $"interleavedRgb={IsInterleavedRgbFormat(DetermineSourcePixelFormat(b))} bands={(IsInterleavedRgbFormat(DetermineSourcePixelFormat(b)) ? BioImage.GetBands(DetermineSourcePixelFormat(b)) : 1)} " +
                $"resLevels={b.Resolutions.Count}");

            int tileW = 512;
            int tileH = 512;

            // ------------------------------------------------------------------
            // 2. Build per-level descriptors from b.Resolutions
            //    Level 0 is always the full-resolution image (b.SizeX / b.SizeY).
            //    Subsequent levels are taken directly from b.Resolutions[1..N].
            // ------------------------------------------------------------------

            var levelDescriptors = new List<ResolutionLevelDescriptor>();
            for (int lvl = 0; lvl < b.Resolutions.Count; lvl++)
            {
                var res        = b.Resolutions[lvl];
                double dsample = b.GetLevelDownsample(lvl);   // 1.0 for level 0
                levelDescriptors.Add(new ResolutionLevelDescriptor(res.SizeX, res.SizeY, dsample));
            }

            // Fallback: if there is somehow no Resolutions list, use the image dims.
            if (levelDescriptors.Count == 0)
                levelDescriptors.Add(new ResolutionLevelDescriptor(b.SizeX, b.SizeY, 1.0));

            // ------------------------------------------------------------------
            // 3. Build the base descriptor (T/C/Z/dtype — shared by all levels)
            // ------------------------------------------------------------------

            var coord = new ZarrNET.Core.ZCT(b.SizeZ, totalChannels, b.SizeT);

            var baseDescriptor = new BioImageDescriptor(b.SizeX, b.SizeY, coord)
            {
                Name          = Path.GetFileNameWithoutExtension(b.Filename),
                DataType      = zarrDataType,
                PhysicalSizeX = b.PhysicalSizeX,
                PhysicalSizeY = b.PhysicalSizeY,
                PhysicalSizeZ = b.PhysicalSizeZ,
                ChunkY        = tileH,
                ChunkX        = tileW,
            };

            // ------------------------------------------------------------------
            // 4. Create the writer (bootstraps zarr.json metadata on disk for
            //    all levels at once)
            // ------------------------------------------------------------------

            var writer = OmeZarrWriter.CreateAsync(outputDir, baseDescriptor, levelDescriptors).Result;

            try
            {
                // ------------------------------------------------------------------
                // 5. Stream tiles into each resolution level
                // ------------------------------------------------------------------
                for (int levelIndex = 0; levelIndex < levelDescriptors.Count; levelIndex++)
                {
                    var lvlDesc = levelDescriptors[levelIndex];
                    Log($"[SaveZarr] level={levelIndex} size={lvlDesc.SizeX}x{lvlDesc.SizeY} downsample={lvlDesc.Downsample}");

                    for (int t = 0; t < b.SizeT; t++)
                    {
                        for (int z = 0; z < b.SizeZ; z++)
                        {
                            for (int c = 0; c < b.SizeC; c++)
                            {
                                WritePlaneInTiles(
                                    writer, b, lvlDesc,
                                    t, c, z,
                                    bytesPerSample, tileW, tileH,
                                    levelIndex: levelIndex).GetAwaiter().GetResult();
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }

            Recorder.Record(BioLib.Recorder.GetCurrentMethodInfo(), false, b);
        }

        // =====================================================================
        // Tiled plane writer
        // =====================================================================

        /// <summary>
        /// Walks the XY extent of a single (t, c, z) plane in tile-sized
        /// chunks at the given resolution level, fetching each tile via
        /// BioImage.GetTile and writing it as a sub-region into the Zarr array.
        /// </summary>
        private static async Task WritePlaneInTiles(
            OmeZarrWriter              writer,
            BioImage                   b,
            ResolutionLevelDescriptor  lvlDesc,
            int t, int c, int z,
            int bytesPerSample,
            int tileW, int tileH,
            int levelIndex      = 0,
            int rgbChannelIndex = -1,
            int srcChannelCount = 1,
            bool isBGRA         = false,
            int logicalC        = -1)
        {
            bool needsDeinterleave = rgbChannelIndex >= 0;
            int  srcC              = needsDeinterleave ? logicalC : c;

            for (int tileY = 0; tileY < lvlDesc.SizeY; tileY += tileH)
            {
                for (int tileX = 0; tileX < lvlDesc.SizeX; tileX += tileW)
                {
                    // Clamp to image bounds — edge tiles may be smaller.
                    int actualW = Math.Min(tileW, lvlDesc.SizeX - tileX);
                    int actualH = Math.Min(tileH, lvlDesc.SizeY - tileY);
                    int pixelCount         = actualW * actualH;
                    int interleavedBytes   = pixelCount * srcChannelCount * bytesPerSample;
                    int singleChannelBytes = pixelCount * bytesPerSample;

                    // Fetch raw tile bytes from BioLib at the requested pyramid level.
                    byte[] pixelBytes = await b.GetTileBytesRaw(
                        b.GetFrameIndex(z, srcC, t), levelIndex, tileX, tileY, actualW, actualH,
                        new AForge.ZCT(z, srcC, t)).ConfigureAwait(false);

                    bool logTile = tileX == 0 && tileY == 0 && s_debugTileLogs < 12;
                    if (logTile)
                    {
                        Log($"[WritePlane] level={levelIndex} t={t} c={c} z={z} srcC={srcC} tile={tileX},{tileY} size={actualW}x{actualH} " +
                            $"rawLen={pixelBytes?.Length ?? 0} expSingle={singleChannelBytes} expInterleaved={interleavedBytes} " +
                            $"needsDeinterleave={needsDeinterleave} srcBands={srcChannelCount} bytesPerSample={bytesPerSample} " +
                            $"rawSample={SampleBytes(pixelBytes)}");
                    }

                    if (needsDeinterleave)
                    {
                        // Ensure the buffer is the full interleaved size before
                        // extracting a single channel from it.
                        if (pixelBytes.Length != interleavedBytes)
                        {
                            var trimmed = new byte[interleavedBytes];
                            Buffer.BlockCopy(pixelBytes, 0, trimmed, 0,
                                Math.Min(pixelBytes.Length, interleavedBytes));
                            pixelBytes = trimmed;
                        }

                        // For BGRA buffers the byte order is [B, G, R, A].
                        // Map output channel index:  0→R(byte2), 1→G(byte1), 2→B(byte0)
                        int srcByteIndex = isBGRA ? (2 - rgbChannelIndex) : rgbChannelIndex;

                        pixelBytes = DeinterleaveChannel(
                            pixelBytes, srcByteIndex, srcChannelCount,
                            bytesPerSample, pixelCount);

                        if (logTile)
                            Log($"[WritePlane] deint c={c} srcByteIndex={srcByteIndex} outLen={pixelBytes.Length} sample={SampleBytes(pixelBytes)}");
                    }
                    else
                    {
                        // Single-channel path: trim/pad to the exact scalar size.
                        if (pixelBytes.Length != singleChannelBytes)
                        {
                            var trimmed = new byte[singleChannelBytes];
                            Buffer.BlockCopy(pixelBytes, 0, trimmed, 0,
                                Math.Min(pixelBytes.Length, singleChannelBytes));
                            pixelBytes = trimmed;
                        }

                        if (logTile)
                            Log($"[WritePlane] single c={c} outLen={pixelBytes.Length} sample={SampleBytes(pixelBytes)}");
                    }

                    await writer.WriteRegionAsync(
                        t, c, z,
                        tileY, tileX,
                        actualH, actualW,
                        pixelBytes,
                        levelIndex: levelIndex).ConfigureAwait(false);

                    if (logTile)
                    {
                        s_debugTileLogs++;
                        Log($"[WritePlane] wrote level={levelIndex} t={t} c={c} z={z} tile={tileX},{tileY}");
                    }
                }
            }
        }

        /// <summary>
        /// Copies exactly width × height × bytesPerSample from the bitmap's
        /// buffer, stripping any stride padding the bitmap format may include.
        /// </summary>
        private static byte[] ExtractRawPixels(
            Bitmap tileBitmap, int width, int height, int bytesPerSample)
        {
            int rowBytes = width * bytesPerSample;
            int totalBytes = rowBytes * height;
            var sourceBytes = tileBitmap.Bytes;

            if (sourceBytes.Length == totalBytes)
                return sourceBytes;

            var output = new byte[totalBytes];
            int sourceStride = tileBitmap.Stride;

            for (int row = 0; row < height; row++)
            {
                Buffer.BlockCopy(sourceBytes, row * sourceStride, output, row * rowBytes, rowBytes);
            }

            return output;
        }

        // =====================================================================
        // Shared helpers
        // =====================================================================

        /// <summary>
        /// Maps BioLib's per-sample bit depth to a Zarr v3 data_type string.
        /// </summary>
        private static string MapDataType(int bitsPerSample)
        {
            return bitsPerSample switch
            {
                8  => "uint8",
                16 => "uint16",
                32 => "float32",
                _  => "uint16"
            };
        }

        private static int DetermineSourceBitsPerSample(BioImage b)
        {
            if (b.Resolutions.Count > 0)
            {
                return b.Resolutions[0].PixelFormat switch
                {
                    AForge.PixelFormat.Format8bppIndexed => 8,
                    AForge.PixelFormat.Format24bppRgb => 8,
                    AForge.PixelFormat.Format32bppArgb => 8,
                    AForge.PixelFormat.Format32bppRgb => 8,
                    AForge.PixelFormat.Format16bppGrayScale => 16,
                    AForge.PixelFormat.Format48bppRgb => 16,
                    AForge.PixelFormat.Format64bppArgb => 16,
                    AForge.PixelFormat.Format64bppPArgb => 16,
                    _ => Math.Max(8, b.bitsPerPixel)
                };
            }

            return Math.Max(8, b.bitsPerPixel);
        }

        private static AForge.PixelFormat DetermineSourcePixelFormat(BioImage image)
        {
            if (image.Resolutions.Count > 0)
                return image.Resolutions[0].PixelFormat;

            if (image.Buffers.Count > 0)
                return image.Buffers[0].PixelFormat;

            return AForge.PixelFormat.Format16bppGrayScale;
        }

        private static bool IsInterleavedRgbFormat(AForge.PixelFormat format)
        {
            return format == AForge.PixelFormat.Format24bppRgb ||
                   format == AForge.PixelFormat.Format32bppArgb ||
                   format == AForge.PixelFormat.Format32bppPArgb ||
                   format == AForge.PixelFormat.Format32bppRgb ||
                   format == AForge.PixelFormat.Format48bppRgb ||
                   format == AForge.PixelFormat.Format64bppArgb ||
                   format == AForge.PixelFormat.Format64bppPArgb;
        }

        private static bool IsBGRAFormat(AForge.PixelFormat format)
        {
            return format == AForge.PixelFormat.Format32bppArgb ||
                   format == AForge.PixelFormat.Format32bppPArgb ||
                   format == AForge.PixelFormat.Format32bppRgb ||
                   format == AForge.PixelFormat.Format64bppArgb ||
                   format == AForge.PixelFormat.Format64bppPArgb;
        }

        /// <summary>
        // =====================================================================
        // ROI persistence — JSON sidecar at <outputDir>/rois.json
        // =====================================================================

        /// <summary>
        /// The file name used for the ROI sidecar that sits beside the Zarr dataset.
        /// </summary>
        public const string RoiFileName = "rois.json";

        /// <summary>
        /// Plain-old-data transfer object written to / read from <c>rois.json</c>.
        /// Every field maps 1-to-1 with the columns in BioImage's CSV format so
        /// that the two representations stay in sync.
        /// </summary>
        private sealed class RoiDto
        {
            public string RoiId        { get; set; } = "";
            public string RoiName      { get; set; } = "";
            public string Type         { get; set; } = "";
            public string Id           { get; set; } = "";
            public int    ShapeIndex   { get; set; }
            public string Text         { get; set; } = "";
            public int    Serie        { get; set; }
            public int    Z            { get; set; }
            public int    C            { get; set; }
            public int    T            { get; set; }
            public double X            { get; set; }
            public double Y            { get; set; }
            public double W            { get; set; }
            public double H            { get; set; }
            /// <summary>Points encoded as "x0,y0 x1,y1 …" (invariant culture).</summary>
            public string Points       { get; set; } = "";
            /// <summary>Stroke colour as "A,R,G,B".</summary>
            public string StrokeColor  { get; set; } = "";
            public double StrokeWidth  { get; set; }
            /// <summary>Fill colour as "A,R,G,B".</summary>
            public string FillColor    { get; set; } = "";
            public float  FontSize     { get; set; }
        }

        /// <summary>
        /// Writes all annotations on <paramref name="b"/> as a JSON sidecar
        /// file at <c>&lt;outputDir&gt;/rois.json</c>.
        ///
        /// Call this after <see cref="SaveZarr"/> so that both image data and
        /// ROIs live in the same directory and can be round-tripped together.
        /// </summary>
        public static void SaveROIs(BioImage b, string outputDir)
        {
            var dtos = new List<RoiDto>(b.Annotations.Count);

            foreach (ROI an in b.Annotations)
            {
                if (an == null)
                    continue;

                if (!string.IsNullOrWhiteSpace(an.roiID) &&
                    an.roiID.StartsWith("zarr-label:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                // Encode points as "x,y x,y …" (invariant culture) — same
                // format used by BioImage.ROIToString / stringToPoints.
                PointD[] pts  = an.GetPoints();
                var      ptSb = new System.Text.StringBuilder();
                for (int i = 0; i < pts.Length; i++)
                {
                    if (i > 0) ptSb.Append(' ');
                    ptSb.Append(pts[i].X.ToString(CultureInfo.InvariantCulture));
                    ptSb.Append(',');
                    ptSb.Append(pts[i].Y.ToString(CultureInfo.InvariantCulture));
                }

                dtos.Add(new RoiDto
                {
                    RoiId       = an.roiID,
                    RoiName     = an.roiName,
                    Type        = an.type.ToString(),
                    Id          = an.id,
                    ShapeIndex  = an.shapeIndex,
                    Text        = an.Text,
                    Serie       = an.serie,
                    Z           = an.coord.Z,
                    C           = an.coord.C,
                    T           = an.coord.T,
                    X           = an.X,
                    Y           = an.Y,
                    W           = an.W,
                    H           = an.H,
                    Points      = ptSb.ToString(),
                    StrokeColor = $"{an.strokeColor.A},{an.strokeColor.R},{an.strokeColor.G},{an.strokeColor.B}",
                    StrokeWidth = an.strokeWidth,
                    FillColor   = $"{an.fillColor.A},{an.fillColor.R},{an.fillColor.G},{an.fillColor.B}",
                    FontSize    = an.fontSize,
                });
            }

            var opts = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(dtos, opts);
            File.WriteAllText(Path.Combine(outputDir, RoiFileName), json);

            Recorder.Record(BioLib.Recorder.GetCurrentMethodInfo(), false, b, outputDir);
        }

        /// <summary>
        /// Writes v2-compatible OME-Zarr sidecar metadata alongside the v3
        /// files produced by the main exporter.
        /// This improves compatibility with readers that still expect .zgroup
        /// / .zattrs / .zarray metadata.
        /// </summary>
        public static void SaveV2Compatibility(BioImage image, string outputDir)
        {
            if (image == null)
                return;

            if (string.IsNullOrWhiteSpace(outputDir))
                return;

            if (!Directory.Exists(outputDir) && !outputDir.EndsWith(".zarr", StringComparison.OrdinalIgnoreCase))
                return;

            try
            {
                WriteText(Path.Combine(outputDir, ".zgroup"), JsonSerializer.Serialize(new { zarr_format = 2 }, new JsonSerializerOptions { WriteIndented = true }));

                var multiscales = new[]
                {
                    new
                    {
                        version = "0.4",
                        name = Path.GetFileNameWithoutExtension(image.Filename),
                        axes = new object[]
                        {
                            new { name = "t", type = "time" },
                            new { name = "c", type = "channel" },
                            new { name = "z", type = "space", unit = "micrometer" },
                            new { name = "y", type = "space", unit = "micrometer" },
                            new { name = "x", type = "space", unit = "micrometer" }
                        },
                        datasets = BuildV2Datasets(image)
                    }
                };

                WriteText(Path.Combine(outputDir, ".zattrs"), JsonSerializer.Serialize(new { multiscales }, new JsonSerializerOptions { WriteIndented = true }));

                for (int i = 0; i < image.Resolutions.Count; i++)
                {
                    var res = image.Resolutions[i];
                    var dtype = MapV2Dtype(DetermineSourcePixelFormat(image));
                    var chunks = new[]
                    {
                        1,
                        1,
                        Math.Max(1, image.SizeZ),
                        Math.Min(512, res.SizeY),
                        Math.Min(512, res.SizeX)
                    };

                    var arrayDoc = new
                    {
                        zarr_format = 2,
                        shape = new long[] { image.SizeT, image.SizeC, image.SizeZ, res.SizeY, res.SizeX },
                        chunks,
                        dtype,
                        compressor = BuildV2BloscCompressor(DetermineSourcePixelFormat(image)),
                        fill_value = 0,
                        order = "C",
                        filters = (object?)null,
                        dimension_separator = "/"
                    };

                    var levelDir = Path.Combine(outputDir, i.ToString());
                    WriteText(Path.Combine(levelDir, ".zarray"), JsonSerializer.Serialize(arrayDoc, new JsonSerializerOptions { WriteIndented = true }));
                    WriteText(Path.Combine(levelDir, ".zattrs"), "{}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Save Zarr v2 compatibility failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Reads <c>&lt;zarrDir&gt;/rois.json</c> and returns the deserialized
        /// list of <see cref="ROI"/> objects.  Returns an empty list when no
        /// sidecar file exists.
        /// </summary>
        public static List<ROI> LoadROIs(string zarrDir)
        {
            var result = new List<ROI>();
            string path = Path.Combine(zarrDir, RoiFileName);

            if (!File.Exists(path))
                return result;

            string json = File.ReadAllText(path);
            var dtos = JsonSerializer.Deserialize<List<RoiDto>>(json);
            if (dtos == null)
                return result;

            foreach (var dto in dtos)
            {
                if (!string.IsNullOrWhiteSpace(dto.RoiId) &&
                    dto.RoiId.StartsWith("zarr-label:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var an = new ROI
                {
                    roiID      = dto.RoiId,
                    roiName    = dto.RoiName,
                    type       = (ROI.Type)Enum.Parse(typeof(ROI.Type), dto.Type),
                    id         = dto.Id,
                    shapeIndex = dto.ShapeIndex,
                    Text       = dto.Text,
                    serie      = dto.Serie,
                    coord      = new AForge.ZCT(dto.Z, dto.C, dto.T),
                    strokeWidth= dto.StrokeWidth,
                    fontSize   = dto.FontSize,
                };

                // Closed shapes
                if (an.type == ROI.Type.Freeform || an.type == ROI.Type.Polygon)
                    an.closed = true;

                // Restore colours
                if (!string.IsNullOrEmpty(dto.StrokeColor))
                {
                    var sc = dto.StrokeColor.Split(',');
                    an.strokeColor = AForge.Color.FromArgb(
                        int.Parse(sc[0]), int.Parse(sc[1]),
                        int.Parse(sc[2]), int.Parse(sc[3]));
                }
                if (!string.IsNullOrEmpty(dto.FillColor))
                {
                    var fc = dto.FillColor.Split(',');
                    an.fillColor = AForge.Color.FromArgb(
                        int.Parse(fc[0]), int.Parse(fc[1]),
                        int.Parse(fc[2]), int.Parse(fc[3]));
                }

                // Restore points then bounding box
                if (!string.IsNullOrEmpty(dto.Points))
                    an.AddPoints(an.stringToPoints(dto.Points));

                an.BoundingBox = new AForge.RectangleD(dto.X, dto.Y, dto.W, dto.H);

                result.Add(an);
            }

            Recorder.Record(BioLib.Recorder.GetCurrentMethodInfo(), false, zarrDir);
            return result;
        }

        /// <summary>
        /// Saves the current Zarr label overlays as an OME-Zarr labels image.
        /// </summary>
        public static void SaveLabelOverlays(BioImage image, IReadOnlyList<ROI> overlays, string outputDir)
        {
            SaveLabelOverlaysAsync(image, overlays, outputDir).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Async implementation for writing Zarr label overlays.
        /// </summary>
        public static async System.Threading.Tasks.Task SaveLabelOverlaysAsync(
            BioImage image,
            IReadOnlyList<ROI> overlays,
            string outputDir,
            CancellationToken ct = default)
        {
            if (image == null || overlays == null || overlays.Count == 0)
                return;

            if (string.IsNullOrWhiteSpace(outputDir))
                return;

            if (!Directory.Exists(outputDir) && !outputDir.EndsWith(".zarr", StringComparison.OrdinalIgnoreCase))
                return;

            var labelRois = overlays
                .Where(IsSupportedLabelRoi)
                .ToList();

            if (labelRois.Count == 0)
                return;

            try
            {
                AppLog.Append("[SaveLabelOverlaysAsync] labelRois=" + labelRois.Count +
                    " outputDir=" + outputDir +
                    " image=" + image?.Filename);
            }
            catch { }

            int width = image.Resolutions.Count > 0 ? image.Resolutions[0].SizeX : image.SizeX;
            int height = image.Resolutions.Count > 0 ? image.Resolutions[0].SizeY : image.SizeY;
            if (width <= 0 || height <= 0)
                return;

            int sourceWidth = image.Resolutions.Count > 0 ? image.Resolutions[0].SizeX : width;
            int sourceHeight = image.Resolutions.Count > 0 ? image.Resolutions[0].SizeY : height;
            double scaleX = sourceWidth > 0 ? width / (double)sourceWidth : 1.0;
            double scaleY = sourceHeight > 0 ? height / (double)sourceHeight : 1.0;

            int sizeT = Math.Max(1, image.SizeT);
            int sizeC = Math.Max(1, image.SizeC);
            int sizeZ = Math.Max(1, image.SizeZ);
            int planePixels = width * height;
            int planeStride = planePixels;
            int zStride = planeStride;
            int cStride = zStride * sizeZ;
            int tStride = cStride * sizeC;

            short[] labelVolume = new short[planePixels * sizeZ * sizeC * sizeT];
            var labelColors = new Dictionary<int, AForge.Color>();
            int fallbackLabel = 1;
            int probeCount = 0;

            foreach (var roi in labelRois)
            {
                int labelId = ParseLabelId(roi.Text, fallbackLabel++);
                if (labelId <= 0)
                    continue;

                if (!labelColors.ContainsKey(labelId))
                {
                    var col = roi.fillColor;
                    if (col.A == 0)
                        col = roi.strokeColor;
                    if (col.A == 0)
                        col = AForge.Color.FromArgb(96, 0, 255, 0);
                    labelColors[labelId] = col;
                }

                int z = Math.Clamp(roi.coord.Z, 0, sizeZ - 1);
                int c = Math.Clamp(roi.coord.C, 0, sizeC - 1);
                int t = Math.Clamp(roi.coord.T, 0, sizeT - 1);
                int offset = (t * tStride) + (c * cStride) + (z * zStride);

                if (probeCount < 4)
                {
                    try
                    {
                        int positiveCount = 0;
                        int firstPosX = -1;
                        int firstPosY = -1;
                        int firstDstX = -1;
                        int firstDstY = -1;
                        if (roi.type == ROI.Type.Mask && roi.roiMask != null)
                        {
                            var mask = roi.roiMask;
                            for (int py = 0; py < mask.Height; py++)
                            {
                                for (int px = 0; px < mask.Width; px++)
                                {
                                    if (mask.GetValue(px, py) <= 0)
                                        continue;

                                    positiveCount++;
                                    if (firstPosX < 0)
                                    {
                                        firstPosX = px;
                                        firstPosY = py;
                                        firstDstX = (int)Math.Round((mask.X + px) * scaleX);
                                        firstDstY = (int)Math.Round((mask.Y + py) * scaleY);
                                    }
                                }
                            }
                        }
                        else if (TryGetRoiProbePoint(roi, scaleX, scaleY, out firstDstX, out firstDstY))
                        {
                            firstPosX = 0;
                            firstPosY = 0;
                            positiveCount = 1;
                        }

                        AppLog.Append("[SaveLabelOverlaysAsync.MaskProbe] roi=" + roi.roiID +
                            " type=" + roi.type +
                            " coord=" + z + "," + c + "," + t +
                            " bbox=" + roi.BoundingBox.X + "," + roi.BoundingBox.Y + "," + roi.BoundingBox.W + "," + roi.BoundingBox.H +
                            " scale=" + scaleX + "," + scaleY +
                            " pos=" + positiveCount +
                            " firstSrc=" + firstPosX + "," + firstPosY +
                            " firstDst=" + firstDstX + "," + firstDstY +
                            (roi.type == ROI.Type.Mask && roi.roiMask != null
                                ? " sample=" + roi.roiMask.GetValue(0, 0) + "," +
                                  roi.roiMask.GetValue(Math.Min(1, Math.Max(0, roi.roiMask.Width - 1)), Math.Min(1, Math.Max(0, roi.roiMask.Height - 1)))
                                : ""));
                    }
                    catch { }
                    probeCount++;
                }
                RasterizeRoi(labelVolume, width, height, roi, labelId, offset, scaleX, scaleY);
            }

            try
            {
                int nonZero = 0;
                for (int i = 0; i < labelVolume.Length; i++)
                {
                    if (labelVolume[i] != 0)
                        nonZero++;
                }
                AppLog.Append("[SaveLabelOverlaysAsync] nonZero=" + nonZero +
                    " volume=" + labelVolume.Length);
            }
            catch { }

            if (labelColors.Count == 0)
                return;

            using var store = new ZarrNET.Core.Zarr.Store.LocalFileSystemStore(outputDir);
            var rootGroup = await ZarrNET.Core.Zarr.ZarrGroup.OpenRootAsync(store, ct).ConfigureAwait(false);

            await WriteJsonAsync(
                store,
                "labels/zarr.json",
                new
                {
                    zarr_format = 3,
                    node_type   = "group",
                    attributes   = new { labels = new[] { "0" } }
                },
                ct).ConfigureAwait(false);

            var colors = labelColors
                .OrderBy(kv => kv.Key)
                .Select(kv => new Dictionary<string, object?>
                {
                    ["label-value"] = kv.Key,
                    ["rgba"] = new[] { kv.Value.R, kv.Value.G, kv.Value.B, kv.Value.A }
                })
                .ToArray();

            var labelImageGroupDoc = new Dictionary<string, object?>
            {
                ["multiscales"] = new[]
                {
                    new
                    {
                        version = "0.5",
                        name = "0",
                        axes = new object[]
                        {
                            new { name = "c", type = "channel" },
                            new { name = "z", type = "space", unit = "micrometer" },
                            new { name = "y", type = "space", unit = "micrometer" },
                            new { name = "x", type = "space", unit = "micrometer" }
                        },
                        datasets = new[]
                        {
                            new
                            {
                                path = "0",
                                coordinateTransformations = new object[]
                                {
                                    new
                                    {
                                        type = "scale",
                                        scale = new[]
                                        {
                                            1.0,
                                            image.PhysicalSizeZ > 0 ? image.PhysicalSizeZ : 1.0,
                                            image.PhysicalSizeY > 0 ? image.PhysicalSizeY : 1.0,
                                            image.PhysicalSizeX > 0 ? image.PhysicalSizeX : 1.0
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                ["image-label"] = new Dictionary<string, object?>
                {
                    ["version"] = "0.5",
                    ["source"] = new Dictionary<string, object?>
                    {
                        ["href"] = "../.."
                    },
                    ["colors"] = colors,
                    ["properties"] = Array.Empty<object>()
                }
            };

            var labelArrayDoc = new
            {
                zarr_format = 3,
                node_type   = "array",
                shape       = new long[] { sizeT, sizeC, sizeZ, height, width },
                data_type   = "int16",
                chunk_grid  = new
                {
                    name = "regular",
                    configuration = new
                    {
                        chunk_shape = new[]
                        {
                            1,
                            1,
                            1,
                            Math.Min(512, height),
                            Math.Min(512, width)
                        }
                    }
                },
                chunk_key_encoding = new
                {
                    name = "default",
                    configuration = new { separator = "/" }
                },
                fill_value = 0,
                dimension_names = new[] { "t", "c", "z", "y", "x" },
                codecs = new object[]
                {
                    new
                    {
                        name = "blosc",
                        configuration = new
                        {
                            cname = "lz4",
                            clevel = 5,
                            shuffle = "byteshuffle",
                            typesize = 2,
                            blocksize = 0
                        }
                    }
                },
                attributes = labelImageGroupDoc
            };

            await WriteJsonAsync(store, "labels/0/zarr.json", new
            {
                zarr_format = 3,
                node_type   = "group",
                attributes  = labelImageGroupDoc
            }, ct).ConfigureAwait(false);

            await WriteJsonAsync(store, "labels/0/0/zarr.json", labelArrayDoc, ct).ConfigureAwait(false);

            var labelGroup = await rootGroup.OpenGroupAsync("labels", ct).ConfigureAwait(false);
            var labelImageGroup = await labelGroup.OpenGroupAsync("0", ct).ConfigureAwait(false);
            var labelArray = await labelImageGroup.OpenArrayAsync("0", ct).ConfigureAwait(false);

            var labelBytes = new byte[labelVolume.Length * sizeof(short)];
            Buffer.BlockCopy(labelVolume, 0, labelBytes, 0, labelBytes.Length);

            await labelArray.WriteRegionAsync(
                new long[] { 0, 0, 0, 0, 0 },
                new long[] { sizeT, sizeC, sizeZ, height, width },
                labelBytes,
                ct).ConfigureAwait(false);

            WriteText(Path.Combine(outputDir, "labels", ".zgroup"), JsonSerializer.Serialize(new { zarr_format = 2 }, new JsonSerializerOptions { WriteIndented = true }));
            WriteText(Path.Combine(outputDir, "labels", ".zattrs"), JsonSerializer.Serialize(new { labels = new[] { "0" } }, new JsonSerializerOptions { WriteIndented = true }));

            WriteText(Path.Combine(outputDir, "labels", "0", ".zgroup"), JsonSerializer.Serialize(new { zarr_format = 2 }, new JsonSerializerOptions { WriteIndented = true }));

            var labelV2Array = new
            {
                zarr_format = 2,
                shape = new long[] { sizeT, sizeC, sizeZ, height, width },
                chunks = new[] { 1, 1, 1, Math.Min(512, height), Math.Min(512, width) },
                dtype = "<i2",
                compressor = new
                {
                    id = "blosc",
                    cname = "lz4",
                    clevel = 5,
                    shuffle = 1,
                    blocksize = 0
                },
                fill_value = 0,
                order = "C",
                filters = (object?)null,
                dimension_separator = "/"
            };

            WriteText(Path.Combine(outputDir, "labels", "0", "0", ".zarray"), JsonSerializer.Serialize(labelV2Array, new JsonSerializerOptions { WriteIndented = true }));
            var labelAttrs = new Dictionary<string, object?>
            {
                ["multiscales"] = new[]
                {
                    new
                    {
                        version = "0.4",
                        name = "0",
                        axes = new object[]
                        {
                            new { name = "t", type = "time" },
                            new { name = "c", type = "channel" },
                            new { name = "z", type = "space", unit = "micrometer" },
                            new { name = "y", type = "space", unit = "micrometer" },
                            new { name = "x", type = "space", unit = "micrometer" }
                        },
                        datasets = new[]
                        {
                            new
                            {
                                path = "0",
                                coordinateTransformations = new object[]
                                {
                                    new
                                    {
                                        type = "scale",
                                        scale = new[]
                                        {
                                            1.0,
                                            1.0,
                                            image.PhysicalSizeZ > 0 ? image.PhysicalSizeZ : 1.0,
                                            image.PhysicalSizeY > 0 ? image.PhysicalSizeY : 1.0,
                                            image.PhysicalSizeX > 0 ? image.PhysicalSizeX : 1.0
                                        }
                                    }
                                }
                            }
                        }
                    }
                },
                ["image-label"] = new Dictionary<string, object?>
                {
                    ["version"] = "0.4",
                    ["source"] = new Dictionary<string, object?>
                    {
                        ["href"] = "../.."
                    },
                    ["colors"] = colors,
                    ["properties"] = Array.Empty<object>()
                }
            };

            WriteText(
                Path.Combine(outputDir, "labels", "0", ".zattrs"),
                JsonSerializer.Serialize(labelAttrs, new JsonSerializerOptions { WriteIndented = true }));

                Recorder.Record(BioLib.Recorder.GetCurrentMethodInfo(), false, image, outputDir, labelRois.Count);
        }

        /// <summary>
        /// Loads OME-Zarr label arrays as temporary mask overlays for display.
        /// These overlays are not part of the saved ROI sidecar.
        /// </summary>
        public static async System.Threading.Tasks.Task<List<ROI>> LoadLabelOverlaysAsync(BioImage image, string? sourceOverride = null)
        {
            var overlays = new List<ROI>();

            if (image == null)
                return overlays;

            string source = sourceOverride ?? image.file;
            if (string.IsNullOrWhiteSpace(source))
                source = image.Filename;

            if (string.IsNullOrWhiteSpace(source) ||
                (!source.Contains(".zarr", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(source)))
                return overlays;

            try
            {
                var reader = await OmeZarrReader.OpenAsync(source).ConfigureAwait(false);
                var imageNode = await reader.AsMultiscaleImageAsync().ConfigureAwait(false);

                if (!await imageNode.HasLabelsAsync().ConfigureAwait(false))
                    return overlays;

                var labelGroup = await imageNode.OpenLabelsAsync().ConfigureAwait(false);
                foreach (var labelName in labelGroup.LabelNames)
                {
                    try
                    {
                        var labelNode = await labelGroup.OpenLabelAsync(labelName).ConfigureAwait(false);
                        var multiscale = labelNode.Multiscales.FirstOrDefault();
                        if (multiscale == null || multiscale.Datasets.Length == 0)
                            continue;

                        var level = await labelNode.OpenResolutionLevelAsync(datasetIndex: 0).ConfigureAwait(false);
                        var axes = level.EffectiveAxes;
                        var rawShape = level.Shape;
                        var shape = rawShape;
                        bool hasLeadingSingletonT = shape.Length == axes.Length + 1 && shape[0] == 1;
                        if (hasLeadingSingletonT)
                        {
                            // Local Zarr label exports currently include an
                            // extra leading singleton t dimension even though
                            // the label metadata only exposes c/z/y/x axes.
                            // Normalize that here so existing saved datasets
                            // can be read without misaligning axes and shape.
                            shape = shape.Skip(1).ToArray();
                        }

                        int sizeX = GetAxisSizeStatic(axes, shape, "x");
                        int sizeY = GetAxisSizeStatic(axes, shape, "y");
                        int sizeZ = Math.Max(1, GetAxisSizeStatic(axes, shape, "z", 1));
                        int sizeC = Math.Max(1, GetAxisSizeStatic(axes, shape, "c", 1));
                        int sizeT = Math.Max(1, GetAxisSizeStatic(axes, shape, "t", 1));
                        Log("[Zarr.LoadLabelOverlaysAsync] label=" + labelName +
                            " axes=" + string.Join("", axes.Select(a => a.Name)) +
                            " shape=" + string.Join("x", shape) +
                            " imageCoord=" + image.Coordinate.Z + "," + image.Coordinate.C + "," + image.Coordinate.T +
                            " labelSizes=Z" + sizeZ + " C" + sizeC + " T" + sizeT);

                        if (sizeX <= 0 || sizeY <= 0)
                            continue;

                        double physX = image.PhysicalSizeX > 0 ? image.PhysicalSizeX : 1.0;
                        double physY = image.PhysicalSizeY > 0 ? image.PhysicalSizeY : 1.0;
                        var color = PickLabelColor(labelNode.ImageLabelMetadata);

                        try
                        {
                            int shapeIdx = 0;
                            for (int t = 0; t < sizeT; t++)
                            {
                                for (int z = 0; z < sizeZ; z++)
                                {
                                    for (int c = 0; c < sizeC; c++)
                                    {
                                        int sourceC = c;
                                        int viewerC = c;
                                        if (sizeZ == 1 && sizeC == 1 && sizeT == 1 && image.SizeC > 1)
                                            viewerC = image.SizeC - 1;
                                        Log("[Zarr.LoadLabelOverlaysAsync] label=" + labelName +
                                            " viewerC=" + viewerC +
                                            " sourceC=" + sourceC +
                                            " z=" + z +
                                            " t=" + t);

                                        long[] start = hasLeadingSingletonT
                                            ? new long[] { t, sourceC, z, 0, 0 }
                                            : BuildStart(axes, shape, t, sourceC, z);
                                        long[] end = hasLeadingSingletonT
                                            ? new long[] { t + 1, sourceC + 1, z + 1, sizeY, sizeX }
                                            : BuildEnd(axes, shape, t, sourceC, z, sizeX, sizeY);
                                        var region = new ZarrNET.Core.OmeZarr.Coordinates.PixelRegion(start, end);
                                        var regionData = await level.ReadPixelRegionAsync(region).ConfigureAwait(false);
                                        if (regionData == null || regionData.Data == null || regionData.Data.Length == 0)
                                            continue;

                                        int totalPixels = sizeX * sizeY;
                                        if (totalPixels <= 0)
                                            continue;

                                        int bytesPerPixel = Math.Max(1, regionData.Data.Length / totalPixels);
                                        var labelMasks = SplitLabelPlane(regionData.Data, sizeX, sizeY, bytesPerPixel);
                                        if (labelMasks.Count == 0)
                                            continue;
                                        Log("[Zarr.LoadLabelOverlaysAsync] label=" + labelName +
                                            " coord=" + z + "," + viewerC + "," + t +
                                            " sourceC=" + sourceC +
                                            " labels=" + labelMasks.Count);

                                        var zct = new AForge.ZCT(z, viewerC, t);
                                        foreach (var kv in labelMasks)
                                        {
                                            int labelId = kv.Key;
                                            byte[] maskData = kv.Value;

                                            var mask = new ROI.Mask(maskData, sizeX, sizeY, physX, physY, 0, 0);
                                            var roi = new ROI
                                            {
                                                type = ROI.Type.Mask,
                                                roiID = $"zarr-label:{labelName}_{labelId}",
                                                roiName = labelName,
                                                Text = labelId.ToString(),
                                                coord = zct,
                                                shapeIndex = shapeIdx++,
                                                strokeColor = color,
                                                fillColor = color,
                                                strokeWidth = 1,
                                                roiMask = mask,
                                            };
                                            roi.UpdateBoundingBox();
                                            overlays.Add(roi);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"Zarr label '{labelName}' plane t={image.Coordinate.T} c={image.Coordinate.C} z={image.Coordinate.Z} failed: {ex.Message}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Load Zarr labels failed for '{labelName}': {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Load Zarr labels failed: {ex.Message}");
            }

            return overlays;
        }

        private static async System.Threading.Tasks.Task WriteJsonAsync(
            ZarrNET.Core.Zarr.Store.IZarrStore store,
            string key,
            object document,
            CancellationToken ct = default)
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.SerializeToUtf8Bytes(document, options);
            await store.WriteAsync(key, json, ct).ConfigureAwait(false);
        }

        private static int ParseLabelId(string? text, int fallback)
        {
            if (!string.IsNullOrWhiteSpace(text) &&
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
            {
                return parsed;
            }

            return fallback;
        }

        private static bool IsSupportedLabelRoi(ROI roi)
        {
            if (roi == null)
                return false;

            return roi.type == ROI.Type.Mask ||
                   roi.type == ROI.Type.Point ||
                   roi.type == ROI.Type.Line ||
                   roi.type == ROI.Type.Rectangle ||
                   roi.type == ROI.Type.Ellipse ||
                   roi.type == ROI.Type.Polygon ||
                   roi.type == ROI.Type.Polyline ||
                   roi.type == ROI.Type.Freeform ||
                   roi.type == ROI.Type.Label;
        }

        private static bool TryGetRoiProbePoint(ROI roi, double scaleX, double scaleY, out int x, out int y)
        {
            x = -1;
            y = -1;

            if (roi == null)
                return false;

            if (roi.type == ROI.Type.Point || roi.type == ROI.Type.Label)
            {
                var p = roi.Point;
                x = (int)Math.Round(p.X * scaleX);
                y = (int)Math.Round(p.Y * scaleY);
                return true;
            }

            var pts = roi.GetPoints();
            if (pts != null && pts.Length > 0)
            {
                x = (int)Math.Round(pts[0].X * scaleX);
                y = (int)Math.Round(pts[0].Y * scaleY);
                return true;
            }

            var bb = roi.BoundingBox;
            x = (int)Math.Round((bb.X + (bb.W / 2.0)) * scaleX);
            y = (int)Math.Round((bb.Y + (bb.H / 2.0)) * scaleY);
            return true;
        }

        private static void RasterizeRoi(short[] labelPlane, int imageWidth, int imageHeight, ROI roi, int labelId, int planeOffset, double scaleX, double scaleY)
        {
            if (roi == null || labelPlane == null || labelPlane.Length == 0)
                return;

            if (roi.type == ROI.Type.Mask && roi.roiMask != null)
            {
                RasterizeMask(labelPlane, imageWidth, imageHeight, roi.roiMask, labelId, planeOffset, scaleX, scaleY);
                return;
            }

            int thickness = Math.Max(1, (int)Math.Round(Math.Max(1.0, roi.strokeWidth)));

            switch (roi.type)
            {
                case ROI.Type.Point:
                case ROI.Type.Label:
                    {
                        var p = roi.Point;
                        int px = (int)Math.Round(p.X * scaleX);
                        int py = (int)Math.Round(p.Y * scaleY);
                        PlotThickPoint(labelPlane, imageWidth, imageHeight, planeOffset, px, py, labelId, Math.Max(1, thickness));
                        return;
                    }

                case ROI.Type.Line:
                    {
                        var pts = roi.GetPoints();
                        if (pts == null || pts.Length < 2)
                            return;

                        DrawLine(labelPlane, imageWidth, imageHeight, planeOffset,
                            (int)Math.Round(pts[0].X * scaleX), (int)Math.Round(pts[0].Y * scaleY),
                            (int)Math.Round(pts[1].X * scaleX), (int)Math.Round(pts[1].Y * scaleY),
                            labelId, thickness);
                        return;
                    }

                case ROI.Type.Rectangle:
                    FillRectangle(labelPlane, imageWidth, imageHeight, planeOffset, roi.BoundingBox, labelId, scaleX, scaleY);
                    return;

                case ROI.Type.Ellipse:
                    FillEllipse(labelPlane, imageWidth, imageHeight, planeOffset, roi.BoundingBox, labelId, scaleX, scaleY);
                    return;

                case ROI.Type.Polygon:
                    RasterizePolygonLike(labelPlane, imageWidth, imageHeight, planeOffset, roi, labelId, scaleX, scaleY, thickness);
                    return;

                case ROI.Type.Polyline:
                    RasterizePolyline(labelPlane, imageWidth, imageHeight, planeOffset, roi, labelId, scaleX, scaleY, thickness);
                    return;

                case ROI.Type.Freeform:
                    RasterizePolygonLike(labelPlane, imageWidth, imageHeight, planeOffset, roi, labelId, scaleX, scaleY, thickness);
                    return;
            }
        }

        private static void RasterizePolyline(short[] labelPlane, int imageWidth, int imageHeight, int planeOffset, ROI roi, int labelId, double scaleX, double scaleY, int thickness)
        {
            var pts = roi.GetPoints();
            if (pts == null || pts.Length < 2)
                return;

            for (int i = 0; i < pts.Length - 1; i++)
            {
                DrawLine(labelPlane, imageWidth, imageHeight, planeOffset,
                    (int)Math.Round(pts[i].X * scaleX), (int)Math.Round(pts[i].Y * scaleY),
                    (int)Math.Round(pts[i + 1].X * scaleX), (int)Math.Round(pts[i + 1].Y * scaleY),
                    labelId, thickness);
            }
        }

        private static void RasterizePolygonLike(short[] labelPlane, int imageWidth, int imageHeight, int planeOffset, ROI roi, int labelId, double scaleX, double scaleY, int thickness)
        {
            var pts = roi.GetPoints();
            if (pts == null || pts.Length == 0)
                return;

            if (pts.Length < 3)
            {
                RasterizePolyline(labelPlane, imageWidth, imageHeight, planeOffset, roi, labelId, scaleX, scaleY, thickness);
                return;
            }

            if (roi.type == ROI.Type.Freeform || roi.closed)
            {
                var scaled = pts.Select(p => new PointD(p.X * scaleX, p.Y * scaleY)).ToArray();
                FillPolygon(labelPlane, imageWidth, imageHeight, planeOffset, scaled, labelId);
            }
            else
            {
                RasterizePolyline(labelPlane, imageWidth, imageHeight, planeOffset, roi, labelId, scaleX, scaleY, thickness);
            }
        }

        private static void FillRectangle(short[] labelPlane, int imageWidth, int imageHeight, int planeOffset, AForge.RectangleD rect, int labelId, double scaleX, double scaleY)
        {
            double x0 = rect.X * scaleX;
            double y0 = rect.Y * scaleY;
            double x1 = (rect.X + rect.W) * scaleX;
            double y1 = (rect.Y + rect.H) * scaleY;

            int minX = Math.Max(0, (int)Math.Floor(Math.Min(x0, x1)));
            int maxX = Math.Min(imageWidth - 1, (int)Math.Ceiling(Math.Max(x0, x1)));
            int minY = Math.Max(0, (int)Math.Floor(Math.Min(y0, y1)));
            int maxY = Math.Min(imageHeight - 1, (int)Math.Ceiling(Math.Max(y0, y1)));

            for (int y = minY; y <= maxY; y++)
            {
                int rowOffset = planeOffset + (y * imageWidth);
                for (int x = minX; x <= maxX; x++)
                {
                    if (rowOffset + x >= 0 && rowOffset + x < labelPlane.Length)
                        labelPlane[rowOffset + x] = (short)Math.Clamp(labelId, short.MinValue, short.MaxValue);
                }
            }
        }

        private static void FillEllipse(short[] labelPlane, int imageWidth, int imageHeight, int planeOffset, AForge.RectangleD rect, int labelId, double scaleX, double scaleY)
        {
            double x0 = rect.X * scaleX;
            double y0 = rect.Y * scaleY;
            double x1 = (rect.X + rect.W) * scaleX;
            double y1 = (rect.Y + rect.H) * scaleY;

            double left = Math.Min(x0, x1);
            double top = Math.Min(y0, y1);
            double right = Math.Max(x0, x1);
            double bottom = Math.Max(y0, y1);
            double rx = (right - left) / 2.0;
            double ry = (bottom - top) / 2.0;
            if (rx <= 0 || ry <= 0)
            {
                FillRectangle(labelPlane, imageWidth, imageHeight, planeOffset, rect, labelId, scaleX, scaleY);
                return;
            }

            double cx = left + rx;
            double cy = top + ry;
            int minX = Math.Max(0, (int)Math.Floor(left));
            int maxX = Math.Min(imageWidth - 1, (int)Math.Ceiling(right));
            int minY = Math.Max(0, (int)Math.Floor(top));
            int maxY = Math.Min(imageHeight - 1, (int)Math.Ceiling(bottom));

            for (int y = minY; y <= maxY; y++)
            {
                int rowOffset = planeOffset + (y * imageWidth);
                double ny = ((y + 0.5) - cy) / ry;
                for (int x = minX; x <= maxX; x++)
                {
                    double nx = ((x + 0.5) - cx) / rx;
                    if ((nx * nx) + (ny * ny) <= 1.0 && rowOffset + x >= 0 && rowOffset + x < labelPlane.Length)
                        labelPlane[rowOffset + x] = (short)Math.Clamp(labelId, short.MinValue, short.MaxValue);
                }
            }
        }

        private static void FillPolygon(short[] labelPlane, int imageWidth, int imageHeight, int planeOffset, PointD[] points, int labelId)
        {
            if (points == null || points.Length < 3)
                return;

            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);

            int iMinX = Math.Max(0, (int)Math.Floor(minX));
            int iMaxX = Math.Min(imageWidth - 1, (int)Math.Ceiling(maxX));
            int iMinY = Math.Max(0, (int)Math.Floor(minY));
            int iMaxY = Math.Min(imageHeight - 1, (int)Math.Ceiling(maxY));

            for (int y = iMinY; y <= iMaxY; y++)
            {
                int rowOffset = planeOffset + (y * imageWidth);
                for (int x = iMinX; x <= iMaxX; x++)
                {
                    if (!PointInPolygon(x + 0.5, y + 0.5, points))
                        continue;

                    if (rowOffset + x >= 0 && rowOffset + x < labelPlane.Length)
                        labelPlane[rowOffset + x] = (short)Math.Clamp(labelId, short.MinValue, short.MaxValue);
                }
            }
        }

        private static bool PointInPolygon(double x, double y, PointD[] points)
        {
            bool inside = false;
            int j = points.Length - 1;

            for (int i = 0; i < points.Length; i++)
            {
                double xi = points[i].X;
                double yi = points[i].Y;
                double xj = points[j].X;
                double yj = points[j].Y;

                bool intersect = ((yi > y) != (yj > y)) &&
                                 (x < (xj - xi) * (y - yi) / Math.Max(1e-12, yj - yi) + xi);
                if (intersect)
                    inside = !inside;

                j = i;
            }

            return inside;
        }

        private static void DrawLine(short[] labelPlane, int imageWidth, int imageHeight, int planeOffset, int x0, int y0, int x1, int y1, int labelId, int thickness)
        {
            int dx = Math.Abs(x1 - x0);
            int sx = x0 < x1 ? 1 : -1;
            int dy = -Math.Abs(y1 - y0);
            int sy = y0 < y1 ? 1 : -1;
            int err = dx + dy;

            while (true)
            {
                PlotThickPoint(labelPlane, imageWidth, imageHeight, planeOffset, x0, y0, labelId, thickness);
                if (x0 == x1 && y0 == y1)
                    break;

                int e2 = 2 * err;
                if (e2 >= dy)
                {
                    err += dy;
                    x0 += sx;
                }
                if (e2 <= dx)
                {
                    err += dx;
                    y0 += sy;
                }
            }
        }

        private static void PlotThickPoint(short[] labelPlane, int imageWidth, int imageHeight, int planeOffset, int x, int y, int labelId, int thickness)
        {
            int radius = Math.Max(0, (thickness - 1) / 2);
            for (int yy = y - radius; yy <= y + radius; yy++)
            {
                if (yy < 0 || yy >= imageHeight)
                    continue;

                int rowOffset = planeOffset + (yy * imageWidth);
                for (int xx = x - radius; xx <= x + radius; xx++)
                {
                    if (xx < 0 || xx >= imageWidth)
                        continue;

                    int index = rowOffset + xx;
                    if (index >= 0 && index < labelPlane.Length)
                        labelPlane[index] = (short)Math.Clamp(labelId, short.MinValue, short.MaxValue);
                }
            }
        }

        private static void RasterizeMask(short[] labelPlane, int imageWidth, int imageHeight, ROI.Mask mask, int labelId, int planeOffset, double scaleX, double scaleY)
        {
            if (scaleX <= 0) scaleX = 1.0;
            if (scaleY <= 0) scaleY = 1.0;

            int startY = 0;
            int startX = 0;
            int endY = mask.Height;
            int endX = mask.Width;

            for (int y = startY; y < endY; y++)
            {
                int dstY = (int)Math.Round((mask.Y + y) * scaleY);
                if (dstY < 0 || dstY >= imageHeight)
                    continue;

                int rowOffset = dstY * imageWidth;

                for (int x = startX; x < endX; x++)
                {
                    if (mask.GetValue(x, y) <= 0)
                        continue;

                    int dstX = (int)Math.Round((mask.X + x) * scaleX);
                    if (dstX < 0 || dstX >= imageWidth)
                        continue;

                    int index = planeOffset + rowOffset + dstX;
                    if (index >= 0 && index < labelPlane.Length)
                        labelPlane[index] = (short)Math.Clamp(labelId, short.MinValue, short.MaxValue);
                }
            }
        }

        private static int GetAxisLength(AxisMetadata[] axes, long[] shape, string axisName)
        {
            for (int i = 0; i < axes.Length && i < shape.Length; i++)
            {
                if (string.Equals(axes[i].Name, axisName, StringComparison.OrdinalIgnoreCase))
                    return (int)shape[i];
            }
            return 0;
        }

        private static Dictionary<int, byte[]> SplitLabelPlane(byte[] raw, int w, int h, int bytesPerPixel)
        {
            int total = w * h;
            var result = new Dictionary<int, byte[]>();

            for (int i = 0; i < total; i++)
            {
                int labelId = ReadLabelId(raw, i, bytesPerPixel);
                if (labelId == 0)
                    continue;

                if (!result.TryGetValue(labelId, out byte[]? mask))
                {
                    mask = new byte[total];
                    result[labelId] = mask;
                }

                mask[i] = 255;
            }

            return result;
        }

        private static int ReadLabelId(byte[] raw, int pixelIndex, int bytesPerPixel)
        {
            int byteOffset = pixelIndex * bytesPerPixel;
            if (byteOffset < 0 || byteOffset >= raw.Length)
                return 0;

            return bytesPerPixel switch
            {
                1 => raw[byteOffset],
                2 => BitConverter.ToUInt16(raw, byteOffset),
                4 => BitConverter.ToInt32(raw, byteOffset),
                _ => raw[byteOffset],
            };
        }

        private static long[] BuildStart(AxisMetadata[] axes, long[] shape, int t, int c, int z)
        {
            long[] start = new long[axes.Length];
            for (int i = 0; i < axes.Length; i++)
            {
                start[i] = axes[i].Name.ToLowerInvariant() switch
                {
                    "t" => t,
                    "c" => c,
                    "z" => z,
                    _ => 0,
                };
            }
            return start;
        }

        private static long[] BuildEnd(AxisMetadata[] axes, long[] shape, int t, int c, int z, int sizeX, int sizeY)
        {
            long[] end = new long[axes.Length];
            for (int i = 0; i < axes.Length; i++)
            {
                end[i] = axes[i].Name.ToLowerInvariant() switch
                {
                    "t" => t + 1,
                    "c" => c + 1,
                    "z" => z + 1,
                    "x" => sizeX,
                    "y" => sizeY,
                    _ => shape[i],
                };
            }
            return end;
        }

        private static int GetAxisSizeStatic(AxisMetadata[] axes, long[] shape, string name, int fallback = 0)
        {
            for (int i = 0; i < axes.Length; i++)
            {
                if (axes[i].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return (int)shape[i];
            }
            return fallback;
        }

        private static byte[] ToBinaryMask(byte[] data, string dataType, int pixelCount)
        {
            int bytesPerPixel = dataType switch
            {
                "int8" => 1,
                "uint8" => 1,
                "int16" => 2,
                "uint16" => 2,
                "int32" => 4,
                "uint32" => 4,
                "float32" => 4,
                "int64" => 8,
                "uint64" => 8,
                "float64" => 8,
                _ => pixelCount > 0 ? Math.Max(1, data.Length / pixelCount) : 1
            };

            if (pixelCount <= 0 || data.Length == 0)
                return Array.Empty<byte>();

            var mask = new byte[pixelCount];
            for (int i = 0; i < pixelCount; i++)
            {
                int offset = i * bytesPerPixel;
                if (offset >= data.Length)
                    break;

                bool nonZero = false;
                for (int b = 0; b < bytesPerPixel && (offset + b) < data.Length; b++)
                {
                    if (data[offset + b] != 0)
                    {
                        nonZero = true;
                        break;
                    }
                }

                if (nonZero)
                    mask[i] = 255;
            }

            return mask;
        }

        private static object[] BuildV2Datasets(BioImage image)
        {
            var datasets = new List<object>();
            for (int i = 0; i < image.Resolutions.Count; i++)
            {
                var res = image.Resolutions[i];
                datasets.Add(new
                {
                    path = i.ToString(),
                    coordinateTransformations = new object[]
                    {
                        new
                        {
                            type = "scale",
                            scale = new[]
                            {
                                1.0,
                                1.0,
                                image.PhysicalSizeZ > 0 ? image.PhysicalSizeZ : 1.0,
                                image.PhysicalSizeY > 0 ? image.PhysicalSizeY : 1.0,
                                image.PhysicalSizeX > 0 ? image.PhysicalSizeX : 1.0
                            }
                        }
                    }
                });
            }

            return datasets.ToArray();
        }

        private static string MapV2Dtype(AForge.PixelFormat pixelFormat)
        {
            return pixelFormat switch
            {
                AForge.PixelFormat.Format8bppIndexed => "|u1",
                AForge.PixelFormat.Format24bppRgb    => "|u1",
                AForge.PixelFormat.Format32bppArgb   => "|u1",
                AForge.PixelFormat.Format32bppRgb    => "|u1",
                AForge.PixelFormat.Format16bppGrayScale => "<u2",
                AForge.PixelFormat.Format48bppRgb    => "<u2",
                _ => "<u2"
            };
        }

        private static object BuildV2BloscCompressor(AForge.PixelFormat pixelFormat)
        {
            return new
            {
                id = "blosc",
                cname = "lz4",
                clevel = 5,
                shuffle = 1,
                blocksize = 0
            };
        }

        private static void WriteText(string path, string content)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(path, content);
        }

        private static bool IsSupportedLabelDataType(string dataType)
        {
            return string.Equals(dataType, "bool", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "int8", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "uint8", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "int16", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "uint16", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "int32", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "uint32", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "int64", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "uint64", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "float32", StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dataType, "float64", StringComparison.OrdinalIgnoreCase);
        }

        private static Color PickLabelColor(ImageLabelMetadata metadata)
        {
            if (metadata?.Colors != null)
            {
                foreach (var entry in metadata.Colors)
                {
                    var rgba = entry.Rgba;
                    if (rgba != null && rgba.Length >= 4 &&
                        (rgba[0] != 0 || rgba[1] != 0 || rgba[2] != 0 || rgba[3] != 0))
                    {
                        return Color.FromArgb(rgba[3], rgba[0], rgba[1], rgba[2]);
                    }
                }
            }

            return Color.FromArgb(96, 0, 255, 0);
        }

        // =====================================================================
        // Channel-extraction helper
        // =====================================================================

        /// Extracts one colour channel from an interleaved buffer.
        ///
        /// Given pixels stored as [C0 C1 C2 C0 C1 C2 ...] with
        /// <paramref name="bytesPerSample"/> bytes per component, returns
        /// a contiguous single-channel plane for <paramref name="channelIndex"/>.
        /// For BGRA sources pass the remapped byte index so that byte-order
        /// correction happens here.
        /// </summary>
        private static byte[] DeinterleaveChannel(
            byte[] interleaved,
            int    channelIndex,
            int    totalChannels,
            int    bytesPerSample,
            int    pixelCount)
        {
            var output       = new byte[pixelCount * bytesPerSample];
            var stride       = totalChannels * bytesPerSample;
            var channelStart = channelIndex  * bytesPerSample;

            for (int px = 0; px < pixelCount; px++)
            {
                var srcOffset = px * stride + channelStart;
                var dstOffset = px * bytesPerSample;

                for (int byteIdx = 0; byteIdx < bytesPerSample; byteIdx++)
                    output[dstOffset + byteIdx] = interleaved[srcOffset + byteIdx];
            }

            return output;
        }
    }
}
