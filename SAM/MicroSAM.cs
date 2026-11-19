using AForge;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace BioGTK
{
    // Retain the public MicroSAM name for callers and recorded scripts.
    public class MicroSAM : SAM
    {
        internal const string EncoderFile = "micro-sam2-encoder.onnx";
        internal const string DecoderFile = "micro-sam2-decoder.onnx";
        internal static string ModelDirectory => AppStorage.IsPackagedMacApp ? AppStorage.DirectoryPath : AppContext.BaseDirectory;

        private const int Resolution = MicroSAMImage.Resolution;
        private const int MaskResolution = MicroSAMImage.MaskResolution;
        private static readonly string[] FeatureNames = { "image_embed", "high_res_feats_0", "high_res_feats_1" };
        private static readonly int[][] FeatureShapes =
        {
            new[] { 1, 256, 64, 64 },
            new[] { 1, 32, 256, 256 },
            new[] { 1, 64, 128, 128 }
        };
        private static readonly int[] FeatureLengths = FeatureShapes.Select(shape => shape.Aggregate(1, (a, b) => a * b)).ToArray();
        private static readonly int FeatureLength = FeatureLengths.Sum();

        private readonly object inferenceLock = new object();
        private InferenceSession encoder;
        private InferenceSession decoder;
        private Bitmap cachedPlane;
        private float[] cachedFeatures;
        public new static MicroSAM theSingleton = null;

        protected MicroSAM() { }

        public new static MicroSAM Instance() => theSingleton ??= new MicroSAM();

        public bool Initialized
        {
            get { lock (inferenceLock) return encoder != null; }
        }

        public override void LoadONNXModel()
        {
            lock (inferenceLock)
            {
                if (encoder != null)
                    return;

                string encoderPath = Path.Combine(ModelDirectory, EncoderFile);
                string decoderPath = Path.Combine(ModelDirectory, DecoderFile);
                if (!File.Exists(encoderPath) || !File.Exists(decoderPath))
                    throw new FileNotFoundException(
                        $"Export and place {EncoderFile} and {DecoderFile} in {ModelDirectory}. See BUILDING.md.");

                InferenceSession nextEncoder = null;
                InferenceSession nextDecoder = null;
                try
                {
                    nextEncoder = new InferenceSession(encoderPath);
                    nextDecoder = new InferenceSession(decoderPath);

                    RequireTensor(nextEncoder.InputMetadata, "image", 1, 3, Resolution, Resolution);
                    for (int i = 0; i < FeatureNames.Length; i++)
                    {
                        RequireTensor(nextEncoder.OutputMetadata, FeatureNames[i], FeatureShapes[i]);
                        RequireTensor(nextDecoder.InputMetadata, FeatureNames[i], FeatureShapes[i]);
                    }
                    RequireTensor(nextDecoder.InputMetadata, "point_coords", 1, -1, 2);
                    RequireTensor(nextDecoder.InputMetadata, "point_labels", 1, -1);
                    RequireTensor(nextDecoder.OutputMetadata, "mask_logits", 1, 1, MaskResolution, MaskResolution);
                    RequireTensor(nextDecoder.OutputMetadata, "iou_predictions", 1, 1);
                    encoder = nextEncoder;
                    decoder = nextDecoder;
                }
                catch
                {
                    nextEncoder?.Dispose();
                    nextDecoder?.Dispose();
                    throw;
                }
            }
        }

        private static void RequireTensor(IReadOnlyDictionary<string, NodeMetadata> metadata, string name, params int[] dims)
        {
            if (!metadata.TryGetValue(name, out var tensor) || tensor.ElementType != typeof(float) ||
                !tensor.Dimensions.SequenceEqual(dims))
                throw new InvalidDataException(
                    $"The Micro-SAM2 ONNX tensor '{name}' is incompatible. Export both models together using the BioGTK exporter.");
        }

        public override void Encode(BioImage image)
        {
            lock (inferenceLock)
            {
                // The explicit Encode button also refreshes an image edited in place.
                cachedPlane = null;
                cachedFeatures = null;
                EncodePlane(image.SelectedBuffer);
            }
            BioLib.Recorder.AddLine("SAMTool.microsam.Encode(ImageView.SelectedImage);", false);
        }

        internal float[] EncodePlane(Bitmap image)
        {
            lock (inferenceLock)
            {
                if (!ReferenceEquals(cachedPlane, image) || cachedFeatures == null)
                {
                    cachedFeatures = EncodeCrop(image, 0, 0, image.Width, image.Height);
                    cachedPlane = image;
                }
                return cachedFeatures;
            }
        }

        internal float[] EncodeCrop(Bitmap image, int left, int top, int width, int height)
        {
            lock (inferenceLock)
            {
                LoadONNXModel();
                var input = NamedOnnxValue.CreateFromTensor("image", MicroSAMImage.Prepare(image, left, top, width, height));
                using var results = encoder.Run(new[] { input }, FeatureNames);
                var features = new float[FeatureLength];
                int offset = 0;
                for (int i = 0; i < FeatureNames.Length; i++)
                {
                    var tensor = results.Single(output => output.Name == FeatureNames[i]).AsTensor<float>();
                    ((DenseTensor<float>)tensor).Buffer.Span.CopyTo(features.AsSpan(offset));
                    offset += FeatureLengths[i];
                }
                return features;
            }
        }

        public override float[] Decode(BioImage image, List<Promotion> promotions, int width, int height)
        {
            using MaskData mask = Decode(promotions, EncodePlane(image.SelectedBuffer), width, height);
            return mask.mMask.ToArray();
        }

        public override MaskData Decode(List<Promotion> promotions, float[] embedding, int width, int height)
        {
            if (embedding == null || embedding.Length != FeatureLength)
                throw new ArgumentException("A Micro-SAM2 embedding must include the two high-resolution feature maps.", nameof(embedding));

            // SAM2 represents the first and second corners of an XYXY box with labels 2 and 3.
            var ordered = promotions.OrderBy(p => p.mType == PromotionType.Box ? 0 : 1).ToArray();
            float[] labels = ordered.SelectMany(p => p.GetLable()).ToArray();
            if (labels.Length == 0)
                throw new ArgumentException("At least one point or box is required.", nameof(promotions));
            float scale = MicroSAMImage.Scale(width, height);
            float[] coordinates = ordered.SelectMany(p => p.GetInput()).Select(value => value * scale).ToArray();

            var inputs = new List<NamedOnnxValue>(5);
            int offset = 0;
            for (int i = 0; i < FeatureNames.Length; i++)
            {
                var tensor = new DenseTensor<float>(embedding.AsMemory(offset, FeatureLengths[i]), FeatureShapes[i]);
                inputs.Add(NamedOnnxValue.CreateFromTensor(FeatureNames[i], tensor));
                offset += FeatureLengths[i];
            }
            inputs.Add(NamedOnnxValue.CreateFromTensor("point_coords", new DenseTensor<float>(coordinates, new[] { 1, labels.Length, 2 })));
            inputs.Add(NamedOnnxValue.CreateFromTensor("point_labels", new DenseTensor<float>(labels, new[] { 1, labels.Length })));

            lock (inferenceLock)
            {
                LoadONNXModel();
                using var results = decoder.Run(inputs);
                float[] logits = results.Single(output => output.Name == "mask_logits").AsTensor<float>().ToArray();
                return new MaskData
                {
                    mShape = new[] { 1, 1, height, width },
                    mMask = MicroSAMImage.RestoreMask(logits, width, height).ToList(),
                    mIoU = results.Single(output => output.Name == "iou_predictions").AsTensor<float>().ToList()
                };
            }
        }

        /// <summary>
        /// Resizes a tensor of shape [1, sizeX, sizeY] using bilinear interpolation.
        /// </summary>
        /// <param name="tensor">The input tensor as a flat array.</param>
        /// <param name="sizeX">The width of the input tensor.</param>
        /// <param name="sizeY">The height of the input tensor.</param>
        /// <param name="newSizeX">The desired width of the output tensor.</param>
        /// <param name="newSizeY">The desired height of the output tensor.</param>
        /// <returns>A resized tensor as a flat array.</returns>
        public static float[] ResizeTensor(float[] tensor, int sizeX, int sizeY, int newSizeX, int newSizeY)
        {
            float[] resizedTensor = new float[newSizeX * newSizeY];

            // Scale factors for mapping output to input
            float scaleX = (float)sizeX / newSizeX;
            float scaleY = (float)sizeY / newSizeY;

            for (int y = 0; y < newSizeY; y++)
            {
                for (int x = 0; x < newSizeX; x++)
                {
                    // Map the new coordinates to the original
                    float origX = x * scaleX;
                    float origY = y * scaleY;

                    // Get the integer and fractional parts
                    int x0 = (int)Math.Floor(origX);
                    int y0 = (int)Math.Floor(origY);
                    int x1 = Math.Min(x0 + 1, sizeX - 1);
                    int y1 = Math.Min(y0 + 1, sizeY - 1);

                    float xFrac = origX - x0;
                    float yFrac = origY - y0;

                    // Get the values at the four surrounding points
                    float topLeft = tensor[y0 * sizeX + x0];
                    float topRight = tensor[y0 * sizeX + x1];
                    float bottomLeft = tensor[y1 * sizeX + x0];
                    float bottomRight = tensor[y1 * sizeX + x1];

                    // Perform bilinear interpolation
                    float top = topLeft + xFrac * (topRight - topLeft);
                    float bottom = bottomLeft + xFrac * (bottomRight - bottomLeft);
                    float value = top + yFrac * (bottom - top);

                    // Assign the computed value to the resized tensor
                    resizedTensor[y * newSizeX + x] = value;
                }
            }

            return resizedTensor;
        }

        public override void Dispose()
        {
            lock (inferenceLock)
            {
                cachedPlane = null;
                cachedFeatures = null;
                encoder?.Dispose();
                decoder?.Dispose();
                encoder = null;
                decoder = null;
            }
        }
    }
}
