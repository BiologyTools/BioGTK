using AForge;
using Gdk;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    /// <summary> Example Test Form for GTKSharp and Glade. </summary>
    public class ColorTool : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649

        [Builder.Object]
        private SpinButton rBox;
        [Builder.Object]
        private SpinButton gBox;
        [Builder.Object]
        private SpinButton bBox;
        [Builder.Object]
        private Scale rBar;
        [Builder.Object]
        private Scale gBar;
        [Builder.Object]
        private Scale bBar;
        [Builder.Object]
        private DrawingArea image;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static ColorTool Create(ColorS color)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.ColorTool.glade", null);
            return new ColorTool(builder, builder.GetObject("colorTool").Handle, color);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected ColorTool(Builder builder, IntPtr handle, ColorS color) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
            rBar.Adjustment.Upper = ushort.MaxValue;
            gBar.Adjustment.Upper = ushort.MaxValue;
            bBar.Adjustment.Upper = ushort.MaxValue;
            rBox.Adjustment.Upper = ushort.MaxValue;
            gBox.Adjustment.Upper = ushort.MaxValue;
            bBox.Adjustment.Upper = ushort.MaxValue;

            rBar.Adjustment.Value = color.R;
            gBar.Adjustment.Value = color.G;
            bBar.Adjustment.Value = color.B;
        }

        #endregion

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            rBar.ChangeValue += ChangeValue;
            gBar.ChangeValue += ChangeValue;
            bBar.ChangeValue += ChangeValue;
            rBox.ChangeValue += Box_ChangeValue;
            gBox.ChangeValue += Box_ChangeValue;
            bBox.ChangeValue += Box_ChangeValue;
            image.Drawn += Image_Drawn;
        }

        private void Image_Drawn(object o, DrawnArgs args)
        {
            args.Cr.SetSourceRGBA(RGBA.Red, RGBA.Green, RGBA.Blue,RGBA.Alpha);
            args.Cr.Rectangle(new Cairo.Rectangle(0,0,image.AllocatedWidth,image.AllocatedHeight));
            args.Cr.Fill();
            args.Cr.Scale(1, 1);
            image.OverrideBackgroundColor(StateFlags.Normal, RGBA);
            args.Cr.Paint();
        }

        private void Box_ChangeValue(object o, ChangeValueArgs args)
        {
            Gdk.RGBA col = new Gdk.RGBA();
            col.Red = rBar.Value / ushort.MaxValue;
            col.Green = gBar.Value / ushort.MaxValue;
            col.Blue = bBar.Value / ushort.MaxValue;
            rBox.Value = rBar.Value;
            gBox.Value = gBar.Value;
            bBox.Value = bBar.Value;
            if (Tools.colorOne)
                Tools.DrawColor = ColorS;
            else
                Tools.EraseColor = ColorS;

        }

        private void ChangeValue(object o, ChangeValueArgs args)
        {
            rBox.Value = rBar.Value;
            gBox.Value = gBar.Value;
            bBox.Value = bBar.Value;
            if (Tools.colorOne)
                Tools.DrawColor = ColorS;
            else
                Tools.EraseColor = ColorS;
        }

        public ColorS ColorS
        {
            get { return new ColorS((ushort)rBox.Value, (ushort)gBox.Value, (ushort)bBox.Value); }
        }
        public RGBA RGBA
        {
            get {
                RGBA rgb = new RGBA();
                rgb.Red = rBox.Value / ushort.MaxValue;
                rgb.Green = gBox.Value / ushort.MaxValue;
                rgb.Blue = bBox.Value / ushort.MaxValue;
                rgb.Alpha = 1.0;
                return rgb; }
        }
        #endregion

    }
}
