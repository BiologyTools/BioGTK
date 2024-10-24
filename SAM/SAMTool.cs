﻿using AForge;
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
        static Random rng = new Random();
        #region Properties
        ImageView view;
        static SAM mSam = SAM.Instance();
        List<Promotion> mPromotionList = new List<Promotion>();
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
                else
                    return view.Images[0].Annotations[0];
                return null;
            }
            set
            {
                for (int i = 0; i < ImageView.SelectedImage.Annotations.Count; i++)
                {
                    if (ImageView.SelectedImage.Annotations[i].Selected)
                        ImageView.SelectedImage.Annotations[i] = value;
                }
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
        private Gtk.CheckMenuItem addMaskMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem removeMaskMenu;
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
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the SAMTool class, which is a class that inherits from
        /// Gtk.Window
        /// 
        /// @return A new instance of the SAMTool class.
        public static SAMTool Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/SAM.glade", FileMode.Open));
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
            Directory.CreateDirectory("Masks");
            filechooser =
            new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "OK", ResponseType.Accept);
            SetupHandlers();
            Init();
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
            addMaskMenu.ButtonPressEvent += AddMaskMenu_ButtonPressEvent;
            removeMaskMenu.ButtonPressEvent += RemoveMaskMenu_ButtonPressEvent;
            setAutoSave.ButtonPressEvent += SetAutoSave_ButtonPressEvent;
            this.DeleteEvent += SAMTool_DeleteEvent;
            textBox.Changed += TextBox_Changed;
            toROIMenu.ButtonPressEvent += ToROIMenu_ButtonPressEvent;
            runButton.ButtonPressEvent += AutoSAMMenu_ButtonPressEvent;
            clearButton.ButtonPressEvent += ClearButton_ButtonPressEvent;
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
        }

        private void AutoSAMMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            AutoSAM asm = new AutoSAM((int)pointsBar.Value,64,(float)predictionBar.Value,(float)stabilityBar.Value,1,0.7f,0,0.7f,0.3413333f,1,null,0,"binary_mask");
            for (int b = 0; b < ImageView.SelectedImage.Buffers.Count; b++)
            {
                MaskData md = asm.Generate(ImageView.SelectedImage,b);
                for (int i = 0; i < md.mfinalMask.Count; i++)
                {
                    PointD loc = new PointD(ImageView.SelectedImage.StageSizeX, ImageView.SelectedImage.StageSizeY);
                    /*
                    int width = md.mBox[(i * 4) + 2];
                    int height = md.mBox[(i * 4) + 3];
                    int x = md.mBox[i * 4];
                    int y = md.mBox[(i * 4) + 1];
                    
                    ROI roi = ROI.CreateRectangle(ImageView.SelectedImage.Buffers[b].Coordinate,loc.X + (x * ImageView.SelectedImage.PhysicalSizeX), loc.Y + (x * ImageView.SelectedImage.PhysicalSizeY),
                        width * ImageView.SelectedImage.PhysicalSizeX, height * ImageView.SelectedImage.PhysicalSizeY);
                    ImageView.SelectedImage.Annotations.Add(roi);
                    */
                    ROI an = ROI.CreateMask(ImageView.SelectedImage.Buffers[b].Coordinate, md.mfinalMask[i].ToArray(), ImageView.SelectedImage.SizeX, ImageView.SelectedImage.SizeY, loc, ImageView.SelectedImage.PhysicalSizeX, ImageView.SelectedImage.PhysicalSizeY);
                    byte r = (byte)rng.Next(0, 255);
                    byte g = (byte)rng.Next(0, 255);
                    byte bb = (byte)rng.Next(0, 255);
                    an.fillColor.R = r;
                    an.fillColor.G = g;
                    an.fillColor.B = bb;
                    ImageView.SelectedImage.Annotations.Add(an);
                    
                }
            }
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
            Thread th = new Thread(mSam.LoadONNXModel);
            th.Start();
        }

        internal static void Encode()
        {
            mSam.Encode(ImageView.SelectedImage);//Image Embedding
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
                SelectedROI = view.Images[0].Annotations[0];
            List<ROI> rois = new List<ROI>();
            rois.AddRange(ImageView.SelectedImage.Annotations);
            PointD[] pls = SelectedROI.ImagePoints(ImageView.SelectedImage.Resolutions[0]);
            if (Tools.currentTool.type == Tools.Tool.Type.point && rois.Count > 0 && e.Event.State.HasFlag(ModifierType.Button1Mask))
            {
                OpType type = this.addMaskMenu.Active == true ? OpType.ADD : OpType.REMOVE;
                Promotion promtt = new PointPromotion(type);
                (promtt as PointPromotion).X = (int)pls[0].X;
                (promtt as PointPromotion).Y = (int)pls[0].Y;
                Transforms trs = new Transforms(1024);
                PointPromotion ptn = trs.ApplyCoords((promtt as PointPromotion), ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
                this.mPromotionList.Add(ptn);
                float[] maskk = mSam.Decode(ImageView.SelectedImage, this.mPromotionList, ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
                this.ShowMask(maskk, ImageView.SelectedBuffer.Width,ImageView.SelectedBuffer.Height);
                this.mPromotionList.Clear();
            }

            if (Tools.currentTool.type == Tools.Tool.Type.rect && e.Event.State.HasFlag(ModifierType.Button1Mask))
            {
                SelectedROI.Selected = true;
                BoxPromotion promt = new BoxPromotion();
                if (pls[0].X < (int)pls[3].X || pls[0].Y < (int)pls[3].Y)
                {
                    (promt as BoxPromotion).mLeftUp.X = (int)pls[0].X;
                    (promt as BoxPromotion).mLeftUp.Y = (int)pls[0].Y;
                    (promt as BoxPromotion).mRightBottom.X = (int)pls[3].X;
                    (promt as BoxPromotion).mRightBottom.Y = (int)pls[3].Y;
                }
                else
                {
                    (promt as BoxPromotion).mLeftUp.X = (int)pls[3].X;
                    (promt as BoxPromotion).mLeftUp.Y = (int)pls[3].Y;
                    (promt as BoxPromotion).mRightBottom.X = (int)pls[0].X;
                    (promt as BoxPromotion).mRightBottom.Y = (int)pls[0].Y;
                }
                Transforms ts = new Transforms(1024);
                var pb = ts.ApplyBox(promt, ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
                this.mPromotionList.Add(pb);
                float[] mask = mSam.Decode(ImageView.SelectedImage,this.mPromotionList, ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
                ShowMask(mask, ImageView.SelectedBuffer.Width, ImageView.SelectedBuffer.Height);
                this.mPromotionList.Clear();
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
        void ShowMask(float[] mask, int width, int height)
        {
            if (SelectedROI.type != ROI.Type.Point && SelectedROI.type != ROI.Type.Rectangle)
                return;
            ROI an = ROI.CreateMask(App.viewer.GetCoordinate(),mask,width,height,
                new PointD(ImageView.SelectedImage.StageSizeX,ImageView.SelectedImage.StageSizeY),ImageView.SelectedImage.PhysicalSizeX,ImageView.SelectedImage.PhysicalSizeY);
            byte r = (byte)rng.Next(0, 255);
            byte g = (byte)rng.Next(0, 255);
            byte b = (byte)rng.Next(0, 255);
            an.fillColor.R = r;
            an.fillColor.G = g;
            an.fillColor.B = b;
            
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
            SelectedROI = an;
        }
    }
}
