using AForge;
using Atk;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gdk;
using System.IO;

namespace BioGTK
{

    public class SAMTool : Gtk.Window
    {
        static Random rng = new Random();
        #region Properties
        ImageView view;
        private readonly SAM model;
        public static SAM sam;
        public static MicroSAM microsam;
        public List<Promotion> Promotions = new List<Promotion>();
        public bool init = false;
        public static bool microSAM
        {
            get
            {
                if (microsam == null)
                    return false;
                else
                    return true;
            }
        }
        int id = 1;
        string autoSavePath = "Masks";
        private ROI SelectedROI
        {
            get 
            {
                List<ROI> roi = view.GetSelectedROIs();
                if (roi.Count > 0)
                    return roi.Last();
                return ImageView.SelectedImage?.Annotations.FirstOrDefault();
            }
        }
        private AForge.Point _startPoint;
        private Builder _builder;
        Gtk.FileChooserDialog filechooser;
#pragma warning disable 649
        [Builder.Object]
        public MenuBar MainMenu;
        [Builder.Object]
        private MenuItem saveSelected;
        [Builder.Object]
        private MenuItem setAutoSave;
        [Builder.Object]
        private MenuItem toROIMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem autoSaveMenu;
        [Builder.Object]
        private Gtk.Button runButton;
        [Builder.Object]
        private Gtk.Button clearButton;
        [Builder.Object]
        private Gtk.Entry textBox;
        [Builder.Object]
        private Gtk.Scale thresholdBar;
        [Builder.Object]
        private Gtk.Scale pointsBar;
        [Builder.Object]
        private Gtk.Scale predictionBar;
        [Builder.Object]
        private Gtk.Scale stabilityBar;
        [Builder.Object]
        private Gtk.Scale layersBar;
        [Builder.Object]
        private Gtk.Button deleteDuplicatesBut;
        [Builder.Object]
        private Gtk.Button clearSliceBut;
        [Builder.Object]
        private Gtk.Button SAMEncodeBut;
        [Builder.Object]
        private Gtk.CheckButton foregroundBut;
        [Builder.Object]
        private Gtk.Scale minAreaBar;
        [Builder.Object]
        private Gtk.Scale maxAreaBar;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the SAMTool class, which is a class that inherits from
        /// Gtk.Window
        /// 
        /// @return A new instance of the SAMTool class.
        public static SAMTool Create(bool micro_SAM)
        {
            SAM model = micro_SAM ? MicroSAM.Instance() : SAM.Instance();
            SAMTool previous = App.samTool;

            using var stream = File.OpenRead(System.IO.Path.Combine(
                AppContext.BaseDirectory, "Glade", "SAM.glade"));
            var builder = new Builder(stream);
            var tool = new SAMTool(builder, builder.GetObject("SAMTool").Handle, model);
            if (tool.init)
            {
                App.samTool = tool;
                sam = model;
                microsam = model as MicroSAM;
                previous?.Destroy();
            }
            return tool;
        }

        protected SAMTool(Builder builder, IntPtr handle) : this(builder, handle, sam ?? SAM.Instance())
        {
        }

        private SAMTool(Builder builder, IntPtr handle, SAM model) : base(handle)
        {
            this.model = model;
            if (ImageView.SelectedImage?.SelectedBuffer == null)
            {
                ShowError("Open a microscopy image before running SAM.");
                return;
            }

            if (model is MicroSAM)
                Title = "Micro-SAM2";
            if (!TryRun(model.LoadONNXModel))
                return;

            view = App.viewer;
            _builder = builder;
            builder.Autoconnect(this);
            thresholdBar.Adjustment.Lower = 0;
            if (ImageView.SelectedImage.SelectedBuffer.BitsPerPixel > 8)
                thresholdBar.Adjustment.Upper = ushort.MaxValue;
            else
                thresholdBar.Adjustment.Upper = 255;
            stabilityBar.Adjustment.Lower = 0;
            stabilityBar.Adjustment.Upper = 1;
            stabilityBar.Adjustment.StepIncrement = 0.05;
            stabilityBar.Value = 0.5;
            predictionBar.Adjustment.Lower = 0;
            predictionBar.Adjustment.Upper = 1;
            predictionBar.Adjustment.StepIncrement = 0.05;
            predictionBar.Value = 0.5;
            pointsBar.Adjustment.Lower = 0;
            pointsBar.Adjustment.Upper = 100;
            pointsBar.Adjustment.StepIncrement = 1;
            pointsBar.Value = 4;
            layersBar.Adjustment.Lower = 0;
            layersBar.Adjustment.Upper = 4;
            layersBar.Value = 0;
            minAreaBar.Value = 0;
            minAreaBar.Adjustment.Upper = ImageView.SelectedImage.Volume.Width * ImageView.SelectedImage.Volume.Height;
            minAreaBar.Adjustment.Lower = 0;
            
            maxAreaBar.Adjustment.Upper = ImageView.SelectedImage.Volume.Width * ImageView.SelectedImage.Volume.Height;
            maxAreaBar.Adjustment.Lower = 0;
            maxAreaBar.Value = ImageView.SelectedImage.Volume.Width * ImageView.SelectedImage.Volume.Height;
            foregroundBut.Active = true;
            Directory.CreateDirectory("Masks");
            filechooser =
            new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "OK", ResponseType.Accept);
            SetupHandlers();
            App.ApplyStyles(this);
            init = true;
        }

