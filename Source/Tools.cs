using AForge.Imaging.Filters;
using AForge.Imaging;
using AForge.Math.Geometry;
using AForge;
using Gtk;
using Bio;
using System;
using System.Collections.Generic;
using Bio.Graphics;
using System.Collections;
using Gdk;
using Color = AForge.Color;
using Rectangle = AForge.Rectangle;
using System.IO;
using ScottPlot.Interactivity.UserActions;
using ij.plugin;
using static sun.awt.image.PixelConverter;
using CSScripting;

namespace BioGTK
{
    public class Tools : Gtk.Window
    {
        #region Properties

        private Builder _builder;
#pragma warning disable 649

        [Builder.Object]
        private Gtk.DrawingArea view;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
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
            magicSelect = MagicSelect.Create(0);
            App.ApplyStyles(this);
        }

        #endregion

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

        public static Rectangle selectionRectangle;
        public static Dictionary<string,Tool> tools = new Dictionary<string, Tool>();
        private AbstractFloodFiller floodFiller = null;
        /* It's a class that defines a tool */
        public class Tool
        {
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
                pointSel,
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
                select,
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
                        if(t != Type.script)
                        if(t != Type.color1 && t != Type.color2)
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
                        if(x > Tools.gridW-1)
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
            else
                return GetTool(Tool.Type.move);
        }
        /// It returns a Tool object from the tools dictionary, using the Tool.Type enum as the key
        /// 
        /// @param typ The type of tool to get.
        /// 
        /// @return The tool object.
        public static Tool GetTool(Tool.Type typ)
        {
            return (Tool)tools[typ.ToString()];
        }
        
        /// > UpdateView() is a function that updates the view of the viewer.
        public void UpdateView()
        {
            App.viewer.UpdateView();
        }

