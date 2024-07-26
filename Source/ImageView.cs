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
using javax.imageio;
using ScottPlot.Colormaps;
namespace BioGTK
{
    public class ImageView : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        public List<BioImage> Images = new List<BioImage>();
        //public List<SKImage> Bitmaps = new List<SKImage>();
        public List<Bitmap> Bitmaps = new List<Bitmap>();
        public List<SKImage> SkImages = new List<SKImage>();
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
            UpdateImage();
            UpdateView();
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
            if(im.Resolutions[0].SizeX <= 1920 && im.Resolutions[0].SizeY <= 1080)
            {
                sk.WidthRequest = im.Resolutions[0].SizeX;
                sk.HeightRequest = im.Resolutions[0].SizeY;
            }
            else
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
            }
        }
        bool allowNavigation = true;
        public bool AllowNavigation
        {
            get { return allowNavigation; }
            set { allowNavigation = value; }
        }
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
            
        }
        private static SkiaSharp.SKRect ToRectangle(float x1, float y1, float x2, float y2)
        {
            return new SkiaSharp.SKRect()
            {
                Location = new SKPoint(Math.Min(x1, x2), Math.Min(y1, y2)),
                Size = new SKSize(Math.Abs(x2 - x1), Math.Abs(y2 - y1))
            };
        }
        private void Render(object sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            canvas.Clear(SKColors.White);
            var paint = new SKPaint();
            paint.Color = SKColors.Gray;
            paint.IsAntialias = true;
            paint.Style = SKPaintStyle.Fill;
            if ((SkImages.Count == 0 || SkImages.Count != SkImages.Count))
                UpdateImages();
            if (!SelectedImage.isPyramidal)
            {
                canvas.Scale(Scale.Width, Scale.Height);
                canvas.Translate(sk.AllocatedWidth / 2, sk.AllocatedHeight / 2);
            }
            SKRect rr = ToScreenSpaceSK(PointD.MinX, PointD.MinY, PointD.MaxX - PointD.MinX, PointD.MaxY - PointD.MinY);
            canvas.DrawRect(rr, paint);
            int i = 0;
            foreach (BioImage im in Images)
            {
                SKRect r = ToScreenSpaceSK(im.Volume.Location.X, im.Volume.Location.Y, im.Volume.Width, im.Volume.Height);
                paint.StrokeWidth = 1;
                if (SelectedImage.isPyramidal)
                {
                    canvas.DrawImage(SkImages[i],0,0,paint);
                    if (ShowOverview)
                    {
                        canvas.DrawImage(overviewImage, 0,0, paint);
                        paint.Style = SKPaintStyle.Stroke;
                        paint.Color = SKColors.Gray;
                        canvas.DrawRect(overview.X, overview.Y, overview.Width, overview.Height,paint);
                        paint.Color = SKColors.Red;
                        if (!openSlide)
                        {
                            double dsx = _slideBase.Schema.Resolutions[Level].UnitsPerPixel / Resolution;
                            Resolution rs = SelectedImage.Resolutions[Level];
                            double dx = ((double)PyramidalOrigin.X / (rs.SizeX * dsx)) * overview.Width;
                            double dy = ((double)PyramidalOrigin.Y / (rs.SizeY * dsx)) * overview.Height;
                            double dw = ((double)sk.AllocatedWidth / (rs.SizeX * dsx)) * overview.Width;
                            double dh = ((double)sk.AllocatedHeight / (rs.SizeY * dsx)) * overview.Height;
                            canvas.DrawRect((float)dx, (float)dy, (float)dw, (float)dh, paint);
                        }
                        else
                        {
                            double dsx = _openSlideBase.Schema.Resolutions[Level].UnitsPerPixel / Resolution;
                            Resolution rs = SelectedImage.Resolutions[Level];
                            double dx = ((double)PyramidalOrigin.X / (rs.SizeX * dsx)) * overview.Width;
                            double dy = ((double)PyramidalOrigin.Y / (rs.SizeY * dsx)) * overview.Height;
                            double dw = ((double)sk.AllocatedWidth / (rs.SizeX * dsx)) * overview.Width;
                            double dh = ((double)sk.AllocatedHeight / (rs.SizeY * dsx)) * overview.Height;
                            canvas.DrawRect((float)dx, (float)dy, (float)dw, (float)dh, paint);
                        }
                    }
                }
                else
                {
                    canvas.DrawImage(SkImages[i],r,paint);
                }
                paint.Style = SKPaintStyle.Stroke;
                List<ROI> rois = new List<ROI>();
                if (im.Annotations.Count > 0)
                {
                    rois.AddRange(im.AnnotationsR);
                    rois.AddRange(im.AnnotationsG);
                    rois.AddRange(im.AnnotationsB);
                }
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
                    else if (zBar.Value != an.coord.Z || cBar.Value != an.coord.C || tBar.Value != an.coord.T)
                        continue;
                    if (an.Selected)
                    {
                        paint.Color = SKColors.Magenta;
                    }
                    else
                        paint.Color = new SKColor(an.strokeColor.R, an.strokeColor.G, an.strokeColor.B);
                    paint.StrokeWidth = (float)an.strokeWidth;
                    
                    PointF pc = new PointF((float)(an.BoundingBox.X + (an.BoundingBox.W / 2)), (float)(an.BoundingBox.Y + (an.BoundingBox.H / 2)));
                    float width = (float)ToScreenScaleW(ROI.selectBoxSize);
                    if (an.type == ROI.Type.Mask || an.roiMask != null)
                    {
                        SKImage sim = an.roiMask.GetColored(an.fillColor).ToSKImage();
                        canvas.DrawImage(sim, new SKPoint(pc.X, pc.Y), paint);
                        sim.Dispose();
                    }
                    if (an.type == ROI.Type.Point)
                    {
                        RectangleD p1 = ToViewSpace(an.Point.X, an.Point.Y, 1, 1);
                        RectangleD p2 = ToViewSpace(an.Point.X + 1, an.Point.Y + 1, 1, 1);
                        canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y),paint);
                    }
                    else
                    if (an.type == ROI.Type.Line)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            RectangleD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                            RectangleD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                            canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y), paint);
                        }
                    }
                    else
                    if (an.type == ROI.Type.Rectangle)
                    {
                        RectangleD rec = ToViewSpace(an.PointsD[0].X, an.PointsD[0].Y, an.W, an.H);
                        canvas.DrawRect((float)rec.X, (float)rec.Y, (float)rec.W, (float)rec.H,paint);
                    }
                    else
                    if (an.type == ROI.Type.Ellipse)
                    {
                        RectangleD rect = ToViewSpace(an.X + (an.W / 2), an.Y + (an.H / 2), an.W, an.H);
                        canvas.DrawOval((float)rect.X, (float)rect.Y, (float)rect.W / 2, (float)rect.H / 2, paint);
                    }
                    else
                    if ((an.type == ROI.Type.Polygon && an.closed))
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            RectangleD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                            RectangleD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                            canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y), paint);
                        }
                        RectangleD pp1 = ToViewSpace(an.PointsD[0].X, an.PointsD[0].Y, 1, 1);
                        RectangleD pp2 = ToViewSpace(an.PointsD[an.PointsD.Count - 1].X, an.PointsD[an.PointsD.Count - 1].Y, 1, 1);
                        canvas.DrawLine(new SKPoint((float)pp1.X, (float)pp1.Y), new SKPoint((float)pp2.X, (float)pp2.Y), paint);
                    }
                    else
                    if ((an.type == ROI.Type.Polygon && !an.closed) || an.type == ROI.Type.Polyline)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            RectangleD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                            RectangleD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                            canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y), paint);
                        }
                    }
                    else
                    if (an.type == ROI.Type.Freeform)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            RectangleD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                            RectangleD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                            canvas.DrawLine(new SKPoint((float)p1.X, (float)p1.Y), new SKPoint((float)p2.X, (float)p2.Y), paint);
                        }
                        RectangleD pp1 = ToViewSpace(an.PointsD[0].X, an.PointsD[0].Y, 1, 1);
                        RectangleD pp2 = ToViewSpace(an.PointsD[an.PointsD.Count - 1].X, an.PointsD[an.PointsD.Count - 1].Y, 1, 1);
                        canvas.DrawLine(new SKPoint((float)pp1.X, (float)pp1.Y), new SKPoint((float)pp2.X, (float)pp2.Y), paint);
                    }
                    else
                    if (an.type == ROI.Type.Label)
                    {
                        RectangleD p = ToViewSpace(an.Point.X, an.Point.Y, 1, 1);
                        canvas.DrawText(an.Text, (float)p.X, (float)p.Y, new SKFont(SKTypeface.Default,an.fontSize,1,0), paint);
                    }

                    if (ROIManager.showText)
                    {
                        RectangleD p = ToViewSpace(an.Point.X, an.Point.Y, 1, 1);
                        canvas.DrawText(an.Text, (float)p.X, (float)p.Y, new SKFont(SKTypeface.Default, an.fontSize, 1, 0), paint);
                    }
                    if (ROIManager.showBounds && an.type != ROI.Type.Rectangle && an.type != ROI.Type.Label)
                    {
                        RectangleD rrf = ToViewSpace(an.BoundingBox.X, an.BoundingBox.Y, an.BoundingBox.W, an.BoundingBox.H);
                        canvas.DrawRect((float)rrf.X, (float)rrf.Y, (float)rrf.W, (float)rrf.H, paint);
                    }
                    paint.Color = SKColors.Red;
                    if (!(an.type == ROI.Type.Freeform && !an.Selected) && an.type != ROI.Type.Mask)
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            canvas.DrawRect((float)recd.X, (float)recd.Y, (float)recd.W, (float)recd.H, paint);
                        }

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
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            if (an.selectedPoints.Contains(ind))
                            {
                                canvas.DrawRect((float)recd.X, (float)recd.Y, (float)recd.W, (float)recd.H, paint);
                            }
                            ind++;
                        }
                    }
                    rects.Clear();
                }
                if (Tools.currentTool.type == Tools.Tool.Type.select && Modifiers == ModifierType.Button1Mask)
                {
                    RectangleD recd = ToViewSpace(Tools.currentTool.Rectangle.X, Tools.currentTool.Rectangle.Y, Tools.currentTool.Rectangle.W,Tools.currentTool.Rectangle.H);
                    canvas.DrawRect((float)recd.X, (float)recd.Y, (float)recd.W, (float)recd.H, paint);
                }
            }
            Plugins.Render(sender, e);
            paint.Dispose();

        }

        #endregion
        public SKImage BitmapToSKImage(Bitmap bitmap)
        {
            // Step 1: Create an SKBitmap from the System.Drawing.Bitmap
            var bitmapData = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            var info = new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
            var skBitmap = new SKBitmap(info);
            skBitmap.InstallPixels(info, bitmapData.Scan0, bitmapData.Stride, delegate { bitmap.UnlockBits(bitmapData); }, null);

            // Step 2: Create an SKImage from the SKBitmap
            var skImage = SKImage.FromBitmap(skBitmap);

            return skImage;
        }
        /// It updates the images.
        public void UpdateImages()
        {
            if (SelectedImage == null)
                return;
            for (int i = 0; i < SkImages.Count; i++)
            {
                SkImages[i].Dispose();
            }
            SkImages.Clear();
           
            if (zBar.Adjustment.Upper != SelectedImage.SizeZ - 1 || tBar.Adjustment.Upper != SelectedImage.SizeT - 1)
            {
                UpdateGUI();
            }
            int bi = 0;
            if (SelectedImage.isPyramidal && sk.AllocatedHeight <= 1 || sk.AllocatedWidth <= 1)
                return;
            SelectedImage.PyramidalSize = new AForge.Size(sk.AllocatedWidth, sk.AllocatedHeight);
            if (SelectedImage.isPyramidal)
                SelectedImage.UpdateBuffersPyramidal();
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
                SkImages.Add(BitmapToSKImage(bitmap.ImageRGB));
                bi++;
            }
        }
        /// It updates the image.
        public void UpdateImage()
        {
            UpdateImages();
        }
        bool showOverview = true;
        Rectangle overview;
        SKImage overviewImage;
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
            overview = new Rectangle(0, 0, 120, 120);
            if (MacroResolution.HasValue)
            {
                double aspx = (double)SelectedImage.Resolutions[MacroResolution.Value - 1].SizeX / (double)SelectedImage.Resolutions[MacroResolution.Value - 1].SizeY;
                double aspy = (double)SelectedImage.Resolutions[MacroResolution.Value - 1].SizeY / (double)SelectedImage.Resolutions[MacroResolution.Value - 1].SizeX;
                overview = new Rectangle(0, 0, (int)(aspx * 120), (int)(aspy * 120));
                Bitmap bm = BioImage.GetTile(SelectedImage, GetCoordinate(), MacroResolution.Value - 1, 0, 0, SelectedImage.Resolutions[MacroResolution.Value - 1].SizeX, SelectedImage.Resolutions[MacroResolution.Value - 1].SizeY);
                ResizeNearestNeighbor re = new ResizeNearestNeighbor(overview.Width, overview.Height);
                Bitmap bmp = re.Apply((Bitmap)bm.ImageRGB);
                overviewImage = BitmapToSKImage(bmp);
            }
            else
            {
                Resolution res = SelectedImage.Resolutions.Last();
                double aspx = (double)res.SizeX / (double)res.SizeY;
                double aspy = (double)res.SizeY / (double)res.SizeX;
                overview = new Rectangle(0, 0, (int)(aspx * 120), (int)(aspy * 120));
                Bitmap bm;
                ResizeNearestNeighbor re = new ResizeNearestNeighbor(overview.Width, overview.Height);
                byte[] bts;
                Bitmap bf;
                if (_openSlideBase != null)
                {
                    bts = _openSlideBase.GetSlice(new OpenSlideGTK.SliceInfo(PyramidalOrigin.X, PyramidalOrigin.Y, SelectedImage.PyramidalSize.Width, SelectedImage.PyramidalSize.Height, SelectedImage.GetUnitPerPixel(Level)));
                    bf = new Bitmap((int)Math.Round(OpenSlideBase.destExtent.Width), (int)Math.Round(OpenSlideBase.destExtent.Height), PixelFormat.Format24bppRgb, bts, new ZCT(), "");
                }
                else
                {
                    bts = _slideBase.GetSlice(new BioLib.SliceInfo(PyramidalOrigin.X, PyramidalOrigin.Y, SelectedImage.PyramidalSize.Width, SelectedImage.PyramidalSize.Height, SelectedImage.GetUnitPerPixel(Level), GetCoordinate())).Result;
                    bf = new Bitmap((int)Math.Round(SlideBase.destExtent.Width), (int)Math.Round(SlideBase.destExtent.Height), PixelFormat.Format24bppRgb, bts, new ZCT(), "");
                }
                bm = re.Apply((Bitmap)bf.ImageRGB);
                overviewImage = BitmapToSKImage(bm);
            }
            ShowOverview = true;
            Console.WriteLine("Preview Initialized.");
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

        private void ImageView_DestroyEvent(object o, DestroyEventArgs args)
        {
            foreach (var item in this.Images)
            {
                BioLib.Images.RemoveImage(item);
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
            foreach (var item in this.Images)
            {
                App.tabsView.RemoveTab(item.Filename);
                BioLib.Images.RemoveImage(item);
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
                UpdateImage();
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
                return;
            }
            if (e.Event.Key == Gdk.Key.v && e.Event.State == ModifierType.ControlMask)
            {
                PasteSelection();
                return;
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
            if (e.Event.Key == Gdk.Key.w)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new PointD(PyramidalOrigin.X, PyramidalOrigin.Y + movepyr);
                }
                else
                    Origin = new PointD(Origin.X, Origin.Y + moveAmount);
            }
            if (e.Event.Key == Gdk.Key.s)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new PointD(PyramidalOrigin.X, PyramidalOrigin.Y - movepyr);
                }
                else
                    Origin = new PointD(Origin.X, Origin.Y - moveAmount);
            }
            if (e.Event.Key == Gdk.Key.a)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new PointD(PyramidalOrigin.X - movepyr, PyramidalOrigin.Y);
                }
                else
                    Origin = new PointD(Origin.X + moveAmount, Origin.Y);
            }
            if (e.Event.Key == Gdk.Key.d)
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
            UpdateView();
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
                if (args.Event.State == ModifierType.ControlMask)
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
                    UpdateView();
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
                SelectedImage.PyramidalOrigin = value;
                UpdateImage();
                UpdateView();
            }
        }
        /* Setting the Level of the image. */
        public int LevelFromResolution(double Resolution)
        {
            int lev;
            if (MacroResolution.HasValue)
            {
                if (Resolution >= MacroResolution.Value)
                {
                    int r = 0;
                    for (int i = 0; i < SelectedImage.Resolutions.Count; i++)
                    {
                        if (i <= Resolution - 1)
                            r = i;
                    }
                    if (r - 1 <= MacroResolution.Value)
                        lev = MacroResolution.Value - 1;
                    else
                        lev = r - 1;
                }
                else
                    return (int)Resolution;
            }
            else
            {
                int r = 0;
                for (int i = 0; i < SelectedImage.Resolutions.Count; i++)
                {
                    if (i <= Resolution - 1)
                        r = i;
                }
                lev = r;
            }
            if (!OpenSlide)
            {
                return lev;
            }
            else
            {
                if (MacroResolution.HasValue)
                {
                    if (lev >= MacroResolution.Value - 1)
                        return lev - 1;
                    else
                        return lev;
                }
                else
                {
                    return lev - 1;
                }
            }
        }
        public double Resolution
        {
            get
            {
                return SelectedImage.Resolution;
            }
            set
            {
                if(SelectedImage.Type == BioImage.ImageType.well && value > SelectedImage.Resolutions.Count - 1)
                    return;
                else
                if (openSlide)
                {
                    double dp = Resolution / value;
                    PyramidalOrigin = new PointD((dp * PyramidalOrigin.X), (dp * PyramidalOrigin.Y));
                }
                SelectedImage.Resolution = value;
                if(SelectedImage.Type == BioImage.ImageType.well)
                SelectedImage.Level = (int)value;
                // update ui on main UI thread
                Application.Invoke(delegate
                {
                    UpdateImage();
                    UpdateView();
                });
            }
        }
        public int Level
        {
            get
            {
                int l = 0;
                if(SelectedImage.isPyramidal)
                if (!openSlide)
                    l = OpenSlideGTK.TileUtil.GetLevel(_slideBase.Schema.Resolutions, Resolution);
                else
                    l = OpenSlideGTK.TileUtil.GetLevel(_openSlideBase.Schema.Resolutions, Resolution);
                return l;
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
                    UpdateView();
                });
            }
        }
        /// It updates the status of the user.
        public void UpdateStatus()
        {
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
        public void UpdateView()
        {
            if (SelectedImage.isPyramidal)
                sk.QueueDraw();
            else
            {
                sk.QueueDraw();
            }
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
            App.viewer = this;
            Modifiers = e.Event.State;
            MouseMoveInt = new PointD((int)e.Event.X, (int)e.Event.Y);
            MouseMove = new PointD(e.Event.X,e.Event.Y);
            PointD p = ImageToViewSpace(e.Event.X, e.Event.Y);
            PointD ip = SelectedImage.ToImageSpace(p);
            App.tools.ToolMove(p, e);
            Tools.currentTool.Rectangle = new RectangleD(mouseDown.X, mouseDown.Y, p.X - mouseDown.X, p.Y - mouseDown.Y);
            mousePoint = "(" + (p.X.ToString("F")) + ", " + (p.Y.ToString("F")) + ")";
            
            //If point selection tool is clicked we  
            if (Tools.currentTool.type == Tools.Tool.Type.pointSel && e.Event.State.HasFlag(ModifierType.Button1Mask))
            {
                List<ROI> rois = new List<ROI>();
                rois.AddRange(SelectedImage.AnnotationsR);
                rois.AddRange(SelectedImage.AnnotationsG);
                rois.AddRange(SelectedImage.AnnotationsB);
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
                    UpdateImage();
                }
                else
                if (Tools.currentTool.type == Tools.Tool.Type.eraser && e.Event.State.HasFlag(ModifierType.Button1Mask))
                {
                    Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                    Bio.Graphics.Pen pen = new Bio.Graphics.Pen(Tools.EraseColor, (int)Tools.StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
                    g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)Tools.StrokeWidth, (int)Tools.StrokeWidth), pen.color);
                    //pen.Dispose();
                    App.viewer.UpdateImages();
                }
            }
            UpdateStatus();
            pd = p;
            UpdateView();
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
                UpdateImage();
                UpdateView();
            }
            if (SelectedImage == null)
                return;
            App.tools.ToolUp(pointer, e);
        }
        PointD pd;
        PointD mouseDownInt = new PointD(0, 0);
        PointD mouseMoveInt = new PointD(0, 0);
        PointD mouseMove = new PointD(0, 0);
        public static PointD mouseDown;
        public static PointD mouseUp;
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
        public PointD MouseDown
        {
            get { return mouseDown; }
            set { mouseDown = value; }
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
            int ind = 0;
            if (e.Event.Button == 3)
                contextMenu.Popup();
            if (e.Event.Button == 4 && Mode != ViewMode.RGBImage)
            {
                if (SelectedImage.Type == BioImage.ImageType.well)
                    Resolution++;
                else
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
                if (SelectedImage.Type == BioImage.ImageType.well)
                    Resolution--;
                else
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
                ind++;
            }

            //Lets handle point selection & move tool clicks.
            if (Tools.currentTool.type == Tools.Tool.Type.pointSel || Tools.currentTool.type == Tools.Tool.Type.move && e.Event.Button == 1)
            {
                bool clearSel = true;
                float width = (float)ToScreenScaleW(ROI.selectBoxSize);
                foreach (BioImage bi in Images)
                {
                    List<ROI> rois = new List<ROI>();
                    rois.AddRange(SelectedImage.AnnotationsR);
                    rois.AddRange(SelectedImage.AnnotationsG);
                    rois.AddRange(SelectedImage.AnnotationsB);
                    foreach (ROI an in rois)
                    {
                        if (!Modifiers.HasFlag(ModifierType.ControlMask))
                            an.Selected = false;
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
                                if (sels[i].ToRectangleF().IntersectsWith(new AForge.RectangleF((float)r.X, (float)r.Y, (float)r.W, (float)r.H)))
                                {
                                    an.selectedPoints.Add(i);
                                }
                            }
                        }  
                    }
                }
                //Clear selection if clicked outside all ROI's.
                if (clearSel)
                {
                    selectedAnnotations.Clear();
                    Tools.selectedROI = null;
                    foreach (BioImage bi in Images)
                    {
                        foreach (ROI an in bi.Annotations)
                        {
                            an.Selected = false;
                            an.selectedPoints.Clear();
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
                        //s = mouseD;
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

                if(SelectedImage.isPyramidal && overview.IntersectsWith(e.Event.X,e.Event.Y))
                {
                    if (!OpenSlide)
                    {
                        Resolution rs = SelectedImage.Resolutions[(int)Level];
                        double r = rs.PhysicalSizeX / Resolution;
                        double dx = ((e.Event.X) / overview.Width) * rs.SizeX;
                        double dy = ((e.Event.Y) / overview.Height) * rs.SizeY;
                        double w = (sk.AllocatedWidth / 2);
                        double h = (sk.AllocatedHeight / 2);
                        PyramidalOrigin = new PointD(dx, dy);
                    }
                    else
                    {
                        Resolution rs = SelectedImage.Resolutions[(int)Level];
                        double r = rs.PhysicalSizeX / Resolution;
                        double dx = ((e.Event.X) / overview.Width) * rs.SizeX;
                        double dy = ((e.Event.Y) / overview.Height) * rs.SizeY;
                        double w = (sk.AllocatedWidth / 2);
                        double h = (sk.AllocatedHeight / 2);
                        PyramidalOrigin = new PointD(dx - w, dy - h);
                    }
                }
            }
            UpdateStatus();
            App.tools.ToolDown(mouseDown, e);
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
        public PointD ImageToViewSpace(double x,double y)
        {
            if(SelectedImage.isPyramidal)
            {
                return new PointD((PyramidalOrigin.X + x) * Resolution, (PyramidalOrigin.Y + y) * Resolution);
            }
            double dx = ToViewW(SelectedImage.Volume.Width);
            double dy = ToViewH(SelectedImage.Volume.Height);
            //The origin is the middle of the screen we want the top left corner
            PointD torig = new PointD((Origin.X - ((Width / 2) * pxWmicron)), (Origin.Y - ((Height / 2) * pxHmicron)));
            PointD orig = new PointD(torig.X - SelectedImage.Volume.Location.X, torig.Y - SelectedImage.Volume.Location.Y);
            PointD diff = new PointD(ToViewW(orig.X), ToViewH(orig.Y));
            PointD f = new PointD((((x + diff.X)/ dx) * SelectedImage.Volume.Width),(((y + diff.Y) / dy) * SelectedImage.Volume.Height));
            PointD ff = new PointD(((SelectedImage.Volume.Location.X + SelectedImage.Volume.Width) + f.X), ((SelectedImage.Volume.Location.Y + SelectedImage.Volume.Height) + f.Y));
            return ff;
        }
        /// The function converts a rectangle from world space to view space.
        /// 
        /// @param RectangleD The RectangleD is a custom data type that represents a rectangle in 2D
        /// space. It has four properties: X (the x-coordinate of the top-left corner), Y (the
        /// y-coordinate of the top-left corner), W (the width of the rectangle), and H (the height of
        /// the
        /// 
        /// @return The method is returning a RectangleD object.
        public RectangleD ToViewSpace(RectangleD p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            double dx = ToScreenScaleW(p.W);
            double dy = ToScreenScaleH(p.H);
            return new RectangleD((float)d.X, (float)d.Y, (float)dx, (float)dy);
        }
        /// The function converts a Point object to PointF object in view space.
        /// 
        /// @param Point The Point class represents an ordered pair of integer x and y coordinates that
        /// define a point in a two-dimensional plane.
        /// 
        /// @return The method is returning a PointF object, which represents a point in 2D space with
        /// floating-point coordinates.
        public PointF ToViewSpace(Point p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            return new PointF((float)d.X, (float)d.Y);
        }
        /// The function converts a PointF object from world space to view space.
        /// 
        /// @param PointF PointF is a structure in C# that represents a point in a two-dimensional
        /// space. It consists of two float values, X and Y, which represent the coordinates of the
        /// point.
        /// 
        /// @return The method is returning a PointF object.
        public PointF ToViewSpace(PointF p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            return new PointF((float)d.X, (float)d.Y);
        }
        /// The function converts a point from a coordinate system to view space.
        /// 
        /// @param PointD The PointD class represents a point in a two-dimensional space. It typically
        /// has two properties: X and Y, which represent the coordinates of the point.
        /// 
        /// @return The method is returning a PointD object.
        public PointD ToViewSpace(PointD p)
        {
            return ToViewSpace(p.X, p.Y); ;
        }
        /// The function converts coordinates from a given space to view space.
        /// 
        /// @param x The x-coordinate in the original coordinate system.
        /// @param y The parameter "y" represents the y-coordinate in the original coordinate system.
        /// 
        /// @return The method is returning a PointD object.
        public PointD ToViewSpace(double x, double y)
        {
            if (SelectedImage.isPyramidal)
            {
                double ddx = x / Resolution;
                double ddy = y / Resolution;
                return new PointD(ddx, ddy);
            }
            double dx = (ToViewSizeW(Origin.X - x)) * Scale.Width;
            double dy = (ToViewSizeH(Origin.Y - y)) * Scale.Height;
            return new PointD(dx, dy);
        }
        /// The function converts coordinates and sizes from a given space to a view space.
        /// 
        /// @param x The x-coordinate of the rectangle's top-left corner in world space.
        /// @param y The parameter "y" represents the y-coordinate of the rectangle in the original
        /// coordinate space.
        /// @param w The width of the rectangle in world space.
        /// @param h The parameter "h" represents the height of the rectangle in the original coordinate
        /// space.
        /// 
        /// @return The method is returning a RectangleD object.
        public RectangleD ToViewSpace(double x, double y, double w, double h)
        {
            PointD d = ToViewSpace(x, y);
            double dw = ToViewSizeW(w);
            double dh = ToViewSizeH(h);
            if (SelectedImage.isPyramidal)
            {
                return new RectangleD(d.X - PyramidalOrigin.X, d.Y - PyramidalOrigin.Y, dw, dh);
            }
            return new RectangleD(-d.X, -d.Y, dw, dh);
        }
        /// The function converts a given value to a view size width based on certain conditions.
        /// 
        /// @param d The parameter "d" represents a size value that needs to be converted to a view
        /// size.
        /// 
        /// @return The method is returning a double value.
        private double ToViewSizeW(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d / Resolution;
            }
            double x = (double)(d / PxWmicron) * Scale.Width;
            return x;
        }
        /// The function converts a given value to a view size in the horizontal direction.
        /// 
        /// @param d The parameter "d" represents a value that needs to be converted to a view size.
        /// 
        /// @return The method is returning a double value.
        public double ToViewSizeH(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d / Resolution;
            }
            double y = (double)(d / PxHmicron) * Scale.Height;
            return y;
        }
        /// The function converts a given value from microns to view width units, taking into account the
       /// scale and whether the image is pyramidal.
       /// 
       /// @param d The parameter "d" represents a value that needs to be converted to a view width.
       /// 
       /// @return The method is returning a double value.
        public double ToViewW(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d / Resolution;
            }
            double x = (double)(d / PxWmicron) * Scale.Width;
            return x;
        }
        /// The function converts a given value from a specific unit to a view height value.
        /// 
        /// @param d The parameter "d" represents a value that needs to be converted to a different unit
        /// of measurement.
        /// 
        /// @return The method is returning a double value.
        public double ToViewH(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d / Resolution;
            }
            double y = (double)(d / PxHmicron) * Scale.Height;
            return y;
        }
        /// The function converts coordinates from a Cartesian plane to screen space.
        /// 
        /// @param x The x-coordinate of the point in the coordinate system.
        /// @param y The parameter "y" represents the y-coordinate of a point in a coordinate system.
        /// 
        /// @return The method is returning a PointD object.
        public PointD ToScreenSpace(double x, double y)
        {
            double fx = ToScreenScaleW(Origin.X - x);
            double fy = ToScreenScaleH(Origin.Y - y);
            return new PointD(fx, fy);
        }
        /// The function converts a point from a coordinate system to screen space.
        /// 
        /// @param PointD The PointD class represents a point in a two-dimensional space. It typically
        /// has two properties, X and Y, which represent the coordinates of the point.
        /// 
        /// @return The method is returning a PointD object.
        public PointD ToScreenSpace(PointD p)
        {
            return ToScreenSpace(p.X, p.Y);
        }
        /// The function converts a PointF object from world space to screen space.
        /// 
        /// @param PointF PointF is a structure in C# that represents a point in a two-dimensional
        /// space. It consists of two float values, X and Y, which represent the coordinates of the
        /// point.
        /// 
        /// @return The method is returning a PointF object.
        public PointF ToScreenSpace(PointF p)
        {
            PointD pd = ToScreenSpace(p.X, p.Y);
            return new PointF((float)pd.X, (float)pd.Y);
        }
        /// The function takes an array of PointF objects and converts them to screen space coordinates.
        /// 
        /// @param p An array of PointF objects representing points in some coordinate system.
        /// 
        /// @return The method is returning an array of PointF objects in screen space.
        public PointF[] ToScreenSpace(PointF[] p)
        {
            PointF[] pf = new PointF[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                pf[i] = ToScreenSpace(p[i]);
            }
            return pf;
        }
        /// The function converts a 3D point to screen space and returns it as a PointF object.
        /// 
        /// @param Point3D The Point3D parameter represents a point in a three-dimensional space. It
        /// typically consists of three coordinates: X, Y, and Z.
        /// 
        /// @return The method is returning a PointF object.
        public PointF ToScreenSpace(Point3D p)
        {
            PointD pd = ToScreenSpace(p.X, p.Y);
            return new PointF((float)pd.X, (float)pd.Y);
        }
        /// The function converts a given value to screen scale width based on the selected image's
        /// properties.
        /// 
        /// @param x The parameter "x" represents a value that needs to be converted to screen scale
        /// width.
        /// 
        /// @return The method is returning a double value.
        public double ToScreenScaleW(double x)
        {
            if (SelectedImage.isPyramidal)
            {
                return x * Resolution;
            }
            return (x * PxWmicron) * Scale.Width;
        }
        /// The function converts a given value to screen scale height based on the selected image's
        /// properties.
        /// 
        /// @param y The parameter "y" represents the vertical coordinate value that needs to be
        /// converted to screen scale.
        /// 
        /// @return The method is returning a double value.
        public double ToScreenScaleH(double y)
        {
            if (SelectedImage.isPyramidal)
            {
                return y * Resolution;
            }
            return (y * PxHmicron) * Scale.Height;
        }
        /// The function takes a PointD object and returns a PointF object with the coordinates scaled
        /// to the screen.
        /// 
        /// @param PointD PointD is a custom data type that represents a point in a two-dimensional
        /// space. It consists of two double values, X and Y, which represent the coordinates of the
        /// point.
        /// 
        /// @return The method is returning a PointF object, which represents a point in a
        /// two-dimensional plane with floating-point coordinates.
        public PointF ToScreenScale(PointD p)
        {
            double x = ToScreenScaleW((float)p.X);
            double y = ToScreenScaleH((float)p.Y);
            return new PointF((float)x, (float)y);
        }
        /// The function converts a set of coordinates and dimensions from a mathematical coordinate
        /// system to a screen coordinate system and returns a rectangle with the converted values.
        /// 
        /// @param x The x-coordinate of the rectangle's top-left corner in world space.
        /// @param y The parameter "y" represents the y-coordinate of the top-left corner of the
        /// rectangle in the coordinate system of the screen.
        /// @param w The width of the rectangle in world space.
        /// @param h The parameter "h" represents the height of the rectangle.
        /// 
        /// @return The method is returning a RectangleD object.
        public RectangleD ToScreenRectF(double x, double y, double w, double h)
        {
            PointD pf = ToScreenSpace(x, y);
            RectangleD rf = new RectangleD((float)pf.X, (float)pf.Y, (float)ToViewW(w), (float)ToViewH(h));
            return rf;
        }
        /// The function converts a set of coordinates and dimensions from a mathematical coordinate
        /// system to a screen coordinate system and returns a rectangle with the converted values.
        /// 
        /// @param x The x-coordinate of the rectangle's top-left corner in world space.
        /// @param y The parameter "y" represents the y-coordinate of the top-left corner of the
        /// rectangle in the coordinate system of the screen.
        /// @param w The width of the rectangle in world space.
        /// @param h The parameter "h" represents the height of the rectangle.
        /// 
        /// @return The method is returning a RectangleD object.
        public SKRect ToScreenSpaceSK(double x, double y, double w, double h)
        {
            PointD pf = ToViewSpace(x, y);
            SKRect rf = new SKRect
            {
                Location = new SKPoint((float)pf.X, (float)pf.Y),
                Size = new SKSize((float)ToViewSizeW(w), (float)ToViewSizeH(h)),
            };
            return rf;
        }
        /// The function converts a RectangleD object to screen space.
        /// 
        /// @param RectangleD The RectangleD is a custom data type that represents a rectangle in 2D
        /// space. It typically has four properties: X (the x-coordinate of the top-left corner), Y (the
        /// y-coordinate of the top-left corner), W (the width of the rectangle), and H (the height of
        /// 
        /// @return The method is returning a RectangleD object.
        public RectangleD ToScreenSpace(RectangleD p)
        {
            return ToScreenRectF(p.X, p.Y, p.W, p.H);
        }
        /// The function takes an array of RectangleD objects and converts them to screen space.
        /// 
        /// @param p An array of RectangleD objects representing rectangles in some coordinate space.
        /// 
        /// @return The method is returning an array of RectangleD objects.
        public RectangleD[] ToScreenSpace(RectangleD[] p)
        {
            RectangleD[] rs = new RectangleD[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                rs[i] = ToScreenSpace(p[i]);
            }
            return rs;
        }
        /// The function takes an array of PointD objects and converts them to an array of PointF
        /// objects in screen space.
        /// 
        /// @param p An array of PointD objects representing points in some coordinate system.
        /// 
        /// @return The method is returning an array of PointF objects.
        public PointF[] ToScreenSpace(PointD[] p)
        {
            PointF[] rs = new PointF[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                PointD pd = ToScreenSpace(p[i]);
                rs[i] = new PointF((float)pd.X, (float)pd.Y);
            }
            return rs;
        }
        /// This function is used to go to the image at the specified index
        public void GoToImage()
        {
            GoToImage(0);
        }
        /// It takes an image index and sets the origin and physical size of the image to the values of
        /// the image at that index
        /// 
        /// @param i the index of the image in the list
        /// 
        /// @return The method is returning the value of the variable "i"
        public void GoToImage(int i)
        {
            if (Images.Count <= i)
                return;
            if(SelectedImage.Type == BioImage.ImageType.pyramidal)
            {
                if (SelectedImage.OpenSlideBase != null)
                {
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
                    if (MacroResolution.HasValue)
                    {
                        int lev = MacroResolution.Value - 1;
                        Resolution = SelectedImage.GetLevelDownsamples()[lev];
                    }
                    else
                    {
                        Resolution = SelectedImage.GetLevelDownsamples()[SelectedImage.Resolutions.Count - 1];
                    }
                }
                return;
            }
            double dx = Images[i].Volume.Width / 2;
            double dy = Images[i].Volume.Height / 2;
            Origin = new PointD((Images[i].Volume.Location.X) - dx, (Images[i].Volume.Location.Y) - dy);
            PxWmicron = Images[i].PhysicalSizeX;
            PxHmicron = Images[i].PhysicalSizeY;
            if (Images[i].SizeX > 1080)
            {
                double w = (double)SelectedImage.SizeX / (double)Width;
                double h = (double)SelectedImage.SizeY / (double)Height;
                PxWmicron *= h;
                PxHmicron *= h;
            }
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
