using Gtk;
using Gdk;
using System;
using System.Collections.Generic;
using System.Linq;
using AForge;
using AForge.Imaging.Filters;
using OpenSlideGTK;
using Bio;
using Point = AForge.Point;
using PointF = AForge.PointF;
using SizeF = AForge.SizeF;
using Color = AForge.Color;
using Rectangle = AForge.Rectangle;
using System.IO;
using SkiaSharp;
using SkiaSharp.Views.Gtk;
using System.Runtime.InteropServices;
using static NetVips.Enums;
using sun.applet;

namespace BioGTK
{
    public class ImageView : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        public List<BioImage> Images = new List<BioImage>();
        public List<SKImage> SKImages = new List<SKImage>();
        public List<Bitmap> Bitmaps = new List<Bitmap>();
        public void SetCoordinate(int z, int c, int t)
        {
            if (SelectedImage == null)
                return;
            if (z > SelectedImage.SizeZ - 1 && SelectedImage.Type == BioImage.ImageType.well)
                zBar.Value = zBar.Adjustment.Upper;
            else
                zBar.Value = z;
            if (c > SelectedImage.SizeC - 1)
                cBar.Value = cBar.Adjustment.Upper;
            else
                cBar.Value = c;
            if (t > SelectedImage.SizeT - 1)
                tBar.Value = tBar.Adjustment.Upper;
            else
                tBar.Value = t;
            if (SelectedImage.Type == BioImage.ImageType.well)
                SelectedImage.Coordinate = new ZCT(0, (int)cBar.Value, (int)tBar.Value);
            else
                SelectedImage.Coordinate = new ZCT((int)zBar.Value, (int)cBar.Value, (int)tBar.Value);
            UpdateView(true, true);
        }
        /// It returns the coordinate of the selected image
        /// 
        /// @return The Coordinate property of the SelectedImage object.
        public ZCT GetCoordinate()
        {
            return SelectedImage.Coordinate;
        }
        /// It adds an image to the list of images, and then updates the GUI and the images
        /// 
        /// @param BioImage a class that contains the image data and metadata
        public void AddImage(BioImage im)
        {
            Images.Add(im);
            selectedIndex = Images.Count - 1;
            if(im.Resolutions[0].SizeX < 1920 && im.Resolutions[0].SizeY < 1080)
            {
                sk.WidthRequest = 600;
                sk.HeightRequest = 400;
            }
            if(im.isPyramidal)
            {
                Initialize();
                InitPreview();
            }
            UpdateGUI();
            UpdateImages();
            GoToImage(Images.Count - 1);
        }
        double pxWmicron = 5;
        double pxHmicron = 5;
       /* A property of the class. */
        public double PxWmicron
        {
            get
            {
                if(SelectedImage.Type == BioImage.ImageType.pyramidal)
                if(openSlide)
                { 
                    int lev = OpenSlideGTK.TileUtil.GetLevel(_openSlideBase.Schema.Resolutions, Resolution);
                    return _openSlideBase.Schema.Resolutions[lev].UnitsPerPixel * pxWmicron;
                }
                else
                {
                    int lev = OpenSlideGTK.TileUtil.GetLevel(_slideBase.Schema.Resolutions, Resolution);
                    return _slideBase.Schema.Resolutions[lev].UnitsPerPixel * pxWmicron;
                }

                return pxWmicron;
            }
            set
            {
                pxWmicron = value;
                UpdateView(true,false);
            }
        }
        public double PxHmicron
        {
            get
            {
                if (SelectedImage.Type == BioImage.ImageType.pyramidal)
                if (openSlide)
                {
                    int lev = OpenSlideGTK.TileUtil.GetLevel(_openSlideBase.Schema.Resolutions, Resolution);
                    return _openSlideBase.Schema.Resolutions[lev].UnitsPerPixel * pxHmicron;
                }
                else
                {
                    int lev = OpenSlideGTK.TileUtil.GetLevel(_slideBase.Schema.Resolutions, Resolution);
                    return _slideBase.Schema.Resolutions[lev].UnitsPerPixel * pxHmicron;
                }
                return pxHmicron;
            }
            set
            {
                pxHmicron = value;
                UpdateView(true, false);
            }
        }
        bool allowNavigation = true;
        public bool AllowNavigation
        {
            get { return allowNavigation; }
            set { allowNavigation = value; }
        }

        public bool ShowMasks { get; set; }

       /* Getting the selected buffer from the selected image. */
        public static AForge.Bitmap SelectedBuffer
        {
            get
            {
                int ind = SelectedImage.GetFrameIndex(SelectedImage.Coordinate.Z, SelectedImage.Coordinate.C, SelectedImage.Coordinate.T);
                return SelectedImage.Buffers[ind];
            }
        }
        int selectedIndex = 0;
        public int SelectedIndex
        {
            get { return selectedIndex; }
            set { selectedIndex = value;
                Images[selectedIndex] = SelectedImage;
            }
        }
        Menu imagesMenu;
        public static List<ROI> selectedAnnotations = new List<ROI>();

#pragma warning disable 649
        [Builder.Object]
        private Gtk.Scale zBar;
        [Builder.Object]
        private Gtk.Scale tBar;
        [Builder.Object]
        private Gtk.Scale cBar;
        [Builder.Object]
        private Gtk.Label statusLabel;
        [Builder.Object]
        private Gtk.Stack rgbStack;
        [Builder.Object]
        private Gtk.Grid grid;
        [Builder.Object]
        private Gtk.Box rgbBox;
        [Builder.Object]
        private Gtk.Box controlsBox;
        [Builder.Object]
        public Gtk.Box mainBox;
        [Builder.Object]
        private Gtk.ComboBox rBox;
        [Builder.Object]
        private Gtk.ComboBox gBox;
        [Builder.Object]
        private Gtk.ComboBox bBox;
        [Builder.Object]
        private Gtk.Stack viewStack;
        [Builder.Object]
        public Menu contextMenu;
        [Builder.Object]
        private MenuItem goToOriginMenu;
        [Builder.Object]
        private MenuItem goToImageMenu;
        [Builder.Object]
        private MenuItem roi;
        [Builder.Object]
        private Menu roiMenu;
        [Builder.Object]
        private MenuItem roiDelete;
        [Builder.Object]
        private MenuItem roiID;
        [Builder.Object]
        private MenuItem copy;
        [Builder.Object]
        private MenuItem paste;
        [Builder.Object]
        private MenuItem draw;
        [Builder.Object]
        private MenuItem fill;
        [Builder.Object]
        private Menu barMenu;
        [Builder.Object]
        private MenuItem play;
        [Builder.Object]
        private MenuItem stop;
        [Builder.Object]
        private MenuItem playSpeed;
        [Builder.Object]
        private MenuItem setValueRange;
        [Builder.Object]
        private MenuItem loop;
#pragma warning restore 649
        private SKDrawingArea sk = new SKDrawingArea();
        #endregion

        #region Constructors / Destructors

        /// The function creates an ImageView object using a BioImage object and returns it.
        /// 
        /// @param BioImage The BioImage parameter is an object that represents an image in a biological
        /// context. It likely contains information about the image file, such as the filename, and
        /// possibly additional metadata related to the image.
        /// 
        /// @return The method is returning an instance of the ImageView class.
        public static ImageView Create(BioImage bm)
        {
            Console.WriteLine("Creating ImageView for " + bm.file);
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/ImageView.glade", FileMode.Open));
            ImageView v = new ImageView(builder, builder.GetObject("imageView").Handle, bm);
            v.Title = bm.Filename;
            return v;
        }

