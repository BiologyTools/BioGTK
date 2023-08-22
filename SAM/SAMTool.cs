using AForge;
using Atk;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Gdk;
using System.Threading;
using System.IO;

namespace BioGTK
{

    public class SAMTool : Gtk.Window
    {
        #region Properties
        ImageView view;
        SAM mSam = SAM.Instance();
        List<Promotion> mPromotionList = new List<Promotion>();
        Pixbuf mask;
        public bool init = false;
        int id = 1;
        string autoSavePath = "Masks";
        private ROI SelectedROI
        {
            get 
            {
                List<ROI> roi = view.GetSelectedROIs();
                if (roi.Count > 0)
                    return roi.Last();
                return null;
            }
        }
        private AForge.Point _startPoint;
        Pixbuf pf;
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
        private Gtk.CheckMenuItem addMaskMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem removeMaskMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem autoSaveMenu;
        [Builder.Object]
        private Gtk.Entry textBox;
        [Builder.Object]
        private Gtk.Scale thresholdBar;
        
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the SAMTool class, which is a class that inherits from
        /// Gtk.Window
        /// 
        /// @return A new instance of the SAMTool class.
        public static SAMTool Create()
        {
            Builder builder = new Builder(null, "BioGTK.SAM.Glade.form.glade", null);
            return new SAMTool(builder, builder.GetObject("SAMTool").Handle);
        }

        /* Setting up the UI. */
        protected SAMTool(Builder builder, IntPtr handle) : base(handle)
        {
            //First lets make sure the model files are installed.
            string app = System.IO.Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (!File.Exists(app + "/decoder-quant.onnx") || !File.Exists(app + "/encoder-quant.onnx"))
            {
                MessageDialog dialog = new MessageDialog(
             this,
             DialogFlags.Modal,
             MessageType.Info,
             ButtonsType.Ok,
             "Place Model files \"decoder-quant.onnx\" and \"encoder-quant.onnx\" in the application folder to use SAM Tool.");
                dialog.Run();
                return;
            }
            //Next lets make sure the image is a single plane as memory requirments make handling multiple images difficult.
            if (ImageView.SelectedImage.SizeZ != 1 || ImageView.SelectedImage.SizeC != 1 || ImageView.SelectedImage.SizeT != 1 || ImageView.SelectedBuffer.PixelFormat != PixelFormat.Format24bppRgb)
            {
                MessageDialog dialog = new MessageDialog(
             this,
             DialogFlags.Modal,
             MessageType.Info,
             ButtonsType.Ok,
             "SAM currently only supports single RGB24 plane images. Reduce image to single plane and ensure 24 bit RGB format.");
                dialog.Run();
                return;
            }
            view = App.viewer;
            _builder = builder;
            builder.Autoconnect(this);
            thresholdBar.Adjustment.Lower = 0;
            thresholdBar.Adjustment.Upper = 255;
            Directory.CreateDirectory("Masks");
            filechooser =
            new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "OK", ResponseType.Accept);
            SetupHandlers();
            Init();
            init = true;
        }

        #endregion

        #region Handlers

        /// This function sets up the event handlers for the menu items
        protected void SetupHandlers()
        {
            saveSelected.ButtonPressEvent += saveSelectedTiffClick;
            view.ButtonReleaseEvent += PictureBox_ButtonReleaseEvent;
            addMaskMenu.ButtonPressEvent += AddMaskMenu_ButtonPressEvent;
            removeMaskMenu.ButtonPressEvent += RemoveMaskMenu_ButtonPressEvent;
            setAutoSave.ButtonPressEvent += SetAutoSave_ButtonPressEvent;
            this.DeleteEvent += SAMTool_DeleteEvent;
            textBox.Changed += TextBox_Changed;
            toROIMenu.ButtonPressEvent += ToROIMenu_ButtonPressEvent;
        }

        private void ToROIMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            List<ROI> rois = new List<ROI>();
            foreach (ROI r in view.AnnotationsRGB)
            {
                if (r.mask == null)
                    continue;
                List<PointD> ps = GetEdgePolygonFromMask(new Bitmap("",r.mask.Width,r.mask.Height,PixelFormat.Format32bppArgb, r.mask.PixelBytes.Data,new ZCT(),0));
                for (int i = 0; i < ps.Count; i++)
                {
                    ps[i] = new PointD(ps[i].X * ImageView.SelectedImage.PhysicalSizeX, ps[i].Y * ImageView.SelectedImage.PhysicalSizeY);
                }
                ROI rs = ROI.CreatePolygon(new ZCT(), ps.ToArray());
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
        private void SAMTool_DeleteEvent(object o, DeleteEventArgs args)
        {
            mSam.Dispose();
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

        private void RemoveMaskMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            addMaskMenu.Active = false;
        }

        private void AddMaskMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            removeMaskMenu.Active = false;
        }

