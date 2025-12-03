using AForge;
using AForge.Imaging;
using AForge.Imaging.Filters;
using Bio;
using Bio.Graphics;
using Cairo;
using Gdk;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using PointD = AForge.PointD;
using Rectangle = AForge.Rectangle;

namespace BioGTK
{
    /// <summary>
    /// Implementations for all tool types defined in Tools.cs
    /// </summary>
    public class Tools : Gtk.Window
    {
#pragma warning disable 649

        [Builder.Object]
        private Gtk.DrawingArea view;
#pragma warning restore 649
        private AForge.Color selectColor;
        private Builder _builder;
        /// It creates a new instance of the Tools class, which is a class that inherits from the
        /// Gtk.Window class
        /// 
        /// @return A new instance of the Tools class.
        public static Tools Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Tools.glade", FileMode.Open));
            return new Tools(builder, builder.GetObject("tools").Handle);
        }
        /* Initializing the tools. */
        protected Tools(Builder builder, IntPtr handle) : base(handle)
        {
            selectColor = AForge.Color.FromArgb(0,0,150,255);
            AForge.Color white = AForge.Color.FromArgb(255, 255, 255, 255);
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
            Tool.Init();
            ColorS col = new ColorS(ushort.MaxValue);
            //We initialize the tools
            currentTool = GetTool(Tool.Type.move.ToString());
            floodFiller = new QueueLinearFloodFiller(floodFiller);
            magicSelect = MagicSelect.Create(0);
            App.ApplyStyles(this);
        }
        public static bool applyToStack = false;
        public static ColorTool colorTool;
        public static bool rEnabled = true;
        public static bool gEnabled = true;
        public static bool bEnabled = true;
        public static ColorS drawColor = new ColorS(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
        public static ColorS eraseColor = new ColorS(0, 0, 0);
        public static ColorS tolerance = new ColorS(0, 0, 0);
        private static MagicSelect magicSelect;
        private static int width = 5;
        private static ROI roi;
        public static ROI selectedROI
        {
            get
            {
                List<ROI> rs =  new List<ROI>();
                foreach (var item in ImageView.SelectedImage.Annotations)
                {
                    if(item.Selected)
                    rs.Add(item);
                }
                if(rs.Count>0)
                roi = rs[0];
                return roi;
            }
            set
            {
                roi = value;
            }
        }
        public static Rectangle selectionRectangle;
        public static Dictionary<string, Tool> tools = new Dictionary<string, Tool>();
        private static AbstractFloodFiller floodFiller = null;
        static List<ROI> rois = new List<ROI>();
        /* Creating a new ROI object called selectedROI. */
        public static List<ROI> selectedROIs
        {
            get
            {
                rois.Clear();
                for (int i = 0; i < Images.images.Count; i++)
                {
                    for (int r = 0; r < Images.images[i].Annotations.Count; r++)
                    {
                        if (Images.images[i].Annotations[r].Selected)
                            rois.Add(Images.images[i].Annotations[r]);
                    }
                }
                return rois;
            }
            set
            {
                rois.Clear();
                rois.AddRange(value);
            }
        }
        void AddROI(ROI roi)
        {
            if (ImageView.SelectedImage.isPyramidal)
                roi.serie = App.viewer.Level;
            else
                roi.serie = ImageView.SelectedImage.series;
            roi.coord = App.viewer.GetCoordinate();
            selectedROIs.Add(roi);
            roi.UpdateBoundingBox();
            ImageView.SelectedImage.Annotations.Add(roi);
        }
        /// <summary> Sets up the handlers. </summary>
        /// It sets up the handlers for the buttons
        protected void SetupHandlers()
        {
            this.ButtonPressEvent += Tools_ButtonPressEvent;
            ti = TextInput.Create();
            view.Drawn += View_Drawn;
            view.DeleteEvent += View_DeleteEvent;
        }
        private void View_DeleteEvent(object o, DeleteEventArgs args)
        {
            this.Hide();
            args.RetVal = true;
        }

        static int gridW = 2;
        static int gridH = 11;

        static List<Rectangle> rects = new List<Rectangle>();
        /// It draws the tools in the toolbox
        /// 
        /// @param o The object that the event is being called from.
        /// @param DrawnArgs This is a class that contains the Cairo SKCanvas and the Gdk Window.
        private void View_Drawn(object o, DrawnArgs e)
        {
            int w = 33;
            int h = 33;
            int x = 0;
            int y = 0;
            foreach (Tool item in tools.Values)
            {
                if (item.image != null)
                {
                    e.Cr.LineWidth = 2;
                    Pixbuf pf = item.image.ScaleSimple(w, h, InterpType.Bilinear);
                    if (item.selected)
                    {
                        e.Cr.SetSourceRGB(item.color.Rf, item.color.Gf, item.color.Bf);
                        e.Cr.Rectangle(x * w, y * h, w, h);
                        e.Cr.Stroke();
                    }
                    Gdk.CairoHelper.SetSourcePixbuf(e.Cr, pf, w * x, h * y);
                    e.Cr.Paint();
                    e.Cr.Stroke();

                }
                else
                {
                    e.Cr.SetSourceRGB(item.color.Rf, item.color.Gf, item.color.Bf);
                    e.Cr.Rectangle(x * w, y * h, w, h);
                    e.Cr.Fill();
                    e.Cr.Stroke();
                }
                if (x < gridW - 1)
                {
                    x += 1;
                }
                else
                {
                    y += 1;
                    x = 0;
                }
            }

        }

        /// It checks if the mouse click is within the bounds of a button, and if it is, it sets the
        /// current tool to the tool that the button represents
        /// 
        /// @param o The object that the event is being called from
        /// @param ButtonPressEventArgs The event that is triggered when a button is pressed.
        private void Tools_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gdk.Rectangle r = new Gdk.Rectangle((int)args.Event.X, (int)args.Event.Y, 1, 1);
            Tool tool = null;
            foreach (Tool t in tools.Values)
            {
                if (t.bounds.IntersectsWith(new AForge.PointD(args.Event.X, args.Event.Y)))
                {
                    tool = t;
                    //if (selectedROI != null)
                    //    selectedROI.Selected = false;
                    t.selected = true;
                }
                else
                    t.selected = false;
            }
            if (tool == null)
                return;
            currentTool = tool;
            if (tool.type == Tool.Type.color1)
                Color1Click();
            if (tool.type == Tool.Type.color2)
                Color2Click();
            if (tool.type == Tool.Type.switchColors)
                SwitchClick();
            view.QueueDraw();
        }
        public static bool colorOne = true;
        /// If the color tool is not visible, create it and show it
        /// 
        /// @return The color of the button.
        private void Color1Click()
        {
            if (App.color != null)
                if (App.color.Visible)
                    return;
            colorOne = true;
            App.color = ColorTool.Create(false);
            App.color.Show();
        }
        /// If the color tool is not visible, create it and show it
        private void Color2Click()
        {
            if (App.color != null)
                if (App.color.Visible)
                    return;
            colorOne = false;
            App.color = ColorTool.Create(true);
            App.color.Show();
        }
        /// It switches the color of the pen and the color of the eraser
        private void SwitchClick()
        {
            ColorS s = DrawColor;
            DrawColor = EraseColor;
            EraseColor = s;
        }
        private void UpdateView()
        {
            App.viewer.UpdateView();
        }
        // Replace the ToolDown, ToolUp, and ToolMove methods in Tools.cs with these fixed versions