        /* The above code is a constructor for the ImageView class in C#. It takes in a Builder object,
        a handle, and a BioImage object as parameters. */
        protected ImageView(Builder builder, IntPtr handle, BioImage im) : base(handle)
        {
            _builder = builder;
            App.viewer = this;
            builder.Autoconnect(this);
            viewStack.Add(sk);
            viewStack.ShowAll();
            sk.Show();
            roi.Submenu = roiMenu;
            roi.ShowAll();
            AddImage(im);
            ShowMasks = true;
            pxWmicron = SelectedImage.PhysicalSizeX;
            pxHmicron = SelectedImage.PhysicalSizeY;
            SetupHandlers();
            //pictureBox.WidthRequest = im.SizeX;
            //pictureBox.HeightRequest = im.SizeY;
            Function.InitializeContextMenu();
            this.Scale = new SizeF(1, 1);
            // Set the text column to display for comboboxs.
            var rendererr = new CellRendererText();
            rBox.PackStart(rendererr, false);
            rBox.AddAttribute(rendererr, "text", 0);
            var rendererg = new CellRendererText();
            gBox.PackStart(rendererg, false);
            gBox.AddAttribute(rendererg, "text", 0);
            var rendererb = new CellRendererText();
            bBox.PackStart(rendererb, false);
            bBox.AddAttribute(rendererb, "text", 0);
            App.ApplyStyles(this);
            if(im.Type == BioImage.ImageType.well)
            {
                Resolution = 0;
            }
            if(im.Type == BioImage.ImageType.pyramidal)
            {
                WidthRequest = 800;
                HeightRequest = 600;
                im.PyramidalSize = new AForge.Size(800, 600);
            }
            else
            if(im.SizeX > 1920 || im.SizeY > 1080)
            {
                WidthRequest = 800;
                HeightRequest = 600;
            }
        }
        private static SkiaSharp.SKRect ToRectangle(float x1, float y1, float x2, float y2)
        {
            return new SkiaSharp.SKRect()
            {
                Location = new SKPoint(x1, y1),
                Size = new SKSize(x2, y2)
            };
        }
        private bool refresh = false;
        private async void Render(object sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
        {
            int ri = 0;
            try
            {
                if (!refresh)
                    return;
                var canvas = e.Surface.Canvas;
                canvas.Clear(SKColors.Transparent);
                var paint = new SKPaint();
                paint.Color = SKColors.Gray;
                paint.BlendMode = SKBlendMode.SrcOver;
                paint.IsAntialias = true;
                paint.Style = SKPaintStyle.Fill;
                if ((SKImages.Count == 0 || Bitmaps.Count != Images.Count))
                    UpdateImages(true);

                if (!SelectedImage.isPyramidal)
                {
                    //canvas.Scale(Scale.Width, Scale.Height);
                    canvas.Translate(sk.AllocatedWidth / 2, sk.AllocatedHeight / 2);
                }

                RectangleD rd = ToScreenRect(PointD.MinX, PointD.MinY, PointD.MaxX - PointD.MinX, PointD.MaxY - PointD.MinY);
                SKRect rr = new SKRect();
                rr.Location = new SKPoint((float)rd.X, (float)rd.Y);
                rr.Size = new SKSize((float)rd.W, (float)rd.H);
                canvas.DrawRect(rr, paint);
                int i = 0;
                
                foreach (BioImage im in Images)
                {
                    RectangleD rec = ToScreenRect(im.Volume.Location.X, im.Volume.Location.Y, im.Volume.Width, im.Volume.Height);
                    SKRect r = new SKRect();
                    r.Location = new SKPoint((float)rec.X, (float)rec.Y);
                    r.Size = new SKSize((float)Math.Abs(rec.W), (float)Math.Abs(rec.H));
                    paint.StrokeWidth = 1;
                    if (SelectedImage.isPyramidal)
                    {
                        try
                        {
                            if (SelectedImage.Buffers.Count > 0)
                            {
                                canvas.DrawImage(SKImages[i], 0, 0, paint);
                                if (overviewImage != null)
                                {
                                    var ims = BitmapToSKImage(overviewImage.GetImageRGB());
                                    paint.Style = SKPaintStyle.Fill;
                                    // Draw the overview image at the top-left corner
                                    canvas.DrawImage(ims, 0, 0, paint);
                                }
                                // Set the paint style to stroke for drawing rectangles
                                paint.Style = SKPaintStyle.Stroke;
                                // Draw the gray rectangle representing the overview's entire visible region
                                paint.Color = SKColors.Gray;
                                canvas.DrawRect(overview.X, overview.Y, overview.Width, overview.Height, paint);
                                paint.Color = SKColors.Red;
                                double dsx = _slideBase.Schema.Resolutions[Level].UnitsPerPixel / Resolution;
                                Resolution rs = SelectedImage.Resolutions[Level];
                                double dx = ((double)PyramidalOrigin.X / (rs.SizeX * dsx)) * overview.Width;
                                double dy = ((double)PyramidalOrigin.Y / (rs.SizeY * dsx)) * overview.Height;
                                double dw = ((double)viewStack.AllocatedWidth / (rs.SizeX)) * overview.Width * dsx;
                                double dh = ((double)viewStack.AllocatedHeight / (rs.SizeY)) * overview.Height * dsx;
                                canvas.DrawRect((int)dx, (int)dy, (int)dw, (int)dh, paint);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }
                    else
                    {
                        canvas.DrawImage(SKImages[i], r, paint);
                    }
                    paint.Style = SKPaintStyle.Stroke;
                    List<ROI> rois = new List<ROI>();
                    rois.AddRange(im.Annotations);
                    if (Tools.currentTool.type == Tools.Tool.Type.select && Modifiers == ModifierType.Button1Mask)
                    {
                        var recd = ToScreenRect(Tools.currentTool.Rectangle.X, Tools.currentTool.Rectangle.Y, Tools.currentTool.Rectangle.W, Tools.currentTool.Rectangle.H);
                        canvas.DrawRect((float)recd.X, (float)recd.Y, (float)recd.W, (float)recd.H, paint);
                    }
                    ri = 0;
                    foreach (ROI an in rois)
                    {
                        if (Mode == ViewMode.RGBImage)
                        {
                            if (!showRROIs && an.coord.C == 0)
                                continue;
                            if (!showGROIs && an.coord.C == 1)
                                continue;
                            if (!showBROIs && an.coord.C == 2)
                                continue;
                        }
                        if (an.Selected)
                        {
                            paint.Color = SKColors.Magenta;
                        }
                        else
                            paint.Color = new SKColor(an.strokeColor.R, an.strokeColor.G, an.strokeColor.B);
                        paint.StrokeWidth = (float)an.strokeWidth;
                        PointF pc = new PointF((float)(an.BoundingBox.X + (an.BoundingBox.W / 2)), (float)(an.BoundingBox.Y + (an.BoundingBox.H / 2)));
                        float width = ROI.selectBoxSize;

                        if (an.type == ROI.Type.Mask && an.coord == App.viewer.GetCoordinate() && ShowMasks)
                        {
                            //paint.BlendMode = SKBlendMode.Modulate;
                            SKImage sim;
                            if(an.Selected)
                                sim = an.roiMask.GetColored(Color.Blue, 10, true).ToSKImage();
                            else
                                sim = an.roiMask.GetColored(Color.FromArgb(1,an.fillColor.R, an.fillColor.G, an.fillColor.B), 1, true).ToSKImage();
                            RectangleD p = ToScreenSpace(new RectangleD(an.roiMask.X * an.roiMask.PhysicalSizeX, an.roiMask.Y * an.roiMask.PhysicalSizeY, an.W,an.H));
                            canvas.DrawImage(sim, ToRectangle((float)p.X, (float)p.Y, (float)p.W, (float)p.H), paint);
                            sim.Dispose();
                            continue;
                        }
                        else
                        if (an.type == ROI.Type.Point)
                        {
                            RectangleD r1 = ToScreenRect(an.Point.X, an.Point.Y, ToViewW(3), ToViewH(3));
                            canvas.DrawCircle((float)r1.X, (float)r1.Y, 3, paint);
                        }
                        else
                        if (an.type == ROI.Type.Line)
                        {
                            for (int p = 0; p < an.PointsD.Count - 1; p++)
                            {
                                PointD p1 = ToScreenSpace(an.PointsD[p].X, an.PointsD[p].Y);
                                PointD p2 = ToScreenSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y);
                                canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y), paint);
                            }
                        }
                        else
                        if (an.type == ROI.Type.Rectangle)
                        {
                            RectangleD rectt = ToScreenRect(an.PointsD[0].X, an.PointsD[0].Y, Math.Abs(an.PointsD[0].X - an.PointsD[1].X), Math.Abs(an.PointsD[0].Y - an.PointsD[2].Y));
                            canvas.DrawRect((float)rectt.X, (float)rectt.Y, (float)rectt.W, (float)rectt.H, paint);
                        }
                        else
                        if (an.type == ROI.Type.Ellipse)
                        {
                            RectangleD rect = ToScreenRect(an.X + (an.W / 2), an.Y + (an.H / 2), an.W, an.H);
                            canvas.DrawOval((float)rect.X, (float)rect.Y, (float)rect.W / 2, (float)rect.H / 2, paint);
                        }
                        else
                        if ((an.type == ROI.Type.Polygon && an.closed))
                        {
                            for (int p = 0; p < an.PointsD.Count - 1; p++)
                            {
                                RectangleD p1 = ToScreenRect(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                                RectangleD p2 = ToScreenRect(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                                canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y), paint);
                            }
                            RectangleD pp1 = ToScreenRect(an.PointsD[0].X, an.PointsD[0].Y, 1, 1);
                            RectangleD pp2 = ToScreenRect(an.PointsD[an.PointsD.Count - 1].X, an.PointsD[an.PointsD.Count - 1].Y, 1, 1);
                            canvas.DrawLine(new SKPoint((float)pp1.X, (float)pp1.Y), new SKPoint((float)pp2.X, (float)pp2.Y), paint);
                        }
                        else
                        if ((an.type == ROI.Type.Polygon && !an.closed) || an.type == ROI.Type.Polyline)
                        {
                            for (int p = 0; p < an.PointsD.Count - 1; p++)
                            {
                                RectangleD p1 = ToScreenRect(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                                RectangleD p2 = ToScreenRect(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                                canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y), paint);
                            }
                        }
                        else
                        if (an.type == ROI.Type.Freeform)
                        {
                            for (int p = 0; p < an.PointsD.Count - 1; p++)
                            {
                                RectangleD p1 = ToScreenRect(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                                RectangleD p2 = ToScreenRect(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                                canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y), paint);
                            }
                            RectangleD pp1 = ToScreenRect(an.PointsD[0].X, an.PointsD[0].Y, 1, 1);
                            RectangleD pp2 = ToScreenRect(an.PointsD[an.PointsD.Count - 1].X, an.PointsD[an.PointsD.Count - 1].Y, 1, 1);
                            canvas.DrawLine(new SKPoint((float)pp1.X, (float)pp1.Y), new SKPoint((float)pp2.X, (float)pp2.Y), paint);
                        }
                        else
                        if (an.type == ROI.Type.Label)
                        {
                            RectangleD p = ToScreenRect(an.Point.X, an.Point.Y, 1, 1);
                            canvas.DrawText(an.Text, (float)p.X, (float)p.Y, new SKFont(SKTypeface.Default, an.fontSize, 1, 0), paint);
                        }

                        if (ROIManager.showText)
                        {
                            RectangleD p = ToScreenRect(an.Point.X, an.Point.Y, 1, 1);
                            canvas.DrawText(an.Text, (float)p.X, (float)p.Y, new SKFont(SKTypeface.Default, an.fontSize, 1, 0), paint);
                        }
                        if (ROIManager.showBounds && an.type != ROI.Type.Rectangle && an.type != ROI.Type.Mask && an.type != ROI.Type.Label)
                        {
                            RectangleD rrf = ToScreenRect(an.BoundingBox.X, an.BoundingBox.Y, an.BoundingBox.W, an.BoundingBox.H);
                            canvas.DrawRect((float)rrf.X, (float)rrf.Y, (float)rrf.W, (float)rrf.H, paint);
                        }
                        paint.Color = SKColors.Red;
                        if (!(an.type == ROI.Type.Freeform && !an.Selected) && an.type != ROI.Type.Mask)
                        foreach (RectangleD re in an.GetSelectBoxes(1))
                        {
                            RectangleD recd = ToScreenRect(re.X, re.Y, re.W, re.H);
                            canvas.DrawRect((float)recd.X, (float)recd.Y, (float)recd.W, (float)recd.H, paint);
                        }
                        if (an.type != ROI.Type.Mask)
                        {
                            //Lets draw the selection Boxes.
                            List<RectangleD> rects = new List<RectangleD>();
                            RectangleD[] sels = an.GetSelectBoxes(width);
                            for (int p = 0; p < an.selectedPoints.Count; p++)
                            {
                                if (an.selectedPoints[p] < an.GetPointCount())
                                {
                                    rects.Add(sels[an.selectedPoints[p]]);
                                }
                            }
                            //Lets draw selected selection boxes.
                            paint.Color = SKColors.Blue;
                            if (rects.Count > 0)
                            {
                                int ind = 0;
                                foreach (RectangleD re in an.GetSelectBoxes(1))
                                {
                                    RectangleD recd = ToScreenRect(re.X, re.Y, re.W, re.H);
                                    if (an.selectedPoints.Contains(ind))
                                    {
                                        canvas.DrawRect((float)recd.X, (float)recd.Y, (float)recd.W, (float)recd.H, paint);
                                    }
                                    ind++;
                                }
                            }
                            rects.Clear();
                        }
                        ri++;
                    }
                }
                Plugins.Render(sender, e);
                paint.Dispose();
                refresh = false;
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
                refresh = false;
            }

        }

        #endregion

        private static SKImage Convert24bppBitmapToSKImage(Bitmap sourceBitmap)
        {
            int width = sourceBitmap.Width;
            int height = sourceBitmap.Height;

            SKBitmap skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);

