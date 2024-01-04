using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class ComboPicker : Dialog
    {
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.Button okBut;
        [Builder.Object]
        private Gtk.Button cancelBut;
        [Builder.Object]
        private Gtk.ComboBox comboBox;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        string selection;
        Type ens;
        /// It creates a new ComboPicker object, which is a Gtk.Window, and returns it
        /// 
        /// @return A new instance of the ComboPicker class.
        public static ComboPicker Create(string[] sts)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/ComboPicker.glade", FileMode.Open));
            return new ComboPicker(builder, builder.GetObject("comboPicker").Handle, sts);
        }
        /// It creates a new ComboPicker object, which is a Gtk.Window, and returns it
        /// 
        /// @return A new instance of the ComboPicker class.
        public static ComboPicker Create(Type ems)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/ComboPicker.glade", FileMode.Open));
            return new ComboPicker(builder, builder.GetObject("comboPicker").Handle, ems);
        }
        /* The constructor for the ComboPicker class. */
        protected ComboPicker(Builder builder, IntPtr handle, Type ems) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            okBut.ButtonPressEvent += OkBut_ButtonPressEvent;
            cancelBut.ButtonPressEvent += CancelBut_ButtonPressEvent;
            ens = ems;
            List<string> sts = new List<string>();
            Array ar = Enum.GetValues(ems);
            foreach (Enum s in ar)
            {
                sts.Add(s.ToString());
            }
            var states = new ListStore(typeof(string));
            //We add button states to buttonState box.
            foreach (string val in sts)
            {
                states.AppendValues(val);
            }
            // Set the model for the ComboBox
            comboBox.Model = states;
            // Set the text column to display
            var renderer2 = new CellRendererText();
            comboBox.PackStart(renderer2, false);
            comboBox.AddAttribute(renderer2, "text", 0);
            comboBox.Active = 0;
            App.ApplyStyles(this);
        }
        /* The constructor for the ComboPicker class. */
        protected ComboPicker(Builder builder, IntPtr handle, string[] sts) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            okBut.ButtonPressEvent += OkBut_ButtonPressEvent;
            cancelBut.ButtonPressEvent += CancelBut_ButtonPressEvent;
            var states = new ListStore(typeof(string));
            //We add button states to buttonState box.
            foreach (string val in sts)
            {
                states.AppendValues(val);
            }
            // Set the model for the ComboBox
            comboBox.Model = states;
            // Set the text column to display
            var renderer2 = new CellRendererText();
            comboBox.PackStart(renderer2, false);
            comboBox.AddAttribute(renderer2, "text", 0);
            comboBox.Active = 0;
            App.ApplyStyles(this);
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
            Respond(ResponseType.Ok);
            Hide();
        }

        /* A property that returns the selected index of the combobox. */
        public int SelectedIndex
        {
            get { return comboBox.Active; }
        }
        #endregion

    }
}
