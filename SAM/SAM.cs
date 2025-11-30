using AForge;
using BioGTK;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Diagnostics;
using Gtk;

namespace BioGTK
{

    public class SAM : IDisposable
    {
        public static bool SAM2 = true;
        public static SAM theSingleton = null;
        InferenceSession mEncoder;
        InferenceSession mDecoder;
        bool mReady = false;
        public float mask_threshold = 0.0f;
        protected SAM()
        {
        }
        /// The function returns an instance of the SAM class, creating it if it doesn't already exist.
        /// 
        /// @return The method is returning an instance of the SAM class.
        public static SAM Instance()
        {
            if (null == theSingleton)
            {
                theSingleton = new SAM();
            }
            return theSingleton;
        }

        /// The function `LoadONNXModel` loads ONNX models for encoding and decoding.
        public void LoadONNXModel()
        {
            if (this.mEncoder != null)
                this.mEncoder.Dispose();

            if (this.mDecoder != null)
                this.mDecoder.Dispose();

            string exePath = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            string encode_model_path = exePath + "/encoder-quant.onnx";
            this.mEncoder = new InferenceSession(encode_model_path);

            string decode_model_path = exePath + "/decoder-quant.onnx";
            this.mDecoder = new InferenceSession(decode_model_path);
            SAMTool.Encode();
        }
        public void Encode()
        {
            Encode(ImageView.SelectedImage);
        }
        /// The Encode function takes a BioImage object, applies a transformation to it, converts it to
        /// a tensor, runs it through an encoder model, and stores the resulting embedding.
        /// 
        /// @param BioImage The BioImage parameter is an object that represents an image. It likely
        /// contains information such as the image data, size, and other properties related to the
        /// image.
        public void Encode(BioImage b)
        {
            if (b.Buffers.Count * 3 * 1024 * 1024 > 4e8)
            {
                // Create the message dialog
                MessageDialog msgBox = new MessageDialog(
                    null,
                    DialogFlags.Modal,
                    MessageType.Info,
                    ButtonsType.Ok | ButtonsType.Cancel,
                    "Memory required is more than 4GB are you sure you want to continue?"
                );

                // Show the message dialog
                if (msgBox.Run() != (int)ResponseType.Ok)
                    return;
            }
            Progress pr = Progress.Create("Encoding Image", "Transforming", "");
            // update ui on main UI thread
            Application.Invoke(delegate
            {
                pr.Show();
                pr.Present();
            });
            int i = 0;
            foreach (Bitmap bu in b.Buffers)
            {
                Transforms tranform = new Transforms(1024);
                float[] img = tranform.ApplyImage(bu);
                var tensor = new DenseTensor<float>(img, new[] { 1, 3, 1024, 1024 });
                IDisposableReadOnlyCollection<DisposableNamedOnnxValue> results = null;
                List<NamedOnnxValue> inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("image", tensor)
                };
                try
                {
                    results = this.mEncoder.Run(inputs);
                }
                catch (Exception e)
                {
                    Console.WriteLine("Tried running SAM-2 next trying SAM-1");
                    SAM2 = false;
                }
                if(!SAM2)
                {
                    inputs = new List<NamedOnnxValue>
                    {
                        NamedOnnxValue.CreateFromTensor("x", tensor)
                    };
                    results = this.mEncoder.Run(inputs);
                }
                if (b.Tag == null)
                {
                    b.Tag = new List<float[]>();
                }
                List<float[]> l = (List<float[]>)b.Tag;
                if(!SAM2)
                    l.Add(results.First().AsTensor<float>().ToArray());
                else
                    l.Add(results.Last().AsTensor<float>().ToArray());
                b.Tag = l;
                results.Dispose();
                pr.ProgressValue = (double)i/b.Buffers.Count;
                i++;
            }
            // update ui on main UI thread
            Application.Invoke(delegate
            {
                pr.Hide();
            });
            this.mReady = true;
            BioLib.Recorder.AddLine("SAM.Encode(Images.GetImage(\"" + b.Filename + "\"));", false);
        }

