using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AForge;
using Gtk;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using MathNet.Numerics.Statistics;
namespace BioGTK.ML
{
    public static class ML
    {
        public class Model
        {
            public Model(string file) 
            {
                File = file;
                Name = Path.GetFileNameWithoutExtension(file);
                InferenceSession = new InferenceSession(file);
                foreach (var m in InferenceSession.InputMetadata)
                {
                    InputValueTypes.Add(m.Value);
                }
                foreach (var m in InferenceSession.InputNames)
                {
                    InputValueNames.Add(m);
                }
                foreach(var m in InferenceSession.OutputMetadata)
                {
                    OutputValueTypes.Add(m.Value);
                }
            }
            public InferenceSession InferenceSession { get; set; }
            public List<NodeMetadata> InputValueTypes { get; set; } = new List<NodeMetadata>();
            public List<NodeMetadata> OutputValueTypes { get; set; } = new List<NodeMetadata>();
            public List<string> InputValueNames { get; set; } = new List<string>();
            public string File { get; set; }
            public string Name { get; set; }
            public int Width
            {
                get
                {
                    if(InputValueTypes.Count>0)
                    {
                        int w = InputValueTypes[0].Dimensions[2];
                        if(w == 0)
                        {
                            if(OutputValueTypes.Count > 0) 
                            {
                                for(int i = 0; i < OutputValueTypes.Count; i++)
                                {
                                    int max = int.MinValue;
                                    for (int j = 0; j < OutputValueTypes[i].Dimensions.Length; j++)
                                    {
                                        if (OutputValueTypes[i].Dimensions[j] > max)
                                            max = OutputValueTypes[i].Dimensions[j];
                                    }
                                    return max;
                                }
                            }
                        }
                        return w;
                    }
                    return 0;
                }
            }
            public int Height
            {
                get
                {
                    if (InputValueTypes.Count > 0)
                    {
                        int h = InputValueTypes[0].Dimensions[3];
                        if (h == 0)
                        {
                            if (OutputValueTypes.Count > 0)
                            {
                                for (int i = 0; i < OutputValueTypes.Count; i++)
                                {
                                    int max = int.MinValue;
                                    for (int j = 0; j < OutputValueTypes[i].Dimensions.Length; j++)
                                    {
                                        if (OutputValueTypes[i].Dimensions[j] > max)
                                            max = OutputValueTypes[i].Dimensions[j];
                                    }
                                    return max;
                                }
                            }
                        }
                        return h;
                    }
                    return 0;
                }
            }
            public ModelOutputType ModelType { get;set; }
            public enum ModelOutputType
            {
                classification,
                image
            }

            public void Run(BioImage b)
            {
                BioImage bb = new BioImage("output.ome.tif");
                if (b.Type != BioImage.ImageType.pyramidal)
                {
                    for (int i = 0; i < b.Buffers.Count; i++)
                    {
                        Bitmap bm = ResizeBilinear(b.Buffers[i],Width,Height);
                        int w = Width;
                        int h = Height;
                        int d = InputValueTypes[0].Dimensions[1];
                        float[] img = new float[w * h * d];
                        for (int y = 0; y < h; y++)
                        {
                            for (int x = 0; x < w; x++)
                            {
                                for (int c = 0; c < d; c++)
                                {
                                    int index = (y * w * d) + x;
                                    if (c > bm.RGBChannelsCount)
                                    {
                                        img[index] = 0;
                                    }
                                    else
                                    {
                                        if (bm.BitsPerPixel > 8)
                                            img[index] = bm.GetValueRGB(x, y, c);
                                        else
                                            img[index] = bm.GetValueRGB(x, y, c);
                                    }
                                }
                            }
                        }
                        var tensor = new DenseTensor<float>(img, new[] { 1, InputValueTypes[0].Dimensions[1], w, h });
                        var inputs = new List<NamedOnnxValue>
                        {
                            NamedOnnxValue.CreateFromTensor(InputValueNames[0], tensor)
                        };
                        var results = InferenceSession.Run(inputs);
                        int r = 0;
                        foreach (var result in results)
                        {
                            if (OutputValueTypes[r].Dimensions[3] == Width)
                            {
                                float[] outputmask = result.AsTensor<float>().ToArray();
                                double mean = outputmask.Mean();
                                for (int c = 0; c < OutputValueTypes[r].Dimensions[1]; c++)
                                {
                                    Bitmap bmp = new Bitmap(Width, Height, b.Buffers[0].PixelFormat);
                                    for (int y = 0; y < h; y++)
                                    {
                                        for (int x = 0; x < w; x++)
                                        {
                                            int index = (y * w * d) + x;
                                            bmp.SetPixel(x, y, new ColorS((ushort)(outputmask[index]/mean)));
                                        }
                                    }
                                    bb.Buffers.Add(ResizeBilinear(bmp, b.SizeX, b.SizeY));
                                }
                            }
                            r++;
                        }
                    }
                    bb.Channels.AddRange(b.Channels);
                    if(OutputValueTypes[0].Dimensions[1] > 1)
                    {
                        bb.Channels.AddRange(b.Channels);
                        for (int i = 0; i < bb.Channels.Count; i++)
                        {
                            bb.Channels[i].Index = i;
                        }
                    }
                    bb.UpdateCoords(b.SizeZ, b.SizeC * OutputValueTypes[0].Dimensions[1], b.SizeT);
                    bb.Resolutions.Add(new Resolution(bb.SizeX, bb.SizeY, bb.Buffers[0].PixelFormat,b.PhysicalSizeX,b.PhysicalSizeY,b.PhysicalSizeZ,b.StageSizeX,b.StageSizeY,b.StageSizeZ));
                    bb.Volume = b.Volume;
                    bb.bitsPerPixel = bb.Buffers[0].BitsPerPixel;
                    
                    BioImage.AutoThreshold(bb, true);
                    Images.AddImage(bb, true);
                }
                else
                    throw new NotImplementedException();
            }
        }
        public static List<Model> Models = new List<Model>();
        public static void Load(string mod)
        {
            Models.Add(new Model(mod));
        }
        
