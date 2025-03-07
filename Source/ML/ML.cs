﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AForge;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using TorchSharp;
using MathNet.Numerics.Statistics;
using YamlDotNet;
using YamlDotNet.Serialization.NamingConventions;
using YamlDotNet.Serialization;
using System.Collections;
using Bitmap = AForge.Bitmap;

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
                string dir = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
                File = dir.Replace("\\","/") + "/Models/" + file;
                if (file.EndsWith(".onnx"))
                {
                    InferenceSession = new InferenceSession(file);
                    foreach (var m in InferenceSession.InputMetadata)
                    {
                        InputValueTypes.Add(m.Value);
                    }
                    foreach (var m in InferenceSession.InputNames)
                    {
                        InputValueNames.Add(m);
                    }
                    foreach (var m in InferenceSession.OutputMetadata)
                    {
                        OutputValueTypes.Add(m.Value);
                    }
                }
                else if (file.EndsWith(".pt"))
                {
                    try
                    {
                        //torch.load(File);
                        Module = torch.jit.load(File);
                        Module.eval();
                        var chs = Module.named_modules();
                        var input = chs.First();
                        InputModule = input.module;
                        var output = chs.Last();
                        OutputModule = output.module;
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine("Failed to run pytorch model. Ensure that PyTorch is properly installed.");
                        Console.WriteLine(e.Message.ToString());
                    }
                    string f = System.IO.Path.GetDirectoryName(System.Environment.ProcessPath) + "/Models/" + Path.GetFileNameWithoutExtension(file) + ".yaml";
                    if (!System.IO.File.Exists(f))
                    {
                        Gtk.MessageDialog md = new Gtk.MessageDialog(App.tabsView, Gtk.DialogFlags.Modal, Gtk.MessageType.Error, Gtk.ButtonsType.Ok,"No corresponding model Yaml metadata file for:" + Name);
                        md.Show();
                        md.Run();
                        return;
                    }
                    using (var reader = new StreamReader(f))
                    {
                        var dynamicDeserializer = new DeserializerBuilder().Build();
                        Metadata = dynamicDeserializer.Deserialize<Dictionary<string, object>>(reader);
                        IDictionary conf = (IDictionary)Metadata["config"];
                        Object inpp = Metadata["inputs"];
                        List<Object> inpps = (List<Object>)inpp;
                        IDictionary inpinfo = (IDictionary)inpps[0];
                        IDictionary imagej = (IDictionary)conf["deepimagej"];
                        IDictionary test = (IDictionary)imagej["test_information"];
                        Object inputs = test["inputs"];
                        List<Object> inps = (List<Object>)inputs;
                        IDictionary info = (IDictionary)inps[0];
                        IDictionary inputinfo = (IDictionary)inpps[0];
                        IDictionary shapeinfo = (IDictionary)inputinfo["shape"];
                        if (shapeinfo != null)
                        {
                            List<Object> shapeinfomin = (List<Object>)shapeinfo["min"];
                            int len = shapeinfomin.Count;
                            string size = (string)info["size"];
                            string[] sts = size.Split("x");
                            if (len != sts.Length)
                            {
                                shape = new long[len];
                                for (int i = 0; i < len; i++)
                                {
                                    if (len - i > sts.Length)
                                    {
                                        shape[i] = 1;
                                    }
                                    else
                                    {
                                        shape[i] = long.Parse(sts[sts.Length - i]);
                                    }
                                }
                            }
                            else
                            {
                                shape = new long[sts.Length];
                                for (int i = 0; i < sts.Length; i++)
                                {
                                    String s = sts[i];
                                    shape[i] = long.Parse(s);
                                }
                            }
                            List<Object> val = (List<Object>)inpinfo["data_range"];
                            MaxValue = int.Parse(val[1].ToString());
                        }
                        else
                        {
                            string sh = (string)info["size"];
                            string[] sts = (string[])sh.Split("x");
                            IList inp = (IList)inputinfo["axes"];
                            int co = inp.Count;
                            shape = new long[co];
                            for (int i = 0; i < co; i++)
                            {
                                IDictionary dict = (IDictionary)inp[i];
                                string type = (string)dict["type"];
                                string id = (string)dict["id"];
                                if (type == "batch")
                                {
                                    shape[co - 5] = 1;
                                }
                                if (type == "channel")
                                {
                                    shape[co - 4] = long.Parse(sts[3]);
                                }
                                if (id == "z")
                                {
                                    shape[co - 3] = long.Parse(sts[2]);
                                }
                                if (id == "y")
                                {
                                    shape[co - 2] = long.Parse(sts[1]);
                                }
                                if (id == "x")
                                {
                                    shape[co - 1] = long.Parse(sts[0]);
                                }
                            }
                            MaxValue = 256;
                        }
                    }
                }
                BioLib.Recorder.AddLine("BioGTK.ML.Model model = new BioGTK.ML.Model(\"" + file + "\");",false);
            }
            public Dictionary<string, object> Metadata { get; set; } = null;
            public InferenceSession InferenceSession { get; set; }
            public torch.jit.ScriptModule Module { get; set; }
            public torch.nn.Module InputModule { get; set; }
            public torch.nn.Module OutputModule { get; set; }
            private List<NodeMetadata> InputValueTypes { get; set; } = new List<NodeMetadata>();
            private List<NodeMetadata> OutputValueTypes { get; set; } = new List<NodeMetadata>();
            private List<string> InputValueNames { get; set; } = new List<string>();
            public string File { get; set; }
            public string Name { get; set; }
            public int Width
            {
                get
                {
                    if (File.EndsWith(".onnx"))
                    {
                        if (InputValueTypes.Count > 0)
                        {
                            int w = InputValueTypes[0].Dimensions[2];
                            if (w == 0)
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
                            return w;
                        }
                        return 0;
                    }
                    else
                    {
                        if (shape.Length == 4)
                            return (int)Shape[0];
                        else
                            return (int)Shape[4];
                    }
                }
            }
            public int Height
            {
                get
                {
                    if (File.EndsWith(".onnx"))
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
                    else
                    {
                        if (shape.Length == 4)
                            return (int)Shape[1];
                        else
                            return (int)Shape[3];
                    }
                }
            }
            public int Depth
            {
                get
                {
                    if (shape.Length == 4)
                        return (int)Shape[2];
                    else
                        return (int)shape[1];
                }
            }
            long[] shape;
            public long[] Shape
            {
                get
                {
                    if (Metadata == null)
                        return new long[] { 1, 3, 256, 256 };
                    else
                    {
                        return shape;
                    }
                }
            }
            public int MaxValue { get; set; }
            public ModelOutputType ModelType { get; set; }
            public enum ModelOutputType
            {
                classification,
                image
            }
            public void Run(BioImage b)
            {
                if (File.EndsWith(".onnx"))
                    RunONNX(b);
                else if (File.EndsWith(".pt"))
                    RunTorch(b);
                BioLib.Recorder.AddLine("model.Run(Images.GetImage(\"" + b.Filename + "\");",false);
            }
            public void RunONNX(BioImage b)
            {
                BioImage bb = new BioImage("output.ome.tif");
                if (b.Type != BioImage.ImageType.pyramidal)
                {
                    for (int i = 0; i < b.Buffers.Count; i++)
                    {
                        Bitmap bm = ResizeBilinear(b.Buffers[i], Width, Height);
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
                                            img[index] = bm.GetValue(x, y, c);
                                        else
                                            img[index] = bm.GetValue(x, y, c);
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
                                            bmp.SetPixel(x, y, new ColorS((ushort)(outputmask[index] / mean)));
                                        }
                                    }
                                    bb.Buffers.Add(ResizeBilinear(bmp, b.SizeX, b.SizeY));
                                }
                            }
                            r++;
                        }
                    }
                    bb.Channels.AddRange(b.Channels);
                    if (OutputValueTypes[0].Dimensions[1] > 1)
                    {
                        bb.Channels.AddRange(b.Channels);
                        for (int i = 0; i < bb.Channels.Count; i++)
                        {
                            bb.Channels[i].Index = i;
                        }
                    }
                    bb.UpdateCoords(b.SizeZ, b.SizeC * OutputValueTypes[0].Dimensions[1], b.SizeT);
                    bb.Resolutions.Add(new Resolution(bb.SizeX, bb.SizeY, bb.Buffers[0].PixelFormat, b.PhysicalSizeX, b.PhysicalSizeY, b.PhysicalSizeZ, b.StageSizeX, b.StageSizeY, b.StageSizeZ));
                    bb.Volume = b.Volume;
                    bb.bitsPerPixel = bb.Buffers[0].BitsPerPixel;

                    BioImage.AutoThreshold(bb, true);
                    Images.AddImage(bb);
                }
                else
                    throw new NotImplementedException();
            }
            public void RunTorch(BioImage b)
            {
                BioImage bb = new BioImage("output.ome.tif");
                if (b.Type != BioImage.ImageType.pyramidal)
                {
                    int r = 0;
                    int resDepth = 1;
                    if (shape.Length == 4)
                    {
                        for (int i = 0; i < b.Buffers.Count; i++)
                        {
                            Bitmap bm = ResizeBilinear(b.Buffers[i], Width, Height);
                            int w = Width;
                            int h = Height;
                            int d = Depth;
                            float[,,,] img;
                            if (shape[0] > shape[3])
                            {
                                img = new float[1, Depth, Width, Height];
                            }
                            else
                            {
                                img = new float[Width, Height, Depth, 1];
                            }
                            for (int y = 0; y < h; y++)
                            {
                                for (int x = 0; x < w; x++)
                                {
                                    for (int c = 0; c < d; c++)
                                    {
                                        int index = (y * w * c) + x;
                                        if (c >= bm.RGBChannelsCount)
                                        {
                                            img[0, c, x, y] = 0;
                                        }
                                        else
                                        {
                                            if (bm.BitsPerPixel > 8)
                                                img[0, c, x, y] = ((float)bm.GetValue(x, y, c) / (float)ushort.MaxValue) * MaxValue;
                                            else
                                                img[0, c, x, y] = ((float)bm.GetValue(x, y, c) / (float)byte.MaxValue) * MaxValue;
                                        }
                                    }
                                }
                            }
                            var tensor = torch.tensor(img);
                            object results = null;
                            try
                            {
                                results = Module.forward(tensor);
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e.ToString());
                            }
                            TorchSharp.Utils.TensorAccessor<float> outputmask = ((torch.Tensor)results).data<float>();
                            double mean = outputmask.Mean();
                            resDepth = (int)((torch.Tensor)results).shape[1];
                            for (int c = 0; c < resDepth; c++)
                            {
                                Bitmap bmp = new Bitmap(Width, Height, b.Buffers[0].PixelFormat);
                                for (int y = 0; y < h; y++)
                                {
                                    for (int x = 0; x < w; x++)
                                    {
                                        int index = (y * w * c) + x;
                                        bmp.SetPixel(x, y, new ColorS((ushort)(outputmask[index] / mean)));
                                    }
                                }
                                bb.Buffers.Add(ResizeBilinear(bmp, b.SizeX, b.SizeY));
                                bb.Buffers[bb.Buffers.Count - 1].RotateFlip(AForge.RotateFlipType.Rotate90FlipX);
                            }
                            r++;

                        }
                    }
                    else if (shape.Length == 5)
                    {
                        torch.Tensor tensor = torch.empty(shape);
                        for (int i = 0; i < shape[2]; i++)
                        {
                            Bitmap bm = ResizeBilinear(b.Buffers[i], Width, Height);
                            for (int y = 0; y < Height; y++)
                            {
                                for (int x = 0; x < Width; x++)
                                {
                                    for (int c = 0; c < Depth; c++)
                                    {
                                        if (c >= bm.RGBChannelsCount)
                                        {
                                            tensor[0, c, i, x, y] = 0;
                                        }
                                        else
                                        {
                                            if (bm.BitsPerPixel > 8)
                                                tensor[0, c, i, x, y] = ((float)bm.GetValue(x, y, c) / (float)ushort.MaxValue) * MaxValue;
                                            else
                                                tensor[0, c, i, x, y] = ((float)bm.GetValue(x, y, c) / (float)byte.MaxValue) * MaxValue;
                                        }
                                    }
                                }
                            }
                        }
                        torch.Tensor outputmask = (torch.Tensor)Module.forward(tensor);
                        resDepth = (int)(outputmask.shape[1]);
                        for (int c = 0; c < resDepth; c++)
                        {
                            for (int buf = 0; buf < (int)outputmask.shape[2]; buf++)
                            {
                                Bitmap bmp = new Bitmap((int)outputmask.shape[3], (int)outputmask.shape[4], b.Buffers[0].PixelFormat);
                                for (int y = 0; y < Height; y++)
                                {
                                    for (int x = 0; x < Width; x++)
                                    {
                                        float by = outputmask[0, c, buf, x, y].ToSingle();
                                        bmp.SetPixel(x, y, new ColorS((ushort)(by * byte.MaxValue)));
                                    }
                                }
                                bb.Buffers.Add(ResizeBilinear(bmp, b.SizeX, b.SizeY));
                                bb.Buffers[bb.Buffers.Count - 1].RotateFlip(AForge.RotateFlipType.Rotate90FlipX);
                            }
                        }
                    }
                    bb.Channels.AddRange(b.Channels);
                    if (resDepth > 1)
                    {
                        bb.Channels.AddRange(b.Channels);
                        for (int i = 0; i < bb.Channels.Count; i++)
                        {
                            bb.Channels[i].Index = i;
                        }
                    }
                    bb.UpdateCoords(b.SizeZ, b.SizeC * resDepth, b.SizeT);
                    bb.Resolutions.Add(new Resolution(bb.SizeX, bb.SizeY, bb.Buffers[0].PixelFormat, b.PhysicalSizeX, b.PhysicalSizeY, b.PhysicalSizeZ, b.StageSizeX, b.StageSizeY, b.StageSizeZ));
                    bb.Volume = b.Volume;
                    bb.bitsPerPixel = bb.Buffers[0].BitsPerPixel;
                    BioImage.AutoThreshold(bb, true);
                    App.tabsView.AddTab(bb);
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
            bool loaded = false;
            foreach (var item in Models)
            {
                if (item.File.Contains(image.Filename))
                    loaded = true;
            }
            if (!loaded)
                Load(modelsName);
            string st = System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/Models/" + modelsName;
            foreach (var m in Models)
            {
                if (m.Name == Path.GetFileNameWithoutExtension(modelsName))
                {
                    loaded = true;
                    break;
                }
            }
            if(!loaded)
            {
                Models.Add(new Model(st));
            }
            foreach (var m in Models)
            {
                if (m.Name == Path.GetFileNameWithoutExtension(modelsName))
                {
                    m.Run(image);
                }
            }
        }
        public static Bitmap ResizeBilinear(Bitmap original, int targetWidth, int targetHeight)
        {
            AForge.Imaging.Filters.ResizeBilinear re = new AForge.Imaging.Filters.ResizeBilinear(targetWidth, targetHeight);
            return re.Apply(original);
        }
       
        public static void Initialize()
        {
            string st = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
            foreach (string f in Directory.GetFiles(st + "/Models"))
            {
                if (f.EndsWith(".onnx") || f.EndsWith(".pt"))
                {
                    string path = "Run/" + Path.GetFileName(f);
                    App.AddMenu(path);
                }
            }
        }
    }
}
