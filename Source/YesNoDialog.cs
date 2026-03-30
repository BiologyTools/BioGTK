using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class YesNoDialog : Dialog
    {
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.Button yesBut;
        [Builder.Object]
        private Gtk.Button noBut;
        [Builder.Object]
        private Gtk.Label textLabel;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        string selection;
        /// It creates a new ComboPicker object, which is a Gtk.Window, and returns it
        /// 
        /// @return A new instance of the ComboPicker class.
        public static YesNoDialog Create(string text)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/YesNoDialog.glade", FileMode.Open));
            return new YesNoDialog(builder, builder.GetObject("yesDialog").Handle, text);
        }
        /* The constructor for the ComboPicker class. */
        protected YesNoDialog(Builder builder, IntPtr handle, string text) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            Text = text;
            yesBut.ButtonPressEvent += OkBut_ButtonPressEvent;
            noBut.ButtonPressEvent += CancelBut_ButtonPressEvent;
        }
        
        /// When the Cancel button is pressed, the dialog box is closed
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonPressEventArgs 
        private void CancelBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Respond(ResponseType.No);
            Hide();
        }

        private void OkBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Respond(ResponseType.Yes);
            Hide();
        }

        public string Text
        {
            get
            {
                return textLabel.Text;
            }
            set
            {
                textLabel.Text = value;
            }
        }

        #endregion

    }
}
