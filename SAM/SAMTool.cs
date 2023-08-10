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
using Pango;
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
            if(!File.Exists("decoder-quant.onnx") || !File.Exists("encoder-quant.onnx"))
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
            if (ImageView.SelectedImage.SizeZ != 1 || ImageView.SelectedImage.SizeC != 1 || ImageView.SelectedImage.SizeT != 1)
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
            BioImage bb = ImageView.SelectedImage;
            bb.To24Bit();
            bb.isPyramidal = true;
            view = ImageView.Create(bb);
            view.Show();
            _builder = builder;
            builder.Autoconnect(this);
            thresholdBar.Adjustment.Lower = 0;
            thresholdBar.Adjustment.Upper = 255;
            filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "OK", ResponseType.Accept);
            Directory.CreateDirectory("Masks");
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
        }

        private void SAMTool_DeleteEvent(object o, DeleteEventArgs args)
        {
            mSam.Dispose();
        }

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

        protected void Init()
        {
            if (ImageView.SelectedImage == null)
                return;
            ImageView.SelectedImage.isPyramidal = true;
            this.ShowStatus("Loading ONNX Model");
            this.mSam.LoadONNXModel();
            this.ShowStatus("ONNX Model Loaded");
            this.mSam.Encode(ImageView.SelectedImage);//Image Embedding
            this.ShowStatus("Image Embedding Calculation Finished");
        }

        private void PictureBox_ButtonReleaseEvent(object o, ButtonReleaseEventArgs e)
        {
            if (view.Images.Count < 1)
                return;
            if (view.Images[0].Annotations.Count == 0)
                return;
            if (SelectedROI == null)
                return;

            //this.pictureBox.CaptureMouse();
            if (Tools.currentTool.type == Tools.Tool.Type.point && view.AnnotationsRGB.Count > 0)
            {
                OpType type = this.addMaskMenu.Active == true ? OpType.ADD : OpType.REMOVE;
                //Brush brush = type == OpType.ADD ? Brushes.Red : Brushes.Black;
                Promotion promtt = new PointPromotion(type);
                (promtt as PointPromotion).X = (int)e.Event.X;
                (promtt as PointPromotion).Y = (int)e.Event.Y;
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
            (promt as BoxPromotion).mLeftUp.X = (int)this.SelectedROI.X;
            (promt as BoxPromotion).mLeftUp.Y = (int)this.SelectedROI.Y;
            (promt as BoxPromotion).mRightBottom.X = (int)this.SelectedROI.X + (int)this.SelectedROI.W;
            (promt as BoxPromotion).mRightBottom.Y = (int)this.SelectedROI.Y + (int)this.SelectedROI.H;

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
