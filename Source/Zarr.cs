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
using ZarrNET.Core.OmeZarr.Nodes;

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
        private static readonly object s_labelColorLock = new();
        private static readonly Random s_labelColorRandom = new(unchecked((int)(DateTime.UtcNow.Ticks ^ Environment.TickCount)));
        private static readonly Dictionary<int, Color> s_generatedLabelColors = new();

        private sealed record ExportLevelInfo(
            int SourceIndex,
            int SizeX,
            int SizeY,
            ResolutionLevelDescriptor Descriptor,
            double Downsample,
            double ScaleX,
            double ScaleY,
            double PhysicalSizeX,
            double PhysicalSizeY,
            double PhysicalSizeZ)
        {
            public double AverageScale => (ScaleX + ScaleY) / 2.0;
        }

        private sealed record AssociatedLevelInfo(
            string Name,
            int SourceIndex,
            int SizeX,
            int SizeY);

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

        private static double NormalizePhysicalSize(double value)
        {
            return value > 0 && !double.IsNaN(value) && !double.IsInfinity(value)
                ? value
                : 1.0;
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
            var sourcePixelFormat = DetermineSourcePixelFormat(b);
            bool sourceIsInterleavedRgb = IsInterleavedRgbFormat(sourcePixelFormat);
            int sourceChannelCount = sourceIsInterleavedRgb ? BioImage.GetBands(sourcePixelFormat) : 1;
            bool sourceIsBGRA = IsBGRAFormat(sourcePixelFormat);
            int exportChannelCount = sourceIsInterleavedRgb && b.SizeC == 1
                ? Math.Min(3, sourceChannelCount)
                : b.SizeC;

            var bytesPerSample = bitsPerSample / 8;
            var zarrDataType   = MapDataType(bitsPerSample);
            var totalChannels  = exportChannelCount;
            s_debugTileLogs = 0;

            Log($"[SaveZarr] file={b.Filename} out={outputDir} size={b.SizeX}x{b.SizeY} zct={b.SizeZ}/{b.SizeC}/{b.SizeT} " +
                $"bits={bitsPerSample} bytesPerSample={bytesPerSample} srcPixFmt={sourcePixelFormat} " +
                $"interleavedRgb={sourceIsInterleavedRgb} bands={sourceChannelCount} " +
                $"resLevels={b.Resolutions.Count}");

            int tileW = 512;
            int tileH = 512;

            // ------------------------------------------------------------------
            // 2. Build a real multiscale pyramid from b.Resolutions.
            //    BioImage may append macro/label images to the end of
            //    Resolutions, but those are exported separately as
            //    associated images. The multiscale pyramid should only
            //    contain the actual pyramid levels, in source order.
            // ------------------------------------------------------------------

            var exportLevels = BuildExportLevels(b);
            var levelDescriptors = exportLevels.Select(l => l.Descriptor).ToList();
            if (levelDescriptors.Count == 0)
            {
                levelDescriptors.Add(new ResolutionLevelDescriptor(b.SizeX, b.SizeY, 1.0));
                exportLevels.Add(new ExportLevelInfo(
                    0,
                    b.SizeX,
                    b.SizeY,
                    levelDescriptors[0],
                    1.0,
                    1.0,
                    1.0,
                    NormalizePhysicalSize(b.PhysicalSizeX),
                    NormalizePhysicalSize(b.PhysicalSizeY),
                    NormalizePhysicalSize(b.PhysicalSizeZ)));
            }

            // ------------------------------------------------------------------
            // 3. Build the base descriptor (T/C/Z/dtype — shared by all levels)
            // ------------------------------------------------------------------

            var coord = new ZarrNET.Core.ZCT(b.SizeZ, totalChannels, b.SizeT);

            var baseDescriptor = new BioImageDescriptor(b.SizeX, b.SizeY, coord)
            {
                Name          = Path.GetFileNameWithoutExtension(b.Filename),
                DataType      = zarrDataType,
                PhysicalSizeX = exportLevels.Count > 0 ? exportLevels[0].PhysicalSizeX : NormalizePhysicalSize(b.PhysicalSizeX),
                PhysicalSizeY = exportLevels.Count > 0 ? exportLevels[0].PhysicalSizeY : NormalizePhysicalSize(b.PhysicalSizeY),
                PhysicalSizeZ = NormalizePhysicalSize(b.PhysicalSizeZ),
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
                for (int exportLevelIndex = 0; exportLevelIndex < exportLevels.Count; exportLevelIndex++)
                {
                    var exportLevel = exportLevels[exportLevelIndex];
                    var lvlDesc = exportLevel.Descriptor;
                    Log($"[SaveZarr] level={exportLevelIndex} srcLevel={exportLevel.SourceIndex} size={lvlDesc.SizeX}x{lvlDesc.SizeY} downsample={lvlDesc.Downsample} " +
                        $"scaleX={exportLevel.ScaleX} scaleY={exportLevel.ScaleY}");

                    for (int t = 0; t < b.SizeT; t++)
                    {
                        for (int z = 0; z < b.SizeZ; z++)
                        {
                            for (int c = 0; c < exportChannelCount; c++)
                            {
                                if (sourceIsInterleavedRgb)
                                {
                                    WritePlaneInTiles(
                                        writer, b, lvlDesc,
                                        t, c, z,
                                        bytesPerSample, tileW, tileH,
                                        sourceLevelIndex: exportLevel.SourceIndex,
                                        scaleX: exportLevel.ScaleX,
                                        scaleY: exportLevel.ScaleY,
                                        levelIndex: exportLevelIndex,
                                        rgbChannelIndex: c,
                                        srcChannelCount: sourceChannelCount,
                                        isBGRA: sourceIsBGRA,
                                        logicalC: 0).GetAwaiter().GetResult();
                                }
                                else
                                {
                                    WritePlaneInTiles(
                                        writer, b, lvlDesc,
                                        t, c, z,
                                        bytesPerSample, tileW, tileH,
                                        sourceLevelIndex: exportLevel.SourceIndex,
                                        scaleX: exportLevel.ScaleX,
                                        scaleY: exportLevel.ScaleY,
                                        levelIndex: exportLevelIndex).GetAwaiter().GetResult();
                                }
                            }
                        }
                    }
                }

                // ------------------------------------------------------------------
                // 6. Export macro/label levels as associated images, if present.
                // ------------------------------------------------------------------
                SaveAssociatedResolutionImages(
                    b,
                    outputDir,
                    zarrDataType,
                    baseDescriptor,
                    bytesPerSample,
                    tileW,
                    tileH,
                    sourcePixelFormat,
                    sourceIsInterleavedRgb,
                    sourceChannelCount,
                    exportChannelCount,
                    sourceIsBGRA);

                WriteRootMetadata(outputDir, b, exportLevels);
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
                try
                {
                    File.WriteAllText(Path.Combine(outputDir, "save-error.txt"), e.ToString());
                }
                catch
                {
                }
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
            int sourceLevelIndex   = 0,
            double scaleX          = 1.0,
            double scaleY          = 1.0,
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

                    int frameIndex = b.GetFrameIndex(z, srcC, t);

                    byte[]? pixelBytes = null;
                    AForge.PixelFormat tilePixelFormat = DetermineSourcePixelFormat(b);
                    bool useDirectSourceRead = sourceLevelIndex <= 0 ||
                        (Math.Abs(scaleX - 1.0) < 0.0001 && Math.Abs(scaleY - 1.0) < 0.0001);

                    if (needsDeinterleave)
                    {
                        if (useDirectSourceRead)
                        {
                            using Bitmap tileBitmap = await b.GetTile(
                                frameIndex, Math.Max(0, sourceLevelIndex), tileX, tileY, actualW, actualH,
                                new AForge.ZCT(z, srcC, t), true).ConfigureAwait(false);

                            tilePixelFormat = tileBitmap.PixelFormat;
                            pixelBytes = tileBitmap.Bytes;
                        }
                        else
                        {
                            int srcX = Math.Max(0, (int)Math.Round(tileX * scaleX));
                            int srcY = Math.Max(0, (int)Math.Round(tileY * scaleY));
                            int srcW = Math.Max(1, (int)Math.Round(actualW * scaleX));
                            int srcH = Math.Max(1, (int)Math.Round(actualH * scaleY));

                            using Bitmap tileBitmap = await b.GetTile(
                                frameIndex, 0, srcX, srcY, srcW, srcH,
                                new AForge.ZCT(z, srcC, t), true).ConfigureAwait(false);

                            tilePixelFormat = tileBitmap.PixelFormat;
                            pixelBytes = DownsampleBgraNearest(tileBitmap.Bytes, srcW, srcH, actualW, actualH);
                        }
                    }
                    else
                    {
                        if (useDirectSourceRead)
                        {
                            pixelBytes = await b.GetTileBytesRaw(
                                frameIndex, Math.Max(0, sourceLevelIndex), tileX, tileY, actualW, actualH,
                                new AForge.ZCT(z, srcC, t)).ConfigureAwait(false);
                        }
                        else
                        {
                            int srcX = Math.Max(0, (int)Math.Round(tileX * scaleX));
                            int srcY = Math.Max(0, (int)Math.Round(tileY * scaleY));
                            int srcW = Math.Max(1, (int)Math.Round(actualW * scaleX));
                            int srcH = Math.Max(1, (int)Math.Round(actualH * scaleY));

                            var srcBytes = await b.GetTileBytesRaw(
                                frameIndex, 0, srcX, srcY, srcW, srcH,
                                new AForge.ZCT(z, srcC, t)).ConfigureAwait(false);
                            pixelBytes = DownsampleRawNearest(srcBytes, srcW, srcH, actualW, actualH, bytesPerSample);
                        }
                    }

                    if (pixelBytes == null || pixelBytes.Length == 0)
                    {
                        pixelBytes = new byte[needsDeinterleave ? interleavedBytes : singleChannelBytes];
                    }

                    bool logTile = tileX == 0 && tileY == 0 && s_debugTileLogs < 12;
                    if (logTile)
                    {
                        Log($"[WritePlane] level={levelIndex} srcLevel={sourceLevelIndex} t={t} c={c} z={z} srcC={srcC} tile={tileX},{tileY} size={actualW}x{actualH} " +
                            $"rawLen={pixelBytes?.Length ?? 0} expSingle={singleChannelBytes} expInterleaved={interleavedBytes} " +
                            $"needsDeinterleave={needsDeinterleave} srcBands={srcChannelCount} bytesPerSample={bytesPerSample} " +
                            $"rawSample={SampleBytes(pixelBytes)}");
                    }

                    if (needsDeinterleave)
                    {
                        int inferredChannelCount = srcChannelCount;
                        if (pixelCount > 0 && pixelBytes.Length > 0)
                        {
                            int inferred = pixelBytes.Length / (pixelCount * bytesPerSample);
                            if (inferred >= 1 && inferred <= 4)
                                inferredChannelCount = inferred;
                        }

                        // Ensure the buffer is the full interleaved size before
                        // extracting a single channel from it.
                        int expectedInterleavedBytes = pixelCount * inferredChannelCount * bytesPerSample;
                        if (pixelBytes.Length != expectedInterleavedBytes)
                        {
                            var trimmed = new byte[expectedInterleavedBytes];
                            Buffer.BlockCopy(pixelBytes, 0, trimmed, 0,
                                Math.Min(pixelBytes.Length, expectedInterleavedBytes));
                            pixelBytes = trimmed;
                        }

                        // For BGRA buffers the byte order is [B, G, R, A].
                        // OpenSlide SVS tiles commonly arrive as BGRA even when the
                        // nominal source pixel format is reported as 24bpp RGB.
                        bool actualIsBGRA = isBGRA || inferredChannelCount == 4 || IsBGRAFormat(tilePixelFormat);
                        int srcByteIndex = actualIsBGRA ? (2 - rgbChannelIndex) : rgbChannelIndex;

                        pixelBytes = DeinterleaveChannel(
                            pixelBytes, srcByteIndex, inferredChannelCount,
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
                        Log($"[WritePlane] wrote level={levelIndex} srcLevel={sourceLevelIndex} t={t} c={c} z={z} tile={tileX},{tileY}");
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
            return format == AForge.PixelFormat.Format24bppRgb ||
                   format == AForge.PixelFormat.Format32bppArgb ||
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
                var sourcePixelFormat = DetermineSourcePixelFormat(image);
                bool sourceIsInterleavedRgb = IsInterleavedRgbFormat(sourcePixelFormat);
                int sourceChannelCount = sourceIsInterleavedRgb ? BioImage.GetBands(sourcePixelFormat) : 1;
                int exportChannelCount = sourceIsInterleavedRgb && image.SizeC == 1
                    ? Math.Min(3, sourceChannelCount)
                    : image.SizeC;

                WriteText(Path.Combine(outputDir, ".zgroup"), JsonSerializer.Serialize(new { zarr_format = 2 }, new JsonSerializerOptions { WriteIndented = true }));

                var exportLevels = BuildExportLevels(image);
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
                        datasets = BuildV2Datasets(image, exportLevels)
                    }
                };

                var omeroNode = ZarrMetadataFixup.CreateRgbDisplayMetadata(image);
                WriteText(Path.Combine(outputDir, ".zattrs"), JsonSerializer.Serialize(new { multiscales, omero = omeroNode }, new JsonSerializerOptions { WriteIndented = true }));

                for (int i = 0; i < exportLevels.Count; i++)
                {
                    var exportLevel = exportLevels[i];
                    var dtype = MapV2Dtype(sourcePixelFormat);
                    var chunks = new[]
                    {
                        1,
                        1,
                        Math.Max(1, image.SizeZ),
                        Math.Min(512, exportLevel.SizeY),
                        Math.Min(512, exportLevel.SizeX)
                    };

                    var arrayDoc = new
                    {
                        zarr_format = 2,
                        shape = new long[] { image.SizeT, exportChannelCount, image.SizeZ, exportLevel.SizeY, exportLevel.SizeX },
                        chunks,
                        dtype,
                        compressor = (object?)null,
                        fill_value = 0,
                        order = "C",
                        filters = (object?)null,
                        dimension_separator = "/"
                    };

                    var levelDir = Path.Combine(outputDir, i.ToString());
                    WriteText(Path.Combine(levelDir, ".zarray"), JsonSerializer.Serialize(arrayDoc, new JsonSerializerOptions { WriteIndented = true }));
                    WriteText(Path.Combine(levelDir, ".zattrs"), JsonSerializer.Serialize(new { omero = omeroNode }, new JsonSerializerOptions { WriteIndented = true }));
                }

                WriteV2Chunks(outputDir, image, exportLevels, sourcePixelFormat, sourceIsInterleavedRgb, sourceChannelCount, exportChannelCount);
                ZarrMetadataFixup.AddRgbDisplayMetadata(outputDir, image);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Save Zarr v2 compatibility failed: {ex.Message}");
                try
                {
                    File.WriteAllText(Path.Combine(outputDir, "save-v2-error.txt"), ex.ToString());
                }
                catch
                {
                }
            }
        }

        private static void SaveAssociatedResolutionImages(
            BioImage image,
            string outputDir,
            string dataType,
            BioImageDescriptor baseDescriptor,
            int bytesPerSample,
            int tileW,
            int tileH,
            AForge.PixelFormat sourcePixelFormat,
            bool sourceIsInterleavedRgb,
            int sourceChannelCount,
            int exportChannelCount,
            bool sourceIsBGRA)
        {
            var associatedLevels = BuildAssociatedLevelInfos(image);
            if (associatedLevels.Count == 0)
                return;

            var manifest = new List<object>();
            foreach (var associated in associatedLevels)
            {
                try
                {
                    string associatedDir = Path.Combine(outputDir, associated.Name);
                    var associatedDescriptor = new BioImageDescriptor(associated.SizeX, associated.SizeY, new ZarrNET.Core.ZCT(image.SizeZ, exportChannelCount, image.SizeT))
                    {
                        Name          = $"{Path.GetFileNameWithoutExtension(image.Filename)}_{associated.Name}",
                        DataType      = dataType,
                        PhysicalSizeX = baseDescriptor.PhysicalSizeX,
                        PhysicalSizeY = baseDescriptor.PhysicalSizeY,
                        PhysicalSizeZ = baseDescriptor.PhysicalSizeZ,
                        ChunkY        = tileH,
                        ChunkX        = tileW,
                    };

                    var associatedWriter = OmeZarrWriter.CreateAsync(
                        associatedDir,
                        associatedDescriptor,
                        new[]
                        {
                            new ResolutionLevelDescriptor(associated.SizeX, associated.SizeY, 1.0)
                        }).Result;

                    try
                    {
                        for (int t = 0; t < image.SizeT; t++)
                        {
                            for (int z = 0; z < image.SizeZ; z++)
                            {
                                for (int c = 0; c < exportChannelCount; c++)
                                {
                                    if (sourceIsInterleavedRgb)
                                    {
                                        WritePlaneInTiles(
                                            associatedWriter, image, new ResolutionLevelDescriptor(associated.SizeX, associated.SizeY, 1.0),
                                            t, c, z,
                                        bytesPerSample, tileW, tileH,
                                        sourceLevelIndex: associated.SourceIndex,
                                        scaleX: 1.0,
                                        scaleY: 1.0,
                                        levelIndex: 0,
                                        rgbChannelIndex: c,
                                        srcChannelCount: sourceChannelCount,
                                            isBGRA: sourceIsBGRA,
                                            logicalC: 0).GetAwaiter().GetResult();
                                    }
                                    else
                                    {
                                        WritePlaneInTiles(
                                            associatedWriter, image, new ResolutionLevelDescriptor(associated.SizeX, associated.SizeY, 1.0),
                                            t, c, z,
                                        bytesPerSample, tileW, tileH,
                                        sourceLevelIndex: associated.SourceIndex,
                                        scaleX: 1.0,
                                        scaleY: 1.0,
                                        levelIndex: 0).GetAwaiter().GetResult();
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        associatedWriter.DisposeAsync().GetAwaiter().GetResult();
                    }

                    ZarrMetadataFixup.AddRgbDisplayMetadata(associatedDir, image);
                    manifest.Add(new
                    {
                        name = associated.Name,
                        path = associated.Name,
                        sourceIndex = associated.SourceIndex,
                        sizeX = associated.SizeX,
                        sizeY = associated.SizeY
                    });
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Save associated Zarr '{associated.Name}' failed: {ex.Message}");
                    try
                    {
                        File.WriteAllText(Path.Combine(outputDir, $"save-associated-{associated.Name}-error.txt"), ex.ToString());
                    }
                    catch
                    {
                    }
                }
            }

            if (manifest.Count > 0)
            {
                WriteText(
                    Path.Combine(outputDir, "associated-images.json"),
                    JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        private static List<AssociatedLevelInfo> BuildAssociatedLevelInfos(BioImage image)
        {
            var levels = new List<AssociatedLevelInfo>();
            if (image == null || image.Resolutions == null || image.Resolutions.Count == 0)
                return levels;

            void AddLevel(string name, int? index)
            {
                if (!index.HasValue)
                    return;

                int sourceIndex = index.Value;
                if (sourceIndex < 0 || sourceIndex >= image.Resolutions.Count)
                    return;

                if (levels.Any(l => l.SourceIndex == sourceIndex))
                    return;

                var res = image.Resolutions[sourceIndex];
                if (res.SizeX <= 0 || res.SizeY <= 0)
                    return;

                levels.Add(new AssociatedLevelInfo(name, sourceIndex, res.SizeX, res.SizeY));
            }

            AddLevel("macro", image.MacroResolution);
            AddLevel("label", image.LabelResolution);
            return levels;
        }

        private static void WriteV2Chunks(
            string outputDir,
            BioImage image,
            List<ExportLevelInfo> exportLevels,
            AForge.PixelFormat sourcePixelFormat,
            bool sourceIsInterleavedRgb,
            int sourceChannelCount,
            int exportChannelCount)
        {
            for (int exportLevelIndex = 0; exportLevelIndex < exportLevels.Count; exportLevelIndex++)
            {
                var exportLevel = exportLevels[exportLevelIndex];
                int bytesPerSample = Math.Max(1, DetermineSourceBitsPerSample(image) / 8);

                for (int t = 0; t < image.SizeT; t++)
                {
                    for (int z = 0; z < image.SizeZ; z++)
                    {
                        for (int tileY = 0; tileY < exportLevel.SizeY; tileY += 512)
                        {
                            for (int tileX = 0; tileX < exportLevel.SizeX; tileX += 512)
                            {
                                int actualW = Math.Min(512, exportLevel.SizeX - tileX);
                                int actualH = Math.Min(512, exportLevel.SizeY - tileY);
                                if (actualW <= 0 || actualH <= 0)
                                    continue;

                                for (int c = 0; c < exportChannelCount; c++)
                                {
                                    byte[] planeBytes = GetV2PlaneBytes(
                                        image, exportLevel.SourceIndex, t, z, c,
                                        tileX, tileY, actualW, actualH,
                                        exportLevel.ScaleX, exportLevel.ScaleY,
                                        sourceIsInterleavedRgb, sourceChannelCount, bytesPerSample, sourcePixelFormat).GetAwaiter().GetResult();

                                    byte[] chunkBytes = BuildV2ChunkBytes(
                                        planeBytes,
                                        actualW,
                                        actualH,
                                        bytesPerSample,
                                        512,
                                        512);

                                    string chunkPath = Path.Combine(
                                        outputDir,
                                        exportLevelIndex.ToString(),
                                        t.ToString(),
                                        c.ToString(),
                                        z.ToString(),
                                        (tileY / 512).ToString(),
                                        (tileX / 512).ToString());
                                    string? chunkDir = Path.GetDirectoryName(chunkPath);
                                    if (!string.IsNullOrWhiteSpace(chunkDir))
                                        Directory.CreateDirectory(chunkDir);

                                    File.WriteAllBytes(chunkPath, chunkBytes);
                                }
                            }
                        }
                    }
                }
            }
        }

        private static async Task<byte[]> GetV2PlaneBytes(
            BioImage image,
            int levelIndex,
            int t,
            int z,
            int c,
            int tileX,
            int tileY,
            int actualW,
            int actualH,
            double scaleX,
            double scaleY,
            bool sourceIsInterleavedRgb,
            int sourceChannelCount,
            int bytesPerSample,
            AForge.PixelFormat sourcePixelFormat)
        {
            if (!sourceIsInterleavedRgb)
            {
                bool useDirectLevel0RawRead = levelIndex <= 0 ||
                    (Math.Abs(scaleX - 1.0) < 0.0001 && Math.Abs(scaleY - 1.0) < 0.0001);
                if (useDirectLevel0RawRead)
                {
                    return await image.GetTileBytesRaw(
                        image.GetFrameIndex(z, c, t),
                        levelIndex,
                        tileX,
                        tileY,
                        actualW,
                        actualH,
                        new AForge.ZCT(z, c, t)).ConfigureAwait(false);
                }

                int srcX = Math.Max(0, (int)Math.Round(tileX * scaleX));
                int srcY = Math.Max(0, (int)Math.Round(tileY * scaleY));
                int srcW = Math.Max(1, (int)Math.Round(actualW * scaleX));
                int srcH = Math.Max(1, (int)Math.Round(actualH * scaleY));
                return await image.GetTileBytesRaw(
                    image.GetFrameIndex(z, c, t),
                    0,
                    srcX,
                    srcY,
                    srcW,
                    srcH,
                    new AForge.ZCT(z, c, t)).ConfigureAwait(false);
            }

            int frameIndex = image.GetFrameIndex(z, 0, t);
            byte[]? pixelBytes = null;
            AForge.PixelFormat tilePixelFormat = sourcePixelFormat;
            bool useDirectLevel0RgbRead = levelIndex <= 0 ||
                (Math.Abs(scaleX - 1.0) < 0.0001 && Math.Abs(scaleY - 1.0) < 0.0001);
            if (useDirectLevel0RgbRead)
            {
                using Bitmap tileBitmap = await image.GetTile(
                    frameIndex,
                    levelIndex,
                    tileX,
                    tileY,
                    actualW,
                    actualH,
                    new AForge.ZCT(z, 0, t),
                    true).ConfigureAwait(false);
                tilePixelFormat = tileBitmap.PixelFormat;
                pixelBytes = tileBitmap.Bytes;
            }
            else
            {
                int srcX = Math.Max(0, (int)Math.Round(tileX * scaleX));
                int srcY = Math.Max(0, (int)Math.Round(tileY * scaleY));
                int srcW = Math.Max(1, (int)Math.Round(actualW * scaleX));
                int srcH = Math.Max(1, (int)Math.Round(actualH * scaleY));
                using Bitmap tileBitmap = await image.GetTile(
                    frameIndex,
                    0,
                    srcX,
                    srcY,
                    srcW,
                    srcH,
                    new AForge.ZCT(z, 0, t),
                    true).ConfigureAwait(false);
                tilePixelFormat = tileBitmap.PixelFormat;
                pixelBytes = DownsampleBgraNearest(tileBitmap.Bytes, srcW, srcH, actualW, actualH);
            }

            pixelBytes ??= Array.Empty<byte>();

            int pixelCount = actualW * actualH;
            if (pixelBytes == null || pixelBytes.Length == 0)
                return new byte[pixelCount * bytesPerSample];

            int singleChannelBytes = pixelCount * bytesPerSample;
            if (pixelBytes.Length == singleChannelBytes)
                return pixelBytes;

            int inferredChannelCount = sourceChannelCount;
            if (pixelCount > 0 && pixelBytes.Length > 0)
            {
                int inferred = pixelBytes.Length / (pixelCount * bytesPerSample);
                if (inferred >= 1 && inferred <= 4)
                    inferredChannelCount = inferred;
            }

            int expectedInterleavedBytes = pixelCount * inferredChannelCount * bytesPerSample;
            if (pixelBytes.Length != expectedInterleavedBytes)
            {
                var trimmed = new byte[expectedInterleavedBytes];
                Buffer.BlockCopy(pixelBytes, 0, trimmed, 0, Math.Min(pixelBytes.Length, expectedInterleavedBytes));
                pixelBytes = trimmed;
            }

            bool actualIsBGRA = IsBGRAFormat(sourcePixelFormat) || inferredChannelCount == 4 || IsBGRAFormat(tilePixelFormat);
            int srcByteIndex = actualIsBGRA ? (2 - c) : c;
            return DeinterleaveChannel(pixelBytes, srcByteIndex, inferredChannelCount, bytesPerSample, pixelCount);
        }

        private static byte[] DownsampleBgraNearest(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight)
        {
            if (targetWidth <= 0 || targetHeight <= 0)
                return Array.Empty<byte>();

            int targetBytes = targetWidth * targetHeight * 4;
            if (source == null || source.Length == 0 || sourceWidth <= 0 || sourceHeight <= 0)
                return new byte[targetBytes];

            if (sourceWidth == targetWidth && sourceHeight == targetHeight)
            {
                if (source.Length == targetBytes)
                    return source;

                var exact = new byte[targetBytes];
                Buffer.BlockCopy(source, 0, exact, 0, Math.Min(source.Length, targetBytes));
                return exact;
            }

            var dst = new byte[targetBytes];
            double scaleX = (double)sourceWidth / targetWidth;
            double scaleY = (double)sourceHeight / targetHeight;

            for (int y = 0; y < targetHeight; y++)
            {
                int srcY = Math.Min(sourceHeight - 1, (int)Math.Floor(y * scaleY));
                int srcRow = srcY * sourceWidth * 4;
                int dstRow = y * targetWidth * 4;
                for (int x = 0; x < targetWidth; x++)
                {
                    int srcX = Math.Min(sourceWidth - 1, (int)Math.Floor(x * scaleX));
                    int srcOff = srcRow + srcX * 4;
                    int dstOff = dstRow + x * 4;
                    if (srcOff + 3 < source.Length)
                    {
                        dst[dstOff + 0] = source[srcOff + 0];
                        dst[dstOff + 1] = source[srcOff + 1];
                        dst[dstOff + 2] = source[srcOff + 2];
                        dst[dstOff + 3] = source[srcOff + 3];
                    }
                }
            }

            return dst;
        }

        private static byte[] DownsampleRawNearest(byte[] source, int sourceWidth, int sourceHeight, int targetWidth, int targetHeight, int bytesPerSample)
        {
            if (targetWidth <= 0 || targetHeight <= 0 || bytesPerSample <= 0)
                return Array.Empty<byte>();

            int targetBytes = targetWidth * targetHeight * bytesPerSample;
            if (source == null || source.Length == 0 || sourceWidth <= 0 || sourceHeight <= 0)
                return new byte[targetBytes];

            if (sourceWidth == targetWidth && sourceHeight == targetHeight)
            {
                if (source.Length == targetBytes)
                    return source;

                var exact = new byte[targetBytes];
                Buffer.BlockCopy(source, 0, exact, 0, Math.Min(source.Length, targetBytes));
                return exact;
            }

            var dst = new byte[targetBytes];
            double scaleX = (double)sourceWidth / targetWidth;
            double scaleY = (double)sourceHeight / targetHeight;

            for (int y = 0; y < targetHeight; y++)
            {
                int srcY = Math.Min(sourceHeight - 1, (int)Math.Floor(y * scaleY));
                int srcRow = srcY * sourceWidth * bytesPerSample;
                int dstRow = y * targetWidth * bytesPerSample;
                for (int x = 0; x < targetWidth; x++)
                {
                    int srcX = Math.Min(sourceWidth - 1, (int)Math.Floor(x * scaleX));
                    int srcOff = srcRow + srcX * bytesPerSample;
                    int dstOff = dstRow + x * bytesPerSample;
                    if (srcOff + bytesPerSample <= source.Length)
                        Buffer.BlockCopy(source, srcOff, dst, dstOff, bytesPerSample);
                }
            }

            return dst;
        }

        private static Task<byte[]?> TryGetRawTileFromBufferAsync(
            BioImage image,
            int index,
            int tileX,
            int tileY,
            int actualW,
            int actualH)
        {
            try
            {
                var method = typeof(BioImage).GetMethod(
                    "TryGetRawTileFromBuffer",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);

                if (method == null)
                    return Task.FromResult<byte[]?>(null);

                var result = method.Invoke(image, new object[] { index, tileX, tileY, actualW, actualH });
                return Task.FromResult(result as byte[]);
            }
            catch
            {
                return Task.FromResult<byte[]?>(null);
            }
        }

        private static byte[] BuildV2ChunkBytes(
            byte[] planeBytes,
            int actualW,
            int actualH,
            int bytesPerSample,
            int chunkW,
            int chunkH)
        {
            if (planeBytes == null)
                return Array.Empty<byte>();

            int chunkPixels = Math.Max(0, chunkW) * Math.Max(0, chunkH);
            var output = new byte[chunkPixels * bytesPerSample];

            int actualRowBytes = Math.Max(0, actualW) * bytesPerSample;
            int paddedRowBytes = Math.Max(0, chunkW) * bytesPerSample;
            int copyHeight = Math.Min(actualH, chunkH);
            int copyWidth = Math.Min(actualW, chunkW);

            for (int row = 0; row < copyHeight; row++)
            {
                int srcOffset = row * actualRowBytes;
                int dstOffset = row * paddedRowBytes;
                int copyBytes = Math.Min(copyWidth * bytesPerSample, Math.Max(0, planeBytes.Length - srcOffset));
                if (copyBytes > 0)
                    Buffer.BlockCopy(planeBytes, srcOffset, output, dstOffset, copyBytes);
            }

            return output;
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

            int baseWidth = image.Resolutions.Count > 0 ? image.Resolutions[0].SizeX : image.SizeX;
            int baseHeight = image.Resolutions.Count > 0 ? image.Resolutions[0].SizeY : image.SizeY;
            if (baseWidth <= 0 || baseHeight <= 0)
                return;

            int sizeT = Math.Max(1, image.SizeT);
            int sizeC = Math.Max(1, image.SizeC);
            int sizeZ = Math.Max(1, image.SizeZ);
            var labelColors = new Dictionary<int, AForge.Color>();
            var roiEntries = new List<(ROI Roi, int LabelId)>();

            foreach (var roi in labelRois)
            {
                int labelId = ParseLabelId(roi.Text, roiEntries.Count + 1);
                if (labelId <= 0)
                    continue;

                roiEntries.Add((roi, labelId));

                if (!labelColors.ContainsKey(labelId))
                {
                    var col = roi.fillColor;
                    if (col.A == 0)
                        col = roi.strokeColor;
                    if (col.A == 0)
                        col = GetFallbackLabelColor(labelId);
                    labelColors[labelId] = col;
                }
            }

            if (labelColors.Count == 0)
                return;

            var exportLevels = BuildExportLevels(image);
            if (baseWidth <= 0 || baseHeight <= 0)
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
                    attributes   = new
                    {
                        ome = new
                        {
                            version = "0.5",
                            labels = new[] { "0" }
                        }
                    }
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

            var labelAxes = new object[]
            {
                new { name = "t", type = "time" },
                new { name = "c", type = "channel" },
                new { name = "z", type = "space", unit = "micrometer" },
                new { name = "y", type = "space", unit = "micrometer" },
                new { name = "x", type = "space", unit = "micrometer" }
            };

            var labelImageGroupDoc = new Dictionary<string, object?>
            {
                ["version"] = "0.5",
                ["multiscales"] = new[]
                {
                    new
                    {
                        version = "0.5",
                        name = "0",
                        axes = labelAxes,
                        datasets = BuildV3Datasets(exportLevels)
                    }
                },
                ["image-label"] = new Dictionary<string, object?>
                {
                    ["version"] = "0.5",
                    ["source"] = new Dictionary<string, object?>
                    {
                        ["image"] = "../../"
                    },
                    ["colors"] = colors,
                    ["properties"] = Array.Empty<object>()
                }
            };

            await WriteJsonAsync(store, "labels/0/zarr.json", new
            {
                zarr_format = 3,
                node_type   = "group",
                attributes  = new
                {
                    ome = labelImageGroupDoc
                }
            }, ct).ConfigureAwait(false);

            var labelGroup = await rootGroup.OpenGroupAsync("labels", ct).ConfigureAwait(false);
            var labelImageGroup = await labelGroup.OpenGroupAsync("0", ct).ConfigureAwait(false);

            for (int levelIndex = 0; levelIndex < exportLevels.Count; levelIndex++)
            {
                var exportLevel = exportLevels[levelIndex];
                int levelWidth = exportLevel.SizeX;
                int levelHeight = exportLevel.SizeY;
                double scaleX = baseWidth > 0 ? levelWidth / (double)baseWidth : 1.0;
                double scaleY = baseHeight > 0 ? levelHeight / (double)baseHeight : 1.0;

                int planePixels = levelWidth * levelHeight;
                int planeStride = planePixels;
                int zStride = planeStride;
                int cStride = zStride * sizeZ;
                int tStride = cStride * sizeC;

                var labelVolume = new short[planePixels * sizeZ * sizeC * sizeT];
                int probeCount = 0;

                foreach (var entry in roiEntries)
                {
                    var roi = entry.Roi;
                    int labelId = entry.LabelId;

                    int z = Math.Clamp(roi.coord.Z, 0, sizeZ - 1);
                    int c = Math.Clamp(roi.coord.C, 0, sizeC - 1);
                    int t = Math.Clamp(roi.coord.T, 0, sizeT - 1);
                    int offset = (t * tStride) + (c * cStride) + (z * zStride);

                    if (probeCount < 2)
                    {
                        try
                        {
                            int firstDstX = -1;
                            int firstDstY = -1;
                            if (TryGetRoiProbePoint(roi, scaleX, scaleY, out firstDstX, out firstDstY))
                            {
                                AppLog.Append("[SaveLabelOverlaysAsync.LevelProbe] level=" + levelIndex +
                                    " roi=" + roi.roiID +
                                    " dst=" + firstDstX + "," + firstDstY +
                                    " scale=" + scaleX + "," + scaleY);
                            }
                        }
                        catch { }
                        probeCount++;
                    }

                    RasterizeRoi(labelVolume, levelWidth, levelHeight, roi, labelId, offset, scaleX, scaleY);
                }

                var levelArrayDoc = new
                {
                    zarr_format = 3,
                    node_type = "array",
                    shape = new long[] { sizeT, sizeC, sizeZ, levelHeight, levelWidth },
                    data_type = "int16",
                    chunk_grid = new
                    {
                        name = "regular",
                        configuration = new
                        {
                            chunk_shape = new[]
                            {
                                1,
                                1,
                                1,
                                Math.Min(512, levelHeight),
                                Math.Min(512, levelWidth)
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
                    }
                };

                await WriteJsonAsync(store, $"labels/0/{levelIndex}/zarr.json", levelArrayDoc, ct).ConfigureAwait(false);

                var labelArray = await labelImageGroup.OpenArrayAsync(levelIndex.ToString(), ct).ConfigureAwait(false);
                var labelBytes = new byte[labelVolume.Length * sizeof(short)];
                Buffer.BlockCopy(labelVolume, 0, labelBytes, 0, labelBytes.Length);

                await labelArray.WriteRegionAsync(
                    new long[] { 0, 0, 0, 0, 0 },
                    new long[] { sizeT, sizeC, sizeZ, levelHeight, levelWidth },
                    labelBytes,
                    ct).ConfigureAwait(false);
            }

            WriteText(Path.Combine(outputDir, "labels", ".zgroup"), JsonSerializer.Serialize(new { zarr_format = 2 }, new JsonSerializerOptions { WriteIndented = true }));
            WriteText(Path.Combine(outputDir, "labels", ".zattrs"), JsonSerializer.Serialize(new { labels = new[] { "0" } }, new JsonSerializerOptions { WriteIndented = true }));

            WriteText(Path.Combine(outputDir, "labels", "0", ".zgroup"), JsonSerializer.Serialize(new { zarr_format = 2 }, new JsonSerializerOptions { WriteIndented = true }));

            var labelV2Array = new
            {
                zarr_format = 2,
                shape = new long[] { sizeT, sizeC, sizeZ, baseHeight, baseWidth },
                chunks = new[] { 1, 1, 1, Math.Min(512, baseHeight), Math.Min(512, baseWidth) },
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
                        double stageX = image.Volume.Location.X;
                        double stageY = image.Volume.Location.Y;
                        if (TryGetMultiscaleLabelTransform(multiscale, axes, out double labelPhysX, out double labelPhysY, out double labelStageX, out double labelStageY))
                        {
                            physX = labelPhysX;
                            physY = labelPhysY;
                            stageX = labelStageX;
                            stageY = labelStageY;
                        }

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

                                        var regionData = await level.ReadTileAsync(
                                            0,
                                            0,
                                            sizeX,
                                            sizeY,
                                            z: z,
                                            c: sourceC,
                                            t: t).ConfigureAwait(false);
                                        if (regionData == null || regionData.Data == null || regionData.Data.Length == 0)
                                            continue;

                                        GetRegionDimensions(regionData, sizeX, sizeY, out int planeW, out int planeH);
                                        if (planeW <= 0 || planeH <= 0)
                                            continue;

                                        int totalPixels = planeW * planeH;
                                        if (totalPixels <= 0)
                                            continue;

                                        int bytesPerPixel = Math.Max(1, regionData.Data.Length / totalPixels);
                                        var knownLabelValues = GetKnownLabelValues(labelNode.ImageLabelMetadata);
                                        var labelMasks = SplitLabelPlane(regionData.Data, planeW, planeH, bytesPerPixel, knownLabelValues);
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
                                            var color = PickLabelColor(labelNode.ImageLabelMetadata, labelId);

                                            var mask = new ROI.Mask(
                                            maskData,
                                            planeW,
                                            planeH,
                                            physX,
                                            physY,
                                            stageX,
                                            stageY,
                                            preserveDimensions: false);
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

        private static void GetRegionDimensions(RegionResult region, int fallbackWidth, int fallbackHeight, out int width, out int height)
        {
            width = Math.Max(1, fallbackWidth);
            height = Math.Max(1, fallbackHeight);

            if (region?.Shape == null || region.Shape.Length == 0)
                return;

            if (region.Axes != null && region.Axes.Length == region.Shape.Length)
            {
                for (int i = 0; i < region.Axes.Length; i++)
                {
                    string axisName = region.Axes[i].Name?.ToLowerInvariant() ?? string.Empty;
                    if (axisName == "x")
                        width = (int)Math.Max(1, region.Shape[i]);
                    else if (axisName == "y")
                        height = (int)Math.Max(1, region.Shape[i]);
                }
                return;
            }

            if (region.Shape.Length >= 2)
            {
                width = (int)Math.Max(1, region.Shape[^1]);
                height = (int)Math.Max(1, region.Shape[^2]);
            }
        }

        private static HashSet<int>? GetKnownLabelValues(ImageLabelMetadata metadata)
        {
            if (metadata?.Colors == null)
                return null;

            HashSet<int> values = new();
            foreach (var entry in metadata.Colors)
            {
                if (entry.LabelValue > 0)
                    values.Add(entry.LabelValue);
            }

            return values.Count > 0 ? values : null;
        }

        private static Dictionary<int, byte[]> SplitLabelPlane(byte[] raw, int w, int h, int bytesPerPixel, ISet<int>? knownLabelValues = null)
        {
            int total = w * h;
            var result = new Dictionary<int, byte[]>();

            for (int i = 0; i < total; i++)
            {
                int labelId = ReadLabelId(raw, i, bytesPerPixel);
                if (labelId == 0)
                    continue;
                if (knownLabelValues != null && !knownLabelValues.Contains(labelId))
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

        private static object[] BuildV2Datasets(BioImage image, List<ExportLevelInfo> exportLevels)
        {
            var datasets = new List<object>();
            for (int i = 0; i < exportLevels.Count; i++)
            {
                var exportLevel = exportLevels[i];
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
                                exportLevel.PhysicalSizeZ > 0 ? exportLevel.PhysicalSizeZ : 1.0,
                                exportLevel.PhysicalSizeY > 0 ? exportLevel.PhysicalSizeY : 1.0,
                                exportLevel.PhysicalSizeX > 0 ? exportLevel.PhysicalSizeX : 1.0
                            }
                        }
                    }
                });
            }

            return datasets.ToArray();
        }

        private static void WriteRootMetadata(string outputDir, BioImage image, List<ExportLevelInfo> exportLevels)
        {
            var omeroNode = ZarrMetadataFixup.CreateRgbDisplayMetadata(image);
            var multiscales = new[]
            {
                new
                {
                    version = "0.5",
                    name = Path.GetFileNameWithoutExtension(image.Filename),
                    axes = new object[]
                    {
                        new { name = "t", type = "time" },
                        new { name = "c", type = "channel" },
                        new { name = "z", type = "space", unit = "micrometer" },
                        new { name = "y", type = "space", unit = "micrometer" },
                        new { name = "x", type = "space", unit = "micrometer" }
                    },
                    datasets = BuildV3Datasets(exportLevels)
                }
            };

            var rootDoc = new
            {
                zarr_format = 3,
                node_type = "group",
                attributes = new
                {
                    ome = new
                    {
                        version = "0.5",
                        multiscales,
                        omero = omeroNode
                    }
                }
            };

            WriteText(
                Path.Combine(outputDir, "zarr.json"),
                JsonSerializer.Serialize(rootDoc, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static object[] BuildV3Datasets(List<ExportLevelInfo> exportLevels)
        {
            var datasets = new List<object>();
            for (int i = 0; i < exportLevels.Count; i++)
            {
                var exportLevel = exportLevels[i];
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
                                exportLevel.PhysicalSizeZ > 0 ? exportLevel.PhysicalSizeZ : 1.0,
                                exportLevel.PhysicalSizeY > 0 ? exportLevel.PhysicalSizeY : 1.0,
                                exportLevel.PhysicalSizeX > 0 ? exportLevel.PhysicalSizeX : 1.0
                            }
                        }
                    }
                });
            }

            return datasets.ToArray();
        }

        private static List<ExportLevelInfo> BuildExportLevels(BioImage b)
        {
            var exportLevels = new List<ExportLevelInfo>();
            if (b == null)
                return exportLevels;

            if (b.Resolutions.Count == 0)
            {
                exportLevels.Add(new ExportLevelInfo(
                    0,
                    b.SizeX,
                    b.SizeY,
                    new ResolutionLevelDescriptor(b.SizeX, b.SizeY, 1.0),
                    1.0,
                    1.0,
                    1.0,
                    NormalizePhysicalSize(b.PhysicalSizeX),
                    NormalizePhysicalSize(b.PhysicalSizeY),
                    NormalizePhysicalSize(b.PhysicalSizeZ)));
                return exportLevels;
            }

            int lastSizeX = int.MaxValue;
            int lastSizeY = int.MaxValue;
            bool haveLast = false;

            for (int sourceIndex = 0; sourceIndex < b.Resolutions.Count; sourceIndex++)
            {
                var res = b.Resolutions[sourceIndex];
                if (res.SizeX <= 0 || res.SizeY <= 0)
                    continue;

                if (haveLast && (res.SizeX > lastSizeX || res.SizeY > lastSizeY))
                    break;

                if (haveLast && res.SizeX == lastSizeX && res.SizeY == lastSizeY)
                    continue;

                double downsample = b.GetLevelDownsample(sourceIndex);
                if (!double.IsFinite(downsample) || downsample <= 0)
                    downsample = 1.0;

                double basePhysicalSizeX = exportLevels.Count > 0
                    ? exportLevels[0].PhysicalSizeX
                    : NormalizePhysicalSize(b.PhysicalSizeX);
                double basePhysicalSizeY = exportLevels.Count > 0
                    ? exportLevels[0].PhysicalSizeY
                    : NormalizePhysicalSize(b.PhysicalSizeY);
                double physicalSizeX = NormalizePhysicalSize(basePhysicalSizeX * downsample);
                double physicalSizeY = NormalizePhysicalSize(basePhysicalSizeY * downsample);

                exportLevels.Add(new ExportLevelInfo(
                    sourceIndex,
                    res.SizeX,
                    res.SizeY,
                    new ResolutionLevelDescriptor(res.SizeX, res.SizeY, downsample),
                    downsample,
                    downsample,
                    downsample,
                    physicalSizeX,
                    physicalSizeY,
                    NormalizePhysicalSize(res.PhysicalSizeZ)));

                lastSizeX = res.SizeX;
                lastSizeY = res.SizeY;
                haveLast = true;
            }

            if (exportLevels.Count == 0 && b.SizeX > 0 && b.SizeY > 0)
            {
                exportLevels.Add(new ExportLevelInfo(
                    0,
                    b.SizeX,
                    b.SizeY,
                    new ResolutionLevelDescriptor(b.SizeX, b.SizeY, 1.0),
                    1.0,
                    1.0,
                    1.0,
                    NormalizePhysicalSize(b.PhysicalSizeX),
                    NormalizePhysicalSize(b.PhysicalSizeY),
                    NormalizePhysicalSize(b.PhysicalSizeZ)));
            }

            return exportLevels;
        }

        private static bool IsAssociatedResolution(BioImage image, int sourceIndex)
        {
            if (image == null)
                return false;

            return (image.MacroResolution.HasValue && image.MacroResolution.Value == sourceIndex) ||
                   (image.LabelResolution.HasValue && image.LabelResolution.Value == sourceIndex);
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

        private static Color PickLabelColor(ImageLabelMetadata metadata, int labelValue)
        {
            if (metadata?.Colors != null)
            {
                foreach (var entry in metadata.Colors)
                {
                    if (entry.LabelValue != labelValue)
                        continue;

                    var rgba = entry.Rgba;
                    if (rgba != null && rgba.Length >= 4 &&
                        (rgba[0] != 0 || rgba[1] != 0 || rgba[2] != 0 || rgba[3] != 0))
                    {
                        return Color.FromArgb(rgba[3], rgba[0], rgba[1], rgba[2]);
                    }
                }
            }

            return GetFallbackLabelColor(labelValue);
        }

        private static bool TryGetMultiscaleLabelTransform(object? multiscale, AxisMetadata[] axes, out double physX, out double physY, out double stageX, out double stageY)
        {
            physX = 1.0;
            physY = 1.0;
            stageX = 0.0;
            stageY = 0.0;

            if (multiscale == null || axes == null || axes.Length == 0)
                return false;

            object? dataset = GetEnumerableFirst(GetPropertyValue(multiscale, "Datasets"));
            if (dataset == null)
                return false;

            object? transforms = GetPropertyValue(dataset, "CoordinateTransformations");
            if (transforms == null)
                return false;

            int xIndex = Array.FindIndex(axes, a => string.Equals(a.Name, "x", StringComparison.OrdinalIgnoreCase));
            int yIndex = Array.FindIndex(axes, a => string.Equals(a.Name, "y", StringComparison.OrdinalIgnoreCase));
            if (xIndex < 0 || yIndex < 0)
                return false;

            double[] origin = ApplyCoordinateTransforms(transforms, axes.Length, xIndex, yIndex, 0.0, 0.0);
            double[] xPoint = ApplyCoordinateTransforms(transforms, axes.Length, xIndex, yIndex, 1.0, 0.0);
            double[] yPoint = ApplyCoordinateTransforms(transforms, axes.Length, xIndex, yIndex, 0.0, 1.0);

            if (origin.Length <= Math.Max(xIndex, yIndex) ||
                xPoint.Length <= xIndex ||
                yPoint.Length <= yIndex)
                return false;

            stageX = origin[xIndex];
            stageY = origin[yIndex];
            physX = xPoint[xIndex] - origin[xIndex];
            physY = yPoint[yIndex] - origin[yIndex];

            if (!double.IsFinite(physX) || physX <= 0)
                physX = 1.0;
            if (!double.IsFinite(physY) || physY <= 0)
                physY = 1.0;

            return true;
        }

        private static object? GetEnumerableFirst(object? value)
        {
            if (value is not System.Collections.IEnumerable enumerable)
                return null;

            foreach (var item in enumerable)
                return item;
            return null;
        }

        private static object? GetPropertyValue(object? obj, params string[] names)
        {
            if (obj == null || names == null || names.Length == 0)
                return null;

            var type = obj.GetType();
            foreach (var name in names)
            {
                var prop = type.GetProperty(name);
                if (prop != null)
                    return prop.GetValue(obj);
            }

            return null;
        }

        private static double[] ApplyCoordinateTransforms(object transforms, int rank, int xIndex, int yIndex, double x, double y)
        {
            var coordinates = new double[Math.Max(rank, Math.Max(xIndex, yIndex) + 1)];
            if (xIndex >= 0 && xIndex < coordinates.Length)
                coordinates[xIndex] = x;
            if (yIndex >= 0 && yIndex < coordinates.Length)
                coordinates[yIndex] = y;

            if (transforms is not System.Collections.IEnumerable enumerable)
                return coordinates;

            foreach (var transform in enumerable)
            {
                if (transform == null)
                    continue;

                string? type = GetPropertyValue(transform, "Type")?.ToString() ?? GetPropertyValue(transform, "type")?.ToString();
                if (string.IsNullOrWhiteSpace(type))
                    continue;

                if (type.Equals("scale", StringComparison.OrdinalIgnoreCase))
                {
                    double[]? scale = GetDoubleArray(GetPropertyValue(transform, "Scale", "scale"));
                    if (scale == null || scale.Length == 0)
                        continue;

                    for (int i = 0; i < coordinates.Length && i < scale.Length; i++)
                        coordinates[i] *= scale[i];
                }
                else if (type.Equals("translation", StringComparison.OrdinalIgnoreCase))
                {
                    double[]? translation = GetDoubleArray(GetPropertyValue(transform, "Translation", "translation"));
                    if (translation == null || translation.Length == 0)
                        continue;

                    for (int i = 0; i < coordinates.Length && i < translation.Length; i++)
                        coordinates[i] += translation[i];
                }
            }

            return coordinates;
        }

        private static double[]? GetDoubleArray(object? value)
        {
            if (value == null)
                return null;

            if (value is double[] d)
                return d;

            if (value is float[] f)
                return f.Select(v => (double)v).ToArray();

            if (value is int[] i)
                return i.Select(v => (double)v).ToArray();

            if (value is long[] l)
                return l.Select(v => (double)v).ToArray();

            if (value is System.Collections.IEnumerable enumerable)
            {
                var list = new List<double>();
                foreach (var item in enumerable)
                {
                    if (item == null)
                        continue;

                    list.Add(Convert.ToDouble(item, CultureInfo.InvariantCulture));
                }
                return list.ToArray();
            }

            return null;
        }

        private static Color GetFallbackLabelColor(int labelValue)
        {
            lock (s_labelColorLock)
            {
                if (s_generatedLabelColors.TryGetValue(labelValue, out var cached))
                    return cached;

                var color = ColorFromHsv((s_labelColorRandom.NextDouble() + (labelValue * 0.6180339887498949)) % 1.0);
                s_generatedLabelColors[labelValue] = color;
                return color;
            }
        }

        private static Color ColorFromHsv(double hue)
        {
            double saturation = 0.65 + (s_labelColorRandom.NextDouble() * 0.2);
            double value = 0.80 + (s_labelColorRandom.NextDouble() * 0.15);
            hue = (hue % 1.0 + 1.0) % 1.0;
            double h = hue * 6.0;
            int sector = (int)Math.Floor(h);
            double fraction = h - sector;
            double p = value * (1.0 - saturation);
            double q = value * (1.0 - saturation * fraction);
            double t = value * (1.0 - saturation * (1.0 - fraction));

            double r, g, b;
            switch (sector % 6)
            {
                case 0: r = value; g = t; b = p; break;
                case 1: r = q; g = value; b = p; break;
                case 2: r = p; g = value; b = t; break;
                case 3: r = p; g = q; b = value; break;
                case 4: r = t; g = p; b = value; break;
                default: r = value; g = p; b = q; break;
            }

            return Color.FromArgb(
                96,
                (byte)Math.Clamp((int)Math.Round(r * 255.0), 0, 255),
                (byte)Math.Clamp((int)Math.Round(g * 255.0), 0, 255),
                (byte)Math.Clamp((int)Math.Round(b * 255.0), 0, 255));
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