        public static void Run(string modelsName, BioImage image)
        {
            foreach (var m in Models)
            {
                if(m.Name == Path.GetFileNameWithoutExtension(modelsName))
                {
                    m.Run(image);
                }
            }
        }
        public static Bitmap ResizeBilinear(Bitmap original, int targetWidth, int targetHeight)
        {
            // Create a new Pixbuf with target dimensions
            Bitmap resized = new Bitmap(targetWidth,targetHeight,original.PixelFormat);

            int originalWidth = original.Width;
            int originalHeight = original.Height;
            float xRatio = originalWidth / (float)targetWidth;
            float yRatio = originalHeight / (float)targetHeight;

            // Lock the bits of the original and resized images (if necessary)

            for (int y = 0; y < targetHeight; y++)
            {
                for (int x = 0; x < targetWidth; x++)
                {
                    float gx = ((float)x) * xRatio;
                    float gy = ((float)y) * yRatio;
                    int gxi = (int)gx;
                    int gyi = (int)gy;

                    ColorS topLeft = original.GetPixel(gxi, gyi);
                    ColorS topRight = original.GetPixel(gxi + 1, gyi);
                    ColorS bottomLeft = original.GetPixel(gxi, gyi + 1);
                    ColorS bottomRight = original.GetPixel(gxi + 1, gyi + 1);

                    ushort red = (ushort)BilinearInterpolate(gx - gxi, gy - gyi, topLeft.R, topRight.R, bottomLeft.R, bottomRight.R);
                    ushort green = (ushort)BilinearInterpolate(gx - gxi, gy - gyi, topLeft.G, topRight.G, bottomLeft.G, bottomRight.G);
                    ushort blue = (ushort)BilinearInterpolate(gx - gxi, gy - gyi, topLeft.B, topRight.B, bottomLeft.B, bottomRight.B);

                    resized.SetPixel(x, y, new ColorS(red, green, blue));
                }
            }

            // Unlock the bits (if necessary)

            return resized;
        }
        private static float BilinearInterpolate(float x, float y, float topLeft, float topRight, float bottomLeft, float bottomRight)
        {
            float top = Interpolate(x, topLeft, topRight);
            float bottom = Interpolate(x, bottomLeft, bottomRight);
            return Interpolate(y, top, bottom);
        }

        private static float Interpolate(float t, float a, float b)
        {
            return a + (b - a) * t;
        }

        // Implement GetPixel and SetPixel methods based on how Gdk.Pixbuf manages pixel data

        public static void Initialize()
        {
            string st = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
            foreach(string f in Directory.GetFiles(st + "/Models"))
            {
                Load(f);
                string path = "Run/" + Path.GetFileName(f);
                App.AddMenu(path);
            }
        }
    }
}