        /* Creating a new ROI object called selectedROI. */
        public static ROI selectedROI = null;
        void AddROI(ROI roi)
        {
            if (ImageView.SelectedImage.isPyramidal)
                roi.serie = App.viewer.Level;
            else
                roi.serie = ImageView.SelectedImage.series;
            
            ImageView.SelectedImage.Annotations.Add(roi);
        }
        /// The function is called when the user clicks the mouse button. 
        /// 
        /// The function checks the current tool and if it's a line, polygon, freeform, rectangle,
        /// ellipse, delete, or text tool, it does something. 
        /// 
        /// If the tool is a line tool, it creates a new ROI object and adds it to the list of
        /// annotations. 
        /// 
        /// If the tool is a polygon tool, it creates a new ROI object and adds it to the list of
        /// annotations. 
        /// 
        /// If the tool is a freeform tool, it creates a new ROI object and adds it to the list of
        /// annotations. 
        /// 
        /// If the tool is a rectangle tool, it creates a new ROI object and adds it to the list of
        /// annotations. 
        /// 
        /// If the tool is an ellipse tool, it creates a new ROI object and adds it to the list of
        /// annotations. 
        /// 
        /// @param PointD A point with double precision
        /// @param ButtonPressEventArgs 
        /// 
        /// @return The return type is void.
        public void ToolDown(PointD e, ButtonPressEventArgs buts)
        {
            if (App.viewer == null || currentTool == null || ImageView.SelectedImage == null)
                return;
            Plugins.MouseDown(ImageView.SelectedImage, e, buts);
            Scripting.UpdateState(Scripting.State.GetDown(e, buts.Event.Button));
            PointF p = new PointF((float)e.X, (float)e.Y);
            if (currentTool.type == Tool.Type.line && buts.Event.Button == 1)
            {
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Line;
                selectedROI.AddPoint(new PointD(e.X, e.Y));
                selectedROI.AddPoint(new PointD(e.X, e.Y));
                selectedROI.coord = App.viewer.GetCoordinate();
                if (ImageView.SelectedImage.isPyramidal)
                    selectedROI.serie = App.viewer.Level;
                else
                    selectedROI.serie = ImageView.SelectedImage.series;
                //ImageView.SelectedImage.Annotations.Add(selectedROI);
                AddROI(selectedROI);
            }
            else
            if (currentTool.type == Tool.Type.polygon && buts.Event.Button == 1)
            {
                if (selectedROI == null)
                {
                    selectedROI = new ROI();
                    selectedROI.type = ROI.Type.Polygon;
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
                    //If we click on a point 1 we close this polygon
                    float width = (float)App.viewer.ToScreenScaleW(ROI.selectBoxSize);
                    RectangleD[] rds = selectedROI.GetSelectBoxes();
                    if (rds[0].IntersectsWith(new RectangleD(e.X,e.Y, 1,1)))
                    {
                        selectedROI.closed = true;
                        selectedROI.Selected = false;
                        selectedROI = null;
                        return;
                    }
                    else
                    {
                        if(selectedROI.closed == false)
                        selectedROI.AddPoint(new PointD(e.X, e.Y));
                    }
                }
            }
            else
            if (currentTool.type == Tool.Type.freeform && buts.Event.Button == 1)
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
            else
            if (currentTool.type == Tool.Type.rect && buts.Event.Button == 1)
            {
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Rectangle;
                selectedROI.Rect = new RectangleD(e.X, e.Y, 1, 1);
                selectedROI.coord = App.viewer.GetCoordinate();
                if (ImageView.SelectedImage.isPyramidal)
                    selectedROI.serie = App.viewer.Level;
                else
                    selectedROI.serie = ImageView.SelectedImage.series;
                AddROI(selectedROI);
            }
            else
            if (currentTool.type == Tool.Type.ellipse && buts.Event.Button == 1)
            {
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Ellipse;
                selectedROI.Rect = new RectangleD(e.X, e.Y, 1, 1);
                selectedROI.coord = App.viewer.GetCoordinate();
                if (ImageView.SelectedImage.isPyramidal)
                    selectedROI.serie = App.viewer.Level;
                else
                    selectedROI.serie = ImageView.SelectedImage.series;
                AddROI(selectedROI);
            }
            else
            if (currentTool.type == Tool.Type.delete && buts.Event.Button == 1)
            {
                for (int i = 0; i < ImageView.SelectedImage.Annotations.Count; i++)
                {
                    ROI an = ImageView.SelectedImage.Annotations[i];
                    if (an.BoundingBox.IntersectsWith(new RectangleD(e.X, e.Y, 1,1)))
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
                selectedROI = new ROI();
                selectedROI.type = ROI.Type.Label;
                if (selectedROI.PointsD.Count == 0)
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                else
                    selectedROI.UpdatePoint(new PointD(e.X, e.Y), 0);
                selectedROI.coord = App.viewer.GetCoordinate();
                if (ImageView.SelectedImage.isPyramidal)
                    selectedROI.serie = App.viewer.Level;
                else
                    selectedROI.serie = ImageView.SelectedImage.series;
                ti = TextInput.Create();
                ti.ShowAll();
                ti.Run();
                AddROI(selectedROI);
            }
            else
            if (buts.Event.Button == 2)
            {
                currentTool = GetTool(Tool.Type.pan);
            }
        }
        /// The function is called when the mouse button is released
        /// 
        /// @param PointD A point with double precision
        /// @param ButtonReleaseEventArgs 
        /// 
        /// @return The return type is void.
        public void ToolUp(PointD e, ButtonReleaseEventArgs buts)
        {
            Plugins.MouseUp(ImageView.SelectedImage, e, buts);
            PointD p = new PointD((float)e.X, (float)e.Y);
            PointD MouseU = ImageView.SelectedImage.ToImageSpace(p);
            PointD MouseD = ImageView.SelectedImage.ToImageSpace(new PointD(App.viewer.MouseDown.X,App.viewer.MouseDown.Y));
            if (App.viewer == null || currentTool == null || ImageView.SelectedImage == null)
                return;
            Scripting.UpdateState(Scripting.State.GetUp(e, buts.Event.Button));
            if (currentTool.type == Tool.Type.pan && buts.Event.Button == 2)
                currentTool = GetTool(Tool.Type.move);
            if (currentTool.type == Tool.Type.point && buts.Event.Button == 1)
            {
                ROI an = new ROI();
                an.AddPoint(new PointD(e.X, e.Y));
                an.type = ROI.Type.Point;
                an.coord = App.viewer.GetCoordinate();
                an.Selected = true;
                selectedROI = an;
                //ImageView.SelectedImage.Annotations.Add(an);
                AddROI(an);
            }
            else
            if (Tools.currentTool.type == Tools.Tool.Type.bucket && buts.Event.Button == 1)
            {
                if (MouseU.X >= ImageView.SelectedImage.SizeX && MouseU.Y >= ImageView.SelectedImage.SizeY)
                    return;
                floodFiller.FillColor = DrawColor;
                floodFiller.Tolerance = new ColorS(0, 0, 0);
                floodFiller.Bitmap = ImageView.SelectedBuffer;
                floodFiller.FloodFill(new AForge.Point((int)MouseU.X, (int)MouseU.Y));
                App.viewer.UpdateImages();
                App.viewer.UpdateView();
            }
            else
            if (Tools.currentTool.type == Tools.Tool.Type.dropper && buts.Event.Button == 1)
            {
                if (MouseU.X < ImageView.SelectedImage.SizeX && MouseU.Y < ImageView.SelectedImage.SizeY)
                {
                    DrawColor = ImageView.SelectedBuffer.GetPixel((int)MouseU.X, (int)MouseU.Y);
                }
            }
            else
            if (Tools.currentTool.type == Tools.Tool.Type.magic && buts.Event.Button == 1)
            {
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
                        ImageView.SelectedBuffer.Stats[0].Range,
                        ImageView.SelectedBuffer.Stats[1].Range,
                        ImageView.SelectedBuffer.Stats[2].Range
                    );
                }
                else
                {
                    bitmap = ImageView.SelectedImage.GetFiltered(
                        coord,
                        ImageView.SelectedBuffer.Stats[0].Range,
                        ImageView.SelectedBuffer.Stats[0].Range,
                        ImageView.SelectedBuffer.Stats[0].Range
                    );
                }

                // Crop the image to the selected rectangle
                bitmap.Crop(rectInImageSpace.ToRectangleInt());
                // Determine the threshold based on magicSelect settings
                Statistics[] st = Statistics.FromBytes(bitmap.Bytes, bitmap.SizeX, bitmap.Height, bitmap.RGBChannelsCount, bitmap.BitsPerPixel, bitmap.Stride, bitmap.PixelFormat);
                int threshold = magicSelect.Numeric ? magicSelect.Threshold : CalculateThreshold(magicSelect.Index, st[0]);
                BlobCounter blobCounter = new BlobCounter();
                bitmap.BinarizeOtsu();
                blobCounter.FilterBlobs = true;
                blobCounter.MinWidth = 2;
                blobCounter.MaxHeight = 2;
                blobCounter.MaxWidth = bitmap.Width;
                blobCounter.MinHeight = bitmap.Height;
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
                    UpdateView();
                }

            }

            if (selectedROI == null)
                return;
            if (currentTool.type == Tool.Type.line && selectedROI.type == ROI.Type.Line && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() > 0)
                {
                    selectedROI.UpdatePoint(new PointD(e.X, e.Y), 1);
                    selectedROI = null;
                }
            }
            else
            if (currentTool.type == Tool.Type.rect && selectedROI.type == ROI.Type.Rectangle && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI = null;
                }
            }
            else
            if (currentTool.type == Tool.Type.ellipse && selectedROI.type == ROI.Type.Ellipse && buts.Event.Button == 1)
            {
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI = null;
                }
            }
            else
            if (currentTool.type == Tool.Type.freeform && selectedROI.type == ROI.Type.Freeform && buts.Event.Button == 1)
            {
                selectedROI = null;
            }
            
        }

        // Helper method to calculate threshold
        private static int CalculateThreshold(int index, Statistics stat)
        {
            return index switch
            {
                2 => (int)(stat.Min + stat.Mean),
                1 => (int)stat.Median,
                _ => (int)stat.Min
            };
        }
        /// This function is called when the mouse is moved. It is used to update the view when the user
        /// is panning, drawing a line, drawing a freeform, drawing a rectangle, drawing an ellipse,
        /// selecting a rectangle, deleting an annotation, drawing a magic wand selection, and erasing
        /// 
        /// @param PointD A point in the image space
        /// @param MotionNotifyEventArgs 
        /// 
        /// @return The return type is void.
        public void ToolMove(PointD e, MotionNotifyEventArgs buts)
        {
            if (App.viewer == null)
                return;
            Plugins.MouseMove(ImageView.SelectedImage, e, buts);
            if (buts.Event.State == ModifierType.Button1Mask)
                Scripting.UpdateState(Scripting.State.GetMove(e, 1));
            if (buts.Event.State == ModifierType.Button2Mask)
                Scripting.UpdateState(Scripting.State.GetMove(e, 2));
            if (buts.Event.State == ModifierType.Button3Mask)
                Scripting.UpdateState(Scripting.State.GetMove(e, 3));
            if (buts.Event.State == ModifierType.Button4Mask)
                Scripting.UpdateState(Scripting.State.GetMove(e, 4));
            if (buts.Event.State == ModifierType.Button5Mask)
                Scripting.UpdateState(Scripting.State.GetMove(e, 5));
            if (buts.Event.State == ModifierType.None)
                Scripting.UpdateState(Scripting.State.GetMove(e, 0));
            if ((Tools.currentTool.type == Tools.Tool.Type.pan && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask)) || buts.Event.State.HasFlag(Gdk.ModifierType.Button2Mask))
            {
                if (ImageView.SelectedImage.isPyramidal)
                {
                    if (App.viewer.MouseMoveInt.X != 0 || App.viewer.MouseMoveInt.Y != 0)
                    {
                        App.viewer.PyramidalOriginTransformed = new PointD(App.viewer.PyramidalOriginTransformed.X + (App.viewer.MouseDown.X - e.X), App.viewer.PyramidalOriginTransformed.Y + (App.viewer.MouseDown.Y - e.Y));
                    }
                }
                else
                {
                    PointD pf = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                    App.viewer.Origin = new PointD(App.viewer.Origin.X + pf.X, App.viewer.Origin.Y + pf.Y);
                }
                UpdateView();
            }
            if (ImageView.SelectedImage == null)
                return;
            if (Tools.currentTool.type == Tools.Tool.Type.magic && !buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                //First we draw the selection rectangle
                PointD d = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                Tools.GetTool(Tools.Tool.Type.select).Rectangle = new RectangleD(App.viewer.MouseDown.X, App.viewer.MouseDown.Y, d.X, d.Y);
                UpdateView();
            }
            if (currentTool.type == Tool.Type.move && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                if(selectedROI!=null)
                for (int i = 0; i < selectedROI.PointsD.Count; i++)
                {
                    PointD pd = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                    selectedROI.UpdatePoint(new PointD(selectedROI.PointsD[i].X + pd.X, selectedROI.PointsD[i].Y + pd.Y), i);
                }
                UpdateView();
            }
            if (currentTool.type == Tool.Type.line && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                selectedROI.UpdatePoint(new PointD(e.X, e.Y), 1);
                UpdateView();
            }
            else
            if (currentTool.type == Tool.Type.freeform && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                if (selectedROI.GetPointCount() == 0)
                {
                    selectedROI.type = ROI.Type.Freeform;
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                    selectedROI.coord = App.viewer.GetCoordinate();
                    selectedROI.closed = true;
                    //ImageView.SelectedImage.Annotations.Add(selectedROI);
                    AddROI(selectedROI);
                }
                else
                {
                    selectedROI.AddPoint(new PointD(e.X, e.Y));
                }
                UpdateView();
            }
            else
            if (currentTool.type == Tool.Type.rect)
            {
                if (selectedROI != null)
                if (selectedROI.GetPointCount() == 4)
                {
                    selectedROI.Rect = new RectangleD(selectedROI.X, selectedROI.Y, e.X - selectedROI.X, e.Y - selectedROI.Y);
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
            if (currentTool.type == Tool.Type.select && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                PointD d = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                Tools.GetTool(Tools.Tool.Type.select).Rectangle = new RectangleD(App.viewer.MouseDown.X, App.viewer.MouseDown.Y,Math.Abs(d.X),Math.Abs(d.Y));
                RectangleD r = Tools.GetTool(Tools.Tool.Type.select).Rectangle;
                List<ROI> rois = ImageView.SelectedImage.Annotations;
                foreach (ROI an in rois)
                {
                    if (an.GetSelectBound(ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeX, ROI.selectBoxSize * ImageView.SelectedImage.PhysicalSizeY).IntersectsWith(r))
                    {
                        an.selectedPoints.Clear();
                        an.Selected = true;
                        RectangleD[] sels = an.GetSelectBoxes();
                        for (int i = 0; i < sels.Length; i++)
                        {
                            if (sels[i].IntersectsWith(r))
                            {
                                an.selectedPoints.Add(i);
                            }
                        }
                    }
                }
                App.viewer.UpdateImages();
                App.viewer.UpdateView();
            }
            else if(currentTool.type == Tool.Type.brush)
            {
                App.viewer.UpdateImages();
                App.viewer.UpdateView();
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
            if (Tools.currentTool.type == Tools.Tool.Type.magic && buts.Event.State.HasFlag(Gdk.ModifierType.Button1Mask))
            {
                //First we draw the selection rectangle
                PointD d = new PointD(e.X - App.viewer.MouseDown.X, e.Y - App.viewer.MouseDown.Y);
                Tools.GetTool(Tools.Tool.Type.select).Rectangle = new RectangleD(App.viewer.MouseDown.X, App.viewer.MouseDown.Y, d.X, d.Y);
                UpdateView();
            }
        }

        Gdk.RGBA selectColor = new Gdk.RGBA();
        Gdk.RGBA white = new Gdk.RGBA();

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        /// It sets up the handlers for the buttons
        protected void SetupHandlers()
        {
            this.ButtonPressEvent += Tools_ButtonPressEvent;
            ti = TextInput.Create();
            view.Drawn += View_Drawn;
        }

        static int gridW = 2;
        static int gridH = 11;

        static List<Rectangle> rects = new List<Rectangle>();
        /// It draws the tools in the toolbox
        /// 
        /// @param o The object that the event is being called from.
        /// @param DrawnArgs This is a class that contains the Cairo Context and the Gdk Window.
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
                    if(item.selected)
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
                if (x < gridW-1)
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
                if (t.bounds.IntersectsWith(new PointD(args.Event.X, args.Event.Y)))
                {
                    tool = t;
                    if (selectedROI != null)
                        selectedROI.Selected = false;
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
            if(App.color!=null)
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

        #endregion

    }
}