        #endregion

        #region Handlers

        /// This function sets up the event handlers for the menu items
        protected void SetupHandlers()
        {
            saveSelected.ButtonPressEvent += saveSelectedTiffClick;
            view.ButtonReleaseEvent += PictureBox_ButtonReleaseEvent;
            setAutoSave.ButtonPressEvent += SetAutoSave_ButtonPressEvent;
            Destroyed += SAMTool_Destroyed;
            textBox.Changed += TextBox_Changed;
            toROIMenu.ButtonPressEvent += ToROIMenu_ButtonPressEvent;
            runButton.ButtonPressEvent += AutoSAMMenu_ButtonPressEvent;
            clearButton.ButtonPressEvent += ClearButton_ButtonPressEvent;
            deleteDuplicatesBut.ButtonPressEvent += DeleteDuplicatesBut_ButtonPressEvent;
            SAMEncodeBut.ButtonPressEvent += SAMEncodeBut_ButtonPressEvent;
            clearSliceBut.ButtonPressEvent += ClearSliceBut_ButtonPressEvent;
        }

        private void ClearSliceBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            for (int i = 0; i < ImageView.SelectedImage.Annotations.Count; i++)
            {
                if (ImageView.SelectedImage.Annotations[i].coord == App.viewer.GetCoordinate())
                    ImageView.SelectedImage.Annotations.Remove(ImageView.SelectedImage.Annotations[i]);
            }
        }

        private void SAMEncodeBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            TryRun(() => model.Encode(ImageView.SelectedImage));
        }

        private void DeleteDuplicatesBut_ButtonPressEvent(object sender, ButtonPressEventArgs args)
        {
            SAM.RemoveDuplicates(true, (float)(3*ImageView.SelectedImage.PhysicalSizeX), true, (float)maxAreaBar.Value);
        }
        private void ClearButton_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            List<ROI> masks = new List<ROI>();
            foreach (ROI item in ImageView.SelectedImage.Annotations)
            {
                if(item.type == ROI.Type.Mask)
                    masks.Add(item);
            }
            foreach (ROI mask in masks)
            {
                ImageView.SelectedImage.Annotations.Remove(mask);
                mask.Dispose();
            }
            foreach(ROI mask in ImageView.selectedAnnotations)
            {
                if(mask.type == ROI.Type.Mask)
                mask.Dispose();
            }
            ImageView.selectedAnnotations.Clear();
            Promotions.Clear();

        }

        private void AutoSAMMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (TryRun(() => SAM.Run((float)minAreaBar.Value, (float)maxAreaBar.Value, (float)stabilityBar.Value,
                (float)predictionBar.Value, (int)layersBar.Value, (int)pointsBar.Value, model)))
                view.UpdateView();
        }

        private void ToROIMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            List<ROI> rois = new List<ROI>();
            List<ROI> roiss = new List<ROI>();
            rois.AddRange(ImageView.SelectedImage.AnnotationsR);
            rois.AddRange(ImageView.SelectedImage.AnnotationsG);
            rois.AddRange(ImageView.SelectedImage.AnnotationsB);
            foreach (ROI r in roiss)
            {
                if (r.roiMask == null)
                    continue;
                List<PointD> ps = GetEdgePolygonFromMask(new Bitmap("",r.roiMask.Width,r.roiMask.Height,PixelFormat.Format32bppArgb, r.roiMask.GetBytes(), App.viewer.GetCoordinate(), 0));
                for (int i = 0; i < ps.Count; i++)
                {
                    ps[i] = new PointD(ps[i].X * ImageView.SelectedImage.PhysicalSizeX, ps[i].Y * ImageView.SelectedImage.PhysicalSizeY);
                }
                ROI rs = ROI.CreatePolygon(App.viewer.GetCoordinate(), ps.ToArray());
                rois.Add(rs);
            }
            ImageView.SelectedImage.Annotations.AddRange(rois.ToArray());
        }
        static List<PointD> GetEdgePolygonFromMask(Bitmap maskBitmap)
        {
            List<PointD> edgePolygon = new List<PointD>();

            // Lock the bitmap data to access pixel values
            BitmapData data = maskBitmap.LockBits(new AForge.Rectangle(0, 0, maskBitmap.Width, maskBitmap.Height),
                                                  ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            unsafe
            {
                byte* ptr = (byte*)data.Scan0;

                for (int y = 1; y < maskBitmap.Height - 1; y++)
                {
                    for (int x = 1; x < maskBitmap.Width - 1; x++)
                    {
                        int index = y * data.Stride + x * 4;

                        // Check if the pixel is non-zero (white)
                        if (ptr[index] > 0)
                        {
                            // Check if at least one neighboring pixel is zero (black)
                            if (ptr[index - data.Stride] == 0 || ptr[index + data.Stride] == 0 || ptr[index - 4] == 0 || ptr[index + 4] == 0)
                            {
                                edgePolygon.Add(new PointD(x, y));
                            }
                        }
                    }
                }
            }

            // Unlock the bitmap data
            maskBitmap.UnlockBits(data);

            return OrderPolygon(edgePolygon);
        }
        static List<PointD> OrderPolygon(List<PointD> polygon)
        {
            // Calculate the centroid of the polygon
            PointD centroid = new PointD((int)polygon.Average(p => p.X), (int)polygon.Average(p => p.Y));

            // Sort the points based on polar angle
            List<PointD> orderedPolygon = polygon.OrderBy(p => Math.Atan2(p.Y - centroid.Y, p.X - centroid.X)).ToList();

            return orderedPolygon;
        }
        private void SAMTool_Destroyed(object o, EventArgs args)
        {
            view.ButtonReleaseEvent -= PictureBox_ButtonReleaseEvent;
            filechooser?.Destroy();
            if (ReferenceEquals(App.samTool, this))
            {
                App.samTool = null;
                sam = null;
                microsam = null;
                BioLib.Recorder.AddLine("App.CloseWindow(\"SAM Tool\");", false);
            }
            if (!ReferenceEquals(App.samTool?.model, model))
                model.Dispose();
        }

        private bool TryRun(System.Action action)
        {
            try
            {
                action();
                return true;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or Microsoft.ML.OnnxRuntime.OnnxRuntimeException)
            {
                ShowError(error.Message);
                return false;
            }
        }

        private void ShowError(string message)
        {
            using var dialog = new MessageDialog(this, DialogFlags.Modal, MessageType.Error, ButtonsType.Ok, message);
            dialog.Run();
            dialog.Destroy();
        }

        /// The SetAutoSave_ButtonPressEvent function allows the user to select a folder for auto-saving
        /// files.
        /// 
        /// @param o The "o" parameter is the object that triggered the event. In this case, it is the
        /// object that the event handler is attached to.
        /// @param ButtonPressEventArgs ButtonPressEventArgs is an event argument that contains
        /// information about a button press event. It provides properties and methods to access details
        /// such as the button that was pressed, the event timestamp, and the event modifiers.
        /// 
        /// @return If the `filechooser.Run()` method returns a value other than
        /// `(int)ResponseType.Accept`, the method will return and no further code will be executed.
        private void SetAutoSave_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            filechooser.Action = FileChooserAction.SelectFolder;
            filechooser.Title = "Choose Auto Save Folder.";
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            autoSavePath = filechooser.CurrentFolder;
        }

        private void TextBox_Changed(object sender, EventArgs e)
        {
            id = 1;
        }

        /// The Init function loads an ONNX model, calculates image embedding, and updates the status
        /// accordingly.
        /// 
        /// @return If the condition `ImageView.SelectedImage == null` is true, then nothing is being
        /// returned and the method will exit. If the condition is false, then the method will continue
        /// executing the remaining code without returning anything.
        public void Init(bool wait = false)
        {
            if (ImageView.SelectedImage == null)
                return;
            model.LoadONNXModel();
            BioLib.Recorder.AddLine("App.samTool.Init(true);", false);
        }


        /// The function handles the button release event for a picture box and performs various
        /// operations based on the selected tool and annotations.
        /// 
        /// @param o The "o" parameter is the object that raised the event. In this case, it is the
        /// PictureBox control that the event is attached to.
        /// @param ButtonReleaseEventArgs The ButtonReleaseEventArgs parameter is an event argument that
        /// contains information about the button release event. It provides access to properties such
        /// as Event, which contains information about the event itself, and X and Y, which represent
        /// the coordinates of the mouse pointer when the button was released.
        /// 
        /// @return The method does not have a return type, so it does not return anything.
        private void PictureBox_ButtonReleaseEvent(object o, ButtonReleaseEventArgs e)
        {
            var image = ImageView.SelectedImage;
            var buffer = ImageView.SelectedBuffer;
            if (image == null || buffer == null || image.Annotations.Count == 0 ||
                !e.Event.State.HasFlag(ModifierType.Button1Mask))
                return;

            ROI roi = SelectedROI;
            if (roi == null)
                return;
            PointD[] points = roi.ImagePoints(image.Resolutions[0]);
            var transform = new Transforms(1024);
            bool useMicroSam = model is MicroSAM;

            if (roi.type == ROI.Type.Point && Tools.currentTool.type == Tools.Tool.Type.point && points.Length > 0)
            {
                var point = new PointPromotion(foregroundBut.Active ? ForeGround.foreground : ForeGround.background)
                {
                    X = (int)points[0].X,
                    Y = (int)points[0].Y
                };
                Promotions.Add(useMicroSam ? point : transform.ApplyCoords(point, buffer.Width, buffer.Height));
            }
            else if (roi.type == ROI.Type.Rectangle && Tools.currentTool.type == Tools.Tool.Type.rect && points.Length > 0)
            {
                roi.Selected = true;
                var box = new BoxPromotion();
                box.mLeftUp.X = (int)points.Min(point => point.X);
                box.mLeftUp.Y = (int)points.Min(point => point.Y);
                box.mRightBottom.X = (int)points.Max(point => point.X);
                box.mRightBottom.Y = (int)points.Max(point => point.Y);
                Promotions.Add(useMicroSam ? box : transform.ApplyBox(box, buffer.Width, buffer.Height));
            }
            else
                return;

            try
            {
                if (TryRun(() => ShowMask(roi, model.Decode(image, Promotions, buffer.Width, buffer.Height), buffer.Width, buffer.Height)))
                    view.UpdateView();
            }
            finally
            {
                Promotions.Clear();
            }
        }

        /// It creates a file chooser dialog, and if the user selects a file, it saves the selected
        /// image to that file
        /// 
        /// @param sender The object that triggered the event.
        /// @param EventArgs This is the event that is being passed to the method.
        /// 
        /// @return The response type of the dialog.
        protected void saveSelectedTiffClick(object sender, EventArgs a)
        {
            filechooser.Action = FileChooserAction.Save;
            //Saving Tiff can cause a crash on Windows so we check the platform.
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                filechooser.Title = "Save File PNG";
            }
            else
            {
                filechooser.Title = "Save File Tiff";
            }
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                SelectedROI.roiMask.Pixbuf.Save(filechooser.Filename, "png");
            }
            else
            {
                SelectedROI.roiMask.Pixbuf.Save(filechooser.Filename, "tiff");
            }
            filechooser.Hide();
        }
        #endregion

        /// The function takes in a mask array, width, and height, and creates a bitmap image with a
        /// specified threshold value, saving it if autoSaveMenu is active.
        /// 
        /// @param mask The `mask` parameter is an array of floating-point values representing the mask
        /// data. Each element in the array corresponds to a pixel in the image.
        /// @param width The width of the mask image in pixels.
        /// @param height The height parameter represents the height of the mask, which is the number of
        /// rows in the mask array.
        void ShowMask(ROI prompt, float[] mask, int width, int height)
        {
            ROI an = ROI.CreateMask(App.viewer.GetCoordinate(),mask,width,height,
                new PointD(ImageView.SelectedImage.StageSizeX,ImageView.SelectedImage.StageSizeY),ImageView.SelectedImage.PhysicalSizeX,ImageView.SelectedImage.PhysicalSizeY);
            byte r = (byte)rng.Next(0, 255);
            byte g = (byte)rng.Next(0, 255);
            byte b = (byte)rng.Next(0, 255);
            an.fillColor = AForge.Color.FromArgb(r, g, b);
            
            if (autoSaveMenu.Active)
            {
                //Saving Tiff can cause a crash on Windows so we check the platform.
                if (Environment.OSVersion.Platform == PlatformID.Win32NT)
                {
                    an.roiMask.Pixbuf.Save(autoSavePath + "/" + textBox.Text + id + ".png", "png");
                }
                else
                {
                    an.roiMask.Pixbuf.Save(autoSavePath + "/" + textBox.Text + id + ".tiff", "tiff");
                }
                id++;
            }
            an.Selected = prompt.Selected;
            var annotations = ImageView.SelectedImage.Annotations;
            int index = annotations.IndexOf(prompt);
            if (index >= 0)
                annotations[index] = an;
            else
                annotations.Add(an);
        }
    }
}
