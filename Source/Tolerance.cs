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
    public class Tolerance : Gtk.Dialog
    {
        #region Properties
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
        public static Tolerance Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Tolerance.glade", FileMode.Open));
            return new Tolerance(builder, builder.GetObject("tolerance").Handle);
        }

       
        /* The constructor for the class. */
        protected Tolerance(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
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
        }

        #endregion

        #region Handlers
        /// It sets up the event handlers for the sliders and text boxes
        protected void SetupHandlers()
        {
            rBar.ChangeValue += ChangeValue;
            gBar.ChangeValue += ChangeValue;
            bBar.ChangeValue += ChangeValue;
            rBox.ChangeValue += Box_ChangeValue;
            gBox.ChangeValue += Box_ChangeValue;
            bBox.ChangeValue += Box_ChangeValue;
            cancelBut.Clicked += CancelBut_Clicked;
            okBut.Clicked += OkBut_Clicked;
        }

        private void OkBut_Clicked(object sender, EventArgs e)
        {
            this.DefaultResponse = ResponseType.Ok;
            this.Destroy();
        }

        private void CancelBut_Clicked(object sender, EventArgs e)
        {
            this.DefaultResponse = ResponseType.Cancel;
            this.Destroy();
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
            Tools.tolerance = ColorS;
        }

        /* Returning a new ColorS object with the values of the spin buttons. */
        public ColorS ColorS
        {
            get 
            {
                return new ColorS((ushort)rBox.Value, (ushort)gBox.Value, (ushort)bBox.Value); 
            }
        }

        #endregion

    }
}
