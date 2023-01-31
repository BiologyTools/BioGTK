using AForge.Imaging.Filters;
using AForge.Imaging;
using AForge.Math.Geometry;
using AForge;
using Gtk;
using Bio;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bio.Graphics;
using System.Collections;
using Gdk;
using java.awt;

namespace BioGTK
{
    /// <summary> Example Test Form for GTKSharp and Glade. </summary>
    public class Tools : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
#pragma warning disable 649

        [Builder.Object]
        private Gtk.Grid grid;
        [Builder.Object]
        private Gtk.Image selectRect;
        [Builder.Object]
        private Gtk.Image selectPoint;
        [Builder.Object]
        private Gtk.Image circle;
        [Builder.Object]
        private Gtk.Image hand;
        [Builder.Object]
        private Gtk.Image point;
        [Builder.Object]
        private Gtk.Image rect;
        [Builder.Object]
        private Gtk.Image poly;
        [Builder.Object]
        private Gtk.Image line;
        [Builder.Object]
        private Gtk.Image text;
        [Builder.Object]
        private Gtk.Image remove;
        [Builder.Object]
        private Gtk.Image magic;
        [Builder.Object]
        private Gtk.Image freeform;
        [Builder.Object]
        private Gtk.Image fill;
        [Builder.Object]
        private Gtk.Image brush;
        [Builder.Object]
        private Gtk.Image dropper;
        [Builder.Object]
        private Gtk.Image eraser;
        [Builder.Object]
        private Gtk.Image switchColor;
        [Builder.Object]
        private Gtk.DrawingArea color1;
        [Builder.Object]
        private Gtk.DrawingArea color2;

#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static Tools Create()
        {
            try
            {
                Builder builder = new Builder(null, "BioGTK.Glade.Tools.glade", null);
                return new Tools(builder, builder.GetObject("tools").Handle);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            return null;
        }

        protected Tools(Builder builder, IntPtr handle) : base(handle)
        {
            selectColor.Red = 0;
            selectColor.Green = 0;
            selectColor.Blue = 150;
            selectColor.Alpha= 255;
            white.Blue = 255;
            white.Green = 255;
            white.Red = 255;
            white.Alpha = 255;
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();

            Tool.Init();
            ColorS col = new ColorS(ushort.MaxValue);
            //We initialize the tools
            currentTool = GetTool(Tool.Type.move);
            floodFiller = new QueueLinearFloodFiller(floodFiller);
        }

        #endregion

        public static bool applyToStack = false;
        public static ColorTool colorTool;
        public static bool rEnabled = true;
        public static bool gEnabled = true;
        public static bool bEnabled = true;
        public static ColorS drawColor = new ColorS(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
        public static ColorS eraseColor = new ColorS(0, 0, 0);
        private static int width = 1;

        public static System.Drawing.Rectangle selectionRectangle;
        public static Hashtable tools = new Hashtable();
        private AbstractFloodFiller floodFiller = null;
        public class Tool
        {
            public enum ToolType
            {
                color,
                annotation,
                select,
                function
            }
            public enum Type
            {
                pencil,
                brush,
                bucket,
                eraser,
                move,
                point,
                line,
                rect,
                ellipse,
                polyline,
                polygon,
                text,
                delete,
                freeform,
                rectSel,
                pointSel,
                pan,
                magic,
                script,
                dropper
            }

            public static void Init()
            {
                if (tools.Count == 0)
                {
                    foreach (Tool.Type tool in (Tool.Type[])Enum.GetValues(typeof(Tool.Type)))
                    {
                        tools.Add(tool.ToString(), new Tool(tool, new ColorS(0, 0, 0)));
                    }
                }
            }
            public List<System.Drawing.Point> Points;
            public ToolType toolType;
            private RectangleD rect;
            public RectangleD Rectangle
            {
                get { return rect; }
                set { rect = value; }
            }
            public RectangleF RectangleF
            {
                get { return new RectangleF((float)rect.X, (float)rect.Y, (float)rect.W, (float)rect.H); }
            }
            public string script;
            public Type type;
            public ColorS tolerance;
            public Tool()
            {
                tolerance = new ColorS(0, 0, 0);
            }
            public Tool(Type t)
            {
                type = t;
                tolerance = new ColorS(0, 0, 0);
            }
            public Tool(Type t, ColorS col)
            {
                type = t;
                tolerance = new ColorS(0, 0, 0);
            }
            public Tool(Type t, RectangleD r)
            {
                type = t;
                rect = r;
                tolerance = new ColorS(0, 0, 0);
            }
            public Tool(Type t, string sc)
            {
                type = t;
                script = sc;
                tolerance = new ColorS(0, 0, 0);
            }
            public override string ToString()
            {
                return type.ToString();
            }
        }
        public static Tool currentTool;
        public static ColorS DrawColor
        {
            get
            {
                return drawColor;
            }
            set
            {
                drawColor = value;
                RGBA rGBA = new RGBA();
                rGBA.Red = (double)drawColor.R / ushort.MaxValue;
                rGBA.Green = (double)drawColor.G / ushort.MaxValue;
                rGBA.Blue = (double)drawColor.B / ushort.MaxValue;
                rGBA.Alpha = 1.0;
                App.tools.color1.OverrideBackgroundColor(StateFlags.Normal, rGBA);
                App.tools.color1.QueueDraw();
            }
        }
        public static ColorS EraseColor
        {
            get
            {
                return eraseColor;
            }
            set
            {
                eraseColor = value;
                RGBA rGBA = new RGBA();
                rGBA.Red = (double)eraseColor.R / ushort.MaxValue;
                rGBA.Green = (double)eraseColor.G / ushort.MaxValue;
                rGBA.Blue = (double)eraseColor.B / ushort.MaxValue;
                rGBA.Alpha = 1.0;
                App.tools.color2.OverrideBackgroundColor(StateFlags.Normal, rGBA);
                App.tools.color2.QueueDraw();
            }
        }

        public static int StrokeWidth
        {
            get
            {
                return width;
            }
            set
            {
                width = value;
                App.tools.UpdateGUI();
            }
        }

        public static RectangleD selectionRect;
        public static TextInput ti = null;

        public static Tool GetTool(string name)
        {
            if (tools.ContainsKey(name))
                return (Tool)tools[name];
            else
                return GetTool(Tool.Type.move);
        }
        public static Tool GetTool(Tool.Type typ)
        {
            return (Tool)tools[typ.ToString()];
        }
        public void UpdateView()
        {
            App.viewer.UpdateView();
        }
        private void UpdateGUI()
        {
            if (ImageView.SelectedImage != null)
            {
                System.Drawing.Color c1 = ColorS.ToColor(EraseColor, ImageView.SelectedImage.Buffers[0].BitsPerPixel);
                System.Drawing.Color c2 = ColorS.ToColor(DrawColor, ImageView.SelectedImage.Buffers[0].BitsPerPixel);
                Gdk.RGBA rGBA = new Gdk.RGBA();
                rGBA.Red = c1.R;
                rGBA.Green = c1.G;
                rGBA.Blue = c1.B;
                rGBA.Alpha = c1.A;

                Gdk.RGBA rGBA2 = new Gdk.RGBA();
                rGBA2.Red = c2.R;
                rGBA2.Green = c2.G;
                rGBA2.Blue = c2.B;
                rGBA2.Alpha = c2.A;
                color1.OverrideBackgroundColor(StateFlags.Normal, rGBA);
                color2.OverrideBackgroundColor(StateFlags.Normal, rGBA2);
            }
        }

        public static ROI selectedROI = new ROI();
        public void ToolDown(PointD e, ButtonPressEventArgs buts)
        {
            if (App.viewer == null || currentTool == null || ImageView.SelectedImage == null)
                return;
            //TODO Scripting.UpdateState(Scripting.State.GetDown(e, buts));
            PointF p = new PointF((float)e.X, (float)e.Y);
            if (currentTool.type == Tool.Type.line && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 0)
                {
                    selectedROI = new ROI();
                    selectedROI.type = ROI.Type.Line;
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.coord = App.viewer.GetCoordinate();
                    ImageView.SelectedImage.Annotations.Add(selectedROI);
                }
            }
            else
            if (currentTool.type == Tool.Type.polygon && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 0)
                {
                    selectedROI = new ROI();
                    selectedROI.type = ROI.Type.Polygon;
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.coord = App.viewer.GetCoordinate();
                    ImageView.SelectedImage.Annotations.Add(selectedROI);
                }
                else
                {
                    //If we click on a point 1 we close this polygon
                    RectangleD d = new RectangleD(e.X, e.Y, ROI.selectBoxSize, ROI.selectBoxSize);
                    if (d.IntersectsWith(selectedROI.Point))
                    {
                        selectedROI.closed = true;
                        selectedROI = new ROI();
                    }
                    else
                    {
                        selectedROI.AddPoint(new PointD(e.X, e.Y));
                    }
                }
            }
            else
            if (currentTool.type == Tool.Type.freeform && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 0)
                {
                    selectedROI = new ROI();
                    selectedROI.type = ROI.Type.Freeform;
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.coord = App.viewer.GetCoordinate();
                    selectedROI.closed = true;
                    ImageView.SelectedImage.Annotations.Add(selectedROI);
                }
                else
                {
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                }
            }
            else
            if (currentTool.type == Tool.Type.rect && buts.Event.Button == 1)
            {
                selectedROI.type = ROI.Type.Rectangle;
                selectedROI.Rect = new RectangleD(e.X, e.Y, 1, 1);
                selectedROI.coord = App.viewer.GetCoordinate();
                ImageView.SelectedImage.Annotations.Add(selectedROI);
            }
            else
            if (currentTool.type == Tool.Type.ellipse && buts.Event.Button == 1)
            {
                selectedROI.type = ROI.Type.Ellipse;
                selectedROI.Rect = new RectangleD(e.X, e.Y, 1, 1);
                selectedROI.coord = App.viewer.GetCoordinate();
                ImageView.SelectedImage.Annotations.Add(selectedROI);
            }
            else
            if (currentTool.type == Tool.Type.delete && buts.Event.Button == 1)
            {
                for (int i = 0; i < ImageView.SelectedImage.Annotations.Count; i++)
                {
                    ROI an = ImageView.SelectedImage.Annotations[i];
                    if (an.BoundingBox.IntersectsWith(e.X, e.Y))
                    {
                        if (an.selectedPoints.Count == 0)
                        {
                            ImageView.SelectedImage.Annotations.Remove(an);
                            break;
                        }
                        else
                        if (an.selectedPoints.Count == 1 && !(an.type == ROI.Type.Polygon || an.type == ROI.Type.Polyline || an.type == ROI.Type.Freeform))
                        {
                            ImageView.SelectedImage.Annotations.Remove(an);
                            break;
                        }
                        else
                        {
                            if (an.type == ROI.Type.Polygon ||
                                an.type == ROI.Type.Polyline ||
                                an.type == ROI.Type.Freeform)
                            {
                                an.closed = false;
                                an.RemovePoints(an.selectedPoints.ToArray());
                                break;
                            }
                        }
                    }
                }
                
                UpdateView();
            }
            else
            if (currentTool.type == Tool.Type.text && buts.Event.Button == 1)
            {
                //ROI an = new ROI();
                selectedROI.type = ROI.Type.Label;
                selectedROI.AddPoint(new PointD(e.X, e.Y));
                selectedROI.coord = App.viewer.GetCoordinate();
                ti.ShowAll();
                ti.Run();
            }
            else
            if (buts.Event.Button == 2)
            {
                currentTool = GetTool(Tool.Type.pan);
            }
        }
        public void ToolUp(PointD e, ButtonReleaseEventArgs buts)
        {
            PointF p = new PointF((float)e.X, (float)e.Y);
            if (App.viewer == null || currentTool == null || ImageView.SelectedImage == null || selectedROI == null)
                return;
            //TODO Scripting.UpdateState(Scripting.State.GetUp(e, buts));
            if (currentTool.type == Tool.Type.pan && buts.Event.Button == 2)
                currentTool = GetTool(Tool.Type.move);
            if (currentTool.type == Tool.Type.point && buts.Event.Button == 1)
            {
                ROI an = new ROI();
                an.AddPoint(new PointD(e.X, e.Y));
                an.type = ROI.Type.Point;
                an.coord = App.viewer.GetCoordinate();
                ImageView.SelectedImage.Annotations.Add(an);
            }
            else
            if (currentTool.type == Tool.Type.line && selectedROI.type == ROI.Type.Line && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() > 0)
                {
                    selectedROI.UpdatePoint(new PointD(e.X, e.Y), 1);
                    selectedROI = new ROI();
                }
            }
            else
            if (currentTool.type == Tool.Type.rect && selectedROI.type == ROI.Type.Rectangle && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI = new ROI();
                }
            }
            else
            if (currentTool.type == Tool.Type.ellipse && selectedROI.type == ROI.Type.Ellipse && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI = new ROI();
                }
            }
            else
            if (currentTool.type == Tool.Type.freeform && selectedROI.type == ROI.Type.Freeform && buts.Event.Button == 1)
            {
                selectedROI = new ROI();
            }
            else
            if (currentTool.type == Tool.Type.rectSel && buts.Event.Button == 1)
            {
                ImageView.selectedAnnotations.Clear();
                RectangleF r = GetTool(Tool.Type.rectSel).RectangleF;
                foreach (ROI an in App.viewer.AnnotationsRGB)
                {
                    if (an.GetSelectBound(App.viewer.GetScale()).ToRectangleF().IntersectsWith(r))
                    {
                        an.selectedPoints.Clear();
                        ImageView.selectedAnnotations.Add(an);
                        an.selected = true;
                        RectangleF[] sels = an.GetSelectBoxes(App.viewer.Scale.Width);
                        for (int i = 0; i < sels.Length; i++)
                        {
                            if (sels[i].IntersectsWith(r))
                            {
                                an.selectedPoints.Add(i);
                            }
                        }
                    }
                    else
                        an.selected = false;
                }
                Tools.GetTool(Tools.Tool.Type.rectSel).Rectangle = new RectangleD(0, 0, 0, 0);
            }
            else
            if (Tools.currentTool.type == Tools.Tool.Type.magic && buts.Event.Button == 1)
            {
                /*
                PointD pf = new PointD(ImageView.mouseUp.X - ImageView.mouseDown.X, ImageView.mouseUp.Y - ImageView.mouseDown.Y);
                ZCT coord = App.viewer.GetCoordinate();

                System.Drawing.Rectangle r = new Rectangle((int)ImageView.mouseDown.X, (int)ImageView.mouseDown.Y, (int)(ImageView.mouseUp.X - ImageView.mouseDown.X), (int)(ImageView.mouseUp.Y - ImageView.mouseDown.Y));
                if (r.Width <= 2 || r.Height <= 2)
                    return;
                AForge.Bitmap bf = ImageView.SelectedImage.Buffers[ImageView.SelectedImage.Coords[coord.Z, coord.C, coord.T]].GetCropBuffer(r);
                Statistics[] sts = Statistics.FromBytes(bf);
                Statistics st = sts[0];
                AForge.Bitmap crop = bf;
                Threshold th;
                if (magicSel.Numeric)
                {
                    th = new Threshold((int)magicSel.Threshold);
                }
                else
                if (magicSel.Index == 2)
                    th = new Threshold((int)(st.Min + st.Mean));
                else
                if (magicSel.Index == 1)
                    th = new Threshold((int)st.Median);
                else
                    th = new Threshold(st.Min);
                th.ApplyInPlace(crop);
                Invert inv = new Invert();
                AForge.Bitmap det;
                if (bf.BitsPerPixel > 8)
                    det = AForge.Imaging.Image.Convert16bppTo8bpp((crop));
                else
                    det = crop;
                BlobCounter blobCounter = new BlobCounter();
                blobCounter.ProcessImage(det);
                Blob[] blobs = blobCounter.GetObjectsInformation();
                // create convex hull searching algorithm
                GrahamConvexHull hullFinder = new GrahamConvexHull();
                // lock image to draw on it
                // process each blob
                foreach (Blob blob in blobs)
                {
                    if (blob.Rectangle.Width < magicSel.Max && blob.Rectangle.Height < magicSel.Max)
                        continue;
                    List<IntPoint> leftPoints = new List<IntPoint>();
                    List<IntPoint> rightPoints = new List<IntPoint>();
                    List<IntPoint> edgePoints = new List<IntPoint>();
                    List<IntPoint> hull = new List<IntPoint>();
                    // get blob's edge points
                    blobCounter.GetBlobsLeftAndRightEdges(blob,
                        out leftPoints, out rightPoints);
                    edgePoints.AddRange(leftPoints);
                    edgePoints.AddRange(rightPoints);
                    // blob's convex hull
                    hull = hullFinder.FindHull(edgePoints);
                    PointD[] pfs = new PointD[hull.Count];
                    for (int i = 0; i < hull.Count; i++)
                    {
                        pfs[i] = new PointD(r.X + hull[i].X, r.Y + hull[i].Y);
                    }
                    ROI an = ROI.CreateFreeform(coord, pfs);
                    ImageView.SelectedImage.Annotations.Add(an);
                }
                */
            }
            else
            if (Tools.currentTool.type == Tools.Tool.Type.bucket && buts.Event.Button == 1)
            {
                ZCT coord = App.viewer.GetCoordinate();
                floodFiller.FillColor = DrawColor;
                floodFiller.Tolerance = currentTool.tolerance;
                floodFiller.Bitmap = ImageView.SelectedImage.Buffers[ImageView.SelectedImage.Coords[coord.C, coord.Z, coord.T]];
                floodFiller.FloodFill(new System.Drawing.Point((int)p.X, (int)p.Y));
                App.viewer.UpdateImages();
            }
            else
            if (Tools.currentTool.type == Tools.Tool.Type.dropper && buts.Event.Button == 1)
            {
                DrawColor = ImageView.SelectedBuffer.GetPixel((int)p.X, (int)p.Y);
                UpdateGUI();
            }
            UpdateView();
        }
        public void ToolMove(PointD e, MotionNotifyEventArgs buts)
        {
            if (App.viewer == null)
                return;
            //TO DO Scripting.UpdateState(Scripting.State.GetMove(e, buts));
            if ((Tools.currentTool.type == Tools.Tool.Type.pan && buts.Event.State == Gdk.ModifierType.Button1Mask) || buts.Event.State == Gdk.ModifierType.Button2Mask && !ImageView.SelectedImage.isPyramidal)
            {
                App.viewer.Origin = e;
                UpdateView();
            }
            if (ImageView.SelectedImage == null)
                return;
            PointF p = ImageView.SelectedImage.ToImageSpace(e);

            if (currentTool.type == Tool.Type.line && buts.Event.State == Gdk.ModifierType.Button1Mask)
            {
                selectedROI.UpdatePoint(new PointD(e.X, e.Y), 1);
                UpdateView();
            }
            else
            if (currentTool.type == Tool.Type.freeform && buts.Event.State == Gdk.ModifierType.Button1Mask)
            {
                if (selectedROI.GetPointCount() == 0)
                {
                    selectedROI.type = ROI.Type.Freeform;
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.coord = App.viewer.GetCoordinate();
                    selectedROI.closed = true;
                    ImageView.SelectedImage.Annotations.Add(selectedROI);
                }
                else
                {
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                }
                UpdateView();
            }
            else
            if (currentTool.type == Tool.Type.rect && selectedROI.type == ROI.Type.Rectangle)
            {
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI.Rect = new RectangleD(selectedROI.X, selectedROI.Y, e.X - selectedROI.X, e.Y - selectedROI.Y);
                    UpdateView();
                    return;
                }
            }
            else
            if (currentTool.type == Tool.Type.ellipse && selectedROI.type == ROI.Type.Ellipse)
            {
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI.Rect = new RectangleD(selectedROI.X, selectedROI.Y, e.X - selectedROI.X, e.Y - selectedROI.Y);
                    UpdateView();
                }
            }
            else
            if (currentTool.type == Tool.Type.rectSel && buts.Event.State == Gdk.ModifierType.Button1Mask)
            {
                PointD d = new PointD(e.X - ImageView.mouseDown.X, e.Y - ImageView.mouseDown.Y);
                Tools.GetTool(Tools.Tool.Type.rectSel).Rectangle = new RectangleD(ImageView.mouseDown.X, ImageView.mouseDown.Y, d.X, d.Y);
                RectangleF r = Tools.GetTool(Tools.Tool.Type.rectSel).RectangleF;
                foreach (ROI an in App.viewer.AnnotationsRGB)
                {
                    if (an.GetSelectBound(App.viewer.GetScale()).ToRectangleF().IntersectsWith(r))
                    {
                        an.selectedPoints.Clear();
                        ImageView.selectedAnnotations.Add(an);
                        an.selected = true;
                        RectangleF[] sels = an.GetSelectBoxes(App.viewer.Scale.Width);
                        for (int i = 0; i < sels.Length; i++)
                        {
                            if (sels[i].IntersectsWith(r))
                            {
                                an.selectedPoints.Add(i);
                            }
                        }
                    }
                    else
                        an.selected = false;
                }
            }
            else
            if (currentTool.type == Tool.Type.rectSel && buts.Event.State != Gdk.ModifierType.Button1Mask)
            {
                Tools.GetTool(Tools.Tool.Type.rectSel).Rectangle = new RectangleD(0, 0, 0, 0);
            }
            else
            if (ImageView.keyDown == Gdk.Key.Delete)
            {
                foreach (ROI an in ImageView.selectedAnnotations)
                {
                    if (an != null)
                    {
                        if (an.selectedPoints.Count == 0)
                        {
                            ImageView.SelectedImage.Annotations.Remove(an);
                        }
                        else
                        {
                            if (an.type == ROI.Type.Polygon ||
                                an.type == ROI.Type.Polyline ||
                                an.type == ROI.Type.Freeform)
                            {
                                an.closed = false;
                                an.RemovePoints(an.selectedPoints.ToArray());

                            }
                        }
                    }
                }
                UpdateView();
            }

            if (Tools.currentTool.type == Tools.Tool.Type.magic && buts.Event.State != Gdk.ModifierType.Button1Mask)
            {
                //First we draw the selection rectangle
                PointD d = new PointD(e.X - ImageView.mouseDown.X, e.Y - ImageView.mouseDown.Y);
                Tools.GetTool(Tools.Tool.Type.rectSel).Rectangle = new RectangleD(ImageView.mouseDown.X, ImageView.mouseDown.Y, d.X, d.Y);
                UpdateView();
            }

            if (buts.Event.State != Gdk.ModifierType.Button1Mask && currentTool.type == Tool.Type.eraser)
            {
                Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                Bio.Graphics.Pen pen = new Bio.Graphics.Pen(Tools.EraseColor, (int)Tools.StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
                g.FillEllipse(new System.Drawing.Rectangle((int)p.X, (int)p.Y, (int)width, (int)Tools.StrokeWidth), pen.color);
                pen.Dispose();
                App.viewer.UpdateImages();
            }
        }

        Gdk.RGBA selectColor = new Gdk.RGBA();
        Gdk.RGBA white = new Gdk.RGBA();

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            selectRect.File = "icons/select.png";
            selectPoint.File = "icons/pointselect.png";
            circle.File = "icons/circle.png";
            hand.File = "icons/hand.png";
            point.File = "icons/point.png";
            rect.File = "icons/rect.png";
            poly.File = "icons/poly.png";
            line.File = "icons/line.png";
            text.File = "icons/text.png";
            remove.File = "icons/del.png";
            magic.File = "icons/magic.png";
            freeform.File = "icons/freeform.png";
            fill.File = "icons/fill.png";
            brush.File = "icons/brush.png";
            dropper.File = "icons/dropper.png";
            eraser.File = "icons/eraser.png";
            switchColor.File = "icons/switch.png";
            this.ButtonPressEvent += Tools_ButtonPressEvent;
            ti = TextInput.Create();
            color1.Drawn += Color1_Drawn;
            color2.Drawn += Color2_Drawn;
            //Tools.ti.Response += Ti_Response;
        }

        private void Color2_Drawn(object o, DrawnArgs args)
        {
            RGBA r = new RGBA();
            r.Red = (double)EraseColor.R / ushort.MaxValue;
            r.Green = (double)EraseColor.G / ushort.MaxValue;
            r.Blue = (double)EraseColor.B / ushort.MaxValue;
            r.Alpha = 1.0;
            args.Cr.SetSourceRGBA(r.Red, r.Green, r.Blue, r.Alpha);
            args.Cr.Rectangle(new Cairo.Rectangle(0,0,color2.AllocatedWidth,color2.AllocatedWidth));
            args.Cr.Fill();
            args.Cr.Scale(1, 1);
            args.Cr.Paint();
            color2.OverrideBackgroundColor(StateFlags.Normal, r);
        }

        private void Color1_Drawn(object o, DrawnArgs args)
        {
            RGBA r = new RGBA();
            r.Red = (double)DrawColor.R / ushort.MaxValue;
            r.Green = (double)DrawColor.G / ushort.MaxValue;
            r.Blue = (double)DrawColor.B / ushort.MaxValue;
            r.Alpha = 1.0;
            args.Cr.SetSourceRGBA(r.Red, r.Green, r.Blue, r.Alpha);
            args.Cr.Rectangle(new Cairo.Rectangle(0, 0, color1.AllocatedWidth, color1.AllocatedWidth));
            args.Cr.Fill();
            args.Cr.Scale(1, 1);
            args.Cr.Paint();
            color1.OverrideBackgroundColor(StateFlags.Normal, r);
        }

        private void Tools_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gdk.Rectangle r = new Gdk.Rectangle((int)args.Event.X, (int)args.Event.Y, 1, 1);
            ResetColor();
            if (selectRect.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.rectSel);
                selectRect.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (selectPoint.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.pointSel);
                selectPoint.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (circle.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.ellipse);
                circle.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (hand.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.pan);
                hand.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (point.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.point);
                point.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (rect.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.rect);
                rect.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (poly.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.polygon);
                poly.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (line.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.line);
                line.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (text.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.text);
                text.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (remove.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.delete);
                remove.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (magic.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.magic);
                magic.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (freeform.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.freeform);
                freeform.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (fill.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.bucket);
                fill.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (brush.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.brush);
                brush.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (dropper.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.dropper);
                dropper.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }
            if (eraser.Allocation.IntersectsWith(r))
            {
                currentTool = GetTool(Tool.Type.eraser);
                eraser.OverrideBackgroundColor(StateFlags.Normal, selectColor);
            }

            if (switchColor.Allocation.IntersectsWith(r))
                SwitchClick();
            if (color1.Allocation.IntersectsWith(r))
                Color1Click();
            if (color2.Allocation.IntersectsWith(r))
                Color2Click();
        }
        public static bool colorOne = true;
        private void Color2Click()
        {
            if (App.color != null)
                if (App.color.Visible)
                return;
            App.color = ColorTool.Create(eraseColor);
            App.color.Show();
            color2.QueueDraw();
            colorOne = false;
        }
        private void Color1Click()
        {
            if(App.color!=null)
                if (App.color.Visible)
                return;
            App.color = ColorTool.Create(drawColor);
            App.color.Show();
            color1.QueueDraw();
            colorOne= true;
        }
        private void SwitchClick()
        {
            ColorS s = DrawColor;
            DrawColor = EraseColor;
            EraseColor = s;
        }

        private void ResetColor()
        {
            selectRect.OverrideBackgroundColor(StateFlags.Normal, white);
            selectPoint.OverrideBackgroundColor(StateFlags.Normal, white);
            circle.OverrideBackgroundColor(StateFlags.Normal, white);
            hand.OverrideBackgroundColor(StateFlags.Normal, white);
            point.OverrideBackgroundColor(StateFlags.Normal, white);
            rect.OverrideBackgroundColor(StateFlags.Normal, white);
            poly.OverrideBackgroundColor(StateFlags.Normal, white);
            line.OverrideBackgroundColor(StateFlags.Normal, white);
            text.OverrideBackgroundColor(StateFlags.Normal, white);
            remove.OverrideBackgroundColor(StateFlags.Normal, white);
            magic.OverrideBackgroundColor(StateFlags.Normal, white);
            freeform.OverrideBackgroundColor(StateFlags.Normal, white);
            fill.OverrideBackgroundColor(StateFlags.Normal, white);
            brush.OverrideBackgroundColor(StateFlags.Normal, white);
            dropper.OverrideBackgroundColor(StateFlags.Normal, white);
            eraser.OverrideBackgroundColor(StateFlags.Normal, white);
            switchColor.OverrideBackgroundColor(StateFlags.Normal, white);
        }

        #endregion

    }
}
