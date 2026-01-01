using AForge;
using AForge.Imaging;
using AForge.Imaging.Filters;
using Bio;
using Bio.Graphics;
using Cairo;
using CSScripting;
using Gdk;
using Gtk;
using ScottPlot.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;
using Color = Cairo.Color;
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
            selectColor = AForge.Color.FromArgb(0, 0, 150, 255);
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
                List<ROI> rs = new List<ROI>();
                foreach (var item in ImageView.SelectedImage.Annotations)
                {
                    if (item.Selected)
                        rs.Add(item);
                }
                if (rs.Count > 0)
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

        // Normalize bounding box (makes W/H >= 0) and update ROI.Points to match corners
        private void SyncPointsFromBoundingBox(ROI roi)
        {
            if (roi == null) return;
            RectangleD r = roi.BoundingBox;

            double left = Math.Min(r.X, r.X + r.W);
            double top = Math.Min(r.Y, r.Y + r.H);
            double right = Math.Max(r.X, r.X + r.W);
            double bottom = Math.Max(r.Y, r.Y + r.H);

            // update normalized bounding box
            roi.BoundingBox = new RectangleD(left, top, right - left, bottom - top);

            // Ensure Points list exists & has 4 corner points for rectangle/ellipse
            if (roi.Points == null) roi.Points = new List<PointD>();

            if (roi.Points.Count < 4)
            {
                roi.Points.Clear();
                roi.Points.Add(new PointD(left, top));      // 0: TL
                roi.Points.Add(new PointD(right, top));     // 1: TR
                roi.Points.Add(new PointD(right, bottom));  // 2: BR
                roi.Points.Add(new PointD(left, bottom));   // 3: BL
            }
            else
            {
                // Keep the index mapping consistent
                roi.UpdatePoint(new PointD(left, top), 0);
                roi.UpdatePoint(new PointD(right, top), 1);
                roi.UpdatePoint(new PointD(right, bottom), 2);
                roi.UpdatePoint(new PointD(left, bottom), 3);
            }
        }

        // Recompute bounding box from the ROI points (useful after moving points)
        private void SyncBoundingBoxFromPoints(ROI roi)
        {
            if (roi == null || roi.Points == null || roi.Points.Count == 0) return;

            double minx = double.MaxValue, miny = double.MaxValue;
            double maxx = double.MinValue, maxy = double.MinValue;

            foreach (var p in roi.Points)
            {
                if (p.X < minx) minx = p.X;
                if (p.Y < miny) miny = p.Y;
                if (p.X > maxx) maxx = p.X;
                if (p.Y > maxy) maxy = p.Y;
            }

            roi.BoundingBox = new RectangleD(minx, miny, maxx - minx, maxy - miny);
        }


        // -----------------------------
        // Fixed tool implementations
        // -----------------------------
        public void ToolDown(PointD e, ButtonPressEventArgs buts)
        {
            if (App.viewer == null || currentTool == null || ImageView.SelectedImage == null)
                return;

            Plugins.MouseDown(ImageView.SelectedImage, e, buts);
            Scripting.UpdateState(Scripting.State.GetDown(e, buts.Event.Button));

            switch (currentTool.type)
            {
                case Tool.Type.pan:
                    ToolDown_Pan(e, buts);
                    break;
                case Tool.Type.magic:
                    ToolDown_Magic(e, buts);
                    break;
                case Tool.Type.point:
                    ToolDown_Point(e, buts);
                    break;
                case Tool.Type.move:
                    ToolDown_Move(e, buts);
                    break;
                case Tool.Type.line:
                    ToolDown_Line(e, buts);
                    break;
                case Tool.Type.rect:
                    ToolDown_Rectangle(e, buts);
                    break;
                case Tool.Type.ellipse:
                    ToolDown_Ellipse(e, buts);
                    break;
                case Tool.Type.polygon:
                    ToolDown_Polygon(e, buts);
                    break;
                case Tool.Type.text:
                    ToolDown_Text(e, buts);
                    break;
                case Tool.Type.delete:
                    ToolDown_Delete(e, buts);
                    break;
                case Tool.Type.freeform:
                    ToolDown_Freeform(e, buts);
                    break;
                default:
                    break;
            }
        }

        public void ToolUp(PointD e, ButtonReleaseEventArgs buts)
        {
            if (App.viewer == null || currentTool == null || ImageView.SelectedImage == null)
                return;

            Plugins.MouseUp(ImageView.SelectedImage, e, buts);
            Scripting.UpdateState(Scripting.State.GetUp(e, buts.Event.Button));

            switch (currentTool.type)
            {
                case Tool.Type.pan:
                    ToolUp_Pan(e, buts);
                    break;
                case Tool.Type.move:
                    ToolUp_Move(e, buts);
                    break;
                case Tool.Type.magic:
                    ToolUp_Magic(e, buts);
                    break;
                case Tool.Type.line:
                    ToolUp_Line(e, buts);
                    break;
                case Tool.Type.rect:
                    ToolUp_Rectangle(e, buts);
                    break;
                case Tool.Type.ellipse:
                    ToolUp_Ellipse(e, buts);
                    break;
                case Tool.Type.polygon:
                    ToolUp_Polygon(e, buts);
                    break;
                case Tool.Type.text:
                    //ToolUp_Text(e, buts);
                    break;
                case Tool.Type.delete:
                    //
                    break;
                case Tool.Type.freeform:
                    ToolUp_Freeform(e, buts);
                    break;
                case Tool.Type.bucket:
                    ToolUp_Bucket(e, buts);
                    break;
                case Tool.Type.dropper:
                    ToolUp_Dropper(e, buts);
                    break;
                default:
                    break;
            }
        }

        public void ToolMove(PointD e, MotionNotifyEventArgs buts)
        {
            if (App.viewer == null)
                return;

            Plugins.MouseMove(ImageView.SelectedImage, e, buts);

            // Update scripting state using available button masks
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

            switch (currentTool.type)
            {
                case Tool.Type.pan:
                    ToolMove_Pan(e, buts);
                    break;
                case Tool.Type.move:
                    ToolMove_Move(e, buts);
                    break;
                case Tool.Type.magic:
                    ToolMove_Magic(e, buts);
                    break;
                case Tool.Type.line:
                    ToolMove_Line(e, buts);
                    break;
                case Tool.Type.rect:
                    ToolMove_Rectangle(e, buts);
                    break;
                case Tool.Type.ellipse:
                    ToolMove_Ellipse(e, buts);
                    break;
                case Tool.Type.polygon:
                    ToolMove_Polygon(e, buts);
                    break;
                case Tool.Type.text:
                    //ToolMove_Text(e, buts);
                    break;
                case Tool.Type.freeform:
                    ToolMove_Freeform(e, buts);
                    break;
                case Tool.Type.brush:
                    ToolMove_Brush(e, buts);
                    break;
                case Tool.Type.eraser:
                    ToolMove_Eraser(e, buts);
                    break;
                default:
                    break;
            }
        }

        // ============================================================================
        // 1. MOVE TOOL - Selection and Dragging (fixed)
        // ============================================================================
        public void ToolDown_Move(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.move || buts.Event.Button != 1)
                return;

            App.viewer.MouseDown = e;
            bool ctrl = buts.Event.State.HasFlag(ModifierType.ControlMask);

            selectedROI = null;

            // 1 — Vertex hit test on selected ROIs
            foreach (var ann in ImageView.SelectedImage.Annotations)
            {
                if (ann == null || !ann.Selected) continue;

                var boxes = ann.GetSelectBoxes(ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeX);
                if (boxes == null) continue;

                for (int i = 0; i < boxes.Length; i++)
                {
                    if (boxes[i].Contains(ann.coord, e))
                    {
                        selectedROI = ann;
                        if (ctrl)
                        {
                            if (ann.selectedPoints.Contains(i)) ann.selectedPoints.Remove(i);
                            else ann.selectedPoints.Add(i);
                        }
                        else
                        {
                            ann.selectedPoints.Clear();
                            ann.selectedPoints.Add(i);
                        }
                        UpdateView();
                        return;
                    }
                }
            }

            // 2 — Click on body of already-selected ROI
            foreach (var ann in ImageView.SelectedImage.Annotations)
            {
                if (ann != null && ann.Selected && ann.BoundingBox.Contains(ann.coord, e))
                {
                    selectedROI = ann;
                    UpdateView();
                    return;
                }
            }

            // 3 — Click on any ROI (select it)
            foreach (var ann in ImageView.SelectedImage.Annotations)
            {
                if (ann != null && ann.BoundingBox.Contains(ann.coord, e))
                {
                    if (!ctrl)
                    {
                        foreach (var other in ImageView.SelectedImage.Annotations)
                        {
                            if (other != ann)
                            {
                                other.Selected = false;
                                other.selectedPoints?.Clear();
                            }
                        }
                    }

                    ann.Selected = true;
                    ann.selectedPoints?.Clear();
                    selectedROI = ann;
                    UpdateView();
                    return;
                }
            }

            // 4 — Start selection rectangle
            if (!ctrl)
            {
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

            Tools.GetTool(Tools.Tool.Type.move).Rectangle = new RectangleD(e.X, e.Y, 0, 0);
            UpdateView();
        }
        public void ToolMove_Move(PointD e, MotionNotifyEventArgs buts)
        {
            if (currentTool.type != Tool.Type.move ||
                !buts.Event.State.HasFlag(ModifierType.Button1Mask))
                return;

            // Moving selected vertices/control points or ROIs
            if (selectedROI != null && selectedROI.Selected && selectedROI.selectedPoints != null)
            {
                PointD delta = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);

                // Rectangle / Ellipse special handling
                if (selectedROI.type == ROI.Type.Rectangle || selectedROI.type == ROI.Type.Ellipse)
                {
                    // If exactly one corner is selected -> resize by that corner
                    if (selectedROI.selectedPoints.Count == 1)
                    {
                        int pointIndex = selectedROI.selectedPoints[0];
                        RectangleD rect = selectedROI.BoundingBox;

                        // Work with normalized copy to make logic clearer
                        double left = rect.X;
                        double top = rect.Y;
                        double right = rect.X + rect.W;
                        double bottom = rect.Y + rect.H;

                        switch (pointIndex)
                        {
                            case 0: // Top-Left
                                left += delta.X;
                                top += delta.Y;
                                break;
                            case 1: // Top-Right
                                right += delta.X;
                                top += delta.Y;
                                break;
                            case 2: // Bottom-Right
                                right += delta.X;
                                bottom += delta.Y;
                                break;
                            case 3: // Bottom-Left
                                left += delta.X;
                                bottom += delta.Y;
                                break;
                            default:
                                break;
                        }

                        // Build and assign normalized bounding box
                        RectangleD newRect = new RectangleD(
                            Math.Min(left, right),
                            Math.Min(top, bottom),
                            Math.Abs(right - left),
                            Math.Abs(bottom - top)
                        );

                        selectedROI.BoundingBox = newRect;

                        // Important: keep points and bbox in sync
                        SyncPointsFromBoundingBox(selectedROI);
                        selectedROI.UpdateBoundingBox(); // if your ROI uses this to recalc derived fields

                        App.viewer.MouseDown = new PointD(e.X, e.Y);
                        App.viewer.UpdateView();
                        return;
                    }
                    else
                    {
                        // No corner selected -> move entire ROI by delta
                        for (int i = 0; i < selectedROI.Points.Count; i++)
                        {
                            selectedROI.UpdatePoint(
                                new PointD(selectedROI.Points[i].X + delta.X,
                                            selectedROI.Points[i].Y + delta.Y), i);
                        }

                        // Keep bounding box in sync with points
                        SyncBoundingBoxFromPoints(selectedROI);
                        selectedROI.UpdateBoundingBox();

                        App.viewer.MouseDown = new PointD(e.X, e.Y);
                        App.viewer.UpdateView();
                        return;
                    }
                }
                else
                {
                    // Other shapes: polygons, polylines, freeforms, lines, points
                    if (selectedROI.selectedPoints.Count == 0)
                    {
                        // Move all points
                        for (int i = 0; i < selectedROI.Points.Count; i++)
                        {
                            selectedROI.UpdatePoint(
                                new PointD(selectedROI.Points[i].X + delta.X,
                                          selectedROI.Points[i].Y + delta.Y), i);
                        }
                    }
                    else
                    {
                        // Move only selected points
                        foreach (int pointIndex in selectedROI.selectedPoints.ToArray())
                        {
                            if (pointIndex >= 0 && pointIndex < selectedROI.Points.Count)
                            {
                                PointD pt = selectedROI.Points[pointIndex];
                                selectedROI.UpdatePoint(
                                    new PointD(pt.X + delta.X, pt.Y + delta.Y), pointIndex);
                            }
                        }
                    }

                    // Keep bbox updated
                    SyncBoundingBoxFromPoints(selectedROI);
                    selectedROI.UpdateBoundingBox();

                    App.viewer.MouseDown = new PointD(e.X, e.Y);
                    App.viewer.UpdateView();
                    return;
                }
            }
            // Dragging entire selected ROI (no selectedPoints list)
            else if (selectedROI != null && selectedROI.Selected)
            {
                PointD delta = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);

                for (int i = 0; i < selectedROI.Points.Count; i++)
                {
                    selectedROI.UpdatePoint(
                        new PointD(selectedROI.Points[i].X + delta.X,
                                  selectedROI.Points[i].Y + delta.Y), i);
                }

                SyncBoundingBoxFromPoints(selectedROI);
                selectedROI.UpdateBoundingBox();

                App.viewer.MouseDown = new PointD(e.X, e.Y);
                App.viewer.UpdateView();
                return;
            }
            // Drawing / updating selection rectangle
            else
            {
                PointD d = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                RectangleD selRect = new RectangleD(
                    Math.Min(App.viewer.MouseDown.X, e.X),
                    Math.Min(App.viewer.MouseDown.Y, e.Y),
                    Math.Abs(d.X), Math.Abs(d.Y));

                Tools.GetTool(Tools.Tool.Type.move).Rectangle = selRect;

                bool controlHeld = buts.Event.State.HasFlag(ModifierType.ControlMask);

                // Select ROIs intersecting with rectangle
                foreach (ROI an in ImageView.SelectedImage.Annotations)
                {
                    if (an == null) continue;

                    RectangleD selBound = an.GetSelectBound(
                        ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeX,
                        ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeY);

                    if (selBound.IntersectsWith(selRect))
                    {
                        an.Selected = true;

                        if (!controlHeld)
                            an.selectedPoints?.Clear();

                        // Select points within rectangle
                        RectangleD[] sels = an.GetSelectBoxes(
                            ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeX);
                        if (sels != null)
                        {
                            for (int i = 0; i < sels.Length; i++)
                            {
                                if (sels[i].IntersectsWith(selRect))
                                {
                                    if (an.selectedPoints == null) an.selectedPoints = new List<int>();
                                    if (!an.selectedPoints.Contains(i))
                                        an.selectedPoints.Add(i);
                                }
                            }
                        }
                    }
                    else if (!controlHeld)
                    {
                        an.Selected = false;
                        an.selectedPoints?.Clear();
                    }
                }

                if (selectedROI != null)
                    selectedROI.UpdateBoundingBox();

                App.viewer.UpdateView();
            }
        }
        public void ToolUp_Move(PointD e, ButtonReleaseEventArgs buts)
        {
            if (currentTool.type != Tool.Type.move || buts.Event.Button != 1)
                return;

            // Clear selection rectangle
            Tools.GetTool(Tools.Tool.Type.move).Rectangle = new RectangleD(0, 0, 0, 0);
            selectedROI?.UpdateBoundingBox();
            App.viewer.UpdateView();
        }


        // ============================================================================
        // 2. POINT TOOL
        // ============================================================================
        public void ToolDown_Point(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.point || buts.Event.Button != 1)
                return;

            ROI an = new ROI();
            an.type = ROI.Type.Point;
            an.Points.Clear();
            an.Points.Add(new PointD(e.X, e.Y));
            an.coord = App.viewer.GetCoordinate();
            an.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
            an.Selected = true;
            selectedROI = an;
            AddROI(selectedROI);
            UpdateView();
        }

        // ============================================================================
        // 3. LINE TOOL
        // ============================================================================
        public void ToolDown_Line(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.line || buts.Event.Button != 1)
                return;

            selectedROI = new ROI();
            selectedROI.type = ROI.Type.Line;
            selectedROI.AddPoint(new PointD(e.X, e.Y));
            selectedROI.AddPoint(new PointD(e.X + ImageView.SelectedImage.PhysicalSizeX * ROI.selectBoxSize,
                                           e.Y + ImageView.SelectedImage.PhysicalSizeX * ROI.selectBoxSize));
            selectedROI.coord = App.viewer.GetCoordinate();
            selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
            AddROI(selectedROI);
        }

        public void ToolMove_Line(PointD e, MotionNotifyEventArgs buts)
        {
            if (currentTool.type != Tool.Type.line ||
                !buts.Event.State.HasFlag(ModifierType.Button1Mask))
                return;

            if (selectedROI != null)
            {
                if (selectedROI.selectedPoints.Count == 1)
                {
                    selectedROI.UpdatePoint(new PointD(e.X, e.Y), selectedROI.selectedPoints[0]);
                    selectedROI.UpdateBoundingBox();
                    UpdateView();
                }
                else
                {
                    selectedROI.UpdatePoint(new PointD(e.X, e.Y), 1);
                    selectedROI.UpdateBoundingBox();
                    UpdateView();
                }
            }
        }

        public void ToolUp_Line(PointD e, ButtonReleaseEventArgs buts)
        {
            if (currentTool.type != Tool.Type.line || buts.Event.Button != 1)
                return;

            if (selectedROI != null && selectedROI.GetPointCount() >= 2)
            {
                selectedROI.UpdatePoint(new PointD(e.X, e.Y), 1);
                selectedROI.UpdateBoundingBox();
                selectedROI = null;
                UpdateView();
            }
        }

        public void ToolDown_Rectangle(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.rect || buts.Event.Button != 1)
                return;

            selectedROI = new ROI();
            selectedROI.type = ROI.Type.Rectangle;
            selectedROI.BoundingBox = new RectangleD(e.X, e.Y, 0, 0); // Start with 0 size
            selectedROI.coord = App.viewer.GetCoordinate();
            selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
            AddROI(selectedROI);
        }

        public void ToolMove_Rectangle(PointD e, MotionNotifyEventArgs buts)
        {
            if (currentTool.type != Tool.Type.rect ||
                !buts.Event.State.HasFlag(ModifierType.Button1Mask))
                return;

            if (selectedROI != null && selectedROI.type == ROI.Type.Rectangle)
            {
                selectedROI.BoundingBox = RectangleDExtensions.FromCorners(
                    new PointD(selectedROI.BoundingBox.X, selectedROI.BoundingBox.Y),
                    new PointD(e.X, e.Y));
                UpdateView();
            }
        }

        public void ToolUp_Rectangle(PointD e, ButtonReleaseEventArgs buts)
        {
            if (currentTool.type != Tool.Type.rect || buts.Event.Button != 1)
                return;

            if (selectedROI != null && selectedROI.type == ROI.Type.Rectangle)
            {
                // Update final rectangle bounds
                selectedROI.BoundingBox = RectangleDExtensions.FromCorners(
                    new PointD(selectedROI.BoundingBox.X, selectedROI.BoundingBox.Y),
                    new PointD(e.X, e.Y));

                // Validate the ROI (might reject if too small)
                selectedROI.Validate();
                selectedROI = null;
                UpdateView();
            }
        }

        // ============================================================================
        // 5. ELLIPSE TOOL
        // ============================================================================
        public void ToolDown_Ellipse(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.ellipse || buts.Event.Button != 1)
                return;

            selectedROI = new ROI();
            selectedROI.type = ROI.Type.Ellipse;
            selectedROI.BoundingBox = new RectangleD(e.X, e.Y, 0, 0); // Start with 0 size
            selectedROI.coord = App.viewer.GetCoordinate();
            selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
            AddROI(selectedROI);
        }

        public void ToolMove_Ellipse(PointD e, MotionNotifyEventArgs buts)
        {
            if (currentTool.type != Tool.Type.ellipse ||
                !buts.Event.State.HasFlag(ModifierType.Button1Mask))
                return;

            if (selectedROI != null && selectedROI.type == ROI.Type.Ellipse)
            {
                selectedROI.BoundingBox = RectangleDExtensions.FromCorners(
                    new PointD(selectedROI.BoundingBox.X, selectedROI.BoundingBox.Y),
                    new PointD(e.X, e.Y));
                UpdateView();
            }
        }

        public void ToolUp_Ellipse(PointD e, ButtonReleaseEventArgs buts)
        {
            if (currentTool.type != Tool.Type.ellipse || buts.Event.Button != 1)
                return;

            if (selectedROI != null && selectedROI.type == ROI.Type.Ellipse)
            {
                // Update final ellipse bounds
                selectedROI.BoundingBox = RectangleDExtensions.FromCorners(
                    new PointD(selectedROI.BoundingBox.X, selectedROI.BoundingBox.Y),
                    new PointD(e.X, e.Y));

                // Validate the ROI (might reject if too small)
                selectedROI.Validate();
                selectedROI = null;
                UpdateView();
            }
        }

        // ============================================================================
        // 6. POLYGON TOOL
        // ============================================================================
        public void ToolDown_Polygon(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.polygon || buts.Event.Button != 1)
                return;

            if (selectedROI == null || selectedROI.type != ROI.Type.Polygon)
            {
                // Start new polygon
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Polygon;
                selectedROI.closed = false;
                selectedROI.AddPoint(new PointD(e.X, e.Y));
                selectedROI.coord = App.viewer.GetCoordinate();
                selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                AddROI(selectedROI);
                UpdateView();
            }
            else if (!selectedROI.closed)
            {
                // Check if clicking near first point to close polygon
                if (selectedROI.Points.Count >= 3)
                {
                    PointD firstPoint = selectedROI.Points[0];
                    double distance = Math.Sqrt(Math.Pow(e.X - firstPoint.X, 2) + Math.Pow(e.Y - firstPoint.Y, 2));
                    double threshold = 10.0; // Adjust based on your zoom/scale

                    if (distance < threshold)
                    {
                        // Close polygon
                        selectedROI.closed = true;
                        selectedROI.Selected = false;
                        selectedROI.UpdateBoundingBox();
                        selectedROI.Validate();
                        selectedROI = null;
                        UpdateView();
                        return;
                    }
                }


            }
            // Add new point to polygon
            selectedROI.AddPoint(new PointD(e.X, e.Y));
            selectedROI.UpdateBoundingBox();
            UpdateView();
        }

        private void ToolMove_Polygon(PointD e, MotionNotifyEventArgs buts)
        {
            if (currentTool.type != Tool.Type.polygon)
                return;

            // Update preview of next point while drawing polygon
            if (selectedROI != null && selectedROI.type == ROI.Type.Polygon && !selectedROI.closed)
            {
                // Store current mouse position for preview rendering
                // (Assuming you have a way to render a preview line from last point to cursor)
                UpdateView();
            }
        }
        private void ToolUp_Polygon(PointD e, ButtonReleaseEventArgs buts)
        {
            // Polygon tool doesn't need mouse up handling since it works on clicks
            // The tool completes either by clicking near the first point or by double-clicking
        }

        // ============================================================================
        // 7. FREEFORM TOOL
        // ============================================================================
        public void ToolDown_Freeform(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.freeform || buts.Event.Button != 1)
                return;

            if (selectedROI == null || selectedROI.type != ROI.Type.Polygon)
            {
                // Start new freeform
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Polygon;
                selectedROI.closed = false;
                selectedROI.AddPoint(new PointD(e.X, e.Y));
                selectedROI.coord = App.viewer.GetCoordinate();
                selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                AddROI(selectedROI);
                UpdateView();
            }
            else if (!selectedROI.closed)
            {
                // Check if clicking near first point to close freeform
                if (selectedROI.Points.Count >= 3)
                {
                    PointD firstPoint = selectedROI.Points[0];
                    double distance = Math.Sqrt(Math.Pow(e.X - firstPoint.X, 2) + Math.Pow(e.Y - firstPoint.Y, 2));
                    double threshold = 10.0; // Adjust based on your zoom/scale

                    if (distance < threshold)
                    {
                        // Close freeform
                        selectedROI.closed = true;
                        selectedROI.Selected = false;
                        selectedROI.UpdateBoundingBox();
                        selectedROI.Validate();
                        selectedROI = null;
                        UpdateView();
                        return;
                    }
                }


            }
            // Add new point to polygon
            selectedROI.AddPoint(new PointD(e.X, e.Y));
            selectedROI.UpdateBoundingBox();
            UpdateView();
        }

        public void ToolMove_Freeform(PointD e, MotionNotifyEventArgs buts)
        {
            if (selectedROI != null)
                if (currentTool.type == Tool.Type.freeform && buts.Event.State.HasFlag(ModifierType.Button1Mask))
                {
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.coord = App.viewer.GetCoordinate();
                    selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;
                }
        }

        public void ToolUp_Freeform(PointD e, ButtonReleaseEventArgs buts)
        {
            if (currentTool.type != Tool.Type.freeform || buts.Event.Button != 1)
                return;

            if (selectedROI != null)
            {
                selectedROI.UpdateBoundingBox();
                //selectedROI = null;
                UpdateView();
            }
        }

        // ============================================================================
        // 8. TEXT/LABEL TOOL
        // ============================================================================
        public void ToolDown_Text(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.text || buts.Event.Button != 1)
                return;

            selectedROI = new ROI();
            selectedROI.type = ROI.Type.Label;
            selectedROI.AddPoint(new PointD(e.X, e.Y));
            selectedROI.coord = App.viewer.GetCoordinate();
            selectedROI.serie = ImageView.SelectedImage.isPyramidal ? App.viewer.Level : ImageView.SelectedImage.series;

            // Show text input dialog
            ti = TextInput.Create();
            ti.ShowAll();
            ti.Run();

            AddROI(selectedROI);
            selectedROI.UpdateBoundingBox();
            UpdateView();
        }

        // ============================================================================
        // 9. DELETE TOOL
        // ============================================================================
        public void ToolDown_Delete(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.delete || buts.Event.Button != 1)
                return;

            for (int i = ImageView.SelectedImage.Annotations.Count - 1; i >= 0; i--)
            {
                ROI an = ImageView.SelectedImage.Annotations[i];
                if (an == null) continue;

                if (an.BoundingBox.IntersectsWith(new RectangleD(e.X, e.Y, 1, 1)))
                {
                    if (an.selectedPoints == null || an.selectedPoints.Count == 0)
                    {
                        // Delete entire ROI
                        ImageView.SelectedImage.Annotations.RemoveAt(i);
                        break;
                    }
                    else if (an.selectedPoints.Count == 1 &&
                             !(an.type == ROI.Type.Polygon ||
                               an.type == ROI.Type.Polyline ||
                               an.type == ROI.Type.Freeform))
                    {
                        // Delete ROI with single selected point (for non multi-point types)
                        ImageView.SelectedImage.Annotations.RemoveAt(i);
                        break;
                    }
                    else
                    {
                        // Delete selected points from polygon/polyline/freeform
                        if (an.type == ROI.Type.Polygon ||
                            an.type == ROI.Type.Polyline ||
                            an.type == ROI.Type.Freeform)
                        {
                            an.closed = false;
                            an.RemovePoints(an.selectedPoints.ToArray());
                            an.UpdateBoundingBox();
                            break;
                        }
                    }
                }
            }
            UpdateView();
        }

        // ============================================================================
        // 10. BRUSH TOOL
        // ============================================================================
        public void ToolDown_Brush(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.brush || buts.Event.Button != 1)
                return;

            PointD ip = ImageView.SelectedImage.ToImageSpace(e);
            if (ImageView.SelectedImage.isPyramidal)
                ip = new PointD(e.X, e.Y);

            Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
            g.pen = new Bio.Graphics.Pen(DrawColor, (int)StrokeWidth, ImageView.SelectedImage.bitsPerPixel);
            g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)StrokeWidth, (int)StrokeWidth), g.pen.color);

            App.viewer.UpdateImage();
            App.viewer.UpdateView();
        }

        public void ToolMove_Brush(PointD e, MotionNotifyEventArgs buts)
        {
            if (currentTool.type != Tool.Type.brush ||
                !buts.Event.State.HasFlag(ModifierType.Button1Mask))
                return;

            PointD ip = ImageView.SelectedImage.ToImageSpace(e);
            if (ImageView.SelectedImage.isPyramidal)
                ip = new PointD(e.X, e.Y);

            Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
            g.pen = new Bio.Graphics.Pen(DrawColor, (int)StrokeWidth, ImageView.SelectedImage.bitsPerPixel);
            g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)StrokeWidth, (int)StrokeWidth), g.pen.color);

            App.viewer.UpdateImages();
            App.viewer.UpdateView();
        }

        // ============================================================================
        // 11. ERASER TOOL
        // ============================================================================
        public void ToolDown_Eraser(PointD e, ButtonPressEventArgs buts)
        {
            if (currentTool.type != Tool.Type.eraser || buts.Event.Button != 1)
                return;

            PointD ip = ImageView.SelectedImage.ToImageSpace(e);
            if (ImageView.SelectedImage.isPyramidal)
                ip = new PointD(e.X, e.Y);

            Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
            Bio.Graphics.Pen pen = new Bio.Graphics.Pen(EraseColor, (int)StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
            g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)StrokeWidth, (int)StrokeWidth), pen.color);

            App.viewer.UpdateImages();
            App.viewer.UpdateView();
        }

        public void ToolMove_Eraser(PointD e, MotionNotifyEventArgs buts)
        {
            if (currentTool.type != Tool.Type.eraser ||
                !buts.Event.State.HasFlag(ModifierType.Button1Mask))
                return;

            PointD ip = ImageView.SelectedImage.ToImageSpace(e);
            if (ImageView.SelectedImage.isPyramidal)
                ip = new PointD(e.X, e.Y);

            Bio.Graphics.Graphics g = Bio.Graphics.Graphics.FromImage(ImageView.SelectedBuffer);
            Bio.Graphics.Pen pen = new Bio.Graphics.Pen(EraseColor, (int)StrokeWidth, ImageView.SelectedBuffer.BitsPerPixel);
            g.FillEllipse(new Rectangle((int)ip.X, (int)ip.Y, (int)StrokeWidth, (int)StrokeWidth), pen.color);

            App.viewer.UpdateImages();
            App.viewer.UpdateView();
        }

        // ============================================================================
        // 12. BUCKET/FILL TOOL
        // ============================================================================
        public void ToolUp_Bucket(PointD e, ButtonReleaseEventArgs buts)
        {
            if (currentTool.type != Tool.Type.bucket || buts.Event.Button != 1)
                return;

            PointD ip = ImageView.SelectedImage.ToImageSpace(e);

            if (ip.X >= ImageView.SelectedImage.SizeX || ip.Y >= ImageView.SelectedImage.SizeY)
                return;

            floodFiller.FillColor = DrawColor;
            floodFiller.Tolerance = tolerance;
            floodFiller.Bitmap = ImageView.SelectedBuffer;
            floodFiller.FloodFill(new AForge.Point((int)ip.X, (int)ip.Y));

            App.viewer.UpdateImages();
            App.viewer.UpdateView();
        }

        // ============================================================================
        // 13. DROPPER/COLOR PICKER TOOL
        // ============================================================================
        public void ToolUp_Dropper(PointD e, ButtonReleaseEventArgs buts)
        {
            if (currentTool.type != Tool.Type.dropper || buts.Event.Button != 1)
                return;

            PointD ip = ImageView.SelectedImage.ToImageSpace(e);

            if (ip.X >= 0 && ip.Y >= 0 && ip.X < ImageView.SelectedImage.SizeX && ip.Y < ImageView.SelectedImage.SizeY)
            {
                DrawColor = ImageView.SelectedBuffer.GetPixel((int)ip.X, (int)ip.Y);
                App.viewer.UpdateView();
            }
        }

        // ============================================================================
        // 14. PAN TOOL
        // ============================================================================
        // --------------------------------------------------------------------
        // PAN STATE
        // --------------------------------------------------------------------
        private bool isPanning;
        private double panStartX;
        private double panStartY;

        private PointD initialPanOrigin;
        private double initialPanScale;

        // Velocity (world units / second)
        private PointD panVelocity;
        private DateTime lastMoveTime;

        // Kinetic settings
        private const double PanFriction = 50.0;   // world units / s²
        private const double VelocityEpsilon = 0.0005;

        // DPI normalization
        private double dpiScale = 1.0;

        // Animation
        private bool kineticActive = true;

        private void UpdateView_Fast()
        {
            // Skip expensive UpdateImages() - just redraw with current state
            App.viewer.UpdateView(false,false);
        }


        // --------------------------------------------------------------------
        // Mouse Down
        // --------------------------------------------------------------------
        public void ToolDown_Pan(PointD e, ButtonPressEventArgs buts)
        {
            if (buts.Event.Button != 1 && buts.Event.Button != 2)
                return;
            if (ImageView.SelectedImage.isPyramidal)
            {
               App.viewer.renderManager.BeginInteraction();
            }

            currentTool = GetTool(Tool.Type.pan);
            isPanning = true;
            kineticActive = true;

            panVelocity = new PointD(0, 0);

            panStartX = buts.Event.X;
            panStartY = buts.Event.Y;

            initialPanScale = App.viewer.Resolution;

            initialPanOrigin = ImageView.SelectedImage.isPyramidal
                ? App.viewer.PyramidalOrigin
                : App.viewer.Origin;

            lastMoveTime = DateTime.UtcNow;
        }


        // --------------------------------------------------------------------
        // Mouse Move
        // --------------------------------------------------------------------
        public void ToolMove_Pan(PointD e, MotionNotifyEventArgs buts)
        {
            if (!isPanning || currentTool.type != Tool.Type.pan)
                return;

            if (!buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask) &&
                !buts.Event.State.HasFlag(Gdk.ModifierType.Button2Mask))
                return;

            double dxScreen = (buts.Event.X - panStartX) / dpiScale;
            double dyScreen = (buts.Event.Y - panStartY) / dpiScale;

            double dxWorld = dxScreen / initialPanScale;
            double dyWorld = dyScreen / initialPanScale;

            PointD newOrigin = new PointD(
                initialPanOrigin.X - dxWorld,
                initialPanOrigin.Y - dyWorld);

            newOrigin = ClampOrigin(newOrigin);

            SetOrigin(newOrigin);
            if (ImageView.SelectedImage.isPyramidal)
            {
                App.viewer.renderManager.ContinueInteraction();
                UpdateView_Fast();  // Lightweight update, no tile fetching
            }
            else
            {
                UpdateView();  // Normal path for non-pyramidal
            }

        }


        // --------------------------------------------------------------------
        // Mouse Up
        // --------------------------------------------------------------------
        public void ToolUp_Pan(PointD e, ButtonReleaseEventArgs buts)
        {
            if (!isPanning)
                return;

            isPanning = false;

            if (Math.Abs(panVelocity.X) > VelocityEpsilon ||
                Math.Abs(panVelocity.Y) > VelocityEpsilon)
            {
                kineticActive = true;
                StartKineticLoop();
            }
        }
        private void StartKineticLoop()
        {
            DateTime last = DateTime.UtcNow;

            GLib.Timeout.Add(16, () =>
            {
                if (!kineticActive)
                    return false;

                DateTime now = DateTime.UtcNow;
                double dt = (now - last).TotalSeconds;
                last = now;

                double speed = Math.Sqrt(
                    panVelocity.X * panVelocity.X +
                    panVelocity.Y * panVelocity.Y);

                if (speed < VelocityEpsilon)
                {
                    kineticActive = false;
                    return false;
                }

                // Apply friction
                double decel = PanFriction * dt;
                double scale = Math.Max(0, (speed - decel) / speed);

                panVelocity = new PointD(
                    panVelocity.X * scale,
                    panVelocity.Y * scale);

                PointD origin = GetOrigin();
                origin = new PointD(
                    origin.X + panVelocity.X * dt,
                    origin.Y + panVelocity.Y * dt);

                origin = ClampOrigin(origin);
                SetOrigin(origin);

                return true;
            });
        }
        private PointD ClampOrigin(PointD origin)
        {
            var img = ImageView.SelectedImage;
            var res0 = img.Resolutions[0];

            double viewW = App.viewer.ImageViewWidth / App.viewer.Resolution;
            double viewH = App.viewer.ImageViewHeight / App.viewer.Resolution;

            double minX = 0;
            double minY = 0;
            double maxX = res0.SizeX - viewW;
            double maxY = res0.SizeY - viewH;

            return new PointD(
                Math.Clamp(origin.X, minX, Math.Abs(maxX)),
                Math.Clamp(origin.Y, minY, Math.Abs(maxY)));
        }
        private PointD GetOrigin()
        {
            return ImageView.SelectedImage.isPyramidal
                ? App.viewer.PyramidalOrigin
                : App.viewer.Origin;
        }

        private void SetOrigin(PointD p)
        {
            if (ImageView.SelectedImage.isPyramidal)
                App.viewer.PyramidalOrigin = p;
            else
                App.viewer.Origin = p;
        }

        // ============================================================================
        // 14. Magic Wand TOOL
        // ============================================================================
        public void ToolDown_Magic(PointD e, ButtonPressEventArgs buts)
        {
            if (selectedROI == null)
            {
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Freeform;
                selectedROI.AddPoint(new PointD(e.X, e.Y));
                selectedROI.coord = App.viewer.GetCoordinate();
                if (ImageView.SelectedImage.isPyramidal)
                    selectedROI.serie = App.viewer.Level;
                else
                    selectedROI.serie = ImageView.SelectedImage.series;
                AddROI(selectedROI);
            }
            else
            {
                selectedROI.AddPoint(new PointD(e.X, e.Y));
            }
        }

        public void ToolMove_Magic(PointD e, MotionNotifyEventArgs buts)
        {
            //First we draw the selection rectangle
            PointD d = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
            Tools.GetTool(Tools.Tool.Type.move).Rectangle = new RectangleD(App.viewer.MouseDown.X, App.viewer.MouseDown.Y, d.X, d.Y);
            UpdateView();
        }

        public void ToolUp_Magic(PointD e, ButtonReleaseEventArgs buts)
        {
            if (!buts.Event.State.HasFlag(ModifierType.Button1Mask))
                return;
            // Define the rectangle from mouse coordinates
            RectangleD rectangle = new RectangleD(
                App.viewer.MouseDown.X,
                App.viewer.MouseDown.Y,
                Math.Abs(App.viewer.MouseUp.X - App.viewer.MouseDown.X),
                Math.Abs(App.viewer.MouseUp.Y - App.viewer.MouseDown.Y)
            );

            // Convert to image space coordinates
            AForge.RectangleF rectInImageSpace = ImageView.SelectedImage.ToImageSpace(rectangle);
            ZCT coord = App.viewer.GetCoordinate();
            Bitmap bitmap;

            // Check the number of RGB channels to choose filtering method
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

            // Crop the image to the selected rectangle
            bitmap.Crop(rectInImageSpace.ToRectangleInt());
            // Determine the threshold based on magicSelect settings
            Statistics[] st = Statistics.FromBytes(bitmap.Bytes, bitmap.SizeX, bitmap.Height, bitmap.RGBChannelsCount, bitmap.BitsPerPixel, bitmap.Stride, bitmap.PixelFormat);
            int threshold = magicSelect.Numeric ? magicSelect.Threshold : CalculateThreshold(magicSelect.Index, st[0]);
            BlobCounter blobCounter = new BlobCounter();
            OtsuThreshold th = new OtsuThreshold();
            bitmap.To8Bit();
            th.ApplyInPlace(bitmap);
            blobCounter.FilterBlobs = true;
            blobCounter.MinWidth = 2;
            blobCounter.MinHeight = 2;
            blobCounter.ProcessImage(bitmap);
            // Retrieve detected blobs
            Blob[] blobs = blobCounter.GetObjectsInformation();
            double pixelSizeX = ImageView.SelectedImage.PhysicalSizeX;
            double pixelSizeY = ImageView.SelectedImage.PhysicalSizeY;
            // Annotate detected blobs in the original image
            foreach (Blob blob in blobs)
            {
                AForge.RectangleD blobRectangle = new AForge.RectangleD(
                    blob.Rectangle.X * pixelSizeX,
                    blob.Rectangle.Y * pixelSizeY,
                    blob.Rectangle.Width * pixelSizeX,
                    blob.Rectangle.Height * pixelSizeY
                );
                // Calculate the location of the detected blob
                PointD location = new PointD(rectangle.X + blobRectangle.X, rectangle.Y + blobRectangle.Y);
                // Create and add ROI annotation
                ROI annotation = ROI.CreateRectangle(coord, location.X, location.Y, blobRectangle.W, blobRectangle.H);
                ImageView.SelectedImage.Annotations.Add(annotation);
            }
            App.viewer.UpdateView(true);
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

        public bool IsPanning { get; set; }

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
    // -----------------------------
    // RectangleD extensions / helpers
    // -----------------------------
    public static class RectangleDExtensions
    {
        // Handles negative width/height gracefully
        public static bool Contains(this RectangleD r, ZCT coord, PointD p)
        {
            if (App.viewer.GetCoordinate() == coord)
            {
                double left = Math.Min(r.X, r.X + r.W);
                double right = Math.Max(r.X, r.X + r.W);
                double top = Math.Min(r.Y, r.Y + r.H);
                double bottom = Math.Max(r.Y, r.Y + r.H);
                return p.X >= left && p.X <= right && p.Y >= top && p.Y <= bottom;
            }
            return false;
        }

        public static RectangleD FromCorners(PointD a, PointD b)
        {
            double x = Math.Min(a.X, b.X);
            double y = Math.Min(a.Y, b.Y);
            double w = Math.Abs(b.X - a.X);
            double h = Math.Abs(b.Y - a.Y);
            return new RectangleD(x, y, w, h);
        }
    }

}
