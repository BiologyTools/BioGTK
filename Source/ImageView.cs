using Gtk;
using Gdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using AForge;
using AForge.Imaging.Filters;
using AForge.Math;
using System.Drawing;
using loci.formats;
using CSScripting;
using org.checkerframework.checker.units.qual;
using Pango;
using java.awt.geom;
using Bio;

namespace BioGTK
{
    /// <summary> Example Test Form for GTKSharp and Glade. </summary>
    public class ImageView : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        public List<BioImage> Images = new List<BioImage>();
        //public List<SKImage> Bitmaps = new List<SKImage>();
        public List<Pixbuf> Bitmaps = new List<Pixbuf>();
        public void SetCoordinate(int z, int c, int t)
        {
            if (SelectedImage == null)
                return;
            if (z >= SelectedImage.SizeZ)
                zBar.Value = zBar.Adjustment.Upper;
            if (c >= SelectedImage.SizeC)
                cBar.Value = cBar.Adjustment.Upper;
            if (t >= SelectedImage.SizeT)
                tBar.Value = tBar.Adjustment.Upper;
            zBar.Value = z;
            cBar.Value = c;
            tBar.Value = t;
            SelectedImage.Coordinate = new ZCT(z, c, t);
            UpdateImage();
            UpdateView();
        }
        public ZCT GetCoordinate()
        {
            return SelectedImage.Coordinate;
        }
        public void AddImage(BioImage im)
        {
            Images.Add(im);
            selectedIndex = Images.Count - 1;
            selectedImage = im;
            if (im.isPyramidal)
            {
                scrollH.Adjustment.Upper = im.Resolutions[resolution].SizeX;
                scrollV.Adjustment.Upper = im.Resolutions[resolution].SizeY;
            }
            UpdateGUI();
            UpdateImages();
            GoToImage(Images.Count - 1);
        }

