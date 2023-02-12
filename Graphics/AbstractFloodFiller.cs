using System;
using System.Collections.Generic;
using System.Text;
using System.Diagnostics;
using AForge;

namespace Bio.Graphics
{
    /// <summary>
    /// The base class that the flood fill algorithms inherit from. Implements the
    /// basic flood filler functionality that is the same across all algorithms.
    /// </summary>
    public abstract class AbstractFloodFiller
    {

        protected Bitmap bitmap;
        protected ColorS tolerance = new ColorS(25, 25, 25);
        protected ColorS fillColor = ColorS.FromColor(Color.Black);
        protected bool fillDiagonally = false;
        protected bool slow = false;

        //cached bitmap properties
        protected int bitmapWidth = 0;
        protected int bitmapHeight = 0;
        protected int bitmapStride = 0;
        protected int bitmapPixelFormatSize = 0;
        protected byte[] bitmapBits = null;
        protected PixelFormat pixelFormat;

        //internal int timeBenchmark = 0;
        internal Stopwatch watch = new Stopwatch();

        //internal, initialized per fill
        //protected BitArray pixelsChecked;
        protected bool[] pixelsChecked;
        protected ColorS byteFillColor;
        protected ColorS startColor;
        //protected int stride;

        public AbstractFloodFiller()
        {

        }

        /* A constructor that takes an AbstractFloodFiller object as a parameter. It then sets the
        properties of the object to the properties of the parameter. */
        public AbstractFloodFiller(AbstractFloodFiller configSource)
        {
            if (configSource != null)
            {
                this.Bitmap = configSource.Bitmap;
                this.FillColor = configSource.FillColor;
                this.FillDiagonally = configSource.FillDiagonally;;
                this.Tolerance = configSource.Tolerance;
            }
        }

        public ColorS FillColor
        {
            get { return fillColor; }
            set { fillColor = value; }
        }

        public bool FillDiagonally
        {
            get { return fillDiagonally; }
            set { fillDiagonally = value; }
        }

        public ColorS Tolerance
        {
            get { return tolerance; }
            set { tolerance = value; }
        }

        public Bitmap Bitmap
        {
            get { return bitmap; }
            set 
            { 
                bitmap = value;
            }
        }

        /// It fills the area of the image that is connected to the point pt with the current color
        /// 
        /// @param pt The starting point for the fill.
        public abstract void FloodFill(Point pt);
        /// It takes a point on the bitmap and gets the color of that point. It then creates a new color
        /// that is the fill color. It then gets the stride, pixel format size, pixel format, and the
        /// bitmap bits. It then creates a new boolean array that is the size of the bitmap bits divided
        /// by the pixel format size
        /// 
        /// @param pt The point to start the flood fill from.
        protected void PrepareForFloodFill(Point pt)
        {
            startColor = bitmap.GetPixel((int)pt.X, (int)pt.Y);
            byteFillColor = new ColorS(fillColor.B, fillColor.G, fillColor.R);
            bitmapStride=bitmap.Stride;
            bitmapPixelFormatSize=bitmap.PixelFormatSize;
            pixelFormat = bitmap.PixelFormat;
            bitmapBits = bitmap.Bytes;
            bitmapWidth = bitmap.SizeX;
            bitmapHeight = bitmap.SizeY;
            pixelsChecked = new bool[bitmapBits.Length / bitmapPixelFormatSize];
        }
    }
}
