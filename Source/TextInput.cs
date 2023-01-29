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
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static TextInput Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.TextInput.glade", null);
            return new TextInput(builder, builder.GetObject("textInput").Handle);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected TextInput(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            okBut.ButtonPressEvent += OkBut_ButtonPressEvent;
            cancelBut.ButtonPressEvent += CancelBut_ButtonPressEvent;
        }

        private void CancelBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Respond(ResponseType.Cancel);
            Hide();
        }

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

        public Gdk.RGBA RGBA
        {
            get { return colorBut.Rgba; }
        }

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
