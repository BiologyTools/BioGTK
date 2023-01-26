using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    /// <summary> Example Test Form for GTKSharp and Glade. </summary>
    public class About : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
#pragma warning disable 649

        [Builder.Object]
        private Gtk.Image image;
        [Builder.Object]
        private Gtk.Label label;
#pragma warning restore 649
        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static About Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.About.glade", null);
            return new About(builder, builder.GetObject("about").Handle);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
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
