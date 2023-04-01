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
    
    public class Progress : Gtk.Dialog
    {
        #region Properties
        private Builder _builder;
        
        /* A property that is used to update the progress bar. */
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
                    statusLabel.Text = BioImage.status;
                });
            }
        }
        /* A property that is used to update the progress bar. */
        public string Text
        {
            get { return progLabel.Text; }
            set 
            {
                // update progress bar on main UI thread
                Application.Invoke(delegate
                {
                    progLabel.Text = value;
                });
            }
        }
        /* A property that is used to update the progress bar. */
        public string Status
        {
            get { return statusLabel.Text; }
            set
            { 
                // update progress bar on main UI thread
                Application.Invoke(delegate
                {
                    statusLabel.Text = value;
                });
            }
        }
#pragma warning disable 649

        [Builder.Object]
        private ProgressBar progressBar;
        [Builder.Object]
        private Label progLabel;
        [Builder.Object]
        private Label statusLabel;
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

        /// The function Progress_DeleteEvent is called when the user clicks the close button on the
        /// progress window. The function sets the return value of the event to true, which tells the
        /// program to continue running. The function then hides the progress window
        /// 
        /// @param o The object that the event is being fired from.
        /// @param DeleteEventArgs The event arguments.
        private void Progress_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

        #endregion

    }
}
