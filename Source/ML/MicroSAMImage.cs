using System;
using System.Buffers.Binary;
using System.IO;
using AForge;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace BioGTK
{
    internal static class MicroSAMImage
    {
        internal const int Resolution = 1024;
        internal const int MaskResolution = 256;
        private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] StdDev = { 0.229f, 0.224f, 0.225f };

        internal static float Scale(int width, int height) => (float)Resolution / Math.Max(width, height);

        private static (int Width, int Height) ResizedShape(int width, int height)
        {
            double scale = (double)Resolution / Math.Max(width, height);
            return (Math.Max(1, (int)(width * scale + 0.5)), Math.Max(1, (int)(height * scale + 0.5)));
        }

        internal static DenseTensor<float> Prepare(Bitmap bitmap, int left, int top, int width, int height)
        {
            using var converted = bitmap.PixelFormat switch
            {
                PixelFormat.Format8bppIndexed or PixelFormat.Format16bppGrayScale or PixelFormat.Format24bppRgb
                    or PixelFormat.Format32bppArgb or PixelFormat.Format32bppPArgb or PixelFormat.Format32bppRgb
                    or PixelFormat.Format48bppRgb => null,
                _ => bitmap.GetImageRGBA(bitmap.LittleEndian)
            };
            bitmap = converted ?? bitmap;
            var (resizedWidth, resizedHeight) = ResizedShape(width, height);
            var input = new DenseTensor<float>(new[] { 1, 3, Resolution, Resolution });
            Span<float> tensor = input.Buffer.Span;
            int channels = bitmap.RGBChannelsCount >= 3 ? 3 : 1;
            bool raw16 = bitmap.PixelFormat is PixelFormat.Format16bppGrayScale or PixelFormat.Format48bppRgb;
            byte[] raw = bitmap.Bytes;
            int stride = raw.Length / bitmap.Height;
            if (stride < bitmap.Width * bitmap.PixelFormatSize)
                throw new InvalidDataException("The selected image plane is shorter than its pixel dimensions.");
            var pixels = new ushort[checked(width * height)];
            var horizontal = SamplingWeights(width, resizedWidth, true);
            var vertical = SamplingWeights(height, resizedHeight, true);
            var rows = new float[checked(resizedWidth * height)];
            var lookup = new float[ushort.MaxValue + 1];
            var histogram = new int[lookup.Length];
            for (int channel = 0; channel < channels; channel++)
            {
                Array.Clear(histogram);
                // BioGTK displays packed 8-bit color as BGR(A). Its 48-bit buffers store RGB.
                int componentOffset = raw16 ? channel * sizeof(ushort) : channels == 3 ? 2 - channel : 0;
                for (int y = 0; y < height; y++)
                {
                    int rowStart = (top + y) * stride + left * bitmap.PixelFormatSize + componentOffset;
                    for (int x = 0; x < width; x++)
                    {
                        int offset = rowStart + x * bitmap.PixelFormatSize;
                        ushort value = !raw16 ? raw[offset] : bitmap.LittleEndian
                            ? BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(offset, sizeof(ushort)))
                            : BinaryPrimitives.ReadUInt16BigEndian(raw.AsSpan(offset, sizeof(ushort)));
                        pixels[y * width + x] = value;
                        histogram[value]++;
                    }
                }

                // Match the 2nd/98th percentile mapping and uint8 conversion in micro_sam.v2.
                float low = Percentile(histogram, (pixels.Length - 1) * 0.02);
                float high = Percentile(histogram, (pixels.Length - 1) * 0.98);
                for (int value = 0; value < lookup.Length; value++)
                    lookup[value] = (byte)(255 * Math.Clamp((value - low) / (high - low + 1e-7f), 0, 1)) / 255f;

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < resizedWidth; x++)
                    {
                        var (start, weights) = horizontal[x];
                        float value = 0;
                        for (int i = 0; i < weights.Length; i++)
                            value += weights[i] * lookup[pixels[y * width + start + i]];
                        rows[y * resizedWidth + x] = value;
                    }
                }

                for (int rgb = channel; rgb < 3; rgb += channels)
                {
                    Span<float> plane = tensor.Slice(rgb * Resolution * Resolution, Resolution * Resolution);
                    plane.Fill(-Mean[rgb] / StdDev[rgb]);
                    for (int y = 0; y < resizedHeight; y++)
                    {
                        var (start, weights) = vertical[y];
                        for (int x = 0; x < resizedWidth; x++)
                        {
                            float value = 0;
                            for (int i = 0; i < weights.Length; i++)
                                value += weights[i] * rows[(start + i) * resizedWidth + x];
                            plane[y * Resolution + x] = (value - Mean[rgb]) / StdDev[rgb];
                        }
                    }
                }
            }
            return input;
        }

        private static float Percentile(int[] histogram, double rank)
        {
            int lowerRank = (int)rank;
            int upperRank = (int)Math.Ceiling(rank);
            int lower = -1, count = 0;
            for (int value = 0; value < histogram.Length; value++)
            {
                count += histogram[value];
                if (lower < 0 && count > lowerRank)
                    lower = value;
                if (count > upperRank)
                    return lower + (value - lower) * (float)(rank - lowerRank);
            }
            throw new ArgumentOutOfRangeException(nameof(rank));
        }

        private static (int Start, float[] Weights)[] SamplingWeights(int input, int output, bool antialias)
        {
            double scale = (double)input / output;
            double radius = antialias ? Math.Max(1, scale) : 1;
            var sampling = new (int, float[])[output];
            for (int i = 0; i < output; i++)
            {
                double center = (i + 0.5) * scale - 0.5;
                int start = Math.Max(0, (int)Math.Ceiling(center - radius));
                int end = Math.Min(input - 1, (int)Math.Floor(center + radius));
                var weights = new float[end - start + 1];
                float sum = 0;
                for (int j = 0; j < weights.Length; j++)
                {
                    weights[j] = (float)(1 - Math.Abs(start + j - center) / radius);
                    sum += weights[j];
                }
                for (int j = 0; j < weights.Length; j++)
                    weights[j] /= sum;
                sampling[i] = (start, weights);
            }
            return sampling;
        }

        internal static float[] RestoreMask(float[] logits, int width, int height)
        {
            var (resizedWidth, resizedHeight) = ResizedShape(width, height);
            // Upsample first, then remove the bottom/right padding before restoring the image size.
            float[] padded = ResizeMask(logits, MaskResolution, MaskResolution, MaskResolution, Resolution, Resolution);
            return ResizeMask(padded, Resolution, resizedWidth, resizedHeight, width, height);
        }

        private static float[] ResizeMask(float[] source, int stride, int sourceWidth, int sourceHeight,
            int width, int height)
        {
            var xWeights = SamplingWeights(sourceWidth, width, false);
            var yWeights = SamplingWeights(sourceHeight, height, false);
            var mask = new float[checked(width * height)];
            for (int y = 0; y < height; y++)
            {
                var (top, rowWeights) = yWeights[y];
                for (int x = 0; x < width; x++)
                {
                    var (left, columnWeights) = xWeights[x];
                    float value = 0;
                    for (int dy = 0; dy < rowWeights.Length; dy++)
                    {
                        for (int dx = 0; dx < columnWeights.Length; dx++)
                            value += rowWeights[dy] * columnWeights[dx] * source[(top + dy) * stride + left + dx];
                    }
                    mask[y * width + x] = value;
                }
            }
            return mask;
        }
    }
}