        /// The function takes a list of promotions, original width, and original height as input, and
        /// returns an array of decoded output masks.
        /// 
        /// @param promotions A list of Promotion objects. Each Promotion object has properties like
        /// mType, mInput, and mLable.
        /// @param orgWid The parameter `orgWid` represents the original width of the image.
        /// @param orgHei The parameter `orgHei` represents the original height of the image.
        /// 
        /// @return The method is returning an array of floats, which represents the output mask.
        public float[] Decode(BioImage b, List<Promotion> promotions, int orgWid, int orgHei)
        {
            ZCT c = App.viewer.GetCoordinate();
            List<float[]> lts = (List<float[]>)b.Tag;
            int fr = b.GetFrameIndex(c.Z, c.C, c.T);
            var embedding_tensor = new DenseTensor<float>(lts[fr], new[] { 1, 256, 64, 64 });
            var bpmos = promotions.FindAll(e => e.mType == PromotionType.Box);
            var pproms = promotions.FindAll(e => e.mType == PromotionType.Point);
            int boxCount = promotions.FindAll(e => e.mType == PromotionType.Box).Count();
            int pointCount = promotions.FindAll(e => e.mType == PromotionType.Point).Count();
            float[] promotion = new float[2 * (boxCount * 2 + pointCount)];
            float[] label = new float[boxCount * 2 + pointCount];
            for (int i = 0; i < boxCount; i++)
            {
                var input = bpmos[i].GetInput();
                for (int j = 0; j < input.Count(); j++)
                {
                    promotion[4 * i + j] = input[j];
                }
                var la = bpmos[i].GetLable();
                for (int j = 0; j < la.Count(); j++)
                {
                    label[2 * i + j] = la[j];
                }
            }
            for (int i = 0; i < pointCount; i++)
            {
                var p = pproms[i].GetInput();
                for (int j = 0; j < p.Count(); j++)
                {
                    promotion[boxCount * 4 + 2 * i + j] = p[j];
                }
                var la = pproms[i].GetLable();
                for (int j = 0; j < la.Count(); j++)
                {
                    label[boxCount * 2 + i + j] = la[j];
                }
            }

            var point_coords_tensor = new DenseTensor<float>(promotion, new[] { 1, boxCount * 2 + pointCount, 2 });

            var point_label_tensor = new DenseTensor<float>(label, new[] { 1, boxCount * 2 + pointCount });

            float[] mask = new float[256 * 256];
            for (int i = 0; i < mask.Count(); i++)
            {
                mask[i] = 0;
            }
            var mask_tensor = new DenseTensor<float>(mask, new[] { 1, 1, 256, 256 });

            float[] hasMaskValues = new float[1] { 0 };
            var hasMaskValues_tensor = new DenseTensor<float>(hasMaskValues, new[] { 1 });

            var decode_inputs = new List<NamedOnnxValue>();
            if (SAM2)
            {
                int[] orig_im_size_values = { (int)orgHei, (int)orgWid };
                var orig_im_size_values_tensor = new DenseTensor<int>(orig_im_size_values, new[] { 2 });
                var fs = new float[32 * 256 * 256];
                var feats = new DenseTensor<float>(fs, new[] { 1, 32, 256, 256 });
                var fs2 = new float[64 * 128 * 128];
                var feats2 = new DenseTensor<float>(fs2, new[] { 1, 64, 128, 128 });
                decode_inputs = new List<NamedOnnxValue>
                {
                NamedOnnxValue.CreateFromTensor("image_embed", embedding_tensor),
                NamedOnnxValue.CreateFromTensor("high_res_feats_0", feats),
                NamedOnnxValue.CreateFromTensor("high_res_feats_1", feats2),
                NamedOnnxValue.CreateFromTensor("point_coords", point_coords_tensor),
                NamedOnnxValue.CreateFromTensor("point_labels", point_label_tensor),
                NamedOnnxValue.CreateFromTensor("mask_input", mask_tensor),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskValues_tensor),
                NamedOnnxValue.CreateFromTensor("orig_im_size", orig_im_size_values_tensor)
                };
            }
            else
            {
                float[] orig_im_size_values = { (float)orgHei, (float)orgWid };
                var orig_im_size_values_tensor = new DenseTensor<float>(orig_im_size_values, new[] { 2 });
                decode_inputs = new List<NamedOnnxValue>
                {
                NamedOnnxValue.CreateFromTensor("image_embeddings", embedding_tensor),
                NamedOnnxValue.CreateFromTensor("point_coords", point_coords_tensor),
                NamedOnnxValue.CreateFromTensor("point_labels", point_label_tensor),
                NamedOnnxValue.CreateFromTensor("mask_input", mask_tensor),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskValues_tensor),
                NamedOnnxValue.CreateFromTensor("orig_im_size", orig_im_size_values_tensor)
                };
            }
            var segmask = this.mDecoder.Run(decode_inputs);
            var outputmask = segmask.First().AsTensor<float>().ToArray();
            BioLib.Recorder.AddLine("App.samTool.sam.Decode(Images.GetImage(\"" + b.Filename + "\"),App.samTool.Promotions," + orgWid + "," + orgHei + ");", false);
            return outputmask;
           
        }

        public MaskData Decode(List<Promotion> promotions, float[] embedding, int orgWid, int orgHei)
        {
            var embedding_tensor = new DenseTensor<float>(embedding, new[] { 1, 256, 64, 64 });

            var bpmos = promotions.FindAll(e => e.mType == PromotionType.Box);
            var pproms = promotions.FindAll(e => e.mType == PromotionType.Point);
            int boxCount = promotions.FindAll(e => e.mType == PromotionType.Box).Count();
            int pointCount = promotions.FindAll(e => e.mType == PromotionType.Point).Count();
            float[] promotion = new float[2 * (boxCount * 2 + pointCount)];
            float[] label = new float[boxCount * 2 + pointCount];
            for (int i = 0; i < boxCount; i++)
            {
                var input = bpmos[i].GetInput();
                for (int j = 0; j < input.Count(); j++)
                {
                    promotion[4 * i + j] = input[j];
                }
                var la = bpmos[i].GetLable();
                for (int j = 0; j < la.Count(); j++)
                {
                    label[2 * i + j] = la[j];
                }
            }
            for (int i = 0; i < pointCount; i++)
            {
                var p = pproms[i].GetInput();
                for (int j = 0; j < p.Count(); j++)
                {
                    promotion[boxCount * 4 + 2 * i + j] = p[j];
                }
                var la = pproms[i].GetLable();
                for (int j = 0; j < la.Count(); j++)
                {
                    label[boxCount * 2 + i + j] = la[j];
                }
            }

            var point_coords_tensor = new DenseTensor<float>(promotion, new[] { 1, boxCount * 2 + pointCount, 2 });

            var point_label_tensor = new DenseTensor<float>(label, new[] { 1, boxCount * 2 + pointCount });

            float[] mask = new float[256 * 256];
            for (int i = 0; i < mask.Count(); i++)
            {
                mask[i] = 0;
            }
            var mask_tensor = new DenseTensor<float>(mask, new[] { 1, 1, 256, 256 });

            float[] hasMaskValues = new float[1] { 0 };
            var hasMaskValues_tensor = new DenseTensor<float>(hasMaskValues, new[] { 1 });
            List<NamedOnnxValue> decode_inputs;
            if(SAM.SAM2)
            {
                int[] orig_im_size_values = { (int)orgHei, (int)orgWid };
                var orig_im_size_values_tensor = new DenseTensor<int>(orig_im_size_values, new[] { 2 });
                var fs = new float[32 * 256 * 256];
                var feats = new DenseTensor<float>(fs, new[] { 1, 32, 256, 256 });
                var fs2 = new float[64 * 128 * 128];
                var feats2 = new DenseTensor<float>(fs2, new[] { 1, 64, 128, 128 });
                decode_inputs = new List<NamedOnnxValue>
                {
                NamedOnnxValue.CreateFromTensor("image_embed", embedding_tensor),
                NamedOnnxValue.CreateFromTensor("high_res_feats_0", feats),
                NamedOnnxValue.CreateFromTensor("high_res_feats_1", feats2),
                NamedOnnxValue.CreateFromTensor("point_coords", point_coords_tensor),
                NamedOnnxValue.CreateFromTensor("point_labels", point_label_tensor),
                NamedOnnxValue.CreateFromTensor("mask_input", mask_tensor),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskValues_tensor),
                NamedOnnxValue.CreateFromTensor("orig_im_size", orig_im_size_values_tensor)
                };
            }
            else
            {
                float[] orig_im_size_values = { (float)orgHei, (float)orgWid };
                var orig_im_size_values_tensor = new DenseTensor<float>(orig_im_size_values, new[] { 2 });
                decode_inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("image_embeddings", embedding_tensor),
                    NamedOnnxValue.CreateFromTensor("point_coords", point_coords_tensor),
                    NamedOnnxValue.CreateFromTensor("point_labels", point_label_tensor),
                    NamedOnnxValue.CreateFromTensor("mask_input", mask_tensor),
                    NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskValues_tensor),
                    NamedOnnxValue.CreateFromTensor("orig_im_size", orig_im_size_values_tensor)
                };
            }
            MaskData md = new MaskData();
            var segmask = this.mDecoder.Run(decode_inputs).ToList();
            md.mMask = segmask[0].AsTensor<float>().ToArray().ToList();
            md.mShape = segmask[0].AsTensor<float>().Dimensions.ToArray();
            md.mIoU = segmask[1].AsTensor<float>().ToList();
            BioLib.Recorder.AddLine("MaskData md = App.samTool.sam.Decode(App.samTool.Promotions,(float[])ImageView.SelectedImage.Tag," + orgWid + "," + orgHei, false);
            return md;

        }

        public static void RemoveDuplicates(bool removeDistance, float distance, bool removeLarge, float largeArea)
        {
            // Track duplicates to remove
            HashSet<ROI> dups = new HashSet<ROI>();

            // Loop through all Z, C, T dimensions
            for (int z = 0; z < ImageView.SelectedImage.SizeZ; z++)
            {
                for (int c = 0; c < ImageView.SelectedImage.SizeC; c++)
                {
                    for (int t = 0; t < ImageView.SelectedImage.SizeT; t++)
                    {
                        // Collect all ROIs matching the current Z, C, T coordinates
                        List<ROI> rois = ImageView.SelectedImage.Annotations
                            .Where(annotation => annotation.coord.Equals(new ZCT(z, c, t)))
                            .ToList();

                        // Remove ROIs that exceed the large area threshold
                        if (removeLarge)
                        {
                            double imageArea = (ImageView.SelectedImage.SizeX) * ImageView.SelectedImage.Resolutions[0].PhysicalSizeX *
                                               (ImageView.SelectedImage.SizeY) * ImageView.SelectedImage.Resolutions[0].PhysicalSizeY;

                            foreach (var roi in rois)
                            {
                                double roiArea = 0;
                                if(roi.type == ROI.Type.Mask)
                                {
                                    roiArea = (roi.roiMask.Width * roi.roiMask.PhysicalSizeX) * (roi.roiMask.Height * roi.roiMask.PhysicalSizeY);
                                }
                                else
                                    roiArea = roi.BoundingBox.W * roi.BoundingBox.H;
                                if (roiArea > largeArea * imageArea) // Check if ROI exceeds the threshold
                                {
                                    dups.Add(roi);
                                }
                            }
                        }
                        double px = ImageView.SelectedImage.PhysicalSizeX;
                        double py = ImageView.SelectedImage.PhysicalSizeY;
                        // Remove ROIs that are too close to each other
                        if (removeDistance)
                        {
                            for (int i = 0; i < rois.Count - 1; i++)
                            {
                                ROI roi1 = rois[i];
                                if(dups.Contains(roi1))
                                    continue;
                                for (int j = i + 1; j < rois.Count; j++)
                                {
                                    ROI roi2 = rois[j];
                                    if (dups.Contains(roi2))
                                        continue;
                                    // Calculate Euclidean distance between centers
                                    double centerX1 = ImageView.SelectedImage.StageSizeX + (roi1.roiMask.X * px) + ((roi1.roiMask.Width * px) / 2);
                                    double centerY1 = ImageView.SelectedImage.StageSizeY + (roi1.roiMask.Y * py) + ((roi1.roiMask.Height * py) / 2);
                                    double centerX2 = ImageView.SelectedImage.StageSizeX + (roi2.roiMask.X * px) + ((roi2.roiMask.Width * px) / 2);
                                    double centerY2 = ImageView.SelectedImage.StageSizeY + (roi2.roiMask.Y * py) + ((roi2.roiMask.Height * py) / 2);

                                    double deltaX = centerX2 - centerX1;
                                    double deltaY = centerY2 - centerY1;
                                    double dist = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);

                                    // Mark the second ROI as duplicate if distance is below threshold
                                    if (dist < distance)
                                    {
                                        dups.Add(roi2); // Keep roi1 by default
                                    }
                                }
                            }
                        }

                        // Remove duplicates from the annotations list
                        foreach (var duplicate in dups)
                        {
                            ImageView.SelectedImage.Annotations.Remove(duplicate);
                        }

                        // Clear duplicates list for the next iteration
                        dups.Clear();
                    }
                }
            }

            // Record the operation
            BioLib.Recorder.Record($"SAM.RemoveDuplicates({removeDistance}, {distance}, {removeLarge}, {largeArea});");
        }

        static Random rng = new Random();
        public static void Run(float minArea, float maxArea,float stability,float prediction, int layers, int points)
        {
            AutoSAM asm = new AutoSAM(points, 64, prediction, stability, 1, 0.7f, layers, 0.7f, 0.3413333f, 1, null, 0, "binary_mask");
            for (int z = 0; z < ImageView.SelectedImage.SizeZ; z++)
            {
                for (int c = 0; c < ImageView.SelectedImage.SizeC; c++)
                {
                    for (int t = 0; t < ImageView.SelectedImage.SizeT; t++)
                    {
                        MaskData md = asm.Generate(ImageView.SelectedImage, ImageView.SelectedImage.Coords[z, c, t]);
                        for (int i = 0; i < md.mfinalMask.Count; i++)
                        {
                            if (md.mfinalMask[i].Count < ImageView.SelectedImage.SizeX * ImageView.SelectedImage.SizeY)
                            {
                                md.mfinalMask[i] = null;
                                md.mfinalMask.RemoveAt(i);
                                continue;
                            }
                            PointD loc = new PointD(ImageView.SelectedImage.StageSizeX, ImageView.SelectedImage.StageSizeY);
                            ROI an = ROI.CreateMask(new ZCT(z, c, t), md.mfinalMask[i].ToArray(), ImageView.SelectedImage.SizeX, ImageView.SelectedImage.SizeY, loc, ImageView.SelectedImage.PhysicalSizeX, ImageView.SelectedImage.PhysicalSizeY);
                            if (an.W * an.H < minArea)
                                continue;
                            if (an.W * an.H > maxArea)
                                continue;
                            byte r = (byte)rng.Next(0, 255);
                            byte g = (byte)rng.Next(0, 255);
                            byte bb = (byte)rng.Next(0, 255);
                            Color fc = Color.FromArgb(r, g, bb);
                            an.fillColor = fc;
                            ImageView.SelectedImage.Annotations.Add(an);
                        }
                        md.Dispose();
                    }
                }
            }
            BioLib.Recorder.AddLine("SAM.Run(SAM.Promotions," + minArea + "," + maxArea + "," + stability + "," + prediction + "," + layers + "," + points + ");",false);
        }

        /// The Dispose function disposes of resources and sets variables to null.
        public void Dispose()
        {
            mEncoder.Dispose();
            mDecoder.Dispose();
            GC.Collect();
        }
    }
}
