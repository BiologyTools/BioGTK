using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class NumberPicker : Dialog
    {
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.Button okBut;
        [Builder.Object]
        private Gtk.Button cancelBut;
        [Builder.Object]
        private Gtk.SpinButton numericBox;
        [Builder.Object]
        private Gtk.Scale bar;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        string selection;
        Enum ens;
        /// It creates a new NumberPicker object, which is a Gtk.Window, and returns it
        /// 
        /// @return A new instance of the NumberPicker class.
        public static NumberPicker Create(double min, double max, double start = 0)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/NumberPicker.glade", FileMode.Open));
            return new NumberPicker(builder, builder.GetObject("numberPicker").Handle, min, max, start);
        }

        /* The constructor for the NumberPicker class. */
        protected NumberPicker(Builder builder, IntPtr handle, double min, double max, double start = 0) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            okBut.ButtonPressEvent += OkBut_ButtonPressEvent;
            cancelBut.ButtonPressEvent += CancelBut_ButtonPressEvent;
            numericBox.ValueChanged += NumericBox_ValueChanged;
            bar.ValueChanged += Bar_ValueChanged;
            Min = min;
            Max = max;
            numericBox.Adjustment.StepIncrement = 1;
            bar.Adjustment.StepIncrement = 1;
            SelectedValue = start;
            Show();
            App.ApplyStyles(this);
        }

        private void Bar_ValueChanged(object sender, EventArgs e)
        {
            numericBox.Value = bar.Value;
        }

        private void NumericBox_ValueChanged(object sender, EventArgs e)
        {
            bar.Value = numericBox.Value;
        }

        /// When the Cancel button is pressed, the dialog box is closed
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonPressEventArgs 
        private void CancelBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Respond(ResponseType.Cancel);
            Hide();
        }


        private void OkBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Hide();
            Respond(ResponseType.Ok);
        }

        public double SelectedValue
        {
            get { return numericBox.Value; }
            set 
            { 
                numericBox.Value = value;
            }
        }
        public double Min
        {
            get { return numericBox.Adjustment.Lower; }
            set
            {
                numericBox.Adjustment.Lower = value;
                bar.Adjustment.Lower = value;
            }
        }
        public double Max
        {
            get { return numericBox.Adjustment.Upper; }
            set 
            { 
                numericBox.Adjustment.Upper = value;
                bar.Adjustment.Upper = value;
            }
        }
        #endregion

    }
}
