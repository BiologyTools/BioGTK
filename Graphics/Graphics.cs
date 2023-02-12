using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AForge;
using Bitmap = AForge.Bitmap; 

namespace Bio.Graphics
{
    /* A struct that holds the color, width and bits per pixel of the pen. */
    public struct Pen : IDisposable
    {
        public ColorS color;
        public ushort width;
        public byte bitsPerPixel;
        /* A constructor for the Pen struct. */
        public Pen(ColorS col, ushort w, int bitsPerPixel)
        {
            color = col;
            width = w;
            this.bitsPerPixel = (byte)bitsPerPixel;
        }
        /* A constructor for the Pen struct. */
        public Pen(ColorS col, int w, int bitsPerPixel)
        {
            color = col;
            width = (ushort)w;
            this.bitsPerPixel = (byte)bitsPerPixel;
        }
        /// > Dispose() is a function that is called when the object is no longer needed
        public void Dispose()
        {
            color.Dispose();
        }
    }
    public class Graphics : IDisposable
    {
        /* The buffer that the graphics are drawn on. */
        public Bitmap buf;
        /* A variable that holds the color, width and bits per pixel of the pen. */
        public Pen pen;
        /* Used for the flood fill algorithm. */
        private AbstractFloodFiller filler;
       /// It creates a new Graphics object, sets the buffer to the bitmap passed in, and sets the pen
       /// to a white pen with a width of 1 and a color depth of the bitmap's color depth
       /// 
       /// @param Bitmap The bitmap to draw on
       /// 
       /// @return A new Graphics object.
        public static Graphics FromImage(Bitmap b)
        {
            Graphics g = new Graphics();
            g.buf = b;
            g.pen = new Pen(new ColorS(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue),1,b.BitsPerPixel);
            return g;
        }
        /// > Draw a line from (x,y) to (x2,y2) by drawing a series of ellipses with the same width and
        /// height as the pen width
        /// 
        /// @param x The x coordinate of the first point.
        /// @param y The y coordinate of the first point.
        /// @param x2 The x coordinate of the end point of the line.
        /// @param y2 The y coordinate of the second point.
        public void DrawLine(int x, int y, int x2, int y2)
        {
            //Bresenham's algorithm for line drawing.
            int w = x2 - x;
            int h = y2 - y;
            int dx1 = 0, dy1 = 0, dx2 = 0, dy2 = 0;
            if (w < 0) dx1 = -1; else if (w > 0) dx1 = 1;
            if (h < 0) dy1 = -1; else if (h > 0) dy1 = 1;
            if (w < 0) dx2 = -1; else if (w > 0) dx2 = 1;
            int longest = Math.Abs(w);
            int shortest = Math.Abs(h);
            if (!(longest > shortest))
            {
                longest = Math.Abs(h);
                shortest = Math.Abs(w);
                if (h < 0) dy2 = -1; else if (h > 0) dy2 = 1;
                dx2 = 0;
            }
            int numerator = longest >> 1;
            for (int i = 0; i <= longest; i++)
            {
                FillEllipse(x, y, pen.width, pen.width, pen.color);
                numerator += shortest;
                if (!(numerator < longest))
                {
                    numerator -= longest;
                    x += dx1;
                    y += dy1;
                }
                else
                {
                    x += dx2;
                    y += dy2;
                }
            }
        }
        /// Draw a line from (x,y) to (x2,y2) on the bitmap bf using the color c
        /// 
        /// @param x The x coordinate of the first point.
        /// @param y The y coordinate of the first point.
        /// @param x2 The x coordinate of the end of the line.
        /// @param y2 The y coordinate of the second point.
        /// @param Bitmap The bitmap to draw on.
        /// @param ColorS A struct that contains the RGB values of a color.
        private void DrawLine(int x, int y, int x2, int y2, Bitmap bf, ColorS c)
        {
            //Bresenham's algorithm for line drawing.
            int w = x2 - x;
            int h = y2 - y;
            int dx1 = 0, dy1 = 0, dx2 = 0, dy2 = 0;
            if (w < 0) dx1 = -1; else if (w > 0) dx1 = 1;
            if (h < 0) dy1 = -1; else if (h > 0) dy1 = 1;
            if (w < 0) dx2 = -1; else if (w > 0) dx2 = 1;
            int longest = Math.Abs(w);
            int shortest = Math.Abs(h);
            if (!(longest > shortest))
            {
                longest = Math.Abs(h);
                shortest = Math.Abs(w);
                if (h < 0) dy2 = -1; else if (h > 0) dy2 = 1;
                dx2 = 0;
            }
            int numerator = longest >> 1;
            for (int i = 0; i <= longest; i++)
            {
                bf.SetPixel(x, y, c);
                numerator += shortest;
                if (!(numerator < longest))
                {
                    numerator -= longest;
                    x += dx1;
                    y += dy1;
                }
                else
                {
                    x += dx2;
                    y += dy2;
                }
            }
        }
        /// DrawLine(PointF p, PointF p2) is a function that takes two points and draws a line between
        /// them
        /// 
        /// @param PointF A structure that defines a point in a two-dimensional plane.
        /// @param PointF A structure that defines a point in a two-dimensional plane.
        public void DrawLine(PointF p, PointF p2)
        {
            DrawLine((int)p.X, (int)p.Y, (int)p2.X, (int)p2.Y);
        }
        /// For every pixel in the rectangle, set the pixel to the color
        /// 
        /// @param Rectangle The rectangle to fill
        /// @param ColorS A struct that contains the RGB values of a color.
        public void FillRectangle(Rectangle r, ColorS col)
        {
            for (int x = r.X; x < r.Width + r.X; x++)
            {
                for (int y = r.Y; y < r.Height + r.Y; y++)
                {
                    buf.SetPixel(x,y, col);
                }
            }
        }
        /// > This function takes a rectangle and a color and fills the rectangle with the color
        /// 
        /// @param RectangleF A rectangle with float values
        /// @param ColorS A struct that contains a byte for each color channel (R, G, B, A)
        public void FillRectangle(RectangleF r, ColorS col)
        {
            FillRectangle(new Rectangle((int)Math.Ceiling(r.X), (int)Math.Ceiling(r.Y), (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height)), col);
        }
        /// For each pixel in the rectangle, draw a line of pixels of the same color
        /// 
        /// @param Rectangle The rectangle to draw
        public void DrawRectangle(Rectangle r)
        {
            for (int x = r.X; x < r.Width + r.X; x++)
            {
                for (int i = 0; i < pen.width; i++)
                {
                    buf.SetPixel(x + i, r.Y, pen.color);
                    buf.SetPixel(x + i, r.Y + r.Height, pen.color);
                }
            }
            for (int y = r.Y; y < r.Height + r.Y; y++)
            {
                for (int i = 0; i < pen.width; i++)
                {
                    buf.SetPixel(r.X, y + i, pen.color);
                    buf.SetPixel(r.X + r.Width, y + i, pen.color);
                }
            }
        }
        /// "Draw a rectangle with the top left corner at (x,y) and the bottom right corner at
        /// (x+width,y+height)."
        /// 
        /// The function is called with a RectangleF object, which is a rectangle with floating point
        /// coordinates. The function converts the floating point coordinates to integers and then calls
        /// the DrawRectangle function that takes integer coordinates
        /// 
        /// @param RectangleF A rectangle with float coordinates.
        public void DrawRectangle(RectangleF r)
        {
            DrawRectangle(new Rectangle((int)Math.Ceiling(r.X), (int)Math.Ceiling(r.Y), (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height)));
        }

