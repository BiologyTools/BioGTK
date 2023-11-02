using Gdk;
using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
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
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/About.glade", FileMode.Open));
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

        /// The function "About_DeleteEvent" sets the "RetVal" property of the "args" parameter to true
        /// and hides the current form.
        /// 
        /// @param o The "o" parameter is of type object and represents the object that raised the
        /// event. In this case, it is not being used in the method.
        /// @param DeleteEventArgs DeleteEventArgs is an event argument class that is used to pass
        /// information about a delete event. It contains properties and methods that provide access to
        /// the event data.
        private void About_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

        /// The function resizes an image and draws it onto a Cairo context.
        /// 
        /// @param o The "o" parameter is the object that triggered the event. In this case, it is the
        /// object that the event handler is attached to.
        /// @param DrawnArgs DrawnArgs is an event argument class that contains information about the
        /// drawing event. It typically includes a Cairo context (Cr) that can be used to perform
        /// drawing operations, such as setting the source image and painting/stroking on the context.
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
