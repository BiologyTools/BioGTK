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
                    if(value <= 1)
                        progressBar.Fraction = value;
                    else
                        progressBar.Fraction = value / 100;
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

        
        public static Progress Create(string title, string status, string state)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Progress.glade", FileMode.Open));
            return new Progress(builder, builder.GetObject("progress").Handle, title, status, state);
        }

       
        /* The constructor for the class. */
        protected Progress(Builder builder, IntPtr handle, string title, string status, string state) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            this.Title = title;
            this.statusLabel.Text = status;
            this.progLabel.Text = state;
            App.ApplyStyles(this);
        }

        #endregion

    }
}
