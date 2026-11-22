using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AForge;
using Microsoft.ML;
using Microsoft.ML.Data;

namespace BioGTK.ML
{
    internal readonly record struct PixelRegion(int Left, int Top, int Right, int Bottom, bool Foreground);

    internal sealed class PixelPlane
    {
        private readonly byte[] bytes;
        private readonly int stride;
        private readonly int pixelBytes;
        private readonly bool color;
        private readonly bool sixteenBit;
        private readonly bool littleEndian;
        public int Width { get; }
        public int Height { get; }

        public PixelPlane(Bitmap bitmap)
        {
            (pixelBytes, color, sixteenBit) = bitmap.PixelFormat switch
            {
                PixelFormat.Format8bppIndexed => (1, false, false),
                PixelFormat.Format16bppGrayScale => (2, false, true),
                PixelFormat.Format24bppRgb => (3, true, false),
                PixelFormat.Format32bppRgb or PixelFormat.Format32bppArgb => (4, true, false),
                PixelFormat.Format48bppRgb => (6, true, true),
                _ => throw new ArgumentException("Use an 8- or 16-bit grayscale or RGB image plane.")
            };
            Width = bitmap.Width;
            Height = bitmap.Height;
            bytes = bitmap.Bytes;
            if (Width <= 0 || Height <= 0 || bytes == null || bytes.Length % Height != 0 ||
                bytes.Length / Height < checked(Width * pixelBytes))
                throw new InvalidDataException("The image buffer does not match its dimensions.");
            stride = bytes.Length / Height;
            littleEndian = bitmap.LittleEndian;
        }

        public void ReadRgb(int x, int y, float[] rgb)
        {
            int offset = y * stride + x * pixelBytes;
            if (!color)
            {
                rgb[0] = rgb[1] = rgb[2] = sixteenBit ? Read16(offset) : bytes[offset] / 255f;
                return;
            }
            // Packed 8-bit color uses BGR(A), while 48-bit planes use RGB.
            rgb[0] = sixteenBit ? Read16(offset) : bytes[offset + 2] / 255f;
            rgb[1] = sixteenBit ? Read16(offset + 2) : bytes[offset + 1] / 255f;
            rgb[2] = sixteenBit ? Read16(offset + 4) : bytes[offset] / 255f;
        }

        private float Read16(int offset)
        {
            var sample = bytes.AsSpan(offset, sizeof(ushort));
            return (littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(sample) :
                BinaryPrimitives.ReadUInt16BigEndian(sample)) / 65535f;
        }
    }

    internal sealed class PixelClassifier
    {
        // The persisted column name identifies the feature order and normalization used at inference.
        private const string FeatureColumn = "BioGTK_PixelRGB_v1";
        private const int MaxPixelsPerClass = 8192;

        private sealed class Sample
        {
            [ColumnName(FeatureColumn), VectorType(3)]
            public float[] Rgb { get; set; } = new float[3];
            public bool Label { get; set; }
        }

        private readonly MLContext context;
        private readonly ITransformer model;
        private readonly DataViewSchema schema;

        private PixelClassifier(MLContext context, ITransformer model, DataViewSchema schema)
        {
            this.context = context;
            this.model = model;
            this.schema = schema;
        }

        public static PixelClassifier Train(PixelPlane plane, IReadOnlyList<PixelRegion> regions, CancellationToken token)
        {
            var labels = new byte[checked(plane.Width * plane.Height)];
            int[] counts = new int[3];
            foreach (var region in regions)
            {
                byte label = region.Foreground ? (byte)2 : (byte)1;
                int left = Math.Max(0, region.Left);
                int right = Math.Min(plane.Width, region.Right);
                for (int y = Math.Max(0, region.Top); y < Math.Min(plane.Height, region.Bottom); y++)
                {
                    token.ThrowIfCancellationRequested();
                    for (int x = left; x < right; x++)
                    {
                        ref byte current = ref labels[y * plane.Width + x];
                        if (current == label)
                            continue;
                        if (current != 0)
                            throw new ArgumentException("Foreground and background rectangles overlap. Adjust their bounds or ignore one of them.");
                        current = label;
                        counts[label]++;
                    }
                }
            }

            int perClass = Math.Min(MaxPixelsPerClass, Math.Min(counts[1], counts[2]));
            if (perClass == 0)
                throw new ArgumentException("Assign at least one rectangle containing image pixels to each class.");

            // Sample evenly through each class so large rectangles cannot dominate the other class.
            var samples = new List<Sample>(2 * perClass);
            long[] accumulator = { 0, counts[1] / 2, counts[2] / 2 };
            for (int y = 0; y < plane.Height; y++)
            {
                token.ThrowIfCancellationRequested();
                for (int x = 0; x < plane.Width; x++)
                {
                    byte label = labels[y * plane.Width + x];
                    if (label == 0)
                        continue;
                    accumulator[label] += perClass;
                    if (accumulator[label] < counts[label])
                        continue;
                    accumulator[label] -= counts[label];
                    var sample = new Sample { Label = label == 2 };
                    plane.ReadRgb(x, y, sample.Rgb);
                    samples.Add(sample);
                }
            }
            var context = new MLContext(seed: 1);
            var data = context.Data.LoadFromEnumerable(samples);
            var estimator = context.Transforms.NormalizeMinMax("Features", FeatureColumn, fixZero: false)
                .Append(context.BinaryClassification.Trainers.SdcaLogisticRegression(
                    new Microsoft.ML.Trainers.SdcaLogisticRegressionBinaryTrainer.Options { NumberOfThreads = 1 }));
            token.ThrowIfCancellationRequested();
            var model = estimator.Fit(data);
            token.ThrowIfCancellationRequested();
            return new PixelClassifier(context, model, data.Schema);
        }

        public void Save(string path)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                context.Model.Save(model, schema, temporary);
                File.Move(temporary, path, overwrite: true);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        public static PixelClassifier Load(string path)
        {
            var context = new MLContext();
            var model = context.Model.Load(path, out var schema);
            if (schema.GetColumnOrNull(FeatureColumn)?.Type is not VectorDataViewType vector ||
                vector.Size != 3 || vector.ItemType != NumberDataViewType.Single ||
                model.GetOutputSchema(schema).GetColumnOrNull("PredictedLabel")?.Type != BooleanDataViewType.Instance)
                throw new InvalidDataException("Choose a model saved with Train Pixel Classifier.");
            return new PixelClassifier(context, model, schema);
        }

        public byte[] Predict(PixelPlane plane, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            IEnumerable<Sample> Pixels()
            {
                // LoadFromEnumerable consumes each row before advancing the iterator.
                var row = new Sample();
                for (int y = 0; y < plane.Height; y++)
                {
                    if (token.IsCancellationRequested)
                        yield break;
                    for (int x = 0; x < plane.Width; x++)
                    {
                        plane.ReadRgb(x, y, row.Rgb);
                        yield return row;
                    }
                }
            }
            var mask = new byte[checked(plane.Width * plane.Height)];
            var predictions = model.Transform(context.Data.LoadFromEnumerable(Pixels()));
            int index = 0;
            foreach (bool foreground in predictions.GetColumn<bool>("PredictedLabel"))
                mask[index++] = foreground ? (byte)255 : (byte)0;
            token.ThrowIfCancellationRequested();
            if (index != mask.Length)
                throw new InvalidDataException("The model returned an unexpected number of pixels.");
            return mask;
        }
    }
}