        double pxWmicron = 5;
        double pxHmicron = 5;
        public double PxWmicron
        {
            get
            {
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
                return pxHmicron;
            }
            set
            {
                pxHmicron = value;
            }
        }
        public static AForge.Bitmap SelectedBuffer
        {
            get
            {
                int ind = SelectedImage.Coords[SelectedImage.Coordinate.Z, SelectedImage.Coordinate.C, SelectedImage.Coordinate.T];
                return selectedImage.Buffers[ind];
            }
        }
        internal int selectedIndex = 0;
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
        private Gtk.DrawingArea pictureBox;
        [Builder.Object]
        private Gtk.DrawingArea imageBox;
        [Builder.Object]
        private Gtk.Stack rgbStack;
        [Builder.Object]
        private Gtk.Grid grid;
        [Builder.Object]
        private Gtk.Box rgbBox;
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
        private Gtk.Scrollbar scrollV;
        [Builder.Object]
        private Gtk.Scrollbar scrollH;
        [Builder.Object]
        private Menu contextMenu;
        [Builder.Object]
        private MenuItem goToOriginMenu;
        [Builder.Object]
        private MenuItem goToImageMenu;
        [Builder.Object]
        private MenuItem roiDelete;
        [Builder.Object]
        private MenuItem roiID;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static ImageView Create(BioImage bm)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.ImageView.glade", null);
            ImageView v = new ImageView(builder, builder.GetObject("imageView").Handle, bm);
            v.Title = bm.Filename;
            return v;
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected ImageView(Builder builder, IntPtr handle, BioImage im) : base(handle)
        {
            _builder = builder;
            selectedImage = im;
            App.viewer = this;
            builder.Autoconnect(this);
            pxWmicron = SelectedImage.physicalSizeX;
            pxHmicron = SelectedImage.physicalSizeY;
            //pictureBox.SetSizeRequest(im.SizeX, im.SizeY);
            AddImage(im);
            if (im.isPyramidal)
            {
                viewStack.VisibleChild = viewStack.Children[1];
                scrollH.HeightRequest = im.SizeY;
                scrollV.WidthRequest = im.SizeX;
            }
            SetupHandlers();
            //pictureBox.WidthRequest = im.SizeX;
            //pictureBox.HeightRequest = im.SizeY;
            this.Title = im.Filename;
            this.Scale = new SizeF(1, 1);
        }
        #endregion
        public void UpdateImages()
        {
            if (SelectedImage == null)
                return;
            for (int i = 0; i < Bitmaps.Count; i++)
            {
                Bitmaps[i] = null;
            }
            GC.Collect();
            Bitmaps.Clear();
            if (zBar.Adjustment.Upper != SelectedImage.SizeZ - 1 || tBar.Adjustment.Upper != SelectedImage.SizeT - 1)
            {
                UpdateGUI();
            }

            int bi = 0;
            foreach (BioImage b in Images)
            {
                ZCT c = GetCoordinate();
                AForge.Bitmap bitmap = null;

                int index = b.Coords[c.Z, c.C, c.T];
                if (Mode == ViewMode.Filtered)
                {
                    if (SelectedImage.isPyramidal)
                    {
                        AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, PyramidalOrigin.X, PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                        bitmap = bf.ImageRGB;
                        SelectedImage.Buffers[index].Dispose();
                        SelectedImage.Buffers[index] = bf;
                    }
                    else
                        bitmap = (AForge.Bitmap)b.GetFiltered(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                }
                else if (Mode == ViewMode.RGBImage)
                {
                    if (SelectedImage.isPyramidal)
                    {
                        AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, PyramidalOrigin.X, PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                        bitmap = bf.ImageRGB;
                        SelectedImage.Buffers[index].Dispose();
                        SelectedImage.Buffers[index] = bf;
                    }
                    else
                        bitmap = b.GetRGBBitmap(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                }
                else if (Mode == ViewMode.Raw)
                {
                    if (SelectedImage.isPyramidal)
                    {
                        AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, PyramidalOrigin.X, PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                        bitmap = bf.ImageRGB;
                        SelectedImage.Buffers[index].Dispose();
                        SelectedImage.Buffers[index] = bf;
                    }
                    else
                        bitmap = b.Buffers[index];
                }
                else
                {
                    if (SelectedImage.isPyramidal)
                    {
                        AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, PyramidalOrigin.X, PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                        bitmap = bf.ImageRGB;
                        SelectedImage.Buffers[index].Dispose();
                        SelectedImage.Buffers[index] = bf;
                    }
                    else
                        bitmap = (AForge.Bitmap)b.GetEmission(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                }
                if (bitmap != null)
                    if (bitmap.PixelFormat == PixelFormat.Format16bppGrayScale || bitmap.PixelFormat == PixelFormat.Format48bppRgb)
                        bitmap = AForge.Imaging.Image.Convert16bppTo8bpp(bitmap);
                Pixbuf pixbuf = new Pixbuf(bitmap.RGBBytes, true, 8, bitmap.Width, bitmap.Height, bitmap.Width * 4);
                Bitmaps.Add(pixbuf);
                bi++;
            }
            UpdateView();
        }
        public void UpdateImage()
        {
            if (SelectedImage == null)
                return;
            if (zBar.Adjustment.Upper != SelectedImage.SizeZ - 1 || tBar.Adjustment.Upper != SelectedImage.SizeT - 1)
            {
                UpdateGUI();
            }
            ZCT c = GetCoordinate();
            AForge.Bitmap bitmap = null;
            BioImage b = SelectedImage;
            int index = SelectedImage.Coords[c.Z, c.C, c.T];
            if (Mode == ViewMode.Filtered)
            {
                if (SelectedImage.isPyramidal)
                {
                    AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, PyramidalOrigin.X, PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                    bitmap = bf.ImageRGB;
                    SelectedImage.Buffers[index].Dispose();
                    SelectedImage.Buffers[index] = bf;
                }
                else
                    bitmap = b.GetFiltered(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
            }
            else if (Mode == ViewMode.RGBImage)
            {
                if (SelectedImage.isPyramidal)
                {
                    AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, PyramidalOrigin.X, PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                    bitmap = bf.ImageRGB;
                    SelectedImage.Buffers[index].Dispose();
                    SelectedImage.Buffers[index] = bf;
                }
                else
                    bitmap = b.GetRGBBitmap(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
            }
            else if (Mode == ViewMode.Raw)
            {
                if (SelectedImage.isPyramidal)
                {
                    AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, PyramidalOrigin.X, PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                    bitmap = bf.ImageRGB;
                    SelectedImage.Buffers[index].Dispose();
                    SelectedImage.Buffers[index] = bf;
                }
                else
                    bitmap = b.Buffers[index].ImageRGB;
            }
            else
            {
                if (SelectedImage.isPyramidal)
                {
                    AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, PyramidalOrigin.X, PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                    bitmap = bf.ImageRGB;
                    SelectedImage.Buffers[index].Dispose();
                    SelectedImage.Buffers[index] = bf;
                }
                else
                    bitmap = b.GetEmission(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
            }
            if (Bitmaps[selectedIndex] != null)
                Bitmaps[selectedIndex].Dispose();
            Pixbuf pixbuf = new Pixbuf(bitmap.RGBBytes, true, 8, bitmap.Width, bitmap.Height, bitmap.Width * 4);
            Bitmaps.Add(pixbuf);
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

            if (!SelectedImage.isPyramidal)
            {
                pictureBox.MotionNotifyEvent += ImageView_MotionNotifyEvent;
                pictureBox.ButtonPressEvent += ImageView_ButtonPressEvent;
                pictureBox.ButtonReleaseEvent += ImageView_ButtonReleaseEvent;
                pictureBox.ScrollEvent += ImageView_ScrollEvent;
                pictureBox.Drawn += PictureBox_Drawn;
                pictureBox.SizeAllocated += PictureBox_SizeAllocated;
                pictureBox.AddEvents((int)
                (EventMask.ButtonPressMask
                | EventMask.ButtonReleaseMask
                | EventMask.KeyPressMask
                | EventMask.PointerMotionMask | EventMask.ScrollMask));
            }
            else
            {
                imageBox.MotionNotifyEvent += ImageView_MotionNotifyEvent;
                imageBox.ButtonPressEvent += ImageView_ButtonPressEvent;
                imageBox.ButtonReleaseEvent += ImageView_ButtonReleaseEvent;
                imageBox.ScrollEvent += ImageView_ScrollEvent;
                
                imageBox.Drawn += PictureBox_Drawn;
                imageBox.SizeAllocated += PictureBox_SizeAllocated;
                imageBox.AddEvents((int)
                (EventMask.ButtonPressMask
                | EventMask.ButtonReleaseMask
                | EventMask.KeyPressMask
                | EventMask.PointerMotionMask | EventMask.ScrollMask));
                scrollH.ButtonReleaseEvent += ScrollH_ButtonReleaseEvent;
                scrollV.ButtonReleaseEvent += ScrollV_ButtonReleaseEvent;
            }
            this.KeyPressEvent += ImageView_KeyPressEvent;
            this.DeleteEvent += ImageView_DeleteEvent;
            rBox.Changed += RBox_Changed;
            gBox.Changed += GBox_Changed;
            bBox.Changed += BBox_Changed;
            //Context Menu
            goToImageMenu.ButtonPressEvent += GoToImageMenu_ButtonPressEvent;
            goToOriginMenu.ButtonPressEvent += GoToOriginMenu_ButtonPressEvent;
            roiDelete.ButtonPressEvent += RoiDelete_ButtonPressEvent;
            roiID.ButtonPressEvent += RoiID_ButtonPressEvent;

        }

        private void ImageView_DeleteEvent(object o, DeleteEventArgs args)
        {
            App.tabsView.RemoveTab(SelectedImage.Filename);
        }

        private void RoiID_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            TextInput ti = TextInput.Create();
            if (ti.Run() != (int)ResponseType.Ok)
                return;
            selectedAnnotations[0].id = ti.Text;
        }

        private void RoiDelete_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            foreach (var item in selectedAnnotations)
            {
                SelectedImage.Annotations.Remove(item);
            }
        }

        private void GoToOriginMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.viewer.Origin = new PointD(-SelectedImage.Volume.Location.X, -SelectedImage.Volume.Location.Y);
        }

        private void GoToImageMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.viewer.GoToImage();
        }

        #endregion

        private void BBox_Changed(object sender, EventArgs e)
        {
            SelectedImage.rgbChannels[2] = bBox.Active;
            UpdateView();
        }

        private void GBox_Changed(object sender, EventArgs e)
        {
            SelectedImage.rgbChannels[1] = gBox.Active;
            UpdateView();
        }

        private void RBox_Changed(object sender, EventArgs e)
        {
            SelectedImage.rgbChannels[0] = rBox.Active;
            UpdateView();
        }

        private void ScrollV_ButtonReleaseEvent(object o, ButtonReleaseEventArgs args)
        {
            PyramidalOrigin = new System.Drawing.Point((int)scrollH.Value, (int)scrollV.Value);
        }

        private void ScrollH_ButtonReleaseEvent(object o, ButtonReleaseEventArgs args)
        {
            PyramidalOrigin = new System.Drawing.Point((int)scrollH.Value, (int)scrollV.Value);
        }

        private void PictureBox_SizeAllocated(object o, SizeAllocatedArgs args)
        {
            //GoToImage();
            if (SelectedImage.isPyramidal)
                UpdateImage();

            UpdateView();
        }

        private static void DrawEllipse(Cairo.Context g, double x, double y, double width, double height)
        {
            g.Save();
            g.MoveTo(x + (width / 2), y);
            g.Translate(x, y);
            g.Scale(width / 2.0, height / 2.0);
            g.Arc(0.0, 0.0, 1.0, 0.0, 2 * Math.PI);
            g.Restore();
            g.Stroke();
            g.StrokePreserve();
        }
        public static Cairo.Color FromColor(System.Drawing.Color color)
        {
            return new Cairo.Color((double)color.R / 255, (double)color.G / 255, (double)color.B / 255);
        }

        private void PictureBox_Drawn(object o, DrawnArgs e)
        {
            if (Bitmaps.Count == 0 || Bitmaps.Count != Images.Count)
                UpdateImages();
            //e.Cr.Translate(-(pictureBox.AllocatedWidth / 2),-(pictureBox.AllocatedHeight / 2));
            e.Cr.Scale(Scale.Width, Scale.Height);
            RectangleD rr = ToViewSpace(PointD.MinX, PointD.MinY, PointD.MaxX - PointD.MinX, PointD.MaxY - PointD.MinY);
            e.Cr.Rectangle(rr.X, rr.Y, rr.W, rr.H);
            e.Cr.StrokePreserve();
            int i = 0;
            foreach (BioImage im in Images)
            {
                if (Bitmaps[i] == null)
                    UpdateImages();
                RectangleD r = ToViewSpace(im.Volume.Location.X, im.Volume.Location.Y, im.Volume.Width, im.Volume.Height);

                if (SelectedImage.isPyramidal)
                {
                    e.Cr.Restore(); //g.ResetTransform();   
                    Gdk.CairoHelper.SetSourcePixbuf(e.Cr, Bitmaps[i], 0, 0);
                }
                else
                {
                    Pixbuf pf = Bitmaps[i].ScaleSimple((int)r.W,(int)r.H, InterpType.Bilinear);
                    Gdk.CairoHelper.SetSourcePixbuf(e.Cr, pf, (int)r.X, (int)r.Y);
                    pf.Dispose();
                }
                e.Cr.Paint();
                foreach (ROI an in im.Annotations)
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

                    if (an.selected)
                    {
                        e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Magenta));
                    }
                    else
                        e.Cr.SetSourceColor(FromColor(an.strokeColor));
                    e.Cr.LineWidth = an.strokeWidth;
                    PointF pc = new PointF((float)(an.BoundingBox.X + (an.BoundingBox.W / 2)), (float)(an.BoundingBox.Y + (an.BoundingBox.H / 2)));
                    float width = (float)ToScreenScaleW(ROI.selectBoxSize);

                    if (an.type == ROI.Type.Point)
                    {
                        PointF p1 = ToViewSpace(an.Point.ToPointF());
                        PointF p2 = ToViewSpace(new PointF((float)an.Point.X + 1, (float)an.Point.Y + 1));

                        e.Cr.MoveTo(p1.X, p1.Y);
                        e.Cr.LineTo(p2.X, p2.Y);
                        e.Cr.StrokePreserve();
                        e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.StrokePreserve();
                        }
                    }
                    else
                    if (an.type == ROI.Type.Line)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            PointD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y);
                            PointD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y);
                            e.Cr.MoveTo(p1.X, p1.Y);
                            e.Cr.LineTo(p2.X, p2.Y);
                            e.Cr.StrokePreserve();
                        }
                        e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.StrokePreserve();
                        }
                    }
                    else
                    if (an.type == ROI.Type.Rectangle)
                    {
                        //ImageToViewSpace
                        //RectangleD rect = ToScreenRectF(an.X, an.Y, an.W, an.H);
                        RectangleD rect = ToViewSpace(an.X, an.Y, an.W, an.H);
                        e.Cr.Rectangle(rect.X, rect.Y, rect.W, rect.H);
                        e.Cr.StrokePreserve();
                        if (!an.selected)
                        {
                            e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Red));
                            foreach (RectangleD re in an.GetSelectBoxes(width))
                            {
                                RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                                e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                                e.Cr.StrokePreserve();
                            }
                        }
                    }
                    else
                    if (an.type == ROI.Type.Ellipse)
                    {
                        RectangleD rect = ToViewSpace(an.X + (an.W / 2), an.Y + (an.H / 2), an.W, an.H);
                        DrawEllipse(e.Cr, rect.X, rect.Y, rect.W, rect.H);
                        e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.StrokePreserve();
                        }
                    }
                    else
                    if ((an.type == ROI.Type.Polygon && an.closed))
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            PointD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y);
                            PointD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y);
                            e.Cr.MoveTo(p1.X, p1.Y);
                            e.Cr.LineTo(p2.X, p2.Y);
                            e.Cr.StrokePreserve();
                        }
                        
                        PointF pp1 = ToViewSpace(an.PointsD[0].X, an.PointsD[0].Y).ToPointF();
                        PointF pp2 = ToViewSpace(an.PointsD[an.PointsD.Count - 1].X, an.PointsD[an.PointsD.Count - 1].Y).ToPointF();
                        e.Cr.MoveTo(pp1.X, pp1.Y);
                        e.Cr.LineTo(pp2.X, pp2.Y);
                        e.Cr.StrokePreserve();
                        e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Red));

                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.StrokePreserve();
                        }
                    }
                    else
                    if ((an.type == ROI.Type.Polygon && !an.closed) || an.type == ROI.Type.Polyline)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            PointD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y);
                            PointD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y);
                            e.Cr.MoveTo(p1.X, p1.Y);
                            e.Cr.LineTo(p2.X, p2.Y);
                            e.Cr.StrokePreserve();
                        }
                        e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.StrokePreserve();
                        }
                    }
                    else
                    if (an.type == ROI.Type.Freeform && an.closed)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            PointD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y);
                            PointD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y);
                            e.Cr.MoveTo(p1.X, p1.Y);
                            e.Cr.LineTo(p2.X, p2.Y);
                            e.Cr.StrokePreserve();
                        }
                        //With free form we don't draw select boxes unless the ROI is selected
                        if (an.selected)
                        {
                            e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Red));
                            foreach (RectangleD re in an.GetSelectBoxes(width))
                            {
                                RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                                e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                                e.Cr.StrokePreserve();
                            }
                        }
                        PointF pp1 = ToViewSpace(an.PointsD[0].X, an.PointsD[0].Y).ToPointF();
                        PointF pp2 = ToViewSpace(an.PointsD[an.PointsD.Count - 1].X, an.PointsD[an.PointsD.Count - 1].Y).ToPointF();
                        e.Cr.MoveTo(pp1.X, pp1.Y);
                        e.Cr.LineTo(pp2.X, pp2.Y);
                        e.Cr.StrokePreserve();
                    }
                    if (an.type == ROI.Type.Label)
                    {
                        e.Cr.SetFontSize(an.fontSize);
                        e.Cr.SelectFontFace(an.family, Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
                        PointF p = ToViewSpace(new PointF((float)an.Point.X, (float)an.Point.Y));
                        e.Cr.MoveTo(p.X, p.Y);
                        e.Cr.ShowText(an.Text);
                        e.Cr.StrokePreserve();
                        e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.StrokePreserve();
                        }
                    }
                    if (labels)
                    {
                        e.Cr.SetFontSize(an.fontSize);
                        e.Cr.SelectFontFace(an.family, Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
                        PointD p = ToScreenSpace(an.Rect.X, an.Rect.Y);
                        e.Cr.MoveTo(p.X, p.Y);
                        e.Cr.ShowText(an.Text);
                        e.Cr.StrokePreserve();
                    }
                    if (bounds && an.type != ROI.Type.Rectangle && an.type != ROI.Type.Label)
                    {
                        RectangleD rrf = ToViewSpace(an.BoundingBox.X, an.BoundingBox.Y, an.BoundingBox.W, an.BoundingBox.H);
                        e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Green));
                        e.Cr.Rectangle(rrf.X, rrf.Y, rrf.W, rrf.H);
                        e.Cr.StrokePreserve();
                    }
                    //Lets draw the selected Boxes.
                    List<RectangleD> rects = new List<RectangleD>();
                    RectangleD[] sels = an.GetSelectBoxes(width);
                    for (int p = 0; p < an.selectedPoints.Count; p++)
                    {
                        if (an.selectedPoints[p] < an.GetPointCount())
                        {
                            rects.Add(sels[an.selectedPoints[p]]);
                        }
                    }
                    e.Cr.SetSourceColor(FromColor(System.Drawing.Color.Blue));
                    if (rects.Count > 0)
                    {
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.StrokePreserve();
                        }
                    }
                    rects.Clear();
                    
                }

            }

        }

        private void TBar_ScrollEvent(object o, ScrollEventArgs args)
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
        private void ImageView_KeyPressEvent(object o, KeyPressEventArgs e)
        {
            keyDown = e.Event.Key;
            double moveAmount = 5 * Scale.Width;
            if (e.Event.Key == Gdk.Key.c && e.Event.State == ModifierType.ControlMask)
            {
                //CopySelection();
                return;
            }
            if (e.Event.Key == Gdk.Key.c && e.Event.State == ModifierType.ControlMask)
            {
                //PasteSelection();
                return;
            }
            if (e.Event.Key == Gdk.Key.minus)
            {
                Scale = new SizeF(Scale.Width - 0.1f, Scale.Height - 0.1f);
            }
            if (e.Event.Key == Gdk.Key.plus)
            {
                Scale = new SizeF(Scale.Width + 0.1f, Scale.Height + 0.1f);
            }
            if (e.Event.Key == Gdk.Key.w)
            {
                Origin = new PointD(Origin.X, Origin.Y + moveAmount);
            }
            if (e.Event.Key == Gdk.Key.s)
            {
                Origin = new PointD(Origin.X, Origin.Y - moveAmount);
            }
            if (e.Event.Key == Gdk.Key.a)
            {
                Origin = new PointD(Origin.X + moveAmount, Origin.Y);
            }
            if (e.Event.Key == Gdk.Key.d)
            {
                Origin = new PointD(Origin.X - moveAmount, Origin.Y);
            }
            UpdateView();
        }
        private void ImageView_KeyUpEvent(object o, KeyPressEventArgs e)
        {
            keyDown = Gdk.Key.Key_3270_Test;
        }

        SizeF dSize = new SizeF(1, 1);
        private void ImageView_ScrollEvent(object o, ScrollEventArgs args)
        {
            float dx = Scale.Width / 50;
            float dy = Scale.Height / 50;
            if (!SelectedImage.isPyramidal)
                if (args.Event.State == ModifierType.ControlMask)
                {
                    float dsx = dSize.Width / 50;
                    float dsy = dSize.Height / 50;
                    if (args.Event.Direction == ScrollDirection.Up)
                    {
                        //Scale = new SizeF(Scale.Width + dx, Scale.Height + dy);
                        dSize.Width += dsx;
                        dSize.Height += dsy;
                        pxWmicron += 0.01f;
                        pxHmicron += 0.01f;
                    }
                    else
                    {
                        //Scale = new SizeF(Scale.Width - dx, Scale.Height - dy);
                        dSize.Width -= dsx;
                        dSize.Height -= dsy;
                        pxWmicron -= 0.01f;
                        pxHmicron -= 0.01f;
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
            if (args.Event.State == ModifierType.ControlMask && SelectedImage.isPyramidal)
                if (args.Event.Direction == ScrollDirection.Up)
                {
                    if (resolution - 1 > 0)
                        Resolution--;
                }
                else
                {
                    if (resolution + 1 < SelectedImage.Resolutions.Count)
                        Resolution++;
                }
        }

        private void ValueChanged(object? sender, EventArgs e)
        {
            SetCoordinate((int)zBar.Value, (int)cBar.Value, (int)tBar.Value);
        }

        private void UpdateGUI()
        {
            cBar.Adjustment.Lower = 0;
            cBar.Adjustment.Upper = Images[selectedIndex].SizeC - 1;
            zBar.Adjustment.Lower = 0;
            zBar.Adjustment.Upper = Images[selectedIndex].SizeZ - 1;
            tBar.Adjustment.Lower = 0;
            tBar.Adjustment.Upper = Images[selectedIndex].SizeT - 1;


            var store = new ListStore(typeof(string));
            foreach (Channel c in SelectedImage.Channels)
            {
                store.AppendValues(c.Name);
            }
            // Set the model for the ComboBox
            rBox.Model = store;
            gBox.Model = store;
            bBox.Model = store;
            // Set the text column to display
            var rendererr = new CellRendererText();
            rBox.PackStart(rendererr, false);
            rBox.AddAttribute(rendererr, "text", 0);
            rBox.Active = 0;
            var rendererg = new CellRendererText();
            gBox.PackStart(rendererg, false);
            gBox.AddAttribute(rendererg, "text", 0);
            if (SelectedImage.Channels.Count > 1)
                gBox.Active = 1;
            else
                gBox.Active = 0;
            var rendererb = new CellRendererText();
            bBox.PackStart(rendererb, false);
            bBox.AddAttribute(rendererb, "text", 0);
            if (SelectedImage.Channels.Count > 2)
                bBox.Active = 2;
            else
                bBox.Active = 0;
            bBox.Active = 0;
        }

        public enum ViewMode
        {
            Raw,
            Filtered,
            RGBImage,
            Emission,
        }
        static BioImage selectedImage;
        public static BioImage SelectedImage
        {
            get
            {
                return selectedImage;
            }
        }
        private ViewMode viewMode = ViewMode.Filtered;
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
                    rgbStack.SetVisibleChildFull("rgbBox", StackTransitionType.None);
                    rgbStack.VisibleChild = rgbStack.Children[1];
                }
                else
                if (viewMode == ViewMode.Filtered)
                {
                    rgbStack.SetVisibleChildFull("cBar", StackTransitionType.None);
                }
                else
                if (viewMode == ViewMode.Raw)
                {
                    rgbStack.SetVisibleChildFull("cBar", StackTransitionType.None);
                }
                else
                {
                    rgbStack.SetVisibleChildFull("cBar", StackTransitionType.None);
                }
            }
        }
        public Channel RChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[0]];
            }
        }
        public Channel GChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[1]];
            }
        }
        public Channel BChannel
        {
            get
            {
                return SelectedImage.Channels[SelectedImage.rgbChannels[2]];
            }
        }
        PointD origin = new PointD(0, 0);
        System.Drawing.Point pyramidalOrigin = new System.Drawing.Point(0, 0);
        public PointD Origin
        {
            get { return origin; }
            set
            {
                origin = value;
            }
        }
        public System.Drawing.Point PyramidalOrigin
        {
            get { return pyramidalOrigin; }
            set
            {
                if (scrollH.Adjustment.Upper > value.X && value.X > -1)
                    scrollH.Adjustment.Value = value.X;
                if (scrollV.Adjustment.Upper > value.Y && value.Y > -1)
                    scrollV.Adjustment.Value = value.Y;
                pyramidalOrigin = value;
                PointD p = SelectedImage.ToStageSpace(new PointD(pyramidalOrigin.X, pyramidalOrigin.Y), resolution);
                origin = new PointD(p.X, p.Y);
                UpdateImage();
                UpdateView();
            }
        }
        int resolution = 0;
        public int Resolution
        {
            get { return resolution; }
            set
            {
                if (SelectedImage.Resolutions.Count <= value || value < 0)
                    return;
                double x = PyramidalOrigin.X * ((double)SelectedImage.Resolutions[resolution].SizeX / (double)SelectedImage.Resolutions[value].SizeX);
                double y = PyramidalOrigin.Y * ((double)SelectedImage.Resolutions[resolution].SizeY / (double)SelectedImage.Resolutions[value].SizeY);
                scrollH.Adjustment.Upper = SelectedImage.Resolutions[value].SizeX;
                scrollV.Adjustment.Upper = SelectedImage.Resolutions[value].SizeY;
                PyramidalOrigin = new System.Drawing.Point((int)x, (int)y);
                SelectedImage.Resolution = value;
                resolution = value;
                UpdateImage();
                UpdateView();

            }
        }
        SizeF scale = new SizeF(1, 1);
        public new SizeF Scale
        {
            get
            {
                return scale;
            }
            set
            {
                scale = value;
                UpdateView();
            }
        }
        public void UpdateStatus()
        {
            statusLabel.Text = (zBar.Value + 1) + "/" + (zBar.Adjustment.Upper + 1) + ", " + (cBar.Value + 1) + "/" + (cBar.Adjustment.Upper + 1) + ", " + (tBar.Value + 1) + "/" + (tBar.Adjustment.Upper + 1) + ", " +
                mousePoint + mouseColor + ", " + SelectedImage.Buffers[0].PixelFormat.ToString() + ", (" + SelectedImage.Volume.Location.X + ", " + SelectedImage.Volume.Location.Y + ") " + Origin.ToString();
        }
        public void UpdateView()
        {
            if (SelectedImage.isPyramidal)
                imageBox.QueueDraw();
            else
                pictureBox.QueueDraw();
        }
        private string mousePoint = "";
        private string mouseColor = "";

        public bool showRROIs = true;
        public bool showGROIs = true;
        public bool showBROIs = true;

        private List<ROI> annotationsR = new List<ROI>();
        public List<ROI> AnnotationsR
        {
            get
            {
                return SelectedImage.GetAnnotations(SelectedImage.Coordinate.Z, SelectedImage.RChannel.Index, SelectedImage.Coordinate.T);
            }
        }
        private List<ROI> annotationsG = new List<ROI>();
        public List<ROI> AnnotationsG
        {
            get
            {
                return SelectedImage.GetAnnotations(SelectedImage.Coordinate.Z, SelectedImage.GChannel.Index, SelectedImage.Coordinate.T);
            }
        }
        private List<ROI> annotationsB = new List<ROI>();
        public List<ROI> AnnotationsB
        {
            get
            {
                return SelectedImage.GetAnnotations(SelectedImage.Coordinate.Z, SelectedImage.BChannel.Index, SelectedImage.Coordinate.T);
            }
        }
        public List<ROI> AnnotationsRGB
        {
            get
            {
                if (SelectedImage == null)
                    return null;
                List<ROI> ans = new List<ROI>();
                if (Mode == ViewMode.RGBImage)
                {
                    if (showRROIs)
                        ans.AddRange(AnnotationsR);
                    if (showGROIs)
                        ans.AddRange(AnnotationsG);
                    if (showBROIs)
                        ans.AddRange(AnnotationsB);
                }
                else
                {
                    ans.AddRange(SelectedImage.GetAnnotations(SelectedImage.Coordinate));
                }
                return ans;
            }
        }
        public static bool labels = false;
        public static bool bounds = true;
        public double GetScale()
        {
            return ToViewSizeW(ROI.selectBoxSize / Scale.Width);
        }
        public static bool x1State;
        public static bool x2State;
        public static bool mouseLeftState;
        public static ModifierType Modifiers;
        System.Drawing.Point mouseD = new System.Drawing.Point(0, 0);

        private void ImageView_MotionNotifyEvent(object o, MotionNotifyEventArgs e)
        {
            Modifiers = e.Event.State;
            PointD p = ImageToViewSpace(e.Event.X, e.Event.Y);
            App.tools.ToolMove(p, e);
            mousePoint = "(" + (p.X) + ", " + (p.Y) + ")";
            //pd = p;
            if (Tools.currentTool.type == Tools.Tool.Type.pointSel && mouseLeftState)
            {
                foreach (ROI an in selectedAnnotations)
                {
                    if (an.selectedPoints.Count > 0 && an.selectedPoints.Count < an.GetPointCount())
                    {
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
                            for (int i = 0; i < an.selectedPoints.Count; i++)
                            {
                                PointD poid = an.GetPoint(an.selectedPoints[i]);
                                an.UpdatePoint(new PointD(poid.X + pod.X, poid.Y + pod.Y), an.selectedPoints[i]);
                            }
                        }
                        UpdateView();
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
                if (Tools.currentTool.type == Tools.Tool.Type.pencil && Modifiers == ModifierType.Button1Mask)
                {
                    Tools.Tool tool = Tools.currentTool;
                    Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(SelectedBuffer);
                    Bio.Graphics.Pen pen = new Bio.Graphics.Pen(Tools.DrawColor, (int)Tools.StrokeWidth, ImageView.SelectedImage.bitsPerPixel);
                    g.FillEllipse(new System.Drawing.Rectangle((int)pd.X, (int)pd.Y, (int)Tools.StrokeWidth, (int)Tools.StrokeWidth), pen.color);
                    UpdateImage();
                }
            UpdateStatus();
            pd = p;
        }
        public static PointD mouseDown;
        public static PointD mouseUp;
        private void ImageView_ButtonReleaseEvent(object o, ButtonReleaseEventArgs e)
        {
            Modifiers = e.Event.State;
            if (e.Event.Button == 1)
                mouseLeftState = false;
            App.viewer = this;
            PointD pointer = ImageToViewSpace(e.Event.X, e.Event.Y);
            mouseUp = pointer;
            if (e.Event.State == ModifierType.Button2Mask)
            {
                if (SelectedImage != null)
                    if (SelectedImage.isPyramidal)
                    {
                        System.Drawing.Point pf = new System.Drawing.Point((int)e.Event.X - mouseD.X, (int)e.Event.Y - mouseD.Y);
                        PyramidalOrigin = new System.Drawing.Point(PyramidalOrigin.X - pf.X, PyramidalOrigin.Y - pf.Y);
                    }
                    else
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
        public PointD ImageToViewSpace(double x,double y)
        {
            double dx = ToViewW(SelectedImage.Volume.Width);
            double dy = ToViewH(SelectedImage.Volume.Height);
            PointD orig = new PointD(Origin.X - SelectedImage.Volume.Location.X, Origin.Y - SelectedImage.Volume.Location.Y);
            PointD dif = new PointD(ToScreenScaleW(orig.X), ToScreenScaleH(orig.Y));
            PointD diff = new PointD(ToViewW(orig.X), ToViewH(orig.Y));
            PointD p = new PointD(x + diff.X, y + diff.Y);
            PointD f = new PointD((((x + diff.X)/ dx) * SelectedImage.Volume.Width),(((y + diff.Y) / dy) * SelectedImage.Volume.Height));
            PointD ff = new PointD(SelectedImage.Volume.Location.X + f.X, SelectedImage.Volume.Location.Y + f.Y);
            return ff;
        }
        PointD pd;
        private void ImageView_ButtonPressEvent(object o, ButtonPressEventArgs e)
        {
            Modifiers = e.Event.State;
            if (e.Event.Button == 1)
                mouseLeftState = true;
            else
                mouseLeftState = false;
            App.viewer = this;
            PointD pointer = ImageToViewSpace(e.Event.X, e.Event.Y);
            pd = pointer;
            mouseDown = pd;
            mouseD = new System.Drawing.Point((int)e.Event.X, (int)e.Event.Y);
            if (SelectedImage == null)
                return;
            PointD ip = pointer; // SelectedImage.ToImageSpace(pointer);
            int ind = 0;
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

            foreach (BioImage b in Images)
            {
                RectangleD r = new RectangleD(b.Volume.Location.X, b.Volume.Location.Y, b.Volume.Width, b.Volume.Height);
                if (r.IntersectsWith(pointer))
                {
                    selectedIndex = ind;
                    selectedImage = SelectedImage;
                    break;
                }
                ind++;
            }
            if (Modifiers != ModifierType.ControlMask && e.Event.Button == 1)
            {
                foreach (ROI item in selectedAnnotations)
                {
                    if (item.selected)
                    {
                        item.selectedPoints.Clear();
                        item.selected = false;
                    }
                }
                selectedAnnotations.Clear();
            }

            if (Tools.currentTool.type == Tools.Tool.Type.pointSel || Tools.currentTool.type == Tools.Tool.Type.move && e.Event.Button == 1)
            {
                //float width = (float)ToViewSizeW(ROI.selectBoxSize / Scale.Width);
                float width = (float)ToScreenScaleW(ROI.selectBoxSize);
                foreach (BioImage bi in Images)
                {
                    foreach (ROI an in bi.Annotations)
                    {
                        if (an.Rect.ToRectangleF().IntersectsWith(new RectangleF(pointer.ToPointF(),new SizeF(1,1))))
                        {
                            selectedAnnotations.Add(an);
                            an.selected = true;
                            RectangleD[] sels = an.GetSelectBoxes(width);
                            RectangleD r = new RectangleD((float)pointer.X, (float)pointer.Y, (float)sels[0].W, (float)sels[0].H);
                            for (int i = 0; i < sels.Length; i++)
                            {
                                if (sels[i].ToRectangleF().IntersectsWith(new RectangleF((float)r.X, (float)r.Y, (float)r.W, (float)r.H)))
                                {
                                    an.selectedPoints.Add(i);
                                }
                            }
                        }
                        else
                            if (Modifiers != ModifierType.ControlMask)
                            an.selected = false;
                    }
                }
                UpdateView();
            }

            if (e.Event.Button == 1)
            {
                System.Drawing.Point s = new System.Drawing.Point(SelectedImage.SizeX, SelectedImage.SizeY);
                if ((ip.X < s.X && ip.Y < s.Y) || (ip.X >= 0 && ip.Y >= 0))
                {
                    int zc = SelectedImage.Coordinate.Z;
                    int cc = SelectedImage.Coordinate.C;
                    int tc = SelectedImage.Coordinate.T;
                    if (SelectedImage.isPyramidal)
                    {
                        if (SelectedImage.isRGB)
                        {
                            int x = (int)e.Event.X;
                            int y = (int)e.Event.Y;
                            if (x < SelectedImage.SizeX && y < SelectedImage.SizeY)
                            {
                                int r = SelectedImage.GetValueRGB(zc, RChannel.Index, tc, x, y, 0);
                                int g = SelectedImage.GetValueRGB(zc, GChannel.Index, tc, (int)e.Event.X, (int)e.Event.Y, 1);
                                int b = SelectedImage.GetValueRGB(zc, BChannel.Index, tc, (int)e.Event.X, (int)e.Event.Y, 2);
                                mouseColor = ", " + r + "," + g + "," + b;
                            }
                            else
                                mouseColor = "";
                        }
                        else
                        {
                            int r = SelectedImage.GetValueRGB(zc, 0, tc, (int)e.Event.X, (int)e.Event.Y, 0);
                            mouseColor = ", " + r;
                        }
                    }
                    else
                    {
                        if (SelectedImage.isRGB)
                        {
                            int r = SelectedImage.GetValueRGB(zc, RChannel.Index, tc, (int)ip.X, (int)ip.Y, 0);
                            int g = SelectedImage.GetValueRGB(zc, GChannel.Index, tc, (int)ip.X, (int)ip.Y, 1);
                            int b = SelectedImage.GetValueRGB(zc, BChannel.Index, tc, (int)ip.X, (int)ip.Y, 2);
                            mouseColor = ", " + r + "," + g + "," + b;
                        }
                        else
                        {
                            int r = SelectedImage.GetValueRGB(zc, 0, tc, (int)ip.X, (int)ip.Y, 0);
                            mouseColor = ", " + r;
                        }
                    }
                }
            }
            UpdateStatus();
            App.tools.ToolDown(mouseDown, e);
        }

        List<ROI> copys = new List<ROI>();
        public RectangleD ToViewSpace(RectangleD p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            double dx = ToScreenScaleW(p.W);
            double dy = ToScreenScaleH(p.H);
            return new RectangleD((float)d.X, (float)d.Y, (float)dx, (float)dy);
        }
        public PointF ToViewSpace(System.Drawing.Point p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            return new PointF((float)d.X, (float)d.Y);
        }
        public PointF ToViewSpace(PointF p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            return new PointF((float)d.X, (float)d.Y);
        }
        public PointD ToViewSpace(PointD p)
        {
            return ToViewSpace(p.X, p.Y); ;
        }
        public PointD ToViewSpace(double x, double y)
        {
            double dx = (ToViewSizeW(Origin.X - x)) * Scale.Width;
            double dy = (ToViewSizeH(Origin.Y - y)) * Scale.Height;
            return new PointD(dx, dy);
        }
        public RectangleD ToViewSpace(double x, double y, double w, double h)
        {
            PointD d = ToViewSpace(x, y);
            double dw = ToViewSizeW(w);
            double dh = ToViewSizeH(h);
            return new RectangleD(-d.X, -d.Y, dw, dh);
        }
        private double ToViewSizeW(double d)
        {
            double x = (double)(d / PxWmicron) * Scale.Width;
            return x;
        }
        public double ToViewSizeH(double d)
        {
            double y = (double)(d / PxHmicron) * Scale.Width;
            return y;
        }
        public double ToViewW(double d)
        {
            double x = (double)(d / PxWmicron) * Scale.Width;
            return x;
        }
        public double ToViewH(double d)
        {
            double y = (double)(d / PxHmicron) * Scale.Height;
            return y;
        }


        public PointD ToScreenSpace(double x, double y)
        {
            double fx = ToScreenScaleW(Origin.X - x);
            double fy = ToScreenScaleH(Origin.Y - y);
            return new PointD(fx, fy);
        }
        public PointD ToScreenSpace(PointD p)
        {
            return ToScreenSpace(p.X, p.Y);
        }
        public PointF ToScreenSpace(PointF p)
        {
            PointD pd = ToScreenSpace(p.X, p.Y);
            return new PointF((float)pd.X, (float)pd.Y);
        }
        public PointF[] ToScreenSpace(PointF[] p)
        {
            PointF[] pf = new PointF[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                pf[i] = ToScreenSpace(p[i]);
            }
            return pf;
        }
        public PointF ToScreenSpace(Point3D p)
        {
            PointD pd = ToScreenSpace(p.X, p.Y);
            return new PointF((float)pd.X, (float)pd.Y);
        }
        public double ToScreenScaleW(double x)
        {
            return (x * PxWmicron) * Scale.Width;
        }
        public double ToScreenScaleH(double y)
        {
            return (y * PxHmicron) * Scale.Height;
        }
        public PointF ToScreenScale(PointD p)
        {
            double x = ToScreenScaleW((float)p.X);
            double y = ToScreenScaleH((float)p.Y);
            return new PointF((float)x, (float)y);
        }
        public RectangleD ToScreenRectF(double x, double y, double w, double h)
        {
            PointD pf = ToScreenSpace(x, y);
            RectangleD rf = new RectangleD((float)pf.X, (float)pf.Y, (float)ToViewW(w), (float)ToViewH(h));
            return rf;
        }
        public RectangleD ToScreenSpace(RectangleD p)
        {
            return ToScreenRectF(p.X, p.Y, p.W, p.H);
        }
        public RectangleD[] ToScreenSpace(RectangleD[] p)
        {
            RectangleD[] rs = new RectangleD[p.Length];
            for (int i = 0; i < p.Length; i++)
            {
                rs[i] = ToScreenSpace(p[i]);
            }
            return rs;
        }
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
        public void GoToImage()
        {
            GoToImage(0);
        }
        public void GoToImage(int i)
        {
            if (Images.Count <= i)
                return;
            double dx = Images[i].Volume.Width / 2;
            double dy = Images[i].Volume.Height / 2;
            Origin = new PointD((Images[i].Volume.Location.X), (Images[i].Volume.Location.Y));
            Scale = new SizeF(1, 1);
            UpdateView();
        }

    }
}
