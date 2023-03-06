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
using loci.formats;
using CSScripting;
using Bio;
using Point = AForge.Point;
using Color = AForge.Color;
using Rectangle = AForge.Rectangle;

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
            if (z > SelectedImage.SizeZ - 1)
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
            SelectedImage.Coordinate = new ZCT((int)zBar.Value, (int)cBar.Value, (int)tBar.Value);
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
        private Gtk.Scrollbar scrollV;
        [Builder.Object]
        private Gtk.Scrollbar scrollH;
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
            roi.Submenu = roiMenu;
            roi.ShowAll();
            pxWmicron = SelectedImage.physicalSizeX;
            pxHmicron = SelectedImage.physicalSizeY;
            if (im.SizeY >= 1080)
            {
                pictureBox.SetSizeRequest(800, 600 - 130);
            }
            else
                pictureBox.SetSizeRequest(im.SizeX, im.SizeY);
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
        }
        #endregion
        /// It updates the images.
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
                        AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, (int)PyramidalOrigin.X, (int)PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                        bitmap = bf.ImageRGB;
                        SelectedImage.Buffers[index].Dispose();
                        SelectedImage.Buffers[index] = bf;
                    }
                    else
                    {
                        bitmap = b.GetFiltered(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                    }
                }
                else if (Mode == ViewMode.RGBImage)
                {
                    if (SelectedImage.isPyramidal)
                    {
                        AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, (int)PyramidalOrigin.X, (int)PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
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
                        AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, (int)PyramidalOrigin.X, (int)PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
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
                        AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, (int)PyramidalOrigin.X, (int)PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                        bitmap = bf.ImageRGB;
                        SelectedImage.Buffers[index].Dispose();
                        SelectedImage.Buffers[index] = bf;
                    }
                    else
                        bitmap = b.GetEmission(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                }
                if (bitmap.PixelFormat == PixelFormat.Format16bppGrayScale || bitmap.PixelFormat == PixelFormat.Format48bppRgb)
                    bitmap = AForge.Imaging.Image.Convert16bppTo8bpp(bitmap);
                AForge.Bitmap bm = bitmap.ImageRGB;
                Pixbuf pixbuf = new Pixbuf(bm.Bytes, true, 8, bm.Width, bm.Height, bm.Stride);
                Bitmaps.Add(pixbuf);
                bi++;
            }
            UpdateView();
        }
        /// It updates the image.
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
                    AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, (int)PyramidalOrigin.X, (int)PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                    bitmap = bf.ImageRGB;
                    SelectedImage.Buffers[index].Dispose();
                    SelectedImage.Buffers[index] = bf;
                }
                else
                {
                    bitmap = b.GetFiltered(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
                }
            }
            else if (Mode == ViewMode.RGBImage)
            {
                if (SelectedImage.isPyramidal)
                {
                    AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, (int)PyramidalOrigin.X, (int)PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
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
                    AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, (int)PyramidalOrigin.X, (int)PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
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
                    AForge.Bitmap bf = BioImage.GetTile(SelectedImage, c, Resolution, (int)PyramidalOrigin.X, (int)PyramidalOrigin.Y, imageBox.AllocatedWidth, imageBox.AllocatedHeight);
                    bitmap = bf.ImageRGB;
                    SelectedImage.Buffers[index].Dispose();
                    SelectedImage.Buffers[index] = bf;
                }
                else
                    bitmap = b.GetEmission(c, b.RChannel.RangeR, b.GChannel.RangeG, b.BChannel.RangeB);
            }
            if(Bitmaps.Count> 0)
            if (Bitmaps[selectedIndex] != null)
                Bitmaps[selectedIndex].Dispose();
            if (bitmap.PixelFormat == PixelFormat.Format16bppGrayScale || bitmap.PixelFormat == PixelFormat.Format48bppRgb)
                bitmap = AForge.Imaging.Image.Convert16bppTo8bpp(bitmap);
            AForge.Bitmap bm = bitmap.ImageRGB;
            Pixbuf pixbuf = new Pixbuf(bm.RGBBytes, true, 8, bm.Width, bm.Height, bm.Stride);
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
        private void CBar_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            bar = 2;
            if (args.Event.Button == 3)
                barMenu.Popup();
        }

        private void TBar_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            bar = 1;
            if (args.Event.Button == 3)
                barMenu.Popup();
        }
        
        private void ZBar_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            bar = 0;
            if (args.Event.Button == 3)
                barMenu.Popup();
        }

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

        private void SetValueRange_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Play play = Play.Create();
            play.Show();
        }

        private void PlaySpeed_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Play play = Play.Create();
            play.Show();
        }

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

        private void Play_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if(bar == 0)
            {
                if (endz == 0)
                    endz = selectedImage.SizeZ - 1;
                playZ = true;
                threadZ = new System.Threading.Thread(PlayZ);
                threadZ.Start();
            }
            else if (bar == 1)
            {
                if (endt == 0)
                    endt = selectedImage.SizeT - 1;
                playT = true;
                threadT = new System.Threading.Thread(PlayT);
                threadT.Start();
            }
            else if (bar == 2)
            {
                if (endc == 0)
                    endc = selectedImage.SizeC - 1;
                playC = true;
                threadC = new System.Threading.Thread(PlayC);
                threadC.Start();
            }
        }

        private void Fill_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(SelectedBuffer);
            foreach (ROI item in AnnotationsRGB)
            {
                Bio.Graphics.Pen p = new Bio.Graphics.Pen(Tools.DrawColor, (int)Tools.StrokeWidth, SelectedBuffer.BitsPerPixel);
                if (item.selected)
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

        private void Draw_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(SelectedBuffer);
            foreach (ROI item in AnnotationsRGB)
            {
                Bio.Graphics.Pen p = new Bio.Graphics.Pen(Tools.DrawColor, (int)Tools.StrokeWidth, SelectedBuffer.BitsPerPixel);
                g.pen = p;
                if (item.selected)
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

        private void Paste_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            PasteSelection();
        }

        private void Copy_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            CopySelection();
        }

        private void ImageView_DeleteEvent(object o, DeleteEventArgs args)
        {
            App.tabsView.RemoveTab(Images[0].Filename);
        }

        private void RoiID_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            TextInput ti = TextInput.Create();
            if (ti.Run() != (int)ResponseType.Ok)
                return;
            selectedAnnotations[0].id = ti.Text;
            UpdateView();
        }

        private void RoiDelete_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            foreach (var item in selectedAnnotations)
            {
                SelectedImage.Annotations.Remove(item);
            }
            UpdateView();
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
            PyramidalOrigin = new Point((int)scrollH.Value, (int)scrollV.Value);
        }

        private void ScrollH_ButtonReleaseEvent(object o, ButtonReleaseEventArgs args)
        {
            PyramidalOrigin = new Point((int)scrollH.Value, (int)scrollV.Value);
        }

        bool initialized = false;
        private void PictureBox_SizeAllocated(object o, SizeAllocatedArgs args)
        {
            if (SelectedImage.isPyramidal)
            {
                UpdateImage();
            }
            if (!initialized)
            {
                GoToImage();
                initialized = true;
            }
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
            g.Stroke();
        }
        public static Cairo.Color FromColor(Color color)
        {
            return new Cairo.Color((double)color.R / 255, (double)color.G / 255, (double)color.B / 255);
        }

        /// The function is called when the picturebox is drawn. It checks if the bitmaps are up to
        /// date, and if not, it updates them. It then draws the image, and then draws the annotations. 
        /// 
        /// The annotations are drawn by looping through the list of annotations, and then drawing them
        /// based on their type. 
        /// 
        /// The annotations are drawn in the view space, which is the space of the picturebox. 
        /// 
        /// The annotations are drawn in the view space by converting the coordinates of the annotation
        /// to the view space. 
        /// 
        /// The coordinates of the annotation are converted to the view space by multiplying the
        /// coordinates by the scale of the image. 
        /// 
        /// The scale of the image is the ratio of the size of the image to the size of the picturebox. 
        /// 
        /// The size of the image is the size of the image in pixels. 
        /// 
        /// The size of the picturebox is the size of the picturebox in pixels.
        /// 
        /// @param o The object that is being drawn
        /// @param DrawnArgs This is a class that contains the Cairo context and the allocated width and
        /// height of the picturebox.
        private void PictureBox_Drawn(object o, DrawnArgs e)
        {
            if (Bitmaps.Count == 0 || Bitmaps.Count != Images.Count)
                UpdateImages();
            //e.Cr.Translate(-(pictureBox.AllocatedWidth / 2),-(pictureBox.AllocatedHeight / 2));
            e.Cr.Scale(Scale.Width, Scale.Height);
            RectangleD rr = ToViewSpace(PointD.MinX, PointD.MinY, PointD.MaxX - PointD.MinX, PointD.MaxY - PointD.MinY);
            e.Cr.Rectangle(rr.X, rr.Y,Math.Abs(rr.W),Math.Abs(rr.H));
            e.Cr.Stroke();
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
                        e.Cr.SetSourceColor(FromColor(Color.Magenta));
                    }
                    else
                        e.Cr.SetSourceColor(FromColor(an.strokeColor));
                    e.Cr.LineWidth = an.strokeWidth;
                    PointF pc = new PointF((float)(an.BoundingBox.X + (an.BoundingBox.W / 2)), (float)(an.BoundingBox.Y + (an.BoundingBox.H / 2)));
                    float width = (float)ToScreenScaleW(ROI.selectBoxSize);

                    if (an.type == ROI.Type.Point)
                    {
                        RectangleD p1 = ToViewSpace(an.Point.X,an.Point.Y,1,1);
                        RectangleD p2 = ToViewSpace(an.Point.X + 1, an.Point.Y + 1,1,1);

                        e.Cr.MoveTo(p1.X, p1.Y);
                        e.Cr.LineTo(p2.X, p2.Y);
                        e.Cr.Stroke();
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            e.Cr.SetSourceColor(FromColor(Color.Red));
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.Stroke();
                        }
                    }
                    else
                    if (an.type == ROI.Type.Line)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            RectangleD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y, 1,1);
                            RectangleD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y,1,1);
                            e.Cr.MoveTo(p1.X, p1.Y);
                            e.Cr.LineTo(p2.X, p2.Y);
                            e.Cr.Stroke();
                        }
                        e.Cr.SetSourceColor(FromColor(Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.Stroke();
                        }
                    }
                    else
                    if (an.type == ROI.Type.Rectangle)
                    {
                        //ImageToViewSpace
                        //RectangleD rect = ToScreenRectF(an.X, an.Y, an.W, an.H);
                        RectangleD rect = ToViewSpace(an.X, an.Y, an.W, an.H);
                        e.Cr.Rectangle(rect.X, rect.Y, rect.W, rect.H);
                        e.Cr.Stroke();
                        if (!an.selected)
                        { 
                            e.Cr.SetSourceColor(FromColor(Color.Red));
                            foreach (RectangleD re in an.GetSelectBoxes(width))
                            {
                                RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                                e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                                e.Cr.Stroke();
                            }
                        }
                    }
                    else
                    if (an.type == ROI.Type.Ellipse)
                    {
                        RectangleD rect = ToViewSpace(an.X + (an.W / 2), an.Y + (an.H / 2), an.W, an.H);
                        DrawEllipse(e.Cr, rect.X, rect.Y, rect.W, rect.H);
                        e.Cr.SetSourceColor(FromColor(Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.Stroke();
                        }
                    }
                    else
                    if ((an.type == ROI.Type.Polygon && an.closed))
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            RectangleD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y, 1,1);
                            RectangleD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y,1,1);
                            e.Cr.MoveTo(p1.X, p1.Y);
                            e.Cr.LineTo(p2.X, p2.Y);
                            e.Cr.Stroke();
                        }

                        RectangleD pp1 = ToViewSpace(an.PointsD[0].X, an.PointsD[0].Y,1,1);
                        RectangleD pp2 = ToViewSpace(an.PointsD[an.PointsD.Count - 1].X, an.PointsD[an.PointsD.Count - 1].Y, 1, 1);
                        e.Cr.MoveTo(pp1.X, pp1.Y);
                        e.Cr.LineTo(pp2.X, pp2.Y);
                        e.Cr.Stroke();
                        e.Cr.SetSourceColor(FromColor(Color.Red));

                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.Stroke();
                        }
                    }
                    else
                    if ((an.type == ROI.Type.Polygon && !an.closed) || an.type == ROI.Type.Polyline)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            RectangleD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                            RectangleD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                            e.Cr.MoveTo(p1.X, p1.Y);
                            e.Cr.LineTo(p2.X, p2.Y);
                            e.Cr.Stroke();
                        }
                        e.Cr.SetSourceColor(FromColor(Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.Stroke();
                        }
                    }
                    else
                    if (an.type == ROI.Type.Freeform)
                    {
                        for (int p = 0; p < an.PointsD.Count - 1; p++)
                        {
                            RectangleD p1 = ToViewSpace(an.PointsD[p].X, an.PointsD[p].Y, 1, 1);
                            RectangleD p2 = ToViewSpace(an.PointsD[p + 1].X, an.PointsD[p + 1].Y, 1, 1);
                            e.Cr.MoveTo(p1.X, p1.Y);
                            e.Cr.LineTo(p2.X, p2.Y);
                            e.Cr.Stroke();
                        }
                        //With freeform we don't draw select boxes unless the ROI is selected
                        if (an.selected)
                        {
                            e.Cr.SetSourceColor(FromColor(Color.Red));
                            foreach (RectangleD re in an.GetSelectBoxes(width))
                            {
                                RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                                e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                                e.Cr.Stroke();
                            }
                        }
                        RectangleD pp1 = ToViewSpace(an.PointsD[0].X, an.PointsD[0].Y, 1, 1);
                        RectangleD pp2 = ToViewSpace(an.PointsD[an.PointsD.Count - 1].X, an.PointsD[an.PointsD.Count - 1].Y, 1, 1);
                        e.Cr.MoveTo(pp1.X, pp1.Y);
                        e.Cr.LineTo(pp2.X, pp2.Y);
                        e.Cr.Stroke();
                    }
                    if (an.type == ROI.Type.Label)
                    {
                        e.Cr.SetFontSize(an.fontSize);
                        e.Cr.SelectFontFace(an.family, Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
                        RectangleD p = ToViewSpace(an.Point.X, an.Point.Y, 1 ,1);
                        e.Cr.MoveTo(p.X, p.Y);
                        e.Cr.ShowText(an.Text);
                        e.Cr.Stroke();
                        e.Cr.SetSourceColor(FromColor(Color.Red));
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                            e.Cr.Stroke();
                        }
                    }
                    if (ROIManager.showText)
                    {
                        e.Cr.SetFontSize(an.fontSize);
                        e.Cr.SelectFontFace(an.family, Cairo.FontSlant.Normal, Cairo.FontWeight.Normal);
                        RectangleD p = ToViewSpace(an.Rect.X, an.Rect.Y, 1, 1);
                        e.Cr.MoveTo(p.X, p.Y);
                        e.Cr.ShowText(an.Text);
                        e.Cr.Stroke();
                    }
                    if (ROIManager.showBounds && an.type != ROI.Type.Rectangle && an.type != ROI.Type.Label)
                    {
                        RectangleD rrf = ToViewSpace(an.BoundingBox.X, an.BoundingBox.Y, an.BoundingBox.W, an.BoundingBox.H);
                        e.Cr.SetSourceColor(FromColor(Color.Green));
                        e.Cr.Rectangle(rrf.X, rrf.Y, rrf.W, rrf.H);
                        e.Cr.Stroke();
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
                    e.Cr.SetSourceColor(FromColor(Color.Blue));
                    if (rects.Count > 0)
                    {
                        int ind = 0;
                        foreach (RectangleD re in an.GetSelectBoxes(width))
                        {
                            RectangleD recd = ToViewSpace(re.X, re.Y, re.W, re.H);
                            if (an.selectedPoints.Contains(ind))
                            {
                                e.Cr.Rectangle(recd.X, recd.Y, recd.W, recd.H);
                                e.Cr.Stroke();
                            }
                            ind++;
                        }
                    }
                    rects.Clear();
                    
                }
                if (Tools.currentTool.type == Tools.Tool.Type.select)
                {
                    RectangleD rrf = ToViewSpace(Tools.currentTool.Rectangle.X, Tools.currentTool.Rectangle.Y, Math.Abs(Tools.currentTool.Rectangle.W),Math.Abs(Tools.currentTool.Rectangle.H));
                    e.Cr.SetSourceColor(FromColor(Color.Magenta));
                    e.Cr.Rectangle(rrf.X, rrf.Y, rrf.W, rrf.H);
                    e.Cr.Stroke();
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
                    PyramidalOrigin = new Point(PyramidalOrigin.X, PyramidalOrigin.Y + 150);
                }
                else
                    Origin = new PointD(Origin.X, Origin.Y + moveAmount);
            }
            if (e.Event.Key == Gdk.Key.s)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new Point(PyramidalOrigin.X, PyramidalOrigin.Y - 150);
                }
                else
                    Origin = new PointD(Origin.X, Origin.Y - moveAmount);
            }
            if (e.Event.Key == Gdk.Key.a)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new Point(PyramidalOrigin.X - 150, PyramidalOrigin.Y);
                }
                else
                    Origin = new PointD(Origin.X + moveAmount, Origin.Y);
            }
            if (e.Event.Key == Gdk.Key.d)
            {
                if (SelectedImage.isPyramidal)
                {
                    PyramidalOrigin = new Point(PyramidalOrigin.X + 150, PyramidalOrigin.Y);
                }
                else
                    Origin = new PointD(Origin.X - moveAmount, Origin.Y);
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
            UpdateView();
        }
        private void ImageView_KeyUpEvent(object o, KeyPressEventArgs e)
        {
            keyDown = Gdk.Key.Key_3270_Test;
        }
        private void ImageView_ScrollEvent(object o, ScrollEventArgs args)
        {
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

        public void CopySelection()
        {
            copys.Clear();
            string s = "";
            foreach (ROI item in AnnotationsRGB)
            {
                if (item.selected)
                {
                    copys.Add(item);
                    s += BioImage.ROIToString(item);
                }
            }
            Clipboard clipboard = Clipboard.Get(Gdk.Selection.Clipboard);
            clipboard.Text = s;
        }
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
        static BioImage selectedImage;
        public static BioImage SelectedImage
        {
            get
            {
                return selectedImage;
            }
            set
            {
                selectedImage = value;
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
        Point pyramidalOrigin = new Point(0, 0);
        public PointD Origin
        {
            get { return origin; }
            set
            {
                origin = value;
            }
        }
        public Point PyramidalOrigin
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
                PyramidalOrigin = new Point((int)x, (int)y);
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
        /// It updates the status of the user.
        public void UpdateStatus()
        {
            statusLabel.Text = (zBar.Value + 1) + "/" + (zBar.Adjustment.Upper + 1) + ", " + (cBar.Value + 1) + "/" + (cBar.Adjustment.Upper + 1) + ", " + (tBar.Value + 1) + "/" + (tBar.Adjustment.Upper + 1) + ", " +
                mousePoint + mouseColor + ", " + SelectedImage.Buffers[0].PixelFormat.ToString() + ", (" + SelectedImage.Volume.Location.X + ", " + SelectedImage.Volume.Location.Y + ") " + Origin.ToString();
        }
        /// It updates the view.
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
        public double GetScale()
        {
            return ToViewSizeW(ROI.selectBoxSize / Scale.Width);
        }
        public static bool x1State;
        public static bool x2State;
        public static bool mouseLeftState;
        public static ModifierType Modifiers;
        PointD mouseD = new PointD(0, 0);

        /// This function is called when the mouse is moved over the image. It updates the mouse
        /// position, and if the user is drawing a brush stroke, it draws the stroke on the image
        /// 
        /// @param o the object that the event is being called from
        /// @param MotionNotifyEventArgs 
        private void ImageView_MotionNotifyEvent(object o, MotionNotifyEventArgs e)
        {
            Modifiers = e.Event.State;
            PointD p = ImageToViewSpace(e.Event.X, e.Event.Y);
            PointD ip = new PointD((p.X - origin.X) / pxWmicron, (p.Y - origin.Y) / pxHmicron);
            App.tools.ToolMove(p, e);
            Tools.currentTool.Rectangle = new RectangleD(mouseDown.X, mouseDown.Y, p.X - mouseDown.X, p.Y - mouseDown.Y);
            mousePoint = "(" + (p.X) + ", " + (p.Y) + ")";
            //pd = p;
            if (Tools.currentTool.type == Tools.Tool.Type.pointSel && (e.Event.State.HasFlag(ModifierType.ControlMask) && e.Event.State.HasFlag(ModifierType.Button1Mask)))
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
            {
                if (Tools.currentTool.type == Tools.Tool.Type.brush && Modifiers == ModifierType.Button1Mask)
                {
                    Tools.Tool tool = Tools.currentTool;
                    Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(SelectedBuffer);
                    g.pen = new Bio.Graphics.Pen(Tools.DrawColor, (int)Tools.StrokeWidth, ImageView.SelectedImage.bitsPerPixel);
                    g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)Tools.StrokeWidth, (int)Tools.StrokeWidth), g.pen.color);
                    UpdateImage();
                    UpdateView();
                }
                else
                if (Tools.currentTool.type == Tools.Tool.Type.eraser && Modifiers == ModifierType.Button1Mask)
                {
                    Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                    Bio.Graphics.Pen pen = new Bio.Graphics.Pen(Tools.EraseColor, (int)Tools.StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
                    g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)Tools.StrokeWidth, (int)Tools.StrokeWidth), pen.color);
                    pen.Dispose();
                    App.viewer.UpdateImages();
                }
            }
            UpdateStatus();
            pd = p;
            if(Tools.currentTool.type == Tools.Tool.Type.select && Modifiers == ModifierType.Button1Mask)
            UpdateView();
        }
        public static PointD mouseDown;
        public static PointD mouseUp;
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
            if (e.Event.State == ModifierType.Button2Mask)
            {
                if (SelectedImage != null)
                    if (SelectedImage.isPyramidal)
                    {
                        PointD pf = new PointD(e.Event.X - mouseD.X, e.Event.Y - mouseD.Y);
                        PyramidalOrigin = new Point(PyramidalOrigin.X - (int)pf.X, PyramidalOrigin.Y - (int)pf.Y);
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
        
        PointD pd;
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
            pd = pointer;
            mouseDown = pd;
            mouseD = new PointD(((pointer.X - Origin.X) / SelectedImage.Volume.Width)*SelectedImage.SizeX,((pointer.Y - Origin.Y) / SelectedImage.Volume.Height) * SelectedImage.SizeY);
            
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
                    UpdateGUI();
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
                        if (an.Rect.ToRectangleF().IntersectsWith(new RectangleF((float)pointer.X, (float)pointer.Y,1,1)))
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
                Point s = new Point(SelectedImage.SizeX, SelectedImage.SizeY);
                if ((mouseD.X < s.X && (mouseD.Y < s.Y) || (mouseD.X >= 0 && (mouseD.Y >= 0))))
                {
                    int zc = SelectedImage.Coordinate.Z;
                    int cc = SelectedImage.Coordinate.C;
                    int tc = SelectedImage.Coordinate.T;
                    if (SelectedImage.isPyramidal)
                    {
                        int x = (int)pointer.X;
                        int y = (int)pointer.Y;
                        if (SelectedImage.isRGB)
                        {
                            
                            if (x < SelectedImage.SizeX && y < SelectedImage.SizeY)
                            {
                                int r = SelectedImage.GetValueRGB(zc, RChannel.Index, tc, (int)mouseD.X, (int)mouseD.Y, 0);
                                int g = SelectedImage.GetValueRGB(zc, GChannel.Index, tc, (int)mouseD.X, (int)mouseD.Y, 1);
                                int b = SelectedImage.GetValueRGB(zc, BChannel.Index, tc, (int)mouseD.X, (int)mouseD.Y, 2);
                                mouseColor = ", " + r + "," + g + "," + b;
                            }
                            else
                                mouseColor = "";
                        }
                        else
                        {
                            if (x < SelectedImage.SizeX && y < SelectedImage.SizeY)
                            {
                                int r = SelectedImage.GetValueRGB(zc, (int)cBar.Value, tc, x, y, 0);
                                mouseColor = ", " + r;
                            }
                            else
                                mouseColor = "";
                        }
                    }
                    else
                    {
                        if (SelectedImage.isRGB)
                        {
                            int r = SelectedImage.GetValueRGB(zc, RChannel.Index, tc, (int)mouseD.X, (int)mouseD.Y, 0);
                            int g = SelectedImage.GetValueRGB(zc, GChannel.Index, tc, (int)mouseD.X, (int)mouseD.Y, 1);
                            int b = SelectedImage.GetValueRGB(zc, BChannel.Index, tc, (int)mouseD.X, (int)mouseD.Y, 2);
                            mouseColor = ", " + r + "," + g + "," + b;
                        }
                        else
                        {
                            int r = SelectedImage.GetValueRGB(zc, (int)cBar.Value, tc, (int)mouseD.X, (int)mouseD.Y, 0);
                            mouseColor = ", " + r;
                        }
                    }
                }
            }
            UpdateStatus();
            App.tools.ToolDown(mouseDown, e);
        }
        List<ROI> copys = new List<ROI>();
        public PointD ImageToViewSpace(double x,double y)
        {
            if(SelectedImage.isPyramidal)
            {
                return new PointD(PyramidalOrigin.X + x, PyramidalOrigin.Y + y);
            }
            double dx = ToViewW(SelectedImage.Volume.Width);
            double dy = ToViewH(SelectedImage.Volume.Height);
            PointD orig = new PointD(Origin.X - SelectedImage.Volume.Location.X, Origin.Y - SelectedImage.Volume.Location.Y);
            PointD diff = new PointD(ToViewW(orig.X), ToViewH(orig.Y));
            PointD f = new PointD((((x + diff.X)/ dx) * SelectedImage.Volume.Width),(((y + diff.Y) / dy) * SelectedImage.Volume.Height));
            PointD ff = new PointD(SelectedImage.Volume.Location.X + f.X, SelectedImage.Volume.Location.Y + f.Y);
            return ff;
        }
        public RectangleD ToViewSpace(RectangleD p)
        {
            PointD d = ToViewSpace(p.X, p.Y);
            double dx = ToScreenScaleW(p.W);
            double dy = ToScreenScaleH(p.H);
            return new RectangleD((float)d.X, (float)d.Y, (float)dx, (float)dy);
        }
        public PointF ToViewSpace(Point p)
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
            if (SelectedImage.isPyramidal)
            {
                return new PointD(x, y);
            }
            double dx = (ToViewSizeW(Origin.X - x)) * Scale.Width;
            double dy = (ToViewSizeH(Origin.Y - y)) * Scale.Height;
            return new PointD(dx, dy);
        }
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
        private double ToViewSizeW(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d;
            }
            double x = (double)(d / PxWmicron) * Scale.Width;
            return x;
        }
        public double ToViewSizeH(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d;
            }
            double y = (double)(d / PxHmicron) * Scale.Width;
            return y;
        }
        public double ToViewW(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d;
            }
            double x = (double)(d / PxWmicron) * Scale.Width;
            return x;
        }
        public double ToViewH(double d)
        {
            if (SelectedImage.isPyramidal)
            {
                return d;
            }
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
            if (SelectedImage.isPyramidal)
            {
                return (double)x;
            }
            return (x * PxWmicron) * Scale.Width;
        }
        public double ToScreenScaleH(double y)
        {
            if (SelectedImage.isPyramidal)
            {
                return (double)y;
            }
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
            if(pictureBox.AllocatedWidth == 1 || pictureBox.AllocatedHeight== 1) { return; }
            if (Images.Count <= i)
                return;
            double dx = Images[i].Volume.Width / 2;
            double dy = Images[i].Volume.Height / 2;
            Origin = new PointD((Images[i].Volume.Location.X), (Images[i].Volume.Location.Y));
            PxWmicron = Images[i].physicalSizeX;
            PxHmicron = Images[i].physicalSizeY;
            if (Images[i].SizeX > 1080)
            {
                double w = (double)SelectedImage.SizeX / (double)pictureBox.AllocatedWidth;
                double h = (double)SelectedImage.SizeY / (double)pictureBox.AllocatedHeight;
                PxWmicron *= h;
                PxHmicron *= h;
            }
            UpdateView();
        }

    }
}
