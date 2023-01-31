using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    /// <summary> Example Test Form for GTKSharp and Glade. </summary>
    public class TextInput : Dialog
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
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
        
       /// It creates a new TextInput object, which is a Gtk.Window, and returns it
       /// 
       /// @return A new instance of the TextInput class.
        public static TextInput Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.TextInput.glade", null);
            return new TextInput(builder, builder.GetObject("textInput").Handle);
        }

        /* The constructor for the TextInput class. */
        protected TextInput(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            okBut.ButtonPressEvent += OkBut_ButtonPressEvent;
            cancelBut.ButtonPressEvent += CancelBut_ButtonPressEvent;
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
            Tools.selectedROI.Text = textinputBox.Text;
            string s = fontBut.Font;
            s = s.Replace(",", "");
            string[] sts = s.Split(' ');
            s = s.Replace(sts[sts.Length-1],"");
            Tools.selectedROI.family = fontBut.FontFamily.Name;
            Tools.selectedROI.fontSize = float.Parse(sts[sts.Length - 1]);
            ImageView.SelectedImage.Annotations.Add(Tools.selectedROI);
            
            Hide();
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
