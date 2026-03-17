using AForge;
using BioLib;
using ZarrNET.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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

            // Derive bits-per-sample and RGB channel count directly from the
            // buffer PixelFormat — the same pattern used throughout the codebase
            // (Fiji.cs, QuPath.cs).  This is more reliable than b.RGBChannelCount
            // or b.bitsPerPixel, which can disagree with the actual buffer layout.
            //
            // Format32bppArgb / Format32bppRgb:
            //   In-memory layout is BGRA (B at byte 0, A at byte 3).
            //   We write only the 3 colour channels remapped to R,G,B order
            //   and discard alpha.  srcChannelCount=4 so stride arithmetic is
            //   correct; outChannelCount=3 is what we write to the Zarr array.
            var pixelFormat    = b.Buffers[0].PixelFormat;
            int bitsPerSample;
            int srcChannelCount;   // channels packed in the raw buffer
            int outChannelCount;   // channels written to Zarr
            bool isBGRA = false;

            switch (pixelFormat)
            {
                case PixelFormat.Format8bppIndexed:
                    bitsPerSample   = 8;
                    srcChannelCount = 1;
                    outChannelCount = 1;
                    break;
                case PixelFormat.Format16bppGrayScale:
                    bitsPerSample   = 16;
                    srcChannelCount = 1;
                    outChannelCount = 1;
                    break;
                case PixelFormat.Format24bppRgb:
                    bitsPerSample   = 8;
                    srcChannelCount = 3;
                    outChannelCount = 3;
                    break;
                case PixelFormat.Format32bppArgb:
                case PixelFormat.Format32bppRgb:
                    // Raw buffer is BGRA (4 bytes/pixel).
                    // We extract 3 channels remapped to R,G,B order and drop A.
                    bitsPerSample   = 8;
                    srcChannelCount = 4;
                    outChannelCount = 3;
                    isBGRA          = true;
                    break;
                case PixelFormat.Format48bppRgb:
                    bitsPerSample   = 16;
                    srcChannelCount = 3;
                    outChannelCount = 3;
                    break;
                default:
                    srcChannelCount = 1;
                    outChannelCount = 1;
                    bitsPerSample   = Math.Max(b.bitsPerPixel, 8);
                    break;
            }

            var bytesPerSample = bitsPerSample / 8;
            var zarrDataType   = MapDataType(bitsPerSample);
            var totalChannels  = b.SizeC * outChannelCount;

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

                    for (int t = 0; t < b.SizeT; t++)
                    {
                        for (int z = 0; z < b.SizeZ; z++)
                        {
                            for (int c = 0; c < b.SizeC; c++)
                            {
                                if (outChannelCount == 1)
                                {
                                    WritePlaneInTiles(
                                        writer, b, lvlDesc,
                                        t, c, z,
                                        bytesPerSample, tileW, tileH,
                                        levelIndex: levelIndex);
                                }
                                else
                                {
                                    for (int rgb = 0; rgb < outChannelCount; rgb++)
                                    {
                                        int globalC = c * outChannelCount + rgb;

                                        WritePlaneInTiles(
                                            writer, b, lvlDesc,
                                            t, globalC, z,
                                            bytesPerSample, tileW, tileH,
                                            levelIndex: levelIndex,
                                            rgbChannelIndex: rgb,
                                            srcChannelCount: srcChannelCount,
                                            isBGRA: isBGRA,
                                            logicalC: c);
                                    }
                                }
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
        private async static void WritePlaneInTiles(
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

                    // Fetch tile from BioLib at the requested pyramid level.
                    var tileBitmap = await b.GetTile(
                        b.Coords[z, srcC, t], levelIndex, tileX, tileY, actualW, actualH);

                    byte[] pixelBytes = tileBitmap.Bytes;

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
                    }

                    writer.WriteRegionAsync(
                        t, c, z,
                        tileY, tileX,
                        actualH, actualW,
                        pixelBytes,
                        levelIndex: levelIndex).Wait();
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