        public void ToolDown(PointD e, ButtonPressEventArgs buts)
        {
            if (App.viewer == null || currentTool == null || ImageView.SelectedImage == null)
                return;

            Plugins.MouseDown(ImageView.SelectedImage, e, buts);
            Scripting.UpdateState(Scripting.State.GetDown(e, buts.Event.Button));

            PointF p = new PointF((float)e.X, (float)e.Y);

            // Move tool - selection and dragging
            if (currentTool.type == Tool.Type.move && buts.Event.Button == 1)
            {
                // Store starting position
                App.viewer.MouseDown = new PointD(e.X, e.Y);

                // Check if Control key is held for multi-selection
                bool controlHeld = buts.Event.State.HasFlag(ModifierType.ControlMask);

                // First check if clicking on a control point (vertex) of a selected ROI
                bool clickedOnVertex = false;
                foreach (var ann in ImageView.SelectedImage.Annotations)
                {
                    if (ann != null && ann.Selected)
                    {
                        RectangleD[] selectBoxes = ann.GetSelectBoxes(ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeX);
                        for (int i = 0; i < selectBoxes.Length; i++)
                        {
                            if (selectBoxes[i].IntersectsWith(new RectangleD(e.X, e.Y, 1, 1)))
                            {
                                // Clicking on a vertex
                                clickedOnVertex = true;
                                selectedROI = ann;

                                if (controlHeld)
                                {
                                    // Control held: toggle selection of this point
                                    if (ann.selectedPoints.Contains(i))
                                        ann.selectedPoints.Remove(i);
                                    else
                                        ann.selectedPoints.Add(i);
                                }
                                else
                                {
                                    // No control: select only this point
                                    ann.selectedPoints.Clear();
                                    ann.selectedPoints.Add(i);
                                }
                                break;
                            }
                        }
                        if (clickedOnVertex)
                            break;
                    }
                }

                // If not on vertex, check if clicking on an existing selected ROI's body
                bool clickedOnSelectedROI = false;
                if (!clickedOnVertex)
                {
                    foreach (var ann in ImageView.SelectedImage.Annotations)
                    {
                        if (ann != null && ann.Selected && ann.BoundingBox.IntersectsWith(new RectangleD(e.X, e.Y, 1, 1)))
                        {
                            clickedOnSelectedROI = true;
                            selectedROI = ann;
                            break;
                        }
                    }
                }

                // If not clicking on selected ROI or vertex, check if clicking on any ROI
                if (!clickedOnVertex && !clickedOnSelectedROI)
                {
                    bool clickedOnAnyROI = false;
                    foreach (var ann in ImageView.SelectedImage.Annotations)
                    {
                        if (ann != null && ann.BoundingBox.IntersectsWith(new RectangleD(e.X, e.Y, 1, 1)))
                        {
                            if (controlHeld)
                            {
                                // Control held: add to selection
                                ann.Selected = true;
                                ann.selectedPoints?.Clear();
                                selectedROI = ann;
                            }
                            else
                            {
                                // Clear other selections
                                foreach (var other in ImageView.SelectedImage.Annotations)
                                {
                                    if (other != null && other != ann)
                                    {
                                        other.Selected = false;
                                        other.selectedPoints?.Clear();
                                    }
                                }

                                // Select this ROI
                                ann.Selected = true;
                                ann.selectedPoints?.Clear();
                                selectedROI = ann;
                            }
                            clickedOnAnyROI = true;
                            break;
                        }
                    }

                    // If not clicking on any ROI, start new selection rectangle
                    if (!clickedOnAnyROI)
                    {
                        if (!controlHeld)
                        {
                            // Clear previous selections only if Control not held
                            foreach (var ann in ImageView.SelectedImage.Annotations)
                            {
                                if (ann != null)
                                {
                                    ann.Selected = false;
                                    ann.selectedPoints?.Clear();
                                }
                            }
                            selectedROI = null;
                        }

                        // Initialize selection rectangle
                        Tools.GetTool(Tools.Tool.Type.move).Rectangle = new RectangleD(e.X, e.Y, 0, 0);
                    }
                }

                UpdateView();
            }
            // Line tool
            else if (currentTool.type == Tool.Type.line && buts.Event.Button == 1)
            {
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Line;
                selectedROI.AddPoint(new PointD(e.X, e.Y));
                selectedROI.AddPoint(new PointD(e.X, e.Y));
                selectedROI.coord = App.viewer.GetCoordinate();
                selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                AddROI(selectedROI);
            }
            // Polygon tool
            else if (currentTool.type == Tool.Type.polygon && buts.Event.Button == 1)
            {
                if (selectedROI == null)
                {
                    selectedROI = new ROI();
                    selectedROI.type = ROI.Type.Polygon;
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.coord = App.viewer.GetCoordinate();
                    selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                    AddROI(selectedROI);
                }
                else
                {
                    RectangleD[] rds = selectedROI.GetSelectBoxes();
                    if (rds != null && rds.Length > 0 &&
                        rds[0].IntersectsWith(new RectangleD(e.X, e.Y, 1, 1)))
                    {
                        selectedROI.closed = true;
                        selectedROI.Selected = false;
                        selectedROI = null;
                        return;
                    }
                    else
                    {
                        if (!selectedROI.closed)
                            selectedROI.AddPoint(new PointD(e.X, e.Y));
                    }
                }
            }
            // Freeform tool
            else if (currentTool.type == Tool.Type.freeform && buts.Event.Button == 1)
            {
                if (selectedROI == null)
                {
                    selectedROI = new ROI();
                    selectedROI.type = ROI.Type.Freeform;
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.coord = App.viewer.GetCoordinate();
                    selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                    AddROI(selectedROI);
                }
                else
                {
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                }
            }
            // Rectangle tool
            else if (currentTool.type == Tool.Type.rect && buts.Event.Button == 1)
            {
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Rectangle;
                selectedROI.BoundingBox = new RectangleD(e.X, e.Y, 1, 1);
                selectedROI.coord = App.viewer.GetCoordinate();
                selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                AddROI(selectedROI);
            }
            // Ellipse tool
            else if (currentTool.type == Tool.Type.ellipse && buts.Event.Button == 1)
            {
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Ellipse;
                selectedROI.BoundingBox = new RectangleD(e.X, e.Y, 1, 1);
                selectedROI.coord = App.viewer.GetCoordinate();
                selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                AddROI(selectedROI);
            }
            // Delete tool
            else if (currentTool.type == Tool.Type.delete && buts.Event.Button == 1)
            {
                for (int i = 0; i < ImageView.SelectedImage.Annotations.Count; i++)
                {
                    ROI an = ImageView.SelectedImage.Annotations[i];
                    if (an == null) continue;
                    if (an.BoundingBox.IntersectsWith(new RectangleD(e.X, e.Y, 1, 1)))
                    {
                        if (an.selectedPoints == null || an.selectedPoints.Count == 0)
                        {
                            ImageView.SelectedImage.Annotations.Remove(an);
                            break;
                        }
                        else if (an.selectedPoints.Count == 1 && !(an.type == ROI.Type.Polygon || an.type == ROI.Type.Polyline || an.type == ROI.Type.Freeform))
                        {
                            ImageView.SelectedImage.Annotations.Remove(an);
                            break;
                        }
                        else
                        {
                            if (an.type == ROI.Type.Polygon || an.type == ROI.Type.Polyline || an.type == ROI.Type.Freeform)
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
            // Text tool
            else if (currentTool.type == Tool.Type.text && buts.Event.Button == 1)
            {
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Label;
                if (selectedROI.Points.Count == 0)
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                else
                    selectedROI.UpdatePoint(new PointD(e.X, e.Y), 0);
                selectedROI.coord = App.viewer.GetCoordinate();
                selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                ti = TextInput.Create();
                ti.ShowAll();
                ti.Run();
                AddROI(selectedROI);
            }
            // Brush tool
            else if (currentTool.type == Tool.Type.brush && buts.Event.Button == 1)
            {
                PointD ip = ImageView.SelectedImage.ToImageSpace(e);
                if (ImageView.SelectedImage.isPyramidal)
                    ip = new PointD(e.X, e.Y);

                Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                g.pen = new Bio.Graphics.Pen(DrawColor, (int)StrokeWidth, ImageView.SelectedImage.bitsPerPixel);
                g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)StrokeWidth, (int)StrokeWidth), g.pen.color);
                App.viewer.UpdateImage(true);
                App.viewer.UpdateView();
            }
            // Eraser tool
            else if (currentTool.type == Tool.Type.eraser && buts.Event.Button == 1)
            {
                PointD ip = ImageView.SelectedImage.ToImageSpace(e);
                if (ImageView.SelectedImage.isPyramidal)
                    ip = new PointD(e.X, e.Y);

                Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                Bio.Graphics.Pen pen = new Bio.Graphics.Pen(EraseColor, (int)StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
                g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)StrokeWidth, (int)StrokeWidth), pen.color);
                App.viewer.UpdateImages(true);
                App.viewer.UpdateView();
            }
            // Script tool
            else if (currentTool.type == Tool.Type.script && buts.Event.Button == 1)
            {
                if (!string.IsNullOrEmpty(currentTool.script))
                {
                    try
                    {
                        Scripting.RunScript(currentTool.script);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Script execution error: " + ex.Message);
                    }
                }
            }
            // Middle mouse button - pan
            else if (buts.Event.Button == 2)
            {
                currentTool = GetTool(Tool.Type.pan);
            }
        }

        public void ToolUp(PointD e, ButtonReleaseEventArgs buts)
        {
            Plugins.MouseUp(ImageView.SelectedImage, e, buts);
            PointD p = new PointD((float)e.X, (float)e.Y);
            PointD MouseU = ImageView.SelectedImage.ToImageSpace(p);
            PointD MouseD = ImageView.SelectedImage.ToImageSpace(new PointD(App.viewer.MouseDown.X, App.viewer.MouseDown.Y));

            if (App.viewer == null || currentTool == null || ImageView.SelectedImage == null)
                return;

            Scripting.UpdateState(Scripting.State.GetUp(e, buts.Event.Button));

            // Pan tool
            if (currentTool.type == Tool.Type.pan && buts.Event.Button == 2)
                currentTool = GetTool(Tool.Type.move);

            // Point tool
            if (currentTool.type == Tool.Type.point && buts.Event.Button == 1)
            {
                ROI an = new ROI();
                an.AddPoint(new PointD(e.X, e.Y));
                an.type = ROI.Type.Point;
                an.coord = App.viewer.GetCoordinate();
                an.Selected = true;
                selectedROI = an;
                AddROI(an);
            }
            // Bucket tool
            else if (currentTool.type == Tool.Type.bucket && buts.Event.Button == 1)
            {
                if (MouseU.X >= ImageView.SelectedImage.SizeX || MouseU.Y >= ImageView.SelectedImage.SizeY)
                    return;
                floodFiller.FillColor = DrawColor;
                floodFiller.Tolerance = tolerance;
                floodFiller.Bitmap = ImageView.SelectedBuffer;
                floodFiller.FloodFill(new AForge.Point((int)MouseU.X, (int)MouseU.Y));
                App.viewer.UpdateImages();
                App.viewer.UpdateView();
            }
            // Dropper tool
            else if (currentTool.type == Tool.Type.dropper && buts.Event.Button == 1)
            {
                if (MouseU.X < ImageView.SelectedImage.SizeX && MouseU.Y < ImageView.SelectedImage.SizeY)
                {
                    DrawColor = ImageView.SelectedBuffer.GetPixel((int)MouseU.X, (int)MouseU.Y);
                    App.viewer.UpdateView();
                }
            }
            // Magic select tool
            else if (currentTool.type == Tool.Type.magic && buts.Event.Button == 1)
            {
                RectangleD rectangle = new RectangleD(
                    Math.Min(App.viewer.MouseDown.X, App.viewer.MouseUp.X),
                    Math.Min(App.viewer.MouseDown.Y, App.viewer.MouseUp.Y),
                    Math.Abs(App.viewer.MouseUp.X - App.viewer.MouseDown.X),
                    Math.Abs(App.viewer.MouseUp.Y - App.viewer.MouseDown.Y)
                );

                AForge.RectangleF rectInImageSpace = ImageView.SelectedImage.ToImageSpace(rectangle);
                ZCT coord = App.viewer.GetCoordinate();
                Bitmap bitmap;

                if (ImageView.SelectedImage.Buffers[0].RGBChannelsCount > 1)
                {
                    bitmap = ImageView.SelectedImage.GetFiltered(
                        coord,
                        new IntRange((int)ImageView.SelectedBuffer.Stats[0].Min, (int)ImageView.SelectedBuffer.Stats[0].Max),
                        new IntRange((int)ImageView.SelectedBuffer.Stats[1].Min, (int)ImageView.SelectedBuffer.Stats[1].Max),
                        new IntRange((int)ImageView.SelectedBuffer.Stats[2].Min, (int)ImageView.SelectedBuffer.Stats[2].Max)
                    );
                }
                else
                {
                    bitmap = ImageView.SelectedImage.GetFiltered(
                        coord,
                        new IntRange((int)ImageView.SelectedBuffer.Stats[0].Min, (int)ImageView.SelectedBuffer.Stats[0].Max),
                        new IntRange((int)ImageView.SelectedBuffer.Stats[0].Min, (int)ImageView.SelectedBuffer.Stats[0].Max),
                        new IntRange((int)ImageView.SelectedBuffer.Stats[0].Min, (int)ImageView.SelectedBuffer.Stats[0].Max)
                    );
                }

                bitmap.Crop(rectInImageSpace.ToRectangleInt());
                Statistics[] st = Statistics.FromBytes(bitmap.Bytes, bitmap.SizeX, bitmap.Height, bitmap.RGBChannelsCount, bitmap.BitsPerPixel, bitmap.Stride, bitmap.PixelFormat);
                int threshold = magicSelect.Numeric ? magicSelect.Threshold : CalculateThreshold(magicSelect.Index, st[0]);

                BlobCounter blobCounter = new BlobCounter();
                OtsuThreshold th = new OtsuThreshold();
                th.ApplyInPlace(bitmap);
                blobCounter.FilterBlobs = true;
                blobCounter.MinWidth = 2;
                blobCounter.MinHeight = 2;
                blobCounter.ProcessImage(bitmap);

                Blob[] blobs = blobCounter.GetObjectsInformation();
                double pixelSizeX = ImageView.SelectedImage.PhysicalSizeX;
                double pixelSizeY = ImageView.SelectedImage.PhysicalSizeY;

                foreach (Blob blob in blobs)
                {
                    AForge.RectangleD blobRectangle = new AForge.RectangleD(
                        blob.Rectangle.X * pixelSizeX,
                        blob.Rectangle.Y * pixelSizeY,
                        blob.Rectangle.Width * pixelSizeX,
                        blob.Rectangle.Height * pixelSizeY
                    );

                    PointD location = new PointD(rectangle.X + blobRectangle.X, rectangle.Y + blobRectangle.Y);
                    ROI annotation = ROI.CreateRectangle(coord, location.X, location.Y, blobRectangle.W, blobRectangle.H);
                    ImageView.SelectedImage.Annotations.Add(annotation);
                }

                // Clear magic selection rectangle
                Tools.GetTool(Tools.Tool.Type.move).Rectangle = new RectangleD(0, 0, 0, 0);
                App.viewer.UpdateView(true);
            }
            // Move tool - finalize selection
            else if (currentTool.type == Tool.Type.move && buts.Event.Button == 1)
            {
                // Clear selection rectangle
                Tools.GetTool(Tools.Tool.Type.move).Rectangle = new RectangleD(0, 0, 0, 0);
                App.viewer.UpdateView();
            }

            if (selectedROI == null)
                return;

            // Line tool completion
            if (currentTool.type == Tool.Type.line && selectedROI.type == ROI.Type.Line && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() > 0)
                {
                    selectedROI.UpdatePoint(new PointD(e.X, e.Y), 1);
                    selectedROI = null;
                }
            }
            // Rectangle tool completion
            else if (currentTool.type == Tool.Type.rect && selectedROI.type == ROI.Type.Rectangle && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI = null;
                }
            }
            // Ellipse tool completion
            else if (currentTool.type == Tool.Type.ellipse && selectedROI.type == ROI.Type.Ellipse && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI = null;
                }
            }
            // Freeform tool completion
            else if (currentTool.type == Tool.Type.freeform && selectedROI.type == ROI.Type.Freeform && buts.Event.Button == 1)
            {
                selectedROI = null;
            }
        }

        public void ToolMove(PointD e, MotionNotifyEventArgs buts)
        {
            if (App.viewer == null)
                return;

            Plugins.MouseMove(ImageView.SelectedImage, e, buts);

            // Update scripting state
            if (buts.Event.State.HasFlag(ModifierType.Button1Mask))
                Scripting.UpdateState(Scripting.State.GetMove(e, 1));
            else if (buts.Event.State.HasFlag(ModifierType.Button2Mask))
                Scripting.UpdateState(Scripting.State.GetMove(e, 2));
            else if (buts.Event.State.HasFlag(ModifierType.Button3Mask))
                Scripting.UpdateState(Scripting.State.GetMove(e, 3));
            else if (buts.Event.State.HasFlag(ModifierType.Button4Mask))
                Scripting.UpdateState(Scripting.State.GetMove(e, 4));
            else if (buts.Event.State.HasFlag(ModifierType.Button5Mask))
                Scripting.UpdateState(Scripting.State.GetMove(e, 5));
            else
                Scripting.UpdateState(Scripting.State.GetMove(e, 0));

            // Pan handling
            if ((currentTool.type == Tool.Type.pan && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask)) ||
                buts.Event.State.HasFlag(Gdk.ModifierType.Button2Mask))
            {
                if (ImageView.SelectedImage.isPyramidal)
                {
                    if (App.viewer.MouseMoveInt.X != 0 || App.viewer.MouseMoveInt.Y != 0)
                    {
                        App.viewer.PyramidalOriginTransformed = new PointD(
                            App.viewer.PyramidalOriginTransformed.X + (App.viewer.MouseDown.X - e.X),
                            App.viewer.PyramidalOriginTransformed.Y + (App.viewer.MouseDown.Y - e.Y)
                        );
                    }
                }
                else
                {
                    PointD pf = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                    App.viewer.Origin = new PointD(App.viewer.Origin.X + pf.X, App.viewer.Origin.Y + pf.Y);
                }
                App.viewer.MouseDown = new PointD(e.X, e.Y);
                UpdateView();
            }

            if (ImageView.SelectedImage == null)
                return;

            // Move tool logic
            if (currentTool.type == Tool.Type.move && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                // Check if we're moving selected vertices/control points
                if (selectedROI != null && selectedROI.Selected && selectedROI.selectedPoints != null)
                {
                    PointD delta = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);

                    // Special handling for Rectangle and Ellipse ROIs
                    if (selectedROI.type == ROI.Type.Rectangle || selectedROI.type == ROI.Type.Ellipse)
                    {
                        // Rectangle/Ellipse has 4 corner points (0=TL, 1=TR, 2=BR, 3=BL)
                        if (selectedROI.selectedPoints.Count == 1)
                        {
                            int pointIndex = selectedROI.selectedPoints[0];
                            RectangleD currentRect = selectedROI.BoundingBox;

                            // Modify rectangle based on which corner is being dragged
                            switch (pointIndex)
                            {
                                case 0: // Top-Left corner
                                    selectedROI.BoundingBox = new RectangleD(
                                        currentRect.X + delta.X,
                                        currentRect.Y + delta.Y,
                                        currentRect.W - delta.X,
                                        currentRect.H - delta.Y
                                    );
                                    break;
                                case 1: // Top-Right corner
                                    selectedROI.BoundingBox = new RectangleD(
                                        currentRect.X,
                                        currentRect.Y + delta.Y,
                                        currentRect.W + delta.X,
                                        currentRect.H - delta.Y
                                    );
                                    break;
                                case 2: // Bottom-Right corner
                                    selectedROI.BoundingBox = new RectangleD(
                                        currentRect.X,
                                        currentRect.Y,
                                        currentRect.W + delta.X,
                                        currentRect.H + delta.Y
                                    );
                                    break;
                                case 3: // Bottom-Left corner
                                    selectedROI.BoundingBox = new RectangleD(
                                        currentRect.X + delta.X,
                                        currentRect.Y,
                                        currentRect.W - delta.X,
                                        currentRect.H + delta.Y
                                    );
                                    break;
                            }
                        }
                        else
                        {
                            // Multiple points selected on rectangle/ellipse - move entire shape
                            for (int i = 0; i < selectedROI.Points.Count; i++)
                            {
                                selectedROI.UpdatePoint(new PointD(selectedROI.Points[i].X + delta.X, selectedROI.Points[i].Y + delta.Y), i);
                            }
                            selectedROI.UpdateBoundingBox();
                        }
                    }
                    else
                    {
                        // For other ROI types (polygon, polyline, freeform, line, point)
                        // Move only the selected control points
                        if(selectedROI.selectedPoints.Count == 0)
                        {
                            for (int i = 0; i < selectedROI.Points.Count; i++)
                            {
                                PointD currentPoint = selectedROI.Points[i];
                                if (i >= 0 && i < selectedROI.Points.Count)
                                {
                                    selectedROI.UpdatePoint(new PointD(currentPoint.X + delta.X, currentPoint.Y + delta.Y), i);
                                }
                            }
                        }
                        else
                            foreach (int pointIndex in selectedROI.selectedPoints)
                            {
                                if (pointIndex >= 0 && pointIndex < selectedROI.Points.Count)
                                {
                                    PointD currentPoint = selectedROI.Points[pointIndex];
                                    selectedROI.UpdatePoint(new PointD(currentPoint.X + delta.X, currentPoint.Y + delta.Y), pointIndex);
                                }
                            }
                    }

                    // Update mouse position for next delta calculation
                    App.viewer.MouseDown = new PointD(e.X, e.Y);
                }
                // Check if we're dragging an entire selected ROI
                else if (selectedROI != null && selectedROI.Selected)
                {
                    PointD delta = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);

                    // Move all points of the ROI
                    for (int i = 0; i < selectedROI.Points.Count; i++)
                    {
                        selectedROI.UpdatePoint(new PointD(selectedROI.Points[i].X + delta.X, selectedROI.Points[i].Y + delta.Y), i);
                    }

                    // Update mouse position for next delta calculation
                    App.viewer.MouseDown = new PointD(e.X, e.Y);
                }
                else
                {
                    // Drawing selection rectangle
                    PointD d = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                    RectangleD selRect = new RectangleD(
                        Math.Min(App.viewer.MouseDown.X, e.X),
                        Math.Min(App.viewer.MouseDown.Y, e.Y),
                        Math.Abs(d.X),
                        Math.Abs(d.Y)
                    );

                    Tools.GetTool(Tools.Tool.Type.move).Rectangle = selRect;

                    // Check if Control is held for additive selection
                    bool controlHeld = buts.Event.State.HasFlag(Gdk.ModifierType.ControlMask);

                    // Select ROIs that intersect with selection rectangle
                    foreach (ROI an in ImageView.SelectedImage.Annotations)
                    {
                        if (an == null) continue;

                        RectangleD selBound = an.GetSelectBound(
                            ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeX,
                            ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeY
                        );

                        if (selBound.IntersectsWith(selRect))
                        {
                            an.Selected = true;

                            if (!controlHeld)
                            {
                                // Without Control: replace selection
                                an.selectedPoints.Clear();
                            }
                            // With Control: keep existing selected points and add new ones

                            RectangleD[] sels = an.GetSelectBoxes(ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeX);
                            for (int i = 0; i < sels.Length; i++)
                            {
                                if (sels[i].IntersectsWith(selRect))
                                {
                                    if (!an.selectedPoints.Contains(i))
                                        an.selectedPoints.Add(i);
                                }
                            }
                        }
                        else if (!controlHeld)
                        {
                            // Without Control: deselect ROIs outside selection rectangle
                            an.Selected = false;
                            an.selectedPoints.Clear();
                        }
                    }
                }

                App.viewer.UpdateView();
            }
            // Line tool
            else if (currentTool.type == Tool.Type.line && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                if (selectedROI != null)
                {
                    selectedROI.UpdatePoint(new PointD(e.X, e.Y), 1);
                    UpdateView();
                }
            }
            // Freeform tool
            else if (currentTool.type == Tool.Type.freeform && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                if (selectedROI != null && selectedROI.GetPointCount() > 0)
                {
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    UpdateView();
                }
            }
            // Rectangle tool
            else if (currentTool.type == Tool.Type.rect && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                if (selectedROI != null && selectedROI.GetPointCount() == 4)
                {
                    selectedROI.BoundingBox = new RectangleD(selectedROI.X, selectedROI.Y, e.X - selectedROI.X, e.Y - selectedROI.Y);
                    UpdateView();
                }
            }
            // Ellipse tool
            else if (currentTool.type == Tool.Type.ellipse && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                if (selectedROI != null && selectedROI.type == ROI.Type.Ellipse && selectedROI.GetPointCount() == 4)
                {
                    selectedROI.BoundingBox = new RectangleD(selectedROI.X, selectedROI.Y, e.X - selectedROI.X, e.Y - selectedROI.Y);
                    UpdateView();
                }
            }
            // Brush tool
            else if (currentTool.type == Tool.Type.brush && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                PointD ip = ImageView.SelectedImage.ToImageSpace(e);
                if (ImageView.SelectedImage.isPyramidal)
                    ip = new PointD(e.X, e.Y);

                Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                g.pen = new Bio.Graphics.Pen(DrawColor, (int)StrokeWidth, ImageView.SelectedImage.bitsPerPixel);
                g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)StrokeWidth, (int)StrokeWidth), g.pen.color);
                App.viewer.UpdateImages(true);
                App.viewer.UpdateView();
            }
            // Eraser tool
            else if (currentTool.type == Tool.Type.eraser && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                PointD ip = ImageView.SelectedImage.ToImageSpace(e);
                if (ImageView.SelectedImage.isPyramidal)
                    ip = new PointD(e.X, e.Y);

                Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
                Bio.Graphics.Pen pen = new Bio.Graphics.Pen(EraseColor, (int)StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
                g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)StrokeWidth, (int)StrokeWidth), pen.color);
                App.viewer.UpdateImages(true);
                App.viewer.UpdateView();
            }
            // Magic tool - preview rectangle
            else if (currentTool.type == Tool.Type.magic && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                PointD d = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                Tools.GetTool(Tools.Tool.Type.move).Rectangle = new RectangleD(
                    Math.Min(App.viewer.MouseDown.X, e.X),
                    Math.Min(App.viewer.MouseDown.Y, e.Y),
                    Math.Abs(d.X),
                    Math.Abs(d.Y)
                );
                UpdateView();
            }
            // Delete key handling
            else if (ImageView.keyDown == Gdk.Key.Delete)
            {
                foreach (ROI an in ImageView.selectedAnnotations.ToArray())
                {
                    if (an == null) continue;
                    if (an.selectedPoints == null || an.selectedPoints.Count == 0)
                    {
                        ImageView.SelectedImage.Annotations.Remove(an);
                    }
                    else
                    {
                        if (an.type == ROI.Type.Polygon || an.type == ROI.Type.Polyline || an.type == ROI.Type.Freeform)
                        {
                            an.closed = false;
                            an.RemovePoints(an.selectedPoints.ToArray());
                        }
                    }
                }
                UpdateView();
            }
        }
        /* It's a class that defines a tool */
        public class Tool
        {
            static int gridW = 2;
            static int gridH = 11;
            /* Defining an enum. */
            public enum ToolType
            {
                color,
                annotation,
                select,
                function
            }
            public enum Type
            {
                pan,
                move,
                point,
                line,
                rect,
                ellipse,
                polygon,
                text,
                delete,
                freeform,
                magic,
                brush,
                bucket,
                eraser,
                dropper,
                switchColors,
                color1,
                color2,
                script,
            }

            /// It adds all the tools to the tools list
            public static void Init()
            {
                int w = 33;
                int h = 33;
                string s = System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/";
                tools.Clear();
                if (tools.Count == 0)
                {
                    int x = 0; int y = 0;
                    foreach (Tool.Type t in (Tool.Type[])Enum.GetValues(typeof(Tool.Type)))
                    {
                        if (t != Type.script)
                            if (t != Type.color1 && t != Type.color2)
                            {
                                Tool tool = new Tool(t, new ColorS(0, 0, 0));
                                tool.bounds = new Rectangle(x * w, y * h, w, h);
                                tool.image = new Pixbuf(s + "Images/" + tool.type.ToString() + ".png");
                                tools.Add(tool.ToString(), tool);
                            }
                            else
                            {
                                Tool tool = new Tool(t, new ColorS(0, 0, 0));
                                tool.bounds = new Rectangle(x * w, y * h, w, h);
                                tools.Add(tool.ToString(), tool);
                            }
                        x++;
                        if (x > Tools.gridW - 1)
                        {
                            x = 0;
                            y++;
                        }
                    }
                }
                DrawColor = new ColorS(ushort.MaxValue, ushort.MaxValue, ushort.MaxValue);
            }
            public List<AForge.Point> Points;
            public ToolType toolType;
            public bool selected;
            public Pixbuf image;
            public Rectangle bounds;
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
            public ColorS color;
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
                color = col;
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
        /* Setting the color of the drawing tool. */
        public static ColorS DrawColor
        {
            get
            {
                return drawColor;
            }
            set
            {
                drawColor = value;
                tools[Tool.Type.color1.ToString()].color = drawColor;
            }
        }
        /* Setting the color of the eraser. */
        public static ColorS EraseColor
        {
            get
            {
                return eraseColor;
            }
            set
            {
                eraseColor = value;
                tools[Tool.Type.color2.ToString()].color = eraseColor;
            }
        }

        /* A property that is used to set the width of the stroke. */
        public static int StrokeWidth
        {
            get
            {
                return width;
            }
            set
            {
                width = value;
            }
        }

        public static RectangleD selectionRect;
        public static TextInput ti = null;

        /// If the tools dictionary contains the name of the tool, return the tool, otherwise return the
        /// move tool
        /// 
        /// @param name The name of the tool to get.
        /// 
        /// @return The tool that is being returned is the tool that is being used.
        public static Tool GetTool(string name)
        {
            if (tools.ContainsKey(name))
                return (Tool)tools[name];
            else return tools[Tool.Type.move.ToString()];
        }
        public static Tool GetTool(Tool.Type t)
        {
            if (tools.ContainsKey(t.ToString()))
                return (Tool)tools[t.ToString()];
            else return tools.FirstOrDefault().Value;
        }
        /// <summary>
        /// Helper method to calculate threshold for magic select
        /// </summary>
        public static int CalculateThreshold(int index, Statistics stat)
        {
            return index switch
            {
                2 => (int)(stat.Min + stat.Mean),
                1 => (int)stat.Median,
                _ => (int)stat.Min
            };
        }
    }
}
