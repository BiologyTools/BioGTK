using AForge;
using Gdk;
using Gtk;
using sun.tools.tree;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{

    public class ColorTool : Gtk.Dialog
    {
        #region Properties

        private Builder _builder;
        bool color1 = false;

#pragma warning disable 649

        [Builder.Object]
        private SpinButton rBox;
        [Builder.Object]
        private SpinButton gBox;
        [Builder.Object]
        private SpinButton bBox;
        [Builder.Object]
        private SpinButton widthBox;
        [Builder.Object]
        private Scale rBar;
        [Builder.Object]
        private Scale gBar;
        [Builder.Object]
        private Scale bBar;
        [Builder.Object]
        private Scale widthBar;
        [Builder.Object]
        private DrawingArea image;
        [Builder.Object]
        private Button cancelBut;
        [Builder.Object]
        private Button okBut;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the ColorTool class, which is a GTK# widget that allows the
        /// user to select a color
        /// 
        /// @param ColorS The color to be displayed in the color tool.
        /// 
        /// @return A new instance of the ColorTool class.
        public static ColorTool Create(bool color1)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/ColorTool.glade", FileMode.Open));
            return new ColorTool(builder, builder.GetObject("colorTool").Handle, color1);
        }

       
        /* The constructor for the class. */
        protected ColorTool(Builder builder, IntPtr handle, bool color1) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            this.color1 = color1;
            SetupHandlers();
            rBar.Adjustment.Upper = ushort.MaxValue;
            gBar.Adjustment.Upper = ushort.MaxValue;
            bBar.Adjustment.Upper = ushort.MaxValue;
            rBox.Adjustment.Upper = ushort.MaxValue;
            rBox.Adjustment.StepIncrement = 1;
            rBox.Adjustment.PageIncrement = 10;
            gBox.Adjustment.Upper = ushort.MaxValue;
            gBox.Adjustment.StepIncrement = 1;
            gBox.Adjustment.PageIncrement = 10;
            bBox.Adjustment.Upper = ushort.MaxValue;
            bBox.Adjustment.StepIncrement = 1;
            bBox.Adjustment.PageIncrement = 10;
            widthBar.Adjustment.Upper = 100;
            widthBar.Adjustment.Value = Tools.StrokeWidth;
            widthBox.Adjustment.Upper = 100;
            widthBox.Adjustment.Value = Tools.StrokeWidth;
            if (!color1)
            {
                rBar.Adjustment.Value = Tools.drawColor.R;
                gBar.Adjustment.Value = Tools.drawColor.G;
                bBar.Adjustment.Value = Tools.drawColor.B;
            }
            else
            {
                rBar.Adjustment.Value = Tools.eraseColor.R;
                gBar.Adjustment.Value = Tools.eraseColor.G;
                bBar.Adjustment.Value = Tools.eraseColor.B;
            }
            image.QueueDraw();
            App.ApplyStyles(this);
        }

        #endregion

        #region Handlers
        /// It sets up the event handlers for the sliders and text boxes
        protected void SetupHandlers()
        {
            rBar.ChangeValue += ChangeValue;
            gBar.ChangeValue += ChangeValue;
            bBar.ChangeValue += ChangeValue;
            widthBar.ChangeValue += ChangeValue;
            rBox.ChangeValue += Box_ChangeValue;
            gBox.ChangeValue += Box_ChangeValue;
            bBox.ChangeValue += Box_ChangeValue;
            widthBox.ChangeValue += Box_ChangeValue;
            image.Drawn += Image_Drawn;
            cancelBut.Clicked += CancelBut_Clicked;
            okBut.Clicked += OkBut_Clicked;
        }

        /// The OkBut_Clicked function is called when the Ok button is clicked
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void OkBut_Clicked(object sender, EventArgs e)
        {
            this.DefaultResponse = ResponseType.Ok;
            this.Destroy();
        }

        /// When the Cancel button is clicked, the default response is set to cancel and the window is
        /// destroyed
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void CancelBut_Clicked(object sender, EventArgs e)
        {
            this.DefaultResponse = ResponseType.Cancel;
            this.Destroy();
        }

        /// The function takes the image and draws a rectangle on it with the color specified by the
        /// RGBA struct
        /// 
        /// @param o The object that is being drawn.
        /// @param DrawnArgs This is the event arguments that are passed to the event handler.
        private void Image_Drawn(object o, DrawnArgs args)
        {
            args.Cr.SetSourceRGBA(RGBA.Red, RGBA.Green, RGBA.Blue,RGBA.Alpha);
            args.Cr.Rectangle(new Cairo.Rectangle(0,0,image.AllocatedWidth,image.AllocatedHeight));
            args.Cr.Fill();
            args.Cr.Scale(1, 1);
            image.OverrideBackgroundColor(StateFlags.Normal, RGBA);
            args.Cr.Paint();
        }

        /// It changes the color of the color picker
        /// 
        /// @param o The object that the event is being called from.
        /// @param ChangeValueArgs
        /// https://developer.gnome.org/gtkmm/stable/classGtk_1_1Scale_1_1ChangeValueArgs.html
        private void Box_ChangeValue(object o, ChangeValueArgs args)
        {
            Gdk.RGBA col = new Gdk.RGBA();
            col.Red = rBar.Value / ushort.MaxValue;
            col.Green = gBar.Value / ushort.MaxValue;
            col.Blue = bBar.Value / ushort.MaxValue;
            rBox.Value = rBar.Value;
            gBox.Value = gBar.Value;
            bBox.Value = bBar.Value;
            Tools.StrokeWidth = (int)widthBox.Value;
            if (color1)
                Tools.DrawColor = ColorS;
            else
                Tools.EraseColor = ColorS;

        }

        /// It changes the value of the textbox to the value of the slider
        /// 
        /// @param o The object that called the event
        /// @param ChangeValueArgs 
        private void ChangeValue(object o, ChangeValueArgs args)
        {
            rBox.Value = rBar.Value;
            gBox.Value = gBar.Value;
            bBox.Value = bBar.Value;
            if (!color1)
                Tools.DrawColor = ColorS;
            else
                Tools.EraseColor = ColorS;
            Tools.StrokeWidth = (int)widthBar.Value;
        }

        /* Returning a new ColorS object with the values of the spin buttons. */
        public ColorS ColorS
        {
            get 
            {
                return new ColorS((ushort)rBox.Value, (ushort)gBox.Value, (ushort)bBox.Value); 
            }
        }
        /* Returning a new RGBA object with the values of the spin buttons. */
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
