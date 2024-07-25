using System;
using System.Buffers;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gtk;
using ij;
using ij.plugin;
using ij.process;
using BioLib;
using AForge;
using java.awt.image;

namespace BioGTK
{
    public class ImageJ
    {
        static short[] ConvertByteArrayToShortArray(byte[] byteArray, bool littleEndian)
        {
            if (byteArray.Length % 2 != 0)
            {
                throw new ArgumentException("The length of the byte array must be even.");
            }

            // Each short is made from two bytes
            short[] shortArray = new short[byteArray.Length / 2];

            for (int i = 0; i < shortArray.Length; i++)
            {
                if (littleEndian)
                {
                    // If the system is little-endian, reverse the byte order
                    shortArray[i] = (short)((byteArray[i * 2 + 1] << 8) | byteArray[i * 2]);
                }
                else
                {
                    // If the system is big-endian, keep the byte order as is
                    shortArray[i] = (short)((byteArray[i * 2] << 8) | byteArray[i * 2 + 1]);
                }
            }
            return shortArray;
        }
      
        public static ImagePlus GetImagePlus(BioImage bm)
        {
            ImageStack ims = new ImageStack(bm.SizeX, bm.SizeY);
            if (bm.Buffers[0].PixelFormat == AForge.PixelFormat.Format16bppGrayScale)
            {
                // Populate the stack with 16-bit images
                for (int t = 0; t < bm.SizeT; t++)
                {
                    for (int c = 0; c < bm.SizeC; c++)
                    {
                        for (int z = 0; z < bm.SizeZ; z++)
                        {
                            short[] pixels = ConvertByteArrayToShortArray(bm.Buffers[bm.GetFrameIndex(z, c, t)].Bytes, true);
                            ImageProcessor ip = new ShortProcessor(bm.SizeX, bm.SizeY, pixels, java.awt.image.ColorModel.getRGBdefault());
                            ims.addSlice("Z" + z + "C" + c + "T" + t, ip);
                        }
                    }
                }
            }
            else if (bm.Buffers[0].PixelFormat == AForge.PixelFormat.Format8bppIndexed)
            {
                // Create a grayscale IndexColorModel
                byte[] grayscale = new byte[256];
                for (int i = 0; i < 256; i++)
                {
                    grayscale[i] = (byte)i;
                }
                IndexColorModel colorModel = new IndexColorModel(8, 256, grayscale, grayscale, grayscale);
                for (int t = 0; t < bm.SizeT; t++)
                {
                    for (int c = 0; c < bm.SizeC; c++)
                    {
                        for (int z = 0; z < bm.SizeZ; z++)
                        {
                            ImageProcessor ip = new ByteProcessor(bm.SizeX, bm.SizeY, bm.Buffers[bm.GetFrameIndex(z, c, t)].Bytes, colorModel);
                            ims.addSlice("Z" + z + "C" + c + "T" + t, ip);
                        }
                    }
                }
            }
            else if (bm.Buffers[0].PixelFormat == AForge.PixelFormat.Format24bppRgb)
            {
                for (int t = 0; t < bm.SizeT; t++)
                {
                    for (int c = 0; c < bm.SizeC; c++)
                    {
                        for (int z = 0; z < bm.SizeZ; z++)
                        {
                            byte[] bts = bm.Buffers[bm.GetFrameIndex(z, c, t)].Bytes;
                            // Convert the byte array to an int array for the RGB processor
                            int[] rgbPixels = new int[bm.SizeX * bm.SizeY];
                            for (int i = 0; i < rgbPixels.Length; i++)
                            {
                                int r = bts[i * 3] & 0xFF; // Red
                                int g = bts[i * 3 + 1] & 0xFF; // Green
                                int b = bts[i * 3 + 2] & 0xFF; // Blue
                                rgbPixels[i] = (r << 16) | (g << 8) | b; // Combine into RGB int
                            }
                            // Create a ColorProcessor
                            ImageProcessor ip = new ColorProcessor(bm.SizeX, bm.SizeY, rgbPixels);
                            ims.addSlice("Z" + z + "C" + c + "T" + t, ip);
                        }
                    }
                }
            }
            else if(bm.Buffers[0].PixelFormat == AForge.PixelFormat.Format32bppArgb || bm.Buffers[0].PixelFormat == AForge.PixelFormat.Format32bppRgb)
            {
                for (int t = 0; t < bm.SizeT; t++)
                {
                    for (int c = 0; c < bm.SizeC; c++)
                    {
                        for (int z = 0; z < bm.SizeZ; z++)
                        {
                            byte[] bts = bm.Buffers[bm.GetFrameIndex(z, c, t)].Bytes;
                            // Convert the byte array to an int array for the ColorProcessor
                            int[] rgbaPixels = new int[bm.SizeX * bm.SizeY];
                            for (int i = 0; i < rgbaPixels.Length; i++)
                            {
                                int r = bts[i * 4] & 0xFF;       // Red
                                int g = bts[i * 4 + 1] & 0xFF;   // Green
                                int b = bts[i * 4 + 2] & 0xFF;   // Blue
                                int a = bts[i * 4 + 3] & 0xFF;   // Alpha
                                rgbaPixels[i] = (a << 24) | (r << 16) | (g << 8) | b; // Combine into ARGB int
                            }
                            // Create a ColorProcessor with the ARGB int array
                            ImageProcessor ip = new ColorProcessor(bm.SizeX, bm.SizeY, rgbaPixels);
                            ims.addSlice("Z" + z + "C" + c + "T" + t, ip);
                        }
                    }
                }
            }
            else if (bm.Buffers[0].PixelFormat == AForge.PixelFormat.Format48bppRgb)
            {
                bm.To16Bit();
                // Populate the stack with 16-bit images
                for (int t = 0; t < bm.SizeT; t++)
                {
                    for (int c = 0; c < bm.SizeC; c++)
                    {
                        for (int z = 0; z < bm.SizeZ; z++)
                        {
                            short[] pixels = ConvertByteArrayToShortArray(bm.Buffers[bm.GetFrameIndex(z, c, t)].Bytes, true);
                            ImageProcessor ip = new ShortProcessor(bm.SizeX, bm.SizeY, pixels, java.awt.image.ColorModel.getRGBdefault());
                            ims.addSlice("Z" + z + "C" + c + "T" + t, ip);
                        }
                    }
                }
            }
            ImagePlus imp = new ImagePlus(bm.ID, ims);
            imp.setDimensions(bm.SizeC, bm.SizeZ, bm.SizeT);
            return imp;
        }

