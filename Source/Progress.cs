using AForge;
using Gdk;
using Gtk;
using sun.tools.tree;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    /// <summary> Example Test Form for GTKSharp and Glade. </summary>
    public class Progress : Gtk.Dialog
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        
        public double ProgressValue
        {
            get
            {
                return progressBar.Fraction;
            }
            set 
            {
                // update progress bar on main UI thread
                Application.Invoke(delegate
                {
                    progressBar.Fraction = value;
                });
            }
        }
        public string Text
        {
            get { return progLabel.Text; }
            set { progLabel.Text = value; }
        }
#pragma warning disable 649

        [Builder.Object]
        private ProgressBar progressBar;
        [Builder.Object]
        private Label progLabel;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the ColorTool class, which is a GTK# widget that allows the
        /// user to select a color
        /// 
        /// @param ColorS The color to be displayed in the color tool.
        /// 
        /// @return A new instance of the ColorTool class.
        public static Progress Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Progress.glade", null);
            return new Progress(builder, builder.GetObject("progress").Handle);
        }

       
        /* The constructor for the class. */
        protected Progress(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            this.DeleteEvent += Progress_DeleteEvent;
        }

        private void Progress_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

        #endregion

    }
}