            BitmapData bitmapData = sourceBitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, sourceBitmap.PixelFormat);

            unsafe
            {
                byte* sourcePtr = (byte*)bitmapData.Scan0.ToPointer();
                byte* destPtr = (byte*)skBitmap.GetPixels().ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        destPtr[0] = sourcePtr[0]; // Blue
                        destPtr[1] = sourcePtr[1]; // Green
                        destPtr[2] = sourcePtr[2]; // Red
                        destPtr[3] = 255;          // Alpha (fully opaque)

                        sourcePtr += 3;
                        destPtr += 4;
                    }
                }
            }

            sourceBitmap.UnlockBits(bitmapData);

            return SKImage.FromBitmap(skBitmap);
        }
        private static SKImage Convert32bppBitmapToSKImage(Bitmap sourceBitmap)
        {
            int width = sourceBitmap.Width;
            int height = sourceBitmap.Height;

            SKBitmap skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);

            BitmapData bitmapData = sourceBitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, sourceBitmap.PixelFormat);

            unsafe
            {
                byte* sourcePtr = (byte*)bitmapData.Scan0.ToPointer();
                byte* destPtr = (byte*)skBitmap.GetPixels().ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        destPtr[0] = sourcePtr[0]; // Blue
                        destPtr[1] = sourcePtr[1]; // Green
                        destPtr[2] = sourcePtr[2]; // Red
                        destPtr[3] = 255;          // Alpha (fully opaque)

                        sourcePtr += 4;
                        destPtr += 4;
                    }
                }
            }

            sourceBitmap.UnlockBits(bitmapData);

            return SKImage.FromBitmap(skBitmap);
        }
        private static SKImage Convert8bppBitmapToSKImage(Bitmap sourceBitmap)
        {
            // Ensure the input bitmap is 8bpp indexed
            if (sourceBitmap.PixelFormat != PixelFormat.Format8bppIndexed)
                throw new ArgumentException("Bitmap must be 8bpp indexed.", nameof(sourceBitmap));

            // Lock the bitmap for reading pixel data
            BitmapData bitmapData = sourceBitmap.LockBits(
                new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height),
                ImageLockMode.ReadOnly,
                sourceBitmap.PixelFormat);

            try
            {
           
                // Read the pixel data
                int dataSize = bitmapData.Stride * bitmapData.Height;
                byte[] pixelData = new byte[dataSize];
                Marshal.Copy(bitmapData.Scan0, pixelData, 0, dataSize);

                // Create an SKBitmap with the same dimensions as the input bitmap
                using (var skBitmap = new SKBitmap(sourceBitmap.Width, sourceBitmap.Height, SKColorType.Gray8, SKAlphaType.Premul))
                {
                    // Copy the pixel data into the SKBitmap
                    var skBitmapPixels = skBitmap.GetPixelSpan();
                    for (int y = 0; y < skBitmap.Height; y++)
                    {
                        int srcOffset = y * bitmapData.Stride;
                        int destOffset = y * skBitmap.Width;

                        for (int x = 0; x < skBitmap.Width; x++)
                        {
                            skBitmapPixels[destOffset + x] = pixelData[srcOffset + x];
                        }
                    }

                    // Create an SKImage from the SKBitmap
                    return SKImage.FromBitmap(skBitmap);
                }
            }
            finally
            {
                // Unlock the bitmap
                sourceBitmap.UnlockBits(bitmapData);
            }
        }
        public static SKImage Convert16bppBitmapToSKImage(Bitmap sourceBitmap)
        {
            Bitmap bm = sourceBitmap.GetImageRGB();
            int width = bm.Width;
            int height = bm.Height;

            SKBitmap skBitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);

            BitmapData bitmapData = sourceBitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, bm.PixelFormat);

            unsafe
            {
                byte* sourcePtr = (byte*)bm.Data.ToPointer();
                byte* destPtr = (byte*)skBitmap.GetPixels().ToPointer();

                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        destPtr[0] = sourcePtr[0]; // Blue
                        destPtr[1] = sourcePtr[1]; // Green
                        destPtr[2] = sourcePtr[2]; // Red
                        destPtr[3] = 255;          // Alpha (fully opaque)

                        sourcePtr += 3;
                        destPtr += 4;
                    }
                }
            }

            sourceBitmap.UnlockBits(bitmapData);

            return SKImage.FromBitmap(skBitmap);
        }
        public static SKImage BitmapToSKImage(AForge.Bitmap bitm)
        {
            if(bitm.PixelFormat == PixelFormat.Format24bppRgb)
                return Convert24bppBitmapToSKImage(bitm);
            if (bitm.PixelFormat == PixelFormat.Format32bppArgb)
                return Convert32bppBitmapToSKImage(bitm);
            if (bitm.PixelFormat == PixelFormat.Float)
                return Convert32bppBitmapToSKImage(bitm.GetImageRGBA());
            if (bitm.PixelFormat == PixelFormat.Format16bppGrayScale)
                return Convert16bppBitmapToSKImage(bitm.GetImageRGB());
            if (bitm.PixelFormat == PixelFormat.Format48bppRgb)
                return Convert16bppBitmapToSKImage(bitm.GetImageRGB());
            if (bitm.PixelFormat == PixelFormat.Format8bppIndexed)
                return Convert8bppBitmapToSKImage(bitm);
            else
                throw new NotSupportedException("PixelFormat " + bitm.PixelFormat + " is not supported for SKImage.");
        }

        public void SetTitle(string s)
        {
            this.Title = s;
        }

        /// It updates the images.
        public void UpdateImages(bool updatePyramidal = false)
        {
            if (SelectedImage == null)
                return;
            if (zBar.Adjustment.Upper != SelectedImage.SizeZ - 1 || tBar.Adjustment.Upper != SelectedImage.SizeT - 1)
            {
                UpdateGUI();
            }
            int bi = 0;
            if (SelectedImage.isPyramidal && sk.AllocatedHeight <= 1 || sk.AllocatedWidth <= 1)
                return;
            if (SelectedImage.isPyramidal && updatePyramidal || SelectedImage.Buffers.Count == 0)
            {
                SelectedImage.Coordinate = GetCoordinate();
                SelectedImage.PyramidalSize = new AForge.Size(sk.AllocatedWidth, sk.AllocatedHeight);
                SelectedImage.UpdateBuffersPyramidal().Wait();
            }
            SKImages.Clear();
            Bitmaps.Clear();
            foreach (BioImage b in Images)
            {
                ZCT c = GetCoordinate();
                AForge.Bitmap bitmap = null;
                int index = b.GetFrameIndex(c.Z, c.C, c.T);
                if (Mode == ViewMode.Filtered)
                {
                    bitmap = b.GetFiltered(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                }
                else if (Mode == ViewMode.RGBImage)
                {
                    bitmap = b.GetRGBBitmap(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                }
                else if (Mode == ViewMode.Raw)
                {
                    bitmap = b.Buffers[index];
                }
                else
                {
                    bitmap = b.GetEmission(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                }
                if (bitmap == null)
                    return;
                Bitmaps.Add(bitmap);
                SKImage skim = BitmapToSKImage(bitmap);
                SKImages.Add(skim);
                bi++;
            }
        }
        /// It updates the image.
        public void UpdateImage(bool updatePyramidal = false)
        {
           UpdateImages(updatePyramidal);
        }
        bool showOverview = false;
        Rectangle overview;
        Bitmap overviewImage;
        /* A property that is used to set the value of the showOverview variable. */
        public bool ShowOverview
        {
            get { return showOverview; }
            set
            {
                showOverview = value;
                UpdateView();
            }
        }
        
        public int? MacroResolution { get { return SelectedImage.MacroResolution; } }
        public int? LabelResolution { get { return SelectedImage.LabelResolution; } }

        /// <summary>
        /// It takes a large image, resizes it to a small image, and then displays it.
        /// </summary>
        private void InitPreview()
        {
            if (!SelectedImage.isPyramidal)
                return;
            refresh = false;
            overview = new Rectangle(0, 0, 120, 120);
            if (SelectedImage.Resolutions.Count == 1)
            {
                ShowOverview = false;
            }
            if (MacroResolution.HasValue)
            {
                double aspx = (double)SelectedImage.Resolutions[MacroResolution.Value - 2].SizeX / (double)SelectedImage.Resolutions[MacroResolution.Value - 2].SizeY;
                double aspy = (double)SelectedImage.Resolutions[MacroResolution.Value - 2].SizeY / (double)SelectedImage.Resolutions[MacroResolution.Value - 2].SizeX;
                overview = new Rectangle(0, 0, (int)(aspx * 120), (int)(aspy * 120));
                if (SelectedImage.Resolutions[MacroResolution.Value - 2].SizeInBytes > 1500000000)
                {
                    ShowOverview = false;
                    return;
                }
                else
                    showOverview = true;
                Bitmap bm = BioImage.GetTile(SelectedImage, SelectedImage.GetFrameIndex(GetCoordinate().Z, GetCoordinate().C, GetCoordinate().T), MacroResolution.Value - 2, 0, 0, SelectedImage.Resolutions[MacroResolution.Value - 2].SizeX, SelectedImage.Resolutions[MacroResolution.Value - 2].SizeY);
                ResizeBilinear re = new ResizeBilinear(overview.Width, overview.Height);
                Bitmap bmp = re.Apply(bm.GetImageRGB());
                overviewImage = bmp;
            }
            else
            {
                int lev = SelectedImage.Resolutions.Count-1;
                double aspx = (double)SelectedImage.Resolutions[lev].SizeX / (double)SelectedImage.Resolutions[lev].SizeY;
                double aspy = (double)SelectedImage.Resolutions[lev].SizeY / (double)SelectedImage.Resolutions[lev].SizeX;
                if (SelectedImage.Resolutions[lev].SizeInBytes > 1500000000)
                {
                    ShowOverview = false;
                    return;
                }
                else
                    showOverview = true;
                overview = new Rectangle(0, 0, (int)(aspx * 120), (int)(aspy * 120));
                Bitmap bm = BioImage.GetTile(SelectedImage, SelectedImage.GetFrameIndex(GetCoordinate().Z, GetCoordinate().C, GetCoordinate().T), lev, 0, 0, SelectedImage.Resolutions[lev].SizeX, SelectedImage.Resolutions[lev].SizeY);
                ResizeBilinear re = new ResizeBilinear(overview.Width, overview.Height);
                Bitmap bmp = re.Apply(bm.GetImageRGB());
                overviewImage = bmp;
            }
            ShowOverview = true;
            Console.WriteLine("Preview Initialized.");
            refresh = true;
        }
        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            zBar.ValueChanged += ValueChanged;
            cBar.ValueChanged += ValueChanged;
            tBar.ValueChanged += ValueChanged;
            zBar.ScrollEvent += ZBar_ScrollEvent;
            cBar.ScrollEvent += CBar_ScrollEvent;
            tBar.ScrollEvent += TBar_ScrollEvent;
            sk.MotionNotifyEvent += ImageView_MotionNotifyEvent;
            sk.ButtonPressEvent += ImageView_ButtonPressEvent;
            sk.ButtonReleaseEvent += ImageView_ButtonReleaseEvent;
            sk.ScrollEvent += ImageView_ScrollEvent;
            sk.PaintSurface += Render;
            sk.SizeAllocated += PictureBox_SizeAllocated;
            sk.AddEvents((int)
            (EventMask.ButtonPressMask
            | EventMask.ButtonReleaseMask
            | EventMask.KeyPressMask
            | EventMask.PointerMotionMask | EventMask.ScrollMask));
            this.KeyPressEvent += ImageView_KeyPressEvent;
            this.DestroyEvent += ImageView_DestroyEvent;
            this.DeleteEvent += ImageView_DeleteEvent;
            this.FocusInEvent += ImageView_FocusInEvent;
            rBox.Changed += RBox_Changed;
            gBox.Changed += GBox_Changed;
            bBox.Changed += BBox_Changed;
            //Context Menu
            goToImageMenu.ButtonPressEvent += GoToImageMenu_ButtonPressEvent;
            goToOriginMenu.ButtonPressEvent += GoToOriginMenu_ButtonPressEvent;

            roiDelete.ButtonPressEvent += RoiDelete_ButtonPressEvent;
            roiID.ButtonPressEvent += RoiID_ButtonPressEvent;
            copy.ButtonPressEvent += Copy_ButtonPressEvent;
            paste.ButtonPressEvent += Paste_ButtonPressEvent;
            draw.ButtonPressEvent += Draw_ButtonPressEvent;
            fill.ButtonPressEvent += Fill_ButtonPressEvent;

            play.ButtonPressEvent += Play_ButtonPressEvent;
            stop.ButtonPressEvent += Stop_ButtonPressEvent;
            playSpeed.ButtonPressEvent += PlaySpeed_ButtonPressEvent;
            setValueRange.ButtonPressEvent += SetValueRange_ButtonPressEvent;
            loop.ButtonPressEvent += Loop_ButtonPressEvent;

            zBar.ButtonPressEvent += ZBar_ButtonPressEvent;
            tBar.ButtonPressEvent += TBar_ButtonPressEvent;
            cBar.ButtonPressEvent += CBar_ButtonPressEvent;
            
        }

        private void ImageView_FocusInEvent(object o, FocusInEventArgs args)
        {
            App.SelectWindow(this.Title);
        }

        private void ImageView_DestroyEvent(object o, DestroyEventArgs args)
        {
            foreach (var item in this.Images)
            {
                BioLib.Images.RemoveImage(item);
                App.tabsView.RemoveViewer(this);
            }
        }

        int bar = 0;
        System.Threading.Thread threadZ = null;
        System.Threading.Thread threadC = null;
        System.Threading.Thread threadT = null;
        static bool playZ = false;
        static bool playC = false;
        static bool playT = false;
        static bool loopZ = true;
        static bool loopC = true;
        static bool loopT = true;
        public static int waitz = 1000;
        public static int waitc = 1000;
        public static int waitt = 1000;
        public static int startz = 0;
        public static int startc = 0;
        public static int startt = 0;
        public static int endz = 0;
        public static int endc = 0;
        public static int endt = 0;
        /// It plays the Z-stack of the image in the viewer.
        /// 
        /// @return The method is returning a ZCT object.
        private static void PlayZ()
        {
            do
            {
                ZCT coord = App.viewer.GetCoordinate();
                //Update view on main UI thread
                Application.Invoke(delegate
                {
                    App.viewer.SetCoordinate(coord.Z + 1, coord.C, coord.T);
                });
                if (coord.Z == endz)
                {
                    if (loopZ)
                    {
                        //Update view on main UI thread
                        Application.Invoke(delegate
                        {
                            App.viewer.SetCoordinate(startz, coord.C, coord.T);
                        });
                    }
                    else
                        return;
                }
                System.Threading.Thread.Sleep(waitz);
            } while (playZ);
        }
        /// It increments the C coordinate of the image by 1, waits for a specified amount of time, and
       /// then repeats until the user stops it.
       /// 
       /// @return The method is returning a string.
        private static void PlayC()
        {
            do
            {
                ZCT coord = App.viewer.GetCoordinate();
                Application.Invoke(delegate
                {
                    App.viewer.SetCoordinate(coord.Z, coord.C + 1, coord.T);
                });
                if (coord.C == endc)
                {
                    if (loopC)
                    {
                        //Update view on main UI thread
                        Application.Invoke(delegate
                        {
                            App.viewer.SetCoordinate(coord.Z, startc, coord.T);
                        });
                    }
                    else
                        return;
                }
                System.Threading.Thread.Sleep(waitc);
            } while (playC);
        }
        /// This function is called when the user clicks the play button. It will play the movie in the
        /// T dimension, and will loop if the user has selected the loop option.
        /// 
        /// @return The method is returning a string.
        private static void PlayT()
        {
            do
            {
                ZCT coord = App.viewer.GetCoordinate();
                Application.Invoke(delegate
                {
                    App.viewer.SetCoordinate(coord.Z, coord.C, coord.T + 1);
                });
                if (coord.T == endt)
                {
                    if (loopT)
                    {
                        //Update view on main UI thread
                        Application.Invoke(delegate
                        {
                            App.viewer.SetCoordinate(coord.Z, coord.C, startt);
                        });
                    }
                    else
                        return;
                }
                System.Threading.Thread.Sleep(waitt);
            } while (playT);
        }
        /// If the user right clicks on the bar, the barMenu pops up
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs The event that is triggered when a button is pressed.
        private void CBar_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            bar = 2;
            if (args.Event.Button == 3)
                barMenu.Popup();
        }

        /// If the user right clicks on the toolbar, the toolbar menu pops up
       /// 
       /// @param o The object that the event is being called from.
       /// @param ButtonPressEventArgs The event that is triggered when a button is pressed.
        private void TBar_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            bar = 1;
            if (args.Event.Button == 3)
                barMenu.Popup();
        }
        
        /// If the user right clicks on the ZBar, the ZBar menu pops up
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs The event that is triggered when a button is pressed.
        private void ZBar_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            bar = 0;
            if (args.Event.Button == 3)
                barMenu.Popup();
        }

        /// If the user clicks on the loop button, then the loop variable for the current bar is toggled
        /// 
        /// @param o the object that the event is attached to
        /// @param ButtonPressEventArgs args
        private void Loop_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (bar == 0)
            {
                if(loopZ)
                    loopZ = false;
                else
                    loopZ = true;
            }
            else if (bar == 1)
            {
                if (loopC)
                    loopC = false;
                else
                    loopC = true;
            }
            else if (bar == 2)
            {
                if (loopT)
                    loopT = false;
                else
                    loopT = true;
            }
        }

        /// It creates a new instance of the Play class, and then calls the Show() method on that
       /// instance
       /// 
       /// @param o The object that the event is being called from.
       /// @param ButtonPressEventArgs The event arguments that are passed to the event handler.
        private void SetValueRange_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Play play = Play.Create();
            play.Show();
        }

        /// This function creates a new instance of the Play class and shows it
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs The event arguments that are passed to the event handler.
        private void PlaySpeed_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Play play = Play.Create();
            play.Show();
        }

        /// If the bar is 0, then if playZ is true, set playZ to false, else set playZ to true.
        /// 
        /// @param o the object that the event is being called on
        /// @param ButtonPressEventArgs args
        private void Stop_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (bar == 0)
            {
                if (playZ)
                    playZ = false;
                else
                    playZ = true;
            }
            else if (bar == 1)
            {
                if (playT)
                    playT = false;
                else
                    playT = true;
            }
            else if (bar == 2)
            {
                if (playC)
                    playC = false;
                else
                    playC = true;
            }
        }

        /// When the play button is pressed, the program checks which bar is selected and then starts a
       /// thread that will play the selected bar
       /// 
       /// @param o the object that called the event
       /// @param ButtonPressEventArgs args
        private void Play_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if(bar == 0)
            {
                if (endz == 0)
                    endz = SelectedImage.SizeZ - 1;
                playZ = true;
                threadZ = new System.Threading.Thread(PlayZ);
                threadZ.Start();
            }
            else if (bar == 1)
            {
                if (endt == 0)
                    endt = SelectedImage.SizeT - 1;
                playT = true;
                threadT = new System.Threading.Thread(PlayT);
                threadT.Start();
            }
            else if (bar == 2)
            {
                if (endc == 0)
                    endc = SelectedImage.SizeC - 1;
                playC = true;
                threadC = new System.Threading.Thread(PlayC);
                threadC.Start();
            }
        }

        /// It takes the selected ROI's and draws them on the image
        /// 
        /// @param o the object that called the event
        /// @param ButtonPressEventArgs 
        private void Fill_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(SelectedBuffer);
            List<ROI> rois = new List<ROI>();
            rois.AddRange(SelectedImage.AnnotationsR);
            rois.AddRange(SelectedImage.AnnotationsG);
            rois.AddRange(SelectedImage.AnnotationsB);
            foreach (ROI item in rois)
            {
                Bio.Graphics.Pen p = new Bio.Graphics.Pen(Tools.DrawColor, (int)Tools.StrokeWidth, SelectedBuffer.BitsPerPixel);
                if (item.Selected)
                {
                    if (item.type == ROI.Type.Line)
                    {
                        PointD pf = SelectedImage.ToImageSpace(item.GetPoint(0));
                        PointD pf2 = SelectedImage.ToImageSpace(item.GetPoint(1));
                        g.DrawLine((int)pf.X, (int)pf.Y, (int)pf2.X, (int)pf2.Y);
                    }
                    else
                    if (item.type == ROI.Type.Rectangle)
                    {
                        g.FillRectangle(SelectedImage.ToImageSpace(item.Rect), p.color);
                    }
                    else
                    if (item.type == ROI.Type.Ellipse)
                    {
                        g.FillEllipse(SelectedImage.ToImageSpace(item.Rect), p.color);
                    }
                    else
                    if (item.type == ROI.Type.Freeform || item.type == ROI.Type.Polygon || item.type == ROI.Type.Polyline)
                    {
                        g.FillPolygon(SelectedImage.ToImageSpace(item.GetPointsF()), SelectedImage.ToImageSpace(item.Rect), p.color);
                    }
                }
            }
            UpdateImage();
            UpdateView();
        }

        /// Draws the selected annotations on the image
        /// 
        /// @param o the object that the event is being called from
        /// @param ButtonPressEventArgs args
        private void Draw_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(SelectedBuffer);
            List<ROI> rois = new List<ROI>();
            rois.AddRange(SelectedImage.AnnotationsR);
            rois.AddRange(SelectedImage.AnnotationsG);
            rois.AddRange(SelectedImage.AnnotationsB);
            foreach (ROI item in rois)
            {
                Bio.Graphics.Pen p = new Bio.Graphics.Pen(Tools.DrawColor, (int)Tools.StrokeWidth, SelectedBuffer.BitsPerPixel);
                g.pen = p;
                if (item.Selected)
                {
                    if (item.type == ROI.Type.Line)
                    {
                        PointD pf = SelectedImage.ToImageSpace(item.GetPoint(0));
                        PointD pf2 = SelectedImage.ToImageSpace(item.GetPoint(1));
                        g.DrawLine((int)pf.X, (int)pf.Y, (int)pf2.X, (int)pf2.Y);
                    }
                    else
                    if (item.type == ROI.Type.Rectangle)
                    {
                        g.DrawRectangle(SelectedImage.ToImageSpace(item.Rect));
                    }
                    else
                    if (item.type == ROI.Type.Ellipse)
                    {
                        g.DrawEllipse(SelectedImage.ToImageSpace(item.Rect));
                    }
                    else
                    if (item.type == ROI.Type.Freeform || item.type == ROI.Type.Polygon || item.type == ROI.Type.Polyline)
                    {
                        if (item.closed)
                        {
                            for (int i = 0; i < item.GetPointCount() - 1; i++)
                            {
                                PointD pf = SelectedImage.ToImageSpace(item.GetPoint(i));
                                PointD pf2 = SelectedImage.ToImageSpace(item.GetPoint(i+1));
                                g.DrawLine((int)pf.X, (int)pf.Y, (int)pf2.X, (int)pf2.Y);
                            }
                            PointD pp = SelectedImage.ToImageSpace(item.GetPoint(0));
                            PointD p2 = SelectedImage.ToImageSpace(item.GetPoint(item.GetPointCount() - 1));
                            g.DrawLine((int)pp.X, (int)pp.Y, (int)p2.X, (int)p2.Y);
                        }
                        else
                        {
                            for (int i = 0; i < item.GetPointCount() - 1; i++)
                            {
                                PointD pf = SelectedImage.ToImageSpace(item.GetPoint(i));
                                PointD pf2 = SelectedImage.ToImageSpace(item.GetPoint(i + 1));
                                g.DrawLine((int)pf.X, (int)pf.Y, (int)pf2.X, (int)pf2.Y);
                            }
                        }
                    }
                }
            }
            UpdateImage();
            UpdateView();
        }

        /// It pastes the selection.
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk3/stable/GtkWidget.html#GtkWidget-button-press-event
        private void Paste_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            PasteSelection();
        }

        /// It copies the selected text from the textview to the clipboard
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void Copy_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            CopySelection();
        }

        /// When the user closes the image tab, the tab is removed from the tab view
        /// 
        /// @param o The object that triggered the event
        /// @param DeleteEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-delete.html.en
        private void ImageView_DeleteEvent(object o, DeleteEventArgs args)
        {
            for (int i = 0; i < this.Images.Count; i++)
            {
                var item = this.Images[i];
                App.CloseWindow(item.Filename);
            }
        }

        /// When the user clicks on the "ID" button, a text input dialog is created and displayed. If
        /// the user clicks "OK", the ID of the selected annotation is set to the text entered by the
        /// user
        /// 
        /// @param o the object that the event is being called from
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk-sharp/stable/Gtk.ButtonPressEventArgs.html
        /// 
        /// @return The response type of the dialog.
        private void RoiID_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            TextInput ti = TextInput.Create();
            ti.Show();
            UpdateView();
        }

        /// This function removes the selected annotations from the image
       /// 
       /// @param o The object that the event is being called on.
       /// @param ButtonPressEventArgs
       /// https://developer.gnome.org/gtk3/stable/GtkButton.html#GtkButton-clicked
        private void RoiDelete_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            foreach (var item in selectedAnnotations)
            {
                SelectedImage.Annotations.Remove(item);
                Tools.selectedROI = null;
            }
            UpdateView();
        }

        /// When the user clicks the "Go to Origin" button, the viewer's origin is set to the negative
        /// of the image's location
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void GoToOriginMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.viewer.Origin = new PointD(-SelectedImage.Volume.Location.X, -SelectedImage.Volume.Location.Y);
        }

        /// This function is called when the user clicks on the "Go to Image" button in the menu
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk3/stable/GtkButton.html#GtkButton-clicked
        private void GoToImageMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.viewer.GoToImage();
        }

        #endregion

        /// When the combobox is changed, the third channel of the image is set to the value of the
        /// combobox
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void BBox_Changed(object sender, EventArgs e)
        {
            SelectedImage.rgbChannels[2] = bBox.Active;
            UpdateView();
        }

        /// When the combobox is changed, the green channel is set to the value of the combobox
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void GBox_Changed(object sender, EventArgs e)
        {
            SelectedImage.rgbChannels[1] = gBox.Active;
            UpdateView();
        }

        /// When the user clicks on the combobox, the function will update the rgbChannels array to
        /// reflect the new state of the combobox
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        private void RBox_Changed(object sender, EventArgs e)
        {
            SelectedImage.rgbChannels[0] = rBox.Active;
            UpdateView();
        }

        bool initialized = false;
        /// If the image is pyramidal, update the image. If the image is not initialized, go to the
       /// image. Update the view
       /// 
       /// @param o The object that the event is being called on.
       /// @param SizeAllocatedArgs
       /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-size-allocation.html.en
        private void PictureBox_SizeAllocated(object o, SizeAllocatedArgs args)
        {
            if(SelectedImage==null) return;
            if (SelectedImage.isPyramidal)
            {
                SelectedImage.PyramidalSize = new AForge.Size(sk.AllocatedWidth, sk.AllocatedHeight);
                UpdateImage(true);
            }
            if (!initialized)
            {
                GoToImage();
                initialized = true;
            }
            UpdateView();
        }

        /// > Draw an ellipse by drawing a circle and then scaling it
       /// 
       /// @param g The Cairo.Context object
       /// @param x The x-coordinate of the upper-left corner of the rectangle that defines the ellipse.
       /// @param y The y-coordinate of the center of the ellipse.
       /// @param width The width of the ellipse.
       /// @param height The height of the ellipse.
        private static void DrawEllipse(Cairo.Context g, double x, double y, double width, double height)
        {
            g.Save();
            g.MoveTo(x + (width / 2), y);
            g.Translate(x, y);
            g.Scale(width / 2.0, height / 2.0);
            g.Arc(0.0, 0.0, 1.0, 0.0, 2 * Math.PI);
            g.Restore();
            g.Stroke();
            g.Stroke();
        }
        /// It takes a System.Drawing.Color and returns a Cairo.Color
        /// 
        /// @param Color The color to convert
        /// 
        /// @return A Cairo.Color object.
        public static Cairo.Color FromColor(Color color)
        {
            return new Cairo.Color((double)color.R / 255, (double)color.G / 255, (double)color.B / 255);
        }

        /// If the scroll direction is up, and the value of the scrollbar is less than the upper limit,
        /// then increment the value of the scrollbar. If the scroll direction is down, and the value of
        /// the scrollbar is greater than the lower limit, then decrement the value of the scrollbar
        /// 
        /// @param o The object that the event is being called from.
        /// @param ScrollEventArgs This is the event that is passed to the event handler.
        private void TBar_ScrollEvent(object o, ScrollEventArgs args)
        {
            if (args.Event.Direction == ScrollDirection.Up)
            {
                if (tBar.Value + 1 <= tBar.Adjustment.Upper)
                    tBar.Value += 1;
            }
            else
            {
                if (tBar.Value - 1 >= 0)
                    tBar.Value -= 1;
            }
        }
        /// If the scroll direction is up, and the value of the scrollbar is less than the upper limit,
       /// then increment the value of the scrollbar. If the scroll direction is down, and the value of
       /// the scrollbar is greater than the lower limit, then decrement the value of the scrollbar
       /// 
       /// @param o The object that the event is being called from.
       /// @param ScrollEventArgs The event arguments for the scroll event.
        private void CBar_ScrollEvent(object o, ScrollEventArgs args)
        {
            if (args.Event.Direction == ScrollDirection.Up)
            {
                if (cBar.Value + 1 <= cBar.Adjustment.Upper)
                    cBar.Value += 1;
            }
            else
            {
                if (cBar.Value - 1 >= 0)
                    cBar.Value -= 1;
            }
        }
        /// If the scroll direction is up, and the value of the scrollbar is less than the upper limit,
        /// then increment the value of the scrollbar. If the scroll direction is down, and the value of
        /// the scrollbar is greater than the lower limit, then decrement the value of the scrollbar
        /// 
        /// @param o The object that the event is being called from.
        /// @param ScrollEventArgs This is the event that is passed to the event handler.
        private void ZBar_ScrollEvent(object o, ScrollEventArgs args)
        {
            if (args.Event.Direction == ScrollDirection.Up)
            {
                if (zBar.Value + 1 <= zBar.Adjustment.Upper)
                    zBar.Value += 1;
            }
            else
            {
                if (zBar.Value - 1 >= 0)
                    zBar.Value -= 1;
            }
        }

        public static Gdk.Key keyDown = Gdk.Key.Key_3270_Test;
        /// This function is called when a key is pressed. It checks if the key is a function key, and
        /// if so, it performs the function
        /// 
        /// @param o The object that the event is being called on
        /// @param KeyPressEventArgs
        /// https://developer.gnome.org/gtk-sharp/stable/Gtk.KeyPressEventArgs.html
        /// 
        /// @return The key that was pressed.
        private void ImageView_KeyPressEvent(object o, KeyPressEventArgs e)
        {
            Plugins.KeyDownEvent(o, e);
            keyDown = e.Event.Key;
            double moveAmount = 5 * Scale.Width;
            double zoom = Level;
            double movepyr = 50 * (Level+1);
            if (e.Event.Key == Gdk.Key.c && e.Event.State == ModifierType.ControlMask)
            {
                CopySelection();
            }
            if (e.Event.Key == Gdk.Key.v && e.Event.State == ModifierType.ControlMask)
            {
                PasteSelection();
            }
            if (e.Event.Key == Gdk.Key.e)
            {
                if (SelectedImage.isPyramidal)
                {
                    Resolution--;
                }
                else
                    Scale = new SizeF(Scale.Width - 0.1f, Scale.Height - 0.1f);
            }
            if (e.Event.Key == Gdk.Key.q)
            {
                if (SelectedImage.isPyramidal)
                {
                    Resolution++;
                }
                else
                    Scale = new SizeF(Scale.Width + 0.1f, Scale.Height + 0.1f);
            }
            if (e.Event.Key == Gdk.Key.w || e.Event.Key == Gdk.Key.W)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new PointD(PyramidalOrigin.X, PyramidalOrigin.Y + movepyr);
                }
                else
                    Origin = new PointD(Origin.X, Origin.Y + moveAmount);
            }
            if (e.Event.Key == Gdk.Key.s || e.Event.Key == Gdk.Key.S)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new PointD(PyramidalOrigin.X, PyramidalOrigin.Y - movepyr);
                }
                else
                    Origin = new PointD(Origin.X, Origin.Y - moveAmount);
            }
            if (e.Event.Key == Gdk.Key.a || e.Event.Key == Gdk.Key.A)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new PointD(PyramidalOrigin.X - movepyr, PyramidalOrigin.Y);
                }
                else
                    Origin = new PointD(Origin.X + moveAmount, Origin.Y);
            }
            if (e.Event.Key == Gdk.Key.d || e.Event.Key == Gdk.Key.D)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new PointD(PyramidalOrigin.X + movepyr, PyramidalOrigin.Y);
                }
                else
                    Origin = new PointD(Origin.X - moveAmount, Origin.Y);
            }
            if (e.Event.Key == Gdk.Key.e)
            {
                Resolution += zoom;
            }
            if (e.Event.Key == Gdk.Key.q)
            {
                Resolution -= zoom;
            }
            foreach (Function item in Function.Functions.Values)
            {
                if(item.Key == e.Event.Key && item.Modifier == e.Event.State)
                {
                    if (item.FuncType == Function.FunctionType.ImageJ)
                        item.PerformFunction(true);
                    else
                        item.PerformFunction(false);
                }
            }
            Scripting.State st = new Scripting.State();
            st.key = e.Event.Key;
            st.p = mouseDown;
            st.type = Scripting.Event.Down;
            Scripting.UpdateState(st);
            if(SelectedImage.isPyramidal)
                UpdateView(true,true);
            else
                UpdateView(true,false);
        }
        /// The function is called when the user presses a key on the keyboard
        /// 
        /// @param o The object that the event is being called from.
        /// @param KeyPressEventArgs
        /// https://developer.gnome.org/gtk-sharp/stable/Gtk.KeyPressEventArgs.html
        private void ImageView_KeyUpEvent(object o, KeyPressEventArgs e)
        {
            Plugins.KeyUpEvent(o, e);
            keyDown = Gdk.Key.Key_3270_Test;
            Scripting.State st = new Scripting.State();
            st.key = e.Event.Key;
            st.p = mouseUp;
            st.type = Scripting.Event.Up;
            Scripting.UpdateState(st);
        }
        /// The function is called when the user scrolls the mouse wheel. If the user is holding down
        /// the control key, the function will change the Level of the image. If the user is not
        /// holding down the control key, the function will change the z-slice of the image
        /// 
        /// @param o the object that the event is being called on
        /// @param ScrollEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-scroll-events.html.en
        private void ImageView_ScrollEvent(object o, ScrollEventArgs args)
        {
            Plugins.ScrollEvent(o, args);
            float dx = Scale.Width / 50;
            float dy = Scale.Height / 50;
            if (!SelectedImage.isPyramidal)
                if (args.Event.State.HasFlag(ModifierType.ControlMask))
                {
                    if (args.Event.Direction == ScrollDirection.Up)
                    {
                        pxWmicron -= 0.01f;
                        pxHmicron -= 0.01f;
                    }
                    else
                    {
                        pxWmicron += 0.01f;
                        pxHmicron += 0.01f;
                    }
                }
                else
                if (args.Event.Direction == ScrollDirection.Up)
                {
                    if (zBar.Value + 1 <= zBar.Adjustment.Upper)
                        zBar.Value += 1;
                }
                else
                {
                    if (zBar.Value - 1 >= zBar.Adjustment.Lower)
                        zBar.Value -= 1;
                }
                if (args.Event.State.HasFlag(ModifierType.ControlMask) && SelectedImage.isPyramidal)
                if (args.Event.Direction == ScrollDirection.Up)
                {
                    Resolution *= 0.80;
                }
                else
                {
                    Resolution *= 1.20;
                }
                UpdateView(true, false);
        }

        /// The function ValueChanged is called when the value of the trackbar is changed.
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void ValueChanged(object? sender, EventArgs e)
        {
            SetCoordinate((int)zBar.Value, (int)cBar.Value, (int)tBar.Value);
        }

        /// It updates the GUI to reflect the current state of the image
        public void UpdateGUI()
        {
            cBar.Adjustment.Lower = 0;
            cBar.Adjustment.Upper = Images[selectedIndex].SizeC - 1;
            zBar.Adjustment.Lower = 0;
            zBar.Adjustment.Upper = Images[selectedIndex].SizeZ - 1;
            tBar.Adjustment.Lower = 0;
            tBar.Adjustment.Upper = Images[selectedIndex].SizeT - 1;
            if (endz == 0 || endz > Images[selectedIndex].SizeZ - 1)
                endz = Images[selectedIndex].SizeZ - 1;
            if (endc == 0 || endc > Images[selectedIndex].SizeC - 1)
                endc = Images[selectedIndex].SizeC - 1;
            if (endt == 0 || endt > Images[selectedIndex].SizeT - 1)
                endt = Images[selectedIndex].SizeT - 1;
            var store = new ListStore(typeof(string));
            foreach (Channel c in SelectedImage.Channels)
            {
                store.AppendValues(c.Name);
            }
            // Set the model for the ComboBox
            rBox.Model = store;
            gBox.Model = store;
            bBox.Model = store;
            if (SelectedImage.Channels.Count > 2)
            {
                rBox.Active = 0;
                gBox.Active = 1;
                bBox.Active = 2;
            }
            else
            if (SelectedImage.Channels.Count == 2)
            {
                rBox.Active = 0;
                gBox.Active = 1;
            }
            imagesMenu = new Menu();
            foreach (BioImage b in Images)
            {
                MenuItem mi = new MenuItem(b.Filename);
                mi.ButtonPressEvent += Mi_ButtonPressEvent;
                imagesMenu.Append(mi);
            }
            goToImageMenu.Submenu = imagesMenu;
            goToImageMenu.ShowAll();
        }

        /// When a menu item is clicked, find the image that matches the menu item's label, and go to
       /// that image
       /// 
       /// @param o The object that the event is being called on.
       /// @param ButtonPressEventArgs
       /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-event-handling.html.en
       /// 
       /// @return The return value is the index of the image in the list.
        private void Mi_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            MenuItem menuItem = (MenuItem)o;
            int i = 0;
            foreach (BioImage b in Images)
            {
                if(b.Filename == menuItem.Label)
                {
                    GoToImage(i);
                    return;
                }
                i++;
            }
        }

       /// It takes the selected ROIs and copies them to the clipboard
        public void CopySelection()
        {
            copys.Clear();
            string s = "";
            List<ROI> rois = new List<ROI>();
            rois.AddRange(SelectedImage.AnnotationsR);
            rois.AddRange(SelectedImage.AnnotationsG);
            rois.AddRange(SelectedImage.AnnotationsB);
            foreach (ROI item in rois)
            {
                if (item.Selected)
                {
                    copys.Add(item);
                    s += BioImage.ROIToString(item);
                }
            }
            Clipboard clipboard = Clipboard.Get(Gdk.Selection.Clipboard);
            clipboard.Text = s;
        }
        /// The function takes the text from the clipboard and splits it into lines. Each line is then
        /// converted into an ROI object and added to the list of annotations
        public void PasteSelection()
        {
            Clipboard clipboard = Clipboard.Get(Gdk.Selection.Clipboard);
            string text = clipboard.WaitForText();
            string[] sts = text.Split(BioImage.NewLine);
            foreach (string line in sts)
            {
                if (line.Length > 8)
                {
                    ROI an = BioImage.StringToROI(line);
                    //We set the coordinates of the ROI's we are pasting
                    an.coord = GetCoordinate();
                    SelectedImage.Annotations.Add(an);
                }
            }
            UpdateView();
        }

        /* Defining an enum. */
        public enum ViewMode
        {
            Raw,
            Filtered,
            RGBImage,
            Emission,
        }
        bool openSlide = false;
        public bool OpenSlide
        {
            get { return openSlide; }
            set { openSlide = value; }
        }
        public static BioImage SelectedImage
        {
            get
            {
                if (App.viewer == null)
                    return null;
                if(App.viewer.Images.Count == 0)
                    return null;
                return App.viewer.Images[App.viewer.SelectedIndex];
            }
            set
            {
                App.viewer.Images[App.viewer.SelectedIndex] = value;
            }
        }
        private ViewMode viewMode = ViewMode.Filtered;
        /* Setting the view mode of the application. */
        public ViewMode Mode
        {
            get
            {
                return viewMode;
            }
            set
            {
                viewMode = value;
                if (viewMode == ViewMode.RGBImage)
                {
                    rgbStack.VisibleChild = rgbStack.Children[1];
                }
                else
                if (viewMode == ViewMode.Filtered)
                {
                    rgbStack.VisibleChild = rgbStack.Children[0];
                }
                else
                if (viewMode == ViewMode.Raw)
                {
                    rgbStack.VisibleChild = rgbStack.Children[0];
                }
                else
                {
                    rgbStack.VisibleChild = rgbStack.Children[0];
                }
                UpdateImage();
                UpdateView();
            }
        }
        /* A property that returns the R channel of the selected image. */
        public Channel RChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[0]];
            }
        }
        /* A property that returns the G channel of the selected image. */
        public Channel GChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[1]];
            }
        }
        /* A property that returns the B channel of the selected image. */
        public Channel BChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[2]];
            }
        }
        PointD origin = new PointD(0, 0);
        /* Origin of the viewer in microns */
        public PointD Origin
        {
            get { return origin; }
            set
            {
                if(AllowNavigation)
                origin = value;
                UpdateView(true, false);
            }
        }
        /* Origin of the viewer in microns */
        public PointD TopRightOrigin
        {
            get 
            {
                return new PointD((Origin.X - ((sk.AllocatedWidth / 2) * pxWmicron)), (Origin.Y - ((sk.AllocatedHeight / 2) * pxHmicron)));
            }
        }
        public PointD PyramidalOriginTransformed
        {
            get { return new PointD(PyramidalOrigin.X * Resolution, PyramidalOrigin.Y * Resolution); }
            set { PyramidalOrigin = new PointD(value.X / Resolution, value.Y / Resolution); }
        }
        /* Setting the origin of a pyramidal image. */
        public PointD PyramidalOrigin
        {
            get 
            {
                return SelectedImage.PyramidalOrigin;
            }
            set
            {
                if (!AllowNavigation)
                    return;
                PointD p = new PointD();
                if (value.X > 0)
                    p.X = value.X;
                if (value.Y > 0)
                    p.Y = value.Y;
                SelectedImage.PyramidalOrigin = p;
                UpdateView(true, true);
            }
        }
        /* Setting the Level of the image. */
        private double zoomFactor = 1.0;
        public double Resolution
        {
            get
            {
                return SelectedImage.Resolution;
            }
            set
            {
                SelectedImage.Resolution = value;
                // Calculate the scaling factor based on the new and current resolution
                double scalingFactor = value / SelectedImage.Resolution;

                // Convert the screen coordinates of the mouse position to image coordinates
                PointD imageMousePosition = new PointD(
                    (PyramidalOrigin.X / scalingFactor),
                    (PyramidalOrigin.Y / scalingFactor)
                );

                // Calculate the mouse position in viewport space (center of viewport)
                PointD viewportCenter = new PointD(
                    SelectedImage.PyramidalSize.Width / 2,
                    SelectedImage.PyramidalSize.Height / 2
                );
                // Update the resolution
                
                // Calculate new PyramidalOrigin so that the image stays centered on the middle
                PyramidalOrigin = new PointD(
                    imageMousePosition.X - (viewportCenter.X * (scalingFactor - 1)),
                    imageMousePosition.Y - (viewportCenter.Y * (scalingFactor - 1))
                );
            }
        }
        private int l;
        public int Level
        {
            get
            {
                if(SelectedImage.Type == BioImage.ImageType.well)
                {
                    return (int)SelectedImage.Level;
                }
                if(SelectedImage.isPyramidal)
                if (!openSlide)
                    l = OpenSlideGTK.TileUtil.GetLevel(_slideBase.Schema.Resolutions, Resolution);
                else
                    l = OpenSlideGTK.TileUtil.GetLevel(_openSlideBase.Schema.Resolutions, Resolution);
                return l;
            }
            set
            {
                if (value < 0)
                    return;
                l = value;
                SelectedImage.Level = l;
                if (SelectedImage.Type == BioImage.ImageType.well)
                {
                    SelectedImage.UpdateBuffersWells();
                    UpdateImages();
                    UpdateView();
                }
            }
        }

        SizeF scale = new SizeF(1, 1);
        /* A property that is used to set the scale of the view. */
        public SizeF Scale
        {
            get
            {
                return scale;
            }
            set
            {
                scale = value;
                // update ui on main UI thread
                Application.Invoke(delegate
                {
                    UpdateView(true,false);
                });
            }
        }
        /// It updates the status of the user.
        public void UpdateStatus()
        {
            if (SelectedImage.Buffers.Count == 0)
                return;
            if(SelectedImage.Type == BioImage.ImageType.well)
            {
                statusLabel.Text = (zBar.Value + 1) + "/" + (zBar.Adjustment.Upper + 1) + ", " + (cBar.Value + 1) + "/" + (cBar.Adjustment.Upper + 1) + ", " + (tBar.Value + 1) + "/" + (tBar.Adjustment.Upper + 1) + ", " +
                mousePoint + mouseColor + ", " + SelectedImage.Buffers[0].PixelFormat.ToString() + ", (" + SelectedImage.Volume.Location.X.ToString("N2") + ", " + SelectedImage.Volume.Location.Y.ToString("N2") + ") " 
                + Origin.X.ToString("N2") + "," + Origin.Y.ToString("N2") + " , Well:" + SelectedImage.Level;
            }
            else
            statusLabel.Text = (zBar.Value + 1) + "/" + (zBar.Adjustment.Upper + 1) + ", " + (cBar.Value + 1) + "/" + (cBar.Adjustment.Upper + 1) + ", " + (tBar.Value + 1) + "/" + (tBar.Adjustment.Upper + 1) + ", " +
                mousePoint + mouseColor + ", " + SelectedImage.Buffers[0].PixelFormat.ToString() + ", (" + SelectedImage.Volume.Location.X.ToString("N2") + ", " + SelectedImage.Volume.Location.Y.ToString("N2") + ") "
                + Origin.X.ToString("N2") + "," + Origin.Y.ToString("N2") + ", Res:" + Resolution;
        }
        /// It updates the view.
        public void UpdateView(bool update = false, bool updateImages = false)
        {
            if(updateImages)
            UpdateImages(true);
            refresh = true;
            if(update)
            sk.QueueDraw();
        }
        private string mousePoint = "";
        private string mouseColor = "";

        public bool showRROIs = true;
        public bool showGROIs = true;
        public bool showBROIs = true;

        public double GetScale()
        {
            return ToViewSizeW(ROI.selectBoxSize / Scale.Width);
        }
        public static bool x1State;
        public static bool x2State;
        public static bool mouseLeftState;
        public static ModifierType Modifiers;
        PointD mouseD = new PointD(0, 0);

        public List<ROI> GetSelectedROIs()
        {
            List<ROI> roi = new List<ROI>();
            List<ROI> rois = new List<ROI>();
            rois.AddRange(SelectedImage.AnnotationsR);
            rois.AddRange(SelectedImage.AnnotationsG);
            rois.AddRange(SelectedImage.AnnotationsB);
            foreach (ROI r in rois)
            {
                if(r.Selected)
                {
                    roi.Add(r);
                }
            }
            return roi;
        }

        /// This function is called when the mouse is moved over the image. It updates the mouse
        /// position, and if the user is drawing a brush stroke, it draws the stroke on the image
        /// 
        /// @param o the object that the event is being called from
        /// @param MotionNotifyEventArgs 
        private void ImageView_MotionNotifyEvent(object o, MotionNotifyEventArgs e)
        {
            UpdateView(true, false);
            App.viewer = this;
            Modifiers = e.Event.State;
            MouseMove = new PointD(e.Event.X,e.Event.Y);
            MouseMoveInt = new PointD((int)e.Event.X, (int)e.Event.Y);
            PointD p = ImageToViewSpace(e.Event.X, e.Event.Y);
            PointD ip = SelectedImage.ToImageSpace(p);
            App.tools.ToolMove(p, e);
            Tools.currentTool.Rectangle = new RectangleD(mouseDown.X, mouseDown.Y, p.X - mouseDown.X, p.Y - mouseDown.Y);
            mousePoint = "(" + (p.X.ToString("F")) + ", " + (p.Y.ToString("F")) + ")";
            
            if (SelectedImage.isPyramidal && overview.IntersectsWith(e.Event.X, e.Event.Y)  && e.Event.State.HasFlag(ModifierType.Button1Mask))
            {
                if (!OpenSlide)
                {
                    double dsx = SelectedImage.SlideBase.Schema.Resolutions[Level].UnitsPerPixel / Resolution;
                    Resolution rs = SelectedImage.Resolutions[Level];
                    double dx = ((double)e.Event.X / overview.Width) * (rs.SizeX * dsx) - ((SelectedImage.PyramidalSize.Width / 2) * dsx);
                    double dy = ((double)e.Event.Y / overview.Height) * (rs.SizeY * dsx) - ((SelectedImage.PyramidalSize.Height / 2) * dsx);
                    PyramidalOrigin = new PointD(dx, dy);
                }
                else
                {
                    double dsx = SelectedImage.OpenSlideBase.Schema.Resolutions[Level].UnitsPerPixel / Resolution;
                    Resolution rs = SelectedImage.Resolutions[Level];
                    double dx = ((double)e.Event.X / overview.Width) * (rs.SizeX * dsx) - ((SelectedImage.PyramidalSize.Width / 2) * dsx);
                    double dy = ((double)e.Event.Y / overview.Height) * (rs.SizeY * dsx) - ((SelectedImage.PyramidalSize.Height / 2) * dsx);
                    PyramidalOrigin = new PointD(dx, dy);
                }
                UpdateView(true);
            }
            //If point selection tool is clicked we  
            if (Tools.currentTool.type == Tools.Tool.Type.pointSel && e.Event.State.HasFlag(ModifierType.Button1Mask))
            {
                List<ROI> rois = new List<ROI>();
                rois.AddRange(SelectedImage.Annotations);
                foreach (ROI an in rois)
                {
                    if(an.Selected)
                    if (an.selectedPoints.Count > 0 && an.selectedPoints.Count < an.GetPointCount())
                    {
                        //If the selection is rectangle or ellipse we resize the annotation based on corners
                        if (an.type == ROI.Type.Rectangle || an.type == ROI.Type.Ellipse)
                        {
                            RectangleD d = an.Rect;
                            if (an.selectedPoints[0] == 0)
                            {
                                double dw = d.X - p.X;
                                double dh = d.Y - p.Y;
                                d.X = p.X;
                                d.Y = p.Y;
                                d.W += dw;
                                d.H += dh;
                            }
                            else
                            if (an.selectedPoints[0] == 1)
                            {
                                double dw = p.X - (d.W + d.X);
                                double dh = d.Y - p.Y;
                                d.W += dw;
                                d.H += dh;
                                d.Y -= dh;
                            }
                            else
                            if (an.selectedPoints[0] == 2)
                            {
                                double dw = d.X - p.X;
                                double dh = p.Y - (d.Y + d.H);
                                d.W += dw;
                                d.H += dh;
                                d.X -= dw;
                            }
                            else
                            if (an.selectedPoints[0] == 3)
                            {
                                double dw = d.X - p.X;
                                double dh = d.Y - p.Y;
                                d.W = p.X - an.X;
                                d.H = p.Y - an.Y;
                            }
                            an.Rect = d;
                        }
                        else
                        {
                            PointD pod = new PointD(p.X - pd.X, p.Y - pd.Y);
                            //PointD dif = new PointD((e.Event.X - ed.X) * PxWmicron, (e.Event.Y - ed.Y) * PxHmicron);
                            for (int i = 0; i < an.selectedPoints.Count; i++)
                            {
                                PointD poid = an.GetPoint(an.selectedPoints[i]);
                                an.UpdatePoint(new PointD(poid.X + pod.X, poid.Y + pod.Y), an.selectedPoints[i]);
                            }
                        }
                    }
                    else
                    {
                        PointD pod = new PointD(p.X - pd.X, p.Y - pd.Y);
                        for (int i = 0; i < an.GetPointCount(); i++)
                        {
                            PointD poid = an.PointsD[i];
                            an.UpdatePoint(new PointD(poid.X + pod.X, poid.Y + pod.Y), i);
                        }
                    }
                }
                UpdateView();
            }

            if (Tools.currentTool != null)
            {
                if(SelectedImage.isPyramidal)
                {
                    ip = new PointD(e.Event.X, e.Event.Y);
                }
                if (Tools.currentTool.type == Tools.Tool.Type.brush && e.Event.State.HasFlag(ModifierType.Button1Mask))
                {
                    Tools.Tool tool = Tools.currentTool;
                    Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(SelectedBuffer);
                    g.pen = new Bio.Graphics.Pen(Tools.DrawColor, (int)Tools.StrokeWidth, ImageView.SelectedImage.bitsPerPixel);
                    g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)Tools.StrokeWidth, (int)Tools.StrokeWidth), g.pen.color);
                    UpdateImage(true);
                }
                else
                if (Tools.currentTool.type == Tools.Tool.Type.eraser && e.Event.State.HasFlag(ModifierType.Button1Mask))
                {
                    Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                    Bio.Graphics.Pen pen = new Bio.Graphics.Pen(Tools.EraseColor, (int)Tools.StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
                    g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)Tools.StrokeWidth, (int)Tools.StrokeWidth), pen.color);
                    //pen.Dispose();
                    App.viewer.UpdateImages(true);
                }
            }
            UpdateStatus();
            pd = p;
        }
        
        /// The function is called when the mouse button is released. It checks if the mouse button is
        /// the left button, and if it is, it sets the mouseLeftState to false. It then sets the viewer
        /// to the current viewer, and converts the mouse coordinates to view space. It then sets the
        /// mouseUp variable to the pointer variable. It then checks if the mouse button is the middle
        /// button, and if it is, it checks if the selected image is pyramidal. If it is, it sets the
        /// pyramidal origin to the mouse coordinates. If it isn't, it sets the origin to the mouse
        /// coordinates. It then updates the image and the view. It then checks if the selected image is
        /// null, and if it is, it returns. It then calls the ToolUp function in the tools class,
        /// passing in the pointer and the event
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonReleaseEventArgs 
        /// 
        /// @return The image is being returned.
        private void ImageView_ButtonReleaseEvent(object o, ButtonReleaseEventArgs e)
        {
            Modifiers = e.Event.State;
            MouseUpInt = new PointD((int)e.Event.X, (int)e.Event.Y);
            if (e.Event.Button == 1)
                mouseLeftState = false;
            App.viewer = this;
            PointD pointer = ImageToViewSpace(e.Event.X, e.Event.Y);
            mouseUp = pointer;
            if (e.Event.State.HasFlag(ModifierType.Button2Mask))
            {
                if (SelectedImage != null && !SelectedImage.isPyramidal)
                {
                    PointD pd = new PointD(pointer.X - mouseDown.X, pointer.Y - mouseDown.Y);
                    origin = new PointD(origin.X + pd.X, origin.Y + pd.Y);
                }
                UpdateImages(true);
                UpdateView();
            }
            if (SelectedImage == null)
                return;
            App.tools.ToolUp(pointer, e);
        }
        PointD pd;
        PointD mouseDownInt = new PointD(0, 0);
        PointD mouseMoveInt = new PointD(0, 0);
        PointD mouseUpInt = new PointD(0, 0);
        PointD mouseUp = new PointD(0, 0);
        PointD mouseDown = new PointD(0, 0);
        PointD mouseMove = new PointD(0, 0);
        /* A property that returns the value of the mouseDownInt variable. */
        public PointD MouseDownInt
        {
            get { return mouseDownInt; }
            set { mouseDownInt = value; }
        }
        public PointD MouseMoveInt
        {
            get { return mouseMoveInt; }
            set { mouseMoveInt = value; }
        }
        public PointD MouseUpInt
        {
            get { return mouseUpInt; }
            set { mouseUpInt = value; }
        }
        public PointD MouseDown
        {
            get { return mouseDown; }
            set { mouseDown = value; }
        }
        public PointD MouseUp
        {
            get { return mouseUp; }
            set { mouseUp = value; }
        }
        public PointD MouseMove
        {
            get { return mouseMove; }
            set { mouseMove = value; }
        }
        /// The function is called when the user clicks on the image. It checks if the user clicked on
        /// an annotation, and if so, it selects the annotation
        /// 
        /// @param o the object that the event is being called on
        /// @param ButtonPressEventArgs e.Event.State
        /// 
        /// @return The return value is a tuple of the form (x,y,z,c,t) where x,y,z,c,t are the
        /// coordinates of the pixel in the image.
        private void ImageView_ButtonPressEvent(object o, ButtonPressEventArgs e)
        {
            Modifiers = e.Event.State;
            if (e.Event.Button == 1)
                mouseLeftState = true;
            else
                mouseLeftState = false;
            App.viewer = this;
            PointD pointer = ImageToViewSpace(e.Event.X, e.Event.Y);
            MouseDownInt = new PointD(e.Event.X, e.Event.Y);
            pd = pointer;
            mouseDown = pd;
            mouseD = SelectedImage.ToImageSpace(pd);
            
            if (SelectedImage == null)
                return;
            PointD ip = pointer; // SelectedImage.ToImageSpace(pointer);
            if (ImageView.SelectedImage.Type == BioImage.ImageType.well)
            {
                if (e.Event.Button == 4)
                {
                    Level = ImageView.SelectedImage.Level - 1;
                }
                else
                if (e.Event.Button == 5)
                {
                    Level = ImageView.SelectedImage.Level + 1;
                }
            }

            if (e.Event.Button == 3)
                contextMenu.Popup();
            if (e.Event.Button == 4 && Mode != ViewMode.RGBImage)
            {
                if (cBar.Value < cBar.Adjustment.Upper)
                    cBar.Value++;
                x1State = true;
            }
            else if (e.Event.Button == 4 && e.Event.State == ModifierType.ControlMask)
            {
                if (tBar.Value < tBar.Adjustment.Upper)
                    tBar.Value++;
                x1State = true;
            }
            if (e.Event.State != ModifierType.Button4Mask)
                x1State = false;

            if (e.Event.Button == 5 && Mode != ViewMode.RGBImage)
            {
                if (cBar.Value > cBar.Adjustment.Lower)
                    cBar.Value--;
                x2State = true;
            }
            else if (e.Event.Button == 5 && Modifiers == ModifierType.ControlMask && Mode != ViewMode.RGBImage)
            {
                if (tBar.Value > cBar.Adjustment.Lower)
                    tBar.Value--;
                x2State = true;
            }
            if (e.Event.State != ModifierType.Button5Mask)
                x2State = false;
            int ind = 0;
            //We select the image that has been clicked
            foreach (BioImage b in Images)
            {
                RectangleD r = new RectangleD(b.Volume.Location.X, b.Volume.Location.Y, b.Volume.Width, b.Volume.Height);
                if (r.IntersectsWith(pointer))
                {
                    selectedIndex = ind;
                    UpdateGUI();
                    break;
                }
                else
                {
                    foreach (var item in b.Annotations)
                    {
                        if (!item.BoundingBox.IntersectsWith(pointer))
                            item.Selected = false;
                    }
                }
                ind++;
            }

            //Lets handle point selection & move tool clicks.
            if (Tools.currentTool.type == Tools.Tool.Type.pointSel || Tools.currentTool.type == Tools.Tool.Type.move  && e.Event.Button == 1 )
            {
                bool clearSel = true;
                float width = ROI.selectBoxSize;
                foreach (BioImage bi in Images)
                {
                    List<ROI> rois = new List<ROI>();
                    rois.AddRange(SelectedImage.Annotations);
                    foreach (ROI an in rois)
                    {
                        if (!Modifiers.HasFlag(ModifierType.ControlMask))
                            an.Selected = false;
                        if(an.type == ROI.Type.Mask)
                        {
                            RectangleD rd = new RectangleD(SelectedImage.StageSizeX + (an.roiMask.X * an.roiMask.PhysicalSizeX), SelectedImage.StageSizeY + (an.roiMask.Y * an.roiMask.PhysicalSizeY), an.roiMask.Width * an.roiMask.PhysicalSizeX, an.roiMask.Height * an.roiMask.PhysicalSizeY);
                            if (rd.IntersectsWith(new RectangleD((float)pointer.X, (float)pointer.Y, SelectedImage.PhysicalSizeX, SelectedImage.PhysicalSizeY)))
                                an.Selected = true;
                            else
                                an.Selected = false;
                        }
                        else
                        if((an.coord == GetCoordinate() && Mode != ViewMode.RGBImage) || (an.coord.Z == GetCoordinate().Z && an.coord.T == GetCoordinate().T && Mode == ViewMode.RGBImage))
                        if (an.GetSelectBound(ROI.selectBoxSize * SelectedImage.PhysicalSizeX, ROI.selectBoxSize * SelectedImage.PhysicalSizeY).IntersectsWith(new RectangleD((float)pointer.X, (float)pointer.Y,SelectedImage.PhysicalSizeX, SelectedImage.PhysicalSizeY)))
                        {
                            //We clicked inside an ROI so selection should not be cleared.
                            clearSel = false;
                            selectedAnnotations.Add(an);
                            an.Selected = true;
                            Tools.selectedROI = an;
                            RectangleD[] sels = an.GetSelectBoxes(width);
                            RectangleD r = new RectangleD((float)pointer.X, (float)pointer.Y, (float)sels[0].W, (float)sels[0].H);
                            if(an.type != ROI.Type.Mask)
                            for (int i = 0; i < sels.Length; i++)
                            {
                                RectangleF rd = new RectangleF((float)pointer.X, (float)pointer.Y, (float)ToViewW(1), (float)ToViewH(1));
                                if (sels[i].ToRectangleF().IntersectsWith(rd))
                                {
                                    an.selectedPoints.Add(i);
                                }
                            }
                        }  
                    }
                }
                UpdateView();
            }
            if (e.Event.Button == 1)
            {
                PointD s = new PointD(e.Event.X, e.Event.Y);
                if ((s.X < SelectedImage.SizeX && (s.Y < SelectedImage.SizeY)) || (s.X >= 0 && (s.Y >= 0)))
                {
                    int zc = SelectedImage.Coordinate.Z;
                    int cc = SelectedImage.Coordinate.C;
                    int tc = SelectedImage.Coordinate.T;
                    if (SelectedImage.isPyramidal)
                    {
                        if (SelectedImage.isRGB)
                        {
                            float r = SelectedImage.GetValueRGB(zc, RChannel.Index, tc, (int)s.X, (int)s.Y, 0);
                            float g = SelectedImage.GetValueRGB(zc, GChannel.Index, tc, (int)s.X, (int)s.Y, 1);
                            float b = SelectedImage.GetValueRGB(zc, BChannel.Index, tc, (int)s.X, (int)s.Y, 2);
                            mouseColor = ", " + r + "," + g + "," + b;
                        }
                        else
                        {
                            float r = SelectedImage.GetValueRGB(zc, (int)cBar.Value, tc, (int)mouseD.X, (int)mouseD.Y, 0);
                            mouseColor = ", " + r;
                        }
                    }
                    else
                    {
                        if (SelectedImage.isRGB)
                        {
                            float r = SelectedImage.GetValueRGB(zc, RChannel.Index, tc, (int)s.X, (int)s.Y, 0);
                            float g = SelectedImage.GetValueRGB(zc, GChannel.Index, tc, (int)s.X, (int)s.Y, 1);
                            float b = SelectedImage.GetValueRGB(zc, BChannel.Index, tc, (int)s.X, (int)s.Y, 2);
                            mouseColor = ", " + r + "," + g + "," + b;
                        }
                        else
                        {
                            float r = SelectedImage.GetValueRGB(zc, (int)cBar.Value, tc, (int)s.X, (int)s.Y, 0);
                            mouseColor = ", " + r;
                        }
                    }
                }
                if (Tools.currentTool != null)
                {
                    if (SelectedImage.isPyramidal)
                    {
                        ip = new PointD(e.Event.X, e.Event.Y);
                    }
                    if (Tools.currentTool.type == Tools.Tool.Type.brush && e.Event.State.HasFlag(ModifierType.Button1Mask))
                    {
                        Tools.Tool tool = Tools.currentTool;
                        Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(SelectedBuffer);
                        g.pen = new Bio.Graphics.Pen(Tools.DrawColor, (int)Tools.StrokeWidth, ImageView.SelectedImage.bitsPerPixel);
                        g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)Tools.StrokeWidth, (int)Tools.StrokeWidth), g.pen.color);
                        UpdateImage(true);
                    }
                    else
                    if (Tools.currentTool.type == Tools.Tool.Type.eraser && e.Event.State.HasFlag(ModifierType.Button1Mask))
                    {
                        Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                        Bio.Graphics.Pen pen = new Bio.Graphics.Pen(Tools.EraseColor, (int)Tools.StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
                        g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)Tools.StrokeWidth, (int)Tools.StrokeWidth), pen.color);
                        //pen.Dispose();
                        App.viewer.UpdateImages(true);
                    }
                }
            }
            UpdateStatus();
            App.tools.ToolDown(pointer, e);
        }
        List<ROI> copys = new List<ROI>();

        #region Conversion
        private int Width
        {
            get 
            { 
                return sk.AllocatedWidth;
            }
        }
        private int Height
        {
            get
            {
                return sk.AllocatedHeight;
            }
        }

        /// It takes a point in the image space and returns the point in the view space
        /// 
        /// @param x the x coordinate of the point in the image
        /// @param y the y coordinate of the point in the image
        /// 
        /// @return The point in the image space that corresponds to the point in the view space.
        public PointD ImageToViewSpace(double x, double y)
        {
            if (SelectedImage == null)
                return ToViewSpace(x, y);
            if (SelectedImage.isPyramidal)
            {
                return new PointD((PyramidalOrigin.X + x) * Resolution, (PyramidalOrigin.Y + y) * Resolution);
            }
            else
                return ToViewSpace(x, y);
        }
        /// Convert a point from world space to view space
        /// 
        /// @param PointF The point to convert
        /// 
        /// @return A PointD object.
        public PointF ToViewSpace(PointF p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            return new PointF((float)d.X, (float)d.Y);
        }
        /// > Converts a point from world space to view space
        /// 
        /// @param PointD A class that contains an X and Y value.
        /// 
        /// @return A PointD object.
        public PointD ToViewSpace(PointD p)
        {
            return ToViewSpace(p.X, p.Y); ;
        }
        /// > ToViewSpace(x, y) = (ToViewSizeW(x - (ViewWidth / 2)) / Scale.Width) - Origin.X;
        /// 
        /// @param x The x coordinate of the point to convert
        /// @param y The y coordinate of the point to convert.
        /// 
        /// @return A PointD object.
        public PointD ToViewSpace(double x, double y)
        {
            if (SelectedImage == null)
            {
                double dx = (ToViewSizeW(x - (viewStack.AllocatedWidth / 2)) / Scale.Width) - Origin.X;
                double dy = (ToViewSizeH(y - (viewStack.AllocatedHeight / 2)) / Scale.Height) - Origin.Y;
                return new PointD(dx, dy);
            }

            if (SelectedImage.isPyramidal)
            {
                PointD p = new PointD(x / Resolution, y / Resolution);
                return new PointD(p.X - PyramidalOrigin.X, p.Y - PyramidalOrigin.Y);
            }
            else
            {
                // Handle non-pyramidal case
                double dx = (ToViewSizeW(x - (viewStack.AllocatedWidth / 2)) / Scale.Width) - Origin.X;
                double dy = (ToViewSizeH(y - (viewStack.AllocatedHeight / 2)) / Scale.Height) - Origin.Y;
                return new PointD(dx, dy);
            }
        }

        /// Convert a value in microns to a value in pixels
        /// 
        /// @param d the size in microns
        /// 
        /// @return The return value is the size of the object in pixels.
        private double ToViewSizeW(double d)
        {
            if (SelectedImage != null)
                if (SelectedImage.isPyramidal)
                {
                    return d / Resolution;
                }
            double x = (double)(d / PxWmicron);
            return x;
        }
        /// > Convert a value in microns to a value in pixels
        /// 
        /// @param d the size in microns
        /// 
        /// @return The return value is the size of the object in pixels.
        public double ToViewSizeH(double d)
        {
            if (SelectedImage != null)
                if (SelectedImage.isPyramidal)
                {
                    return d / Resolution;
                }
            double y = (double)(d / PxHmicron);
            return y;
        }
        /// Convert a distance in microns to a distance in pixels on the screen
        /// 
        /// @param d the distance in microns
        /// 
        /// @return The width of the image in pixels.
        public double ToViewW(double d)
        {
            if (SelectedImage != null)
                if (SelectedImage.isPyramidal)
                {
                    return d / Resolution;
                }
            double x = (double)(d / PxWmicron) / scale.Width;
            return x;
        }
        /// > Convert a distance in microns to a distance in pixels
        /// 
        /// @param d the distance in microns
        /// 
        /// @return The return value is the y-coordinate of the point in the view.
        public double ToViewH(double d)
        {
            if (SelectedImage != null)
                if (SelectedImage.isPyramidal)
                {
                    return d / Resolution;
                }
            double y = (double)(d / PxHmicron) / scale.Height;
            return y;
        }
        /// > It converts a point in world space to a point in screen space
        /// 
        /// @param x The x coordinate of the point to convert.
        /// @param y The y coordinate of the point to transform.
        /// 
        /// @return A PointD object.
        public PointD ToScreenSpace(double x, double y)
        {
            RectangleD f = ToScreenRect(x, y, 1, 1);
            return new PointD(f.X, f.Y);
        }
        /// > Converts a point from world space to screen space
        /// 
        /// @param PointD A class that contains an X and Y value.
        /// 
        /// @return A PointD object.
        public PointD ToScreenSpace(PointD p)
        {
            return ToScreenSpace(p.X, p.Y);
        }
        /// Convert a point in the world coordinate system to the screen coordinate system
        /// 
        /// @param PointF The point you want to convert to screen space.
        /// 
        /// @return A PointD object.
        public PointF ToScreenSpace(PointF p)
        {
            PointD pd = ToScreenSpace(p.X, p.Y);
            return new PointF((float)pd.X, (float)pd.Y);
        }
        /// > It takes an array of points and returns an array of points
        /// 
        /// @param p The point to convert
        /// 
        /// @return A PointF array.
        public PointF[] ToScreenSpace(PointF[] p)
        {
            PointF[] pf = new PointF[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                pf[i] = ToScreenSpace(p[i]);
            }
            return pf;
        }
        /// > It converts a 3D point to a 2D point
        /// 
        /// @param Point3D 
        /// 
        /// @return A PointF object.
        public PointF ToScreenSpace(Point3D p)
        {
            PointD pd = ToScreenSpace(p.X, p.Y);
            return new PointF((float)pd.X, (float)pd.Y);
        }
        /// ToScreenScaleW() returns the number of pixels that correspond to the given number of microns
        /// 
        /// @param x the x coordinate of the point to be converted
        /// 
        /// @return The return value is a float.
        public float ToScreenScaleW(double x)
        {
            return (float)(-x * PxWmicron * Scale.Width);
        }
        /// > Convert a value in microns to a value in pixels
        /// 
        /// @param y the y coordinate of the point to be converted
        /// 
        /// @return The return value is a float.
        public float ToScreenScaleH(double y)
        {
           return (float)(-y * PxHmicron * Scale.Height);
        }
        /// > Convert a point in the world coordinate system to a point in the screen coordinate system
        /// 
        /// @param PointD 
        /// 
        /// @return A PointF object.
        public PointF ToScreenScale(PointD p)
        {
            float x = ToScreenScaleW((float)p.X);
            float y = ToScreenScaleH((float)p.Y);
            return new PointF(x, y);
        }
        /// It converts a rectangle in microns to a rectangle in pixels
        /// 
        /// @param x The x coordinate of the rectangle
        /// @param y -0.0015
        /// @param w width of the image in microns
        /// @param h height of the rectangle
        /// 
        /// @return A RectangleF object.
        public RectangleD ToScreenRect(double x, double y, double w, double h)
        {
            if (SelectedImage == null)
            {
                double dx = (pxWmicron * (-Origin.X)) * Scale.Width;
                double dy = (pxHmicron * (-Origin.Y)) * Scale.Height;
                RectangleD rf = new RectangleD((PxWmicron * x * Scale.Width + dx), (PxHmicron * y * Scale.Height + dy), (PxWmicron * w * Scale.Width), (PxHmicron * h * Scale.Height));
                return rf;
            }
            if (SelectedImage.isPyramidal)
            {
                PointD d = ToViewSpace(x, y);
                double dw = ToViewSizeW(w);
                double dh = ToViewSizeH(h);
                return new RectangleD(d.X, d.Y, dw, dh);
            }
            else
            {
                double dx = (pxWmicron * (Origin.X)) * Scale.Width;
                double dy = (pxHmicron * (Origin.Y)) * Scale.Height;
                RectangleD rf = new RectangleD((PxWmicron * x * Scale.Width + dx), (PxHmicron * y * Scale.Height + dy), (PxWmicron * w * Scale.Width), (PxHmicron * h * Scale.Height));
                return rf;              
            }
        }
       
        /// > It converts a rectangle from world space to screen space
        /// 
        /// @param RectangleD The rectangle to convert.
        /// 
        /// @return A RectangleF object.
        public RectangleD ToScreenSpace(RectangleD p)
        {
            return ToScreenRect(p.X, p.Y, p.W, p.H);
        }
        /// > It converts a rectangle from world space to screen space
        /// 
        /// @param RectangleF The rectangle to convert.
        /// 
        /// @return A RectangleF object.
        public RectangleD ToScreenSpace(RectangleF p)
        {
            return ToScreenRect(p.X, p.Y, p.Width, p.Height);
        }
        /// It takes an array of RectangleD objects and returns an array of RectangleF objects
        /// 
        /// @param p The rectangle to convert
        /// 
        /// @return A RectangleF[]
        public RectangleD[] ToScreenSpace(RectangleD[] p)
        {
            RectangleD[] rs = new RectangleD[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                rs[i] = ToScreenSpace(p[i]);
            }
            return rs;
        }

        /// > Convert a list of points from world space to screen space
        /// 
        /// @param p The point to convert
        /// 
        /// @return A PointF[] array of points.
        public PointD[] ToScreenSpace(PointD[] p)
        {
            PointD[] rs = new PointD[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                PointD pd = ToScreenSpace(p[i]);
                rs[i] = new PointD((float)pd.X, (float)pd.Y);
            }
            return rs;
        }
        /// ToScreenW(x) = x * PxWmicron
        /// 
        /// @param x the x coordinate of the point to be converted
        /// 
        /// @return The return value is a float.
        public double ToScreenW(double x)
        {
            return (float)(x * PxWmicron);
        }
        /// > Convert a value in microns to a value in pixels
        /// 
        /// @param y the y coordinate of the point to be converted
        /// 
        /// @return The return value is a float.
        public float ToScreenH(double y)
        {
            return (float)(y * PxHmicron);
        }
        /// This function is used to go to the image at the specified index
        public void GoToImage()
        {
            GoToImage(0);
        }
        /// It takes an image index and centers the image in the viewport
        /// 
        /// @param i the index of the image to go to
        /// 
        /// @return The method is returning the value of the variable "i"
        public void GoToImage(int i)
        {
            if (Images.Count <= i)
                return;
            if (SelectedImage.Type == BioImage.ImageType.pyramidal)
            {
                if (SelectedImage.OpenSlideBase != null)
                {
                    if (MacroResolution.HasValue)
                    {
                        int lev = MacroResolution.Value - 2;
                        Resolution = _openSlideBase.Schema.Resolutions[lev].UnitsPerPixel * 0.98;
                    }
                    else
                    {
                        int lev = 0;
                        Resolution = _openSlideBase.Schema.Resolutions[lev].UnitsPerPixel * 0.98;
                    }
                }
                else
                {
                    if (MacroResolution.HasValue)
                    {
                        int lev = MacroResolution.Value - 1;
                        Resolution = SelectedImage.SlideBase.Schema.Resolutions[lev].UnitsPerPixel * 0.98;
                        PyramidalOrigin = new PointD(0, 0);
                    }
                    else
                    {
                        Resolution = SelectedImage.GetUnitPerPixel(SelectedImage.Resolutions.Count - 1) * 0.98;
                    }
                }
            }
            double dx = Images[i].Volume.Width / 2;
            double dy = Images[i].Volume.Height / 2;
            Origin = new PointD(-(Images[i].Volume.Location.X + dx), -(Images[i].Volume.Location.Y + dy));
            double wx, wy;
            wx = viewStack.AllocatedWidth / ToScreenW(SelectedImage.Volume.Width);
            wy = viewStack.AllocatedHeight / ToScreenH(SelectedImage.Volume.Height);
            Scale = new SizeF((float)wy, (float)wy);
            UpdateView();
        }
        
        #endregion

        #region OpenSlide
        private OpenSlideBase _openSlideBase;
        private OpenSlideGTK.ISlideSource _openSlideSource;
        private BioLib.ISlideSource _slideSource;
        private SlideBase _slideBase;
        /// <summary>
        /// Open slide file
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Initialize()
        {
            if(SelectedImage.OpenSlideBase != null)
            {
                _openSlideSource = SelectedImage.OpenSlideBase;
                _openSlideBase = SelectedImage.OpenSlideBase as OpenSlideBase;
                openSlide = true;
                if (MacroResolution.HasValue)
                {
                    int lev = MacroResolution.Value - 2;
                    Resolution = _openSlideBase.Schema.Resolutions[lev].UnitsPerPixel;
                }
                else
                {
                    Resolution = _openSlideBase.Schema.Resolutions[0].UnitsPerPixel;
                }
            }
            else
            {
                _slideSource = SelectedImage.SlideBase;
                _slideBase = SelectedImage.SlideBase;
                openSlide = false;
                if (MacroResolution.HasValue)
                {
                    int lev = MacroResolution.Value - 2;
                    double[] ds = SelectedImage.GetLevelDownsamples();
                    Resolution = ds[lev];
                }
                else
                {
                    Resolution = SelectedImage.GetLevelDownsamples()[SelectedImage.Resolutions.Count-1];
                }
            }
        }
        #endregion

    }
}
