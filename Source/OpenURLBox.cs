using Gtk;
using javax.swing.text;
using System;
using System.IO;
using System.Threading;

namespace BioGTK
{
    public partial class OpenUrlBox : Dialog
    {
        #region Properties
        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        int line = 0;
#pragma warning disable 649
        [Builder.Object]
        private Entry textBox;
        [Builder.Object]
        private Menu contextMenu;
        [Builder.Object]
        private Button okButton;
        [Builder.Object]
        private Menu cancelButton;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// "Create a new instance of the SetTool class, using the Builder class to load the glade file
        /// and get the handle of the main window."
        /// 
        /// The Builder class is a class that is used to load the glade file and get the handle of the
        /// main window
        /// 
        /// @return A new instance of the SetTool class.
        public static OpenUrlBox Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/SetTool.glade", FileMode.Open));
            return new OpenUrlBox(builder, builder.GetObject("mainwin").Handle);
        }
        /* The constructor of the class. */
        protected OpenUrlBox(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            App.ApplyStyles(this);
        }
        public string GetUrl()
        {
            return textBox.Text;
        }
        /// When the user clicks on the "Focus" button, the function "SetTool_FocusActivated" is called
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        private void SetTool_FocusActivated(object sender, EventArgs e)
        {
            
        }
        #endregion
       
    }
}