        /// For every angle from 0 to 360, draw a point at the angle's cosine and sine, multiplied by
        /// the radius, plus the radius, plus the center point
        /// 
        /// @param Rectangle The rectangle that the ellipse will be drawn in.
        public void DrawEllipse(Rectangle r)
        {
            if (r.Width == 1 && r.Height == 1)
                buf.SetPixel(r.X, r.Y, pen.color);
            double radiusx = r.Width / 2;
            double radiusy = r.Height / 2;
            int x, y;
            for (double a = 0.0; a < 360.0; a += 0.1)
            {
                double angle = a * System.Math.PI / 180;
                for (int i = 0; i < pen.width; i++)
                {
                    x = (int)(radiusx * System.Math.Cos(angle) + radiusx + r.X);
                    y = (int)(radiusy * System.Math.Sin(angle) + radiusy + r.Y);
                    buf.SetPixel(x+i, y+i, pen.color);
                }
            }
        }
        /// If you pass a RectangleF to DrawEllipse, it will convert it to a Rectangle and then call
        /// DrawEllipse(Rectangle)
        /// 
        /// @param RectangleF A rectangle with floating point coordinates.
        public void DrawEllipse(RectangleF r)
        {
            DrawEllipse(new Rectangle((int)Math.Ceiling(r.X), (int)Math.Ceiling(r.Y), (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height)));
        }
        /// It draws an ellipse by drawing a bunch of horizontal lines
        /// 
        /// @param xx x position of the ellipse
        /// @param yy y position of the ellipse
        /// @param w width of the ellipse
        /// @param h height of the ellipse
        /// @param ColorS A struct that contains the color information.
        /// 
        /// @return the color of the pixel at the specified coordinates.
        public void FillEllipse(float xx, float yy, int w, int h, ColorS c)
        {
            if (w <= 1 && w <= 1)
            {
                buf.SetPixel((int)xx,(int)yy, c);
                return;
            }
            double radiusx = w / 2;
            double radiusy = h / 2;
            int x, y;
            for (double a = 90; a < 270; a += 0.1)
            {
                double angle = a * System.Math.PI / 180;
                x = (int)(radiusx * System.Math.Cos(angle) + radiusx + xx);
                y = (int)(radiusy * System.Math.Sin(angle) + radiusy + yy);
                double angle2 = (a+180) * System.Math.PI / 180;
                int x2 = (int)(radiusx * System.Math.Cos(angle2) + radiusx + xx);
                DrawScanline(x,x2,y,c);
            }
        }
        /// > This function takes a rectangle and a color and draws an ellipse inside the rectangle
        /// 
        /// @param RectangleF A rectangle with float values
        /// @param ColorS A struct that contains a byte for each color channel (R, G, B, A)
        public void FillEllipse(RectangleF r, ColorS c)
        {
            FillEllipse(new Rectangle((int)Math.Ceiling(r.X), (int)Math.Ceiling(r.Y), (int)Math.Ceiling(r.Width), (int)Math.Ceiling(r.Height)), c);
        }
        /// > Draws a filled ellipse with the specified color
        /// 
        /// @param Rectangle The rectangle to draw the ellipse in.
        /// @param ColorS A struct that contains the color information.
        public void FillEllipse(Rectangle r, ColorS c)
        {
            FillEllipse(r.X, r.Y, r.Width, r.Height, c);
        }
        /// We draw the polygon onto a new bitmap, then we find a point inside the polygon and flood
        /// fill it, then we draw the flood filled bitmap onto the original bitmap
        /// 
        /// @param pfs The points of the polygon
        /// @param Rectangle The rectangle that contains the polygon.
        public void FillPolygon(PointF[] pfs, Rectangle r)
        {
            //We will use the flood fill algorithm to fill the polygon.
            //First we need to create a new Buffer incase the current Buffer contains filled pixels that could prevent flood fill from filling the whole area.
            Bitmap bf = buf.CopyInfo();
            bf.Bytes = new byte[buf.Bytes.Length];

            DrawPolygon(pfs, bf, pen.color);
            
            filler = new QueueLinearFloodFiller(filler);
            filler.FillColor = pen.color;
            filler.Tolerance = new ColorS(0, 0, 0);
            filler.Bitmap = bf;
            //Next we need to find a point inside the polygon from where to start the flood fill.
            //We use the center points x-line till we find a point inside.
            Point p = new Point(r.X + (r.Width / 2), r.Y + (r.Height / 2));
            Point? pp = null;
            polygon = pfs;
            for (int x = r.X; x < r.Width + r.X; x++)
            {
                if (PointInPolygon(x, (int)p.Y))
                {
                    pp = new Point(x, p.Y);
                    break;
                }
            }
            filler.FloodFill(pp.Value);
            //Now that we have a filled shape we draw it onto the original bitmap
            for (int x = 0; x < bf.SizeX; x++)
            {
                for (int y = 0; y < bf.SizeY; y++)
                {
                    if(bf.GetPixel(x,y) == pen.color)
                    {
                        buf.SetPixel(x, y, pen.color);
                    }
                }
            }
            bf.Dispose();
        }
        /// > This function takes a list of points and a rectangle and fills the polygon defined by the
        /// points with the rectangle
        /// 
        /// @param pfs An array of PointF structures that represent the vertices of the polygon to fill.
        /// @param RectangleF A rectangle with float coordinates.
        public void FillPolygon(PointF[] pfs, RectangleF r)
        {
            FillPolygon(pfs, new Rectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height));
        }
        /* Declaring a variable called polygon that is an array of PointF objects. */
        private static PointF[] polygon;
        /// If the point is on the left side of the line, then the point is inside the polygon
        /// 
        /// @param x The x coordinate of the point to test.
        /// @param y The y coordinate of the point to test.
        /// 
        /// @return A boolean value.
        public bool PointInPolygon(int x, int y)
        {
            int j = polygon.Length - 1;
            bool c = false;
            for (int i = 0; i < polygon.Length; j = i++) c ^= polygon[i].Y > y ^ polygon[j].Y > y && x < (polygon[j].X - polygon[i].X) * (y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X;
            return c;
        }
        
        /// > This function takes an array of points, a rectangle, and a color, and fills the polygon
        /// with the color
        /// 
        /// @param pfs An array of points that make up the polygon
        /// @param Rectangle The rectangle that the polygon is in.
        /// @param ColorS A struct that contains a color.
        public void FillPolygon(PointF[] pfs, Rectangle r, ColorS c)
        {
            pen.color = c;
            FillPolygon(pfs, r);
        }
        /// > This function takes an array of points, a rectangle, and a color, and fills the polygon
        /// with the color
        /// 
        /// @param pfs An array of points that make up the polygon
        /// @param RectangleF The rectangle that the polygon is in.
        /// @param ColorS A struct that contains a color.
        public void FillPolygon(PointF[] pfs, RectangleF r, ColorS c)
        {
            pen.color = c;
            FillPolygon(pfs, r);
        }
        /// Draw a line between each point in the array of points, and then draw a line between the
        /// first and last point
        /// 
        /// @param pfs The points of the polygon
        /// @param Bitmap The bitmap to draw on
        /// @param ColorS A struct that contains the color of the line.
        private void DrawPolygon(PointF[] pfs, Bitmap bf, ColorS s)
        {
            for (int i = 0; i < pfs.Length - 1; i++)
            {
                DrawLine((int)pfs[i].X, (int)pfs[i].Y, (int)pfs[i + 1].X, (int)pfs[i + 1].Y, bf, s);
            }
            DrawLine((int)pfs[0].X, (int)pfs[0].Y, (int)pfs[pfs.Length - 1].X, (int)pfs[pfs.Length - 1].Y, bf, s);
        }
        /// DrawScanline(int x, int x2, int line, ColorS col)
        /// 
        /// This function draws a scanline from x to x2 on the y line
        /// 
        /// @param x The starting x position of the scanline
        /// @param x2 The end of the scanline
        /// @param line The line to draw the scanline on
        /// @param ColorS A struct that contains the RGB values of a color.
        public void DrawScanline(int x, int x2, int line, ColorS col)
        {
            for (int xx = x; xx < x2; xx++)
            {
                buf.SetPixel(xx, line, col);
            }
        }
       /// The Dispose() function is used to release the resources used by the object
        public void Dispose()
        {
            pen.Dispose();
        }
    }
}
