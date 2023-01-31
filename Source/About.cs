using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
   
    /* It's a window that displays the version of the program and the author */
    public class About : Window
    {
        #region Properties

        private Builder _builder;
#pragma warning disable 649

        [Builder.Object]
        private Gtk.Image image;
        [Builder.Object]
        private Gtk.Label label;
#pragma warning restore 649
        #endregion

        #region Constructors / Destructors
        
        /// It creates a new instance of the About class.
        /// 
        /// @return A new instance of the About class.
        public static About Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.About.glade", null);
            return new About(builder, builder.GetObject("about").Handle);
        }

        
        /* It's the constructor of the class. */
        protected About(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            image.File = "banner.jpg";
            label.Text = "BioGTK " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + " by Erik Repo, Github.com/BiologyTools/BioGTK.";
        }

        #endregion

    }
}
