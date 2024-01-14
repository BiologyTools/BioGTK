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

    class SAM : IDisposable
    {
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
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("x", tensor)
                };

                var results = this.mEncoder.Run(inputs);
                if (b.Tag == null)
                {
                    b.Tag = new List<float[]>();
                }
                List<float[]> l = (List<float[]>)b.Tag;
                l.Add(results.First().AsTensor<float>().ToArray());
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
            var embedding_tensor = new DenseTensor<float>(lts[b.Coords[c.Z,c.C,c.T]], new[] { 1, 256, 64, 64 });
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

            float[] orig_im_size_values = { (float)orgHei, (float)orgWid };
            var orig_im_size_values_tensor = new DenseTensor<float>(orig_im_size_values, new[] { 2 });

            var decode_inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", embedding_tensor),
                NamedOnnxValue.CreateFromTensor("point_coords", point_coords_tensor),
                NamedOnnxValue.CreateFromTensor("point_labels", point_label_tensor),
                NamedOnnxValue.CreateFromTensor("mask_input", mask_tensor),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskValues_tensor),
                NamedOnnxValue.CreateFromTensor("orig_im_size", orig_im_size_values_tensor)
            };

            var segmask = this.mDecoder.Run(decode_inputs);
            var outputmask = segmask.First().AsTensor<float>().ToArray();
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

            float[] orig_im_size_values = { (float)orgHei, (float)orgWid };
            var orig_im_size_values_tensor = new DenseTensor<float>(orig_im_size_values, new[] { 2 });

            var decode_inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("image_embeddings", embedding_tensor),
                NamedOnnxValue.CreateFromTensor("point_coords", point_coords_tensor),
                NamedOnnxValue.CreateFromTensor("point_labels", point_label_tensor),
                NamedOnnxValue.CreateFromTensor("mask_input", mask_tensor),
                NamedOnnxValue.CreateFromTensor("has_mask_input", hasMaskValues_tensor),
                NamedOnnxValue.CreateFromTensor("orig_im_size", orig_im_size_values_tensor)
            };
            MaskData md = new MaskData();
            var segmask = this.mDecoder.Run(decode_inputs).ToList();
            md.mMask = segmask[0].AsTensor<float>().ToArray().ToList();
            md.mShape = segmask[0].AsTensor<float>().Dimensions.ToArray();
            md.mIoU = segmask[1].AsTensor<float>().ToList();
            return md;

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
