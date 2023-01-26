using System.Drawing;
using System;
using BioGTK;
using Gtk;

namespace Bio
{
    public partial class ApplyFilter : Gtk.Window
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
        /// <summary> Default Shared Constructor. </summary>
        public static ApplyFilter Create(Filt filter,bool two)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.ApplyFilter.glade", null);
            filt = filter;
            return new ApplyFilter(builder, builder.GetObject("applyFilter").Handle, two);
        }
        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected ApplyFilter(Builder builder, IntPtr handle, bool two) : base(handle)
        {
            _builder = builder;
            App.applyFilter = this;
            builder.Autoconnect(this);
            UpdateStacks();
            stackABox.Changed += StackABox_Changed;
            stackBBox.Changed += StackBBox_Changed;
            applyBut.ButtonPressEvent += ApplyBut_ButtonPressEvent;
            inPlaceBut.ButtonPressEvent += InPlaceBut_ButtonPressEvent;
            this.FocusActivated += ApplyFilter_FocusActivated;
            if (!two)
            {
                stackBBox.Sensitive = false;
                if (stackBBox.Children.Count() > 1)
                    stackBBox.Active = 1;
            }
            if (stackABox.Children.Count() > 0)
                stackABox.Active = 0;
        }

        private void InPlaceBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Apply(true);
        }

        private void ApplyBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Apply(false);
        }

        private void ApplyFilter_FocusActivated(object? sender, EventArgs e)
        {
            UpdateStacks();
        }

        private void StackBBox_Changed(object? sender, EventArgs e)
        {
            if (stackABox.Active == stackBBox.Active)
            {
                //Same image selected for A & B
                stackBBox.Active = -1;
            }
        }

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
        public void UpdateStacks()
        {
            ListStore store = new ListStore(typeof(string));
            if (Images.images.Count != stackABox.Children.Count())
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
        }
        public BioImage ImageA
        {
            get { return Images.images[stackABox.Active]; }
        }
        public BioImage ImageB
        {
            get { return Images.images[stackBBox.Active]; }
        }
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
        public int Angle
        {
            get
            {
                return (int)angleBox.Value;
            }
        }
        public Color Color
        {
            get
            {
               return System.Drawing.Color.FromArgb(fillColor.Color.Red, fillColor.Color.Green, fillColor.Color.Blue);
            }
        }
        public int W
        {
            get
            {
                return (int)wBox.Value;
            }
        }
        public int H
        {
            get
            {
                return (int)hBox.Value;
            }
        }
        private void cancelBut_Click(object sender, EventArgs e)
        {
            this.Close();
        }
        private void Apply(bool inPlace)
        {
            if (filt.type == Filt.Type.Base)
            {
                Filters.Base(ImageA.ID, filt.name, false);
            }
            if (filt.type == Filt.Type.Base2)
            {
                Filters.Base2(ImageA.ID, ImageB.ID, filt.name, false);
            }
            else
            if (filt.type == Filt.Type.InPlace)
            {
                Filters.InPlace(ImageA.ID, filt.name, false);
            }
            else
            if (filt.type == Filt.Type.InPlace2)
            {
                Filters.InPlace2(ImageA.ID, ImageB.ID, filt.name, false);
            }
            else
            if (filt.type == Filt.Type.InPlacePartial)
            {
                Filters.InPlacePartial(ImageA.ID, filt.name, false);
            }
            else
            if (filt.type == Filt.Type.Resize)
            {
                Filters.Resize(ImageA.ID, filt.name, false, W, H);
            }
            else
            if (filt.type == Filt.Type.Rotate)
            {
                Filters.Rotate(ImageA.ID, filt.name, false, Angle, Color.A, Color.R, Color.G, Color.B);
            }
            else
            if (filt.type == Filt.Type.Transformation)
            {
                //We use the Crop function of Bio as AForge doesn't support croppping 16bit images.
                if (filt.name == "Crop")
                {
                    Filters.Crop(ImageA.ID, Rectangle);
                }
                else
                {
                    Filters.Transformation(ImageA.ID, filt.name, false, Angle);
                }
            }
            else
            if (filt.type == Filt.Type.Copy)
            {
                Filters.Copy(ImageA.ID, filt.name, false);
            }
            App.viewer.UpdateView();
        }
    }
}
