
using System;
using BioGTK;
using Gtk;
using AForge;
using sun.tools.tree;

namespace BioGTK
{
    public partial class ApplyFilter : Gtk.Dialog
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private ComboBox stackABox;
        [Builder.Object]
        private ComboBox stackBBox;
        [Builder.Object]
        private ComboBox roiBox;
        [Builder.Object]
        private SpinButton wBox;
        [Builder.Object]
        private SpinButton hBox;
        [Builder.Object]
        private SpinButton xBox;
        [Builder.Object]
        private SpinButton yBox;
        [Builder.Object]
        private SpinButton angleBox;
        [Builder.Object]
        private ColorButton fillColor;
        [Builder.Object]
        private Button cancelBut;
        [Builder.Object]
        private Button applyBut;
        [Builder.Object]
        private Button inPlaceBut;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        static Filt filt;
        
        /// This function creates a new instance of the ApplyFilter class, which is a class that
        /// inherits from the Gtk.Window class
        /// 
        /// @param Filt The filter that is being applied.
        /// @param two a boolean that determines whether the user is applying a filter to a single image
        /// or a set of images.
        /// 
        /// @return The ApplyFilter class is being returned.
        public static ApplyFilter Create(Filt filter,bool two)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.ApplyFilter.glade", null);
            filt = filter;
            return new ApplyFilter(builder, builder.GetObject("applyFilter").Handle, two, filter);
        }
        
        /* This is the constructor for the ApplyFilter class. */
        protected ApplyFilter(Builder builder, IntPtr handle, bool two,Filt filter) : base(handle)
        {
            _builder = builder;
            App.applyFilter = this;
            builder.Autoconnect(this);
            InitStacks();
            stackABox.Changed += StackABox_Changed;
            stackBBox.Changed += StackBBox_Changed;
            applyBut.ButtonPressEvent += ApplyBut_ButtonPressEvent;
            inPlaceBut.ButtonPressEvent += InPlaceBut_ButtonPressEvent;
            this.FocusActivated += ApplyFilter_FocusActivated;
            this.Title = filter.name;
            if (!two)
            {
                stackBBox.Sensitive = false;
                if (stackBBox.Children.Length > 1)
                    stackBBox.Active = 1;
            }
            if (stackABox.Children.Length > 0)
                stackABox.Active = 0;
        }

        /// If the user clicks the "In Place" button, then apply the changes to the current file
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk-sharp/stable/Gtk.ButtonPressEventArgs.html
        private void InPlaceBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Apply(true);
            DefaultResponse = ResponseType.Ok;
            Destroy();
        }

        /// The ApplyBut_ButtonPressEvent function is called when the Apply button is pressed
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void ApplyBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Apply(false);
            DefaultResponse = ResponseType.Ok;
            Destroy();
        }

        /// > When the user clicks on the Apply Filter button, the function UpdateStacks() is called
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        private void ApplyFilter_FocusActivated(object? sender, EventArgs e)
        {
            UpdateStacks();
        }

        /// If the user selects the same image for both A and B, then the B selection is reset to
        /// nothing
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void StackBBox_Changed(object? sender, EventArgs e)
        {
            if (stackABox.Active == stackBBox.Active)
            {
                //Same image selected for A & B
                stackBBox.Active = -1;
            }
        }

        /// When the user selects an image from the dropdown menu, the function populates the second
        /// dropdown menu with the ROIs from the selected image
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs System.EventArgs
        /// 
        /// @return The ROI object is being returned.
        private void StackABox_Changed(object? sender, EventArgs e)
        {
            if (stackABox.Active == -1)
                return;
            ListStore roi = new ListStore(typeof(string));
            roiBox.Model = null;
            foreach (ROI r in ImageA.Annotations)
            {
                roi.AppendValues(r);
            }
            roiBox.Model = roi;
            if (stackABox.Active == stackBBox.Active)
            {
                //Same image selected for A & B
                stackBBox.Active = -1;
            }
            var renderer = new CellRendererText();
            roiBox.PackStart(renderer, false);
            roiBox.AddAttribute(renderer, "text", 0);
        }
        #endregion
        /// It takes the list of images and updates the dropdown boxes with the filenames of the images
        public void InitStacks()
        {
            ListStore store = new ListStore(typeof(string));
            if (Images.images.Count != stackABox.Children.Length)
            {
                stackABox.Model = null;
                stackBBox.Model = null;
                foreach (BioImage b in Images.images)
                {
                    store.AppendValues(System.IO.Path.GetFileName(b.Filename));
                }
                stackABox.Active = 0;
            }
            stackABox.Model = store;
            stackBBox.Model = store;
            // Set the text column to display
            var renderer = new CellRendererText();
            stackABox.PackStart(renderer, false);
            stackABox.AddAttribute(renderer, "text", 0);
            // Set the text column to display
            var renderer2 = new CellRendererText();
            stackBBox.PackStart(renderer2, false);
            stackBBox.AddAttribute(renderer2, "text", 0);

            ListStore rois = new ListStore(typeof(string));
            foreach (ROI b in ImageA.Annotations)
            {
                rois.AppendValues(b.ToString());
            }
            roiBox.Active = 0;
            roiBox.Model = rois;
            // Set the text column to display
            var renderer3 = new CellRendererText();
            roiBox.PackStart(renderer3, false);
            roiBox.AddAttribute(renderer3, "text", 0);

        }
        /// It takes the list of images and updates the dropdown boxes with the filenames of the images
        public void UpdateStacks()
        {
            ListStore store = new ListStore(typeof(string));
            if (Images.images.Count != stackABox.Children.Length)
            {
                stackABox.Model = null;
                stackBBox.Model = null;
                foreach (BioImage b in Images.images)
                {
                    store.AppendValues(System.IO.Path.GetFileName(b.Filename));
                }
                stackABox.Active = 0;
            }
            stackABox.Model = store;
            stackBBox.Model = store;
            ListStore rois = new ListStore(typeof(string));
            foreach (ROI b in ImageA.Annotations)
            {
                rois.AppendValues(b.ToString());
            }
            roiBox.Model = rois;
        }
        /* Returning the image that is selected in the dropdown menu. */
        public BioImage ImageA
        {
            get { return Images.images[stackABox.Active]; }
        }
        /* Returning the image that is selected in the dropdown menu. */
        public BioImage ImageB
        {
            get { return Images.images[stackBBox.Active]; }
        }
        /* Returning the rectangle that is selected in the dropdown menu. */
        public RectangleD Rectangle
        {
            get
            {
                if (roiBox.Active != -1)
                    return ImageA.Annotations[roiBox.Active].BoundingBox;
                else
                    return new RectangleD((double)xBox.Value, (double)yBox.Value, (double)wBox.Value, (double)hBox.Value);
            }
        }
        /* Returning the angle of the image. */
        public int Angle
        {
            get
            {
                return (int)angleBox.Value;
            }
        }
        /* Returning the color of the image. */
        public Color Color
        {
            get
            {
               return Color.FromArgb(fillColor.Color.Red, fillColor.Color.Green, fillColor.Color.Blue);
            }
        }
        /* Returning the value of the wBox. */
        public int W
        {
            get
            {
                return (int)wBox.Value;
            }
        }
        /* Returning the value of the hBox. */
        public int H
        {
            get
            {
                return (int)hBox.Value;
            }
        }
        /// This function closes the form when the cancel button is clicked
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        private void cancelBut_Click(object sender, EventArgs e)
        {
            DefaultResponse = ResponseType.Cancel;
            Destroy();
        }
        /// The function takes the image ID and the name of the filter and applies the filter to the
        /// image
        /// 
        /// @param inPlace If true, the filter will be applied to the current image. If false, the
        /// filter will be applied to a new image.
        private void Apply(bool inPlace)
        {
            if (filt.type == Filt.Type.Base)
            {
                ImageView.SelectedImage = Filters.Base(ImageA.ID, filt.name, false);
            }
            if (filt.type == Filt.Type.Base2)
            {
                ImageView.SelectedImage = Filters.Base2(ImageA.ID, ImageB.ID, filt.name, false);
            }
            else
            if (filt.type == Filt.Type.InPlace)
            {
                ImageView.SelectedImage = Filters.InPlace(ImageA.ID, filt.name, false);
            }
            else
            if (filt.type == Filt.Type.InPlace2)
            {
                ImageView.SelectedImage = Filters.InPlace2(ImageA.ID, ImageB.ID, filt.name, false);
            }
            else
            if (filt.type == Filt.Type.InPlacePartial)
            {
                ImageView.SelectedImage = Filters.InPlacePartial(ImageA.ID, filt.name, false);
            }
            else
            if (filt.type == Filt.Type.Resize)
            {
                ImageView.SelectedImage = Filters.Resize(ImageA.ID, filt.name, false, W, H);
            }
            else
            if (filt.type == Filt.Type.Rotate)
            {
                ImageView.SelectedImage = Filters.Rotate(ImageA.ID, filt.name, false, Angle, Color.A, Color.R, Color.G, Color.B);
            }
            else
            if (filt.type == Filt.Type.Transformation)
            {
                //We use the Crop function of Bio as AForge doesn't support croppping 16bit images.
                if (filt.name == "Crop")
                {
                    App.tabsView.AddTab(Filters.Crop(ImageA.ID, Rectangle));
                }
                else
                {
                    ImageView.SelectedImage = Filters.Transformation(ImageA.ID, filt.name, false, Angle);
                }
            }
            else
            if (filt.type == Filt.Type.Copy)
            {
                ImageView.SelectedImage = Filters.Copy(ImageA.ID, filt.name, false);
            }
            App.viewer.UpdateView();
        }
    }
}
