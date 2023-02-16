using Gdk;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
   
    /* It's a window that displays the version of the program and the author */
    public class About : Gtk.Window
    {
        #region Properties
        Pixbuf pixbuf;
        private Builder _builder;
#pragma warning disable 649
        [Builder.Object]
        private Gtk.DrawingArea image;
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
            Console.WriteLine(Environment.CurrentDirectory);
            string s = System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/";
            pixbuf = new Pixbuf(s + "Resources/banner.jpg");
            label.Text = "BioGTK " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + " by Erik Repo, Github.com/BiologyTools/BioGTK.";
            image.Drawn += Image_Drawn;
            this.DeleteEvent += About_DeleteEvent;
        }

        private void About_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

        private void Image_Drawn(object o, DrawnArgs e)
        {
            Pixbuf pf = pixbuf.ScaleSimple(image.AllocatedWidth, image.AllocatedHeight, InterpType.Bilinear);
            Gdk.CairoHelper.SetSourcePixbuf(e.Cr,pf,0,0);
            e.Cr.Paint();
            e.Cr.Stroke();
        }

        #endregion

    }
}
