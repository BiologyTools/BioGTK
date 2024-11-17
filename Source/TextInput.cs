using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class TextInput : Dialog
    {
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.Button okBut;
        [Builder.Object]
        private Gtk.Button cancelBut;
        [Builder.Object]
        private Gtk.Entry textinputBox;
        [Builder.Object]
        private Gtk.ColorButton colorBut;
        [Builder.Object]
        private Gtk.FontButton fontBut;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        private bool ROItype = true;
       /// It creates a new TextInput object, which is a Gtk.Window, and returns it
       /// 
       /// @return A new instance of the TextInput class.
        public static TextInput Create(bool ROItype = true)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/TextInput.glade", FileMode.Open));
            var ti = new TextInput(builder, builder.GetObject("textInput").Handle);
            ti.ROItype = ROItype;
            return ti;
        }

        /* The constructor for the TextInput class. */
        protected TextInput(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            okBut.ButtonPressEvent += OkBut_ButtonPressEvent;
            cancelBut.ButtonPressEvent += CancelBut_ButtonPressEvent;
            textinputBox.Activated += TextinputBox_Activated;
            App.ApplyStyles(this);
        }

        private void TextinputBox_Activated(object sender, EventArgs e)
        {
            if(ROItype)
            SetText();
            Hide();
            Respond(ResponseType.Ok);
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

        /// The function is called when the user clicks the OK button. It sets the text of the selected
        /// ROI to the text entered by the user in the text box. It also sets the font family and font
        /// size of the selected ROI to the font family and font size selected by the user. Finally, it
        /// adds the selected ROI to the image
        /// 
        /// @param o the object that the event is attached to
        /// @param ButtonPressEventArgs 
        private void OkBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (ROItype)
                SetText();
            Hide();
            Respond(ResponseType.Ok);
        }

        void SetText()
        {
            Tools.selectedROI.Text = textinputBox.Text;
            string s = fontBut.Font;
            s = s.Replace(",", "");
            string[] sts = s.Split(' ');
            s = s.Replace(sts[sts.Length-1],"");
            Tools.selectedROI.family = fontBut.FontFamily.Name;
            Tools.selectedROI.fontSize = float.Parse(sts[sts.Length - 1]);
        }

        /* A property that returns the Text value of the text input textbox. */
        public string Text
        {
            get { return textinputBox.Text; }
        }

        /* A property that returns the RGBA value of the color button. */
        public Gdk.RGBA RGBA
        {
            get { return colorBut.Rgba; }
        }

        /* Returning the font family of the font button. */
        public string FontFamily
        {
            get
            {
                return fontBut.FontOptions.HintStyle.ToString();
            }
        }
        #endregion

    }
}