        /// The Init function loads an ONNX model, calculates image embedding, and updates the status
        /// accordingly.
        /// 
        /// @return If the condition `ImageView.SelectedImage == null` is true, then nothing is being
        /// returned and the method will exit. If the condition is false, then the method will continue
        /// executing the remaining code without returning anything.
        protected void Init()
        {
            if (ImageView.SelectedImage == null)
                return;
            this.ShowStatus("Loading ONNX Model");
            this.mSam.LoadONNXModel();
            this.ShowStatus("ONNX Model Loaded");
            this.mSam.Encode(ImageView.SelectedImage);//Image Embedding
            this.ShowStatus("Image Embedding Calculation Finished");
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
            if (view.Images.Count < 1)
                return;
            if (view.Images[0].Annotations.Count == 0)
                return;
            if (SelectedROI == null)
                return;
            PointD pointer = view.ImageToViewSpace(e.Event.X, e.Event.Y);
            PointD mouse = new PointD(((pointer.X - view.Origin.X) / ImageView.SelectedImage.Volume.Width) * ImageView.SelectedImage.SizeX, ((pointer.Y - view.Origin.Y) / ImageView.SelectedImage.Volume.Height) * ImageView.SelectedImage.SizeY);

            //this.pictureBox.CaptureMouse();
            if (Tools.currentTool.type == Tools.Tool.Type.point && view.AnnotationsRGB.Count > 0)
            {
                OpType type = this.addMaskMenu.Active == true ? OpType.ADD : OpType.REMOVE;
                //Brush brush = type == OpType.ADD ? Brushes.Red : Brushes.Black;
                Promotion promtt = new PointPromotion(type);
                (promtt as PointPromotion).X = (int)mouse.X;
                (promtt as PointPromotion).Y = (int)mouse.Y;
                ShowStatus("Decode Started");
                Transforms trs = new Transforms(1024);
                PointPromotion ptn = trs.ApplyCoords((promtt as PointPromotion), ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
                this.mPromotionList.Add(ptn);
                float[] maskk = this.mSam.Decode(this.mPromotionList, ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
                this.ShowMask(maskk, ImageView.SelectedBuffer.Width,ImageView.SelectedBuffer.Height);
                ShowStatus("Decode Done");
                this.mPromotionList.Clear();
            }

            if (Tools.currentTool.type != Tools.Tool.Type.rect)
                return;
            
            BoxPromotion promt = new BoxPromotion();
            (promt as BoxPromotion).mLeftUp.X = (int)SelectedROI.PointsImage[0].X;
            (promt as BoxPromotion).mLeftUp.Y = (int)SelectedROI.PointsImage[0].Y;
            (promt as BoxPromotion).mRightBottom.X = (int)SelectedROI.PointsImage[3].X;
            (promt as BoxPromotion).mRightBottom.Y = (int)SelectedROI.PointsImage[3].Y;

            Transforms ts = new Transforms(1024);
            var pb = ts.ApplyBox(promt, ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
            this.mPromotionList.Add(pb);
            float[] mask = this.mSam.Decode(this.mPromotionList, ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
            ShowMask(mask,ImageView.SelectedBuffer.Width,ImageView.SelectedBuffer.Height);
            this.mPromotionList.Clear();
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
            filechooser.Title = "Save File";
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            mask.Save(filechooser.Filename, "tiff");
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
        void ShowMask(float[] mask, int width, int height)
        {
            Bitmap bp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
            byte[] pixelData = new byte[width * height * 4];
            Array.Clear(pixelData, 0, pixelData.Length);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int ind = y * width + x;
                    if (mask[ind] > thresholdBar.Value)
                    {
                        pixelData[4 * ind] = 255;// Blue
                        pixelData[4 * ind + 1] = 255;// Green
                        pixelData[4 * ind + 2] = 255;// Red
                        pixelData[4 * ind + 3] = 255;// Alpha
                    }
                    else
                        pixelData[4 * ind + 3] = 0;
                }
            }
            Pixbuf pf = new Pixbuf(pixelData, true, 8, ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height, ImageView.SelectedBuffer.Width*4);
            this.SelectedROI.mask = pf;
            if (autoSaveMenu.Active)
            {
                pf.Save(autoSavePath + "/" + textBox.Text + id + ".tif", "tiff");
                id++;
            }
            this.mask = pf;
        }

        void ShowStatus(string message)
        {
            view.Title = message;
        }
    }
}