        public static bool isRGB(ImagePlus image)
        {
            return image.getType() == ImagePlus.COLOR_RGB;
        }

        public static int[] getRGBPixelsFromSlice(ImageStack stack, int sliceIndex)
        {
            // Get the ImageProcessor for the specified slice
            ImageProcessor ip = stack.getProcessor(sliceIndex + 1); // ImageStack uses 1-based indexing
            // Cast to ColorProcessor to access RGB data
            ColorProcessor colorProcessor = (ColorProcessor)ip;
            // Get the RGB pixel data
            return (int[])colorProcessor.getPixels();
        }
        /// <summary>
        /// Converts ImagePlus to BioImage.
        /// </summary>
        /// <param name="pl"></param>
        /// <param name="maxz"></param>
        /// <param name="maxc"></param>
        /// <param name="maxt"></param>
        /// <param name="location"></param>
        /// <returns>The BioImage represented by the ImagePlus object.</returns>
        public static BioImage GetBioImage(ImagePlus pl, VolumeD vol, double PhysicalX, double PhysicalY, double PhysicalZ)
        {
            BioImage bm = new BioImage(pl.getTitle());
            ImageStack st = pl.getImageStack();
            int b = pl.getBitDepth();
            int slices = pl.getNSlices();
            int chs = pl.getNChannels();
            int frs = pl.getNFrames();
            int rgb = (int)(pl.getBytesPerPixel() * 8);
            bool isrgb = isRGB(pl);
            bm.UpdateCoords(slices,chs,frs,BioImage.Order.TCZ);
            for (int t = 0; t < frs; t++)
            {
                for (int c = 0; c < chs; c++)
                {
                    for (int z = 0; z < slices; z++)
                    {
                        AForge.Bitmap bmp;
                        if (!isrgb)
                        {
                            if (rgb > 8)
                            {
                                bmp = new AForge.Bitmap(st.getWidth(), st.getHeight(), AForge.PixelFormat.Format16bppGrayScale);
                                bm.Buffers.Add(bmp);
                            }
                            else
                            {
                                bmp = new AForge.Bitmap(st.getWidth(), st.getHeight(), AForge.PixelFormat.Format8bppIndexed);
                                bm.Buffers.Add(bmp);
                            }
                        }
                        else
                        {
                            bmp = new AForge.Bitmap(st.getWidth(), st.getHeight(), AForge.PixelFormat.Format24bppRgb);
                            bm.Buffers.Add(bmp);
                        }
                        int ind = bm.GetFrameIndex(z, c, t);
                        if (!isrgb)
                        {
                            for (int y = 0; y < pl.getHeight(); y++)
                            {
                                for (int x = 0; x < pl.getWidth(); x++)
                                {
                                    double d = st.getVoxel(x, y, ind);
                                    if (bm.Buffers[0].PixelFormat == PixelFormat.Format16bppGrayScale)
                                        bmp.SetValue(x, y, (ushort)d);
                                    else if (bm.Buffers[0].PixelFormat == PixelFormat.Format8bppIndexed)
                                        bmp.SetValue(x, y, (byte)d);
                                }
                            }
                        }
                        else
                        {
                            int[] rgbPixels = getRGBPixelsFromSlice(st, ind);
                            // Get the ImageProcessor for the specified slice
                            ImageProcessor ip = st.getProcessor(ind + 1); // ImageStack uses 1-based indexing
                            // Cast to ColorProcessor to access RGB data
                            ColorProcessor colorProcessor = (ColorProcessor)ip;
                            for (int y = 0; y < pl.getHeight(); y++)
                            {
                                for (int x = 0; x < pl.getWidth(); x++)
                                {
                                    int v = colorProcessor.getPixel(x, y);
                                    int rv = (v >> 16) & 0xFF; // Red
                                    int gv = (v >> 8) & 0xFF;  // Green
                                    int bv = v & 0xFF;         // Blue
                                    bmp.SetValue(x, y, 0, (byte)rv);
                                    bmp.SetValue(x, y, 1, (byte)gv);
                                    bmp.SetValue(x, y, 2, (byte)bv);
                                }
                            }
                            
                        }
                    }
                }
            }
            for(int t = 0;t < chs; t++)
            {
                if(isrgb)
                    bm.Channels.Add(new AForge.Channel(t, rgb, 3));
                else
                    bm.Channels.Add(new AForge.Channel(t, rgb, 1));
                if (t == 0)
                {
                    bm.rgbChannels[0] = 0;
                }
                else
                if (t == 1)
                {
                    bm.rgbChannels[1] = 1;
                }
                else
                if (t == 2)
                {
                    bm.rgbChannels[2] = 2;
                }
            }
            bm.Resolutions.Add(new Resolution(pl.getWidth(), pl.getHeight(), bm.Buffers[0].PixelFormat, PhysicalX, PhysicalY, PhysicalZ, vol.Location.X, vol.Location.Y, vol.Location.X));
            bm.Volume = vol;
            bm.littleEndian = BitConverter.IsLittleEndian;
            bm.seriesCount = 1;
            bm.bitsPerPixel = b;
            BioImage.AutoThreshold(bm, true);
            if (rgb > 8)
                bm.StackThreshold(true);
            else
                bm.StackThreshold(false);

            if (bm.RGBChannelCount == 4)
            {
                bm.Channels.Last().SamplesPerPixel = 4;
            }
            return bm;
        }
    }
}
