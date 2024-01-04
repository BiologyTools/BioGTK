using Gtk;
using Gdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.IO;

namespace BioGTK
{
    
    public class Resolutions : Gtk.Dialog
    {
        #region Properties
        private Builder _builder;
        public static int resolution;
        Resolution[] ress;
#pragma warning disable 649


        [Builder.Object]
        private Gtk.Button okBut;
        [Builder.Object]
        private Gtk.Label label;
        [Builder.Object]
        private Gtk.SpinButton resolutionsBox;

#pragma warning restore 649

        #endregion

        /* A property. */
        public int Resolution
        {
            get { return resolution; }
        }

        #region Constructors / Destructors
        
        /// Create a new Resolutions object, and return it
        /// 
        /// @param ress The array of resolutions to be displayed.
        /// 
        /// @return A new instance of the Resolutions class.
        public static Resolutions Create(Resolution[] re)
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Resolutions.glade", FileMode.Open));
            Resolutions res = new Resolutions(builder, builder.GetObject("resolutions").Handle, re);
            return res;
        }

        /* A constructor. */
        protected Resolutions(Builder builder, IntPtr handle, Resolution[] res) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            resolutionsBox.Adjustment.Upper = res.Length - 1;
            resolutionsBox.Adjustment.StepIncrement = 1;
            resolutionsBox.Adjustment.PageIncrement = 1;
            SetupHandlers();
            App.ApplyStyles(this);
        }

     
        #endregion

        #region Handlers

        /// It sets up the event handlers for the delete event and the ok and cancel buttons.
        protected void SetupHandlers()
        {
            okBut.Clicked += OkBut_Clicked;
            resolutionsBox.Changed += ResolutionsBox_Changed;
            this.DeleteEvent += Resolutions_DeleteEvent;
        }

       /// The function is called when the user clicks the close button on the window. The function
       /// hides the window and returns true
       /// 
       /// @param o The object that the event is being fired from.
       /// @param DeleteEventArgs
       /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-delete.html.en
        private void Resolutions_DeleteEvent(object o, DeleteEventArgs args)
        {
            Hide();
            args.RetVal = true;
        }

        /// When the user changes the value of the dropdown box, the resolution variable is set to the
        /// value of the dropdown box, and the label is set to the value of the resolution variable.
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void ResolutionsBox_Changed(object sender, EventArgs e)
        {
            resolution = resolutionsBox.ValueAsInt;
            label.Text = ress[resolution].ToString();
        }

        /// The OkBut_Clicked function is called when the Ok button is clicked
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        private void OkBut_Clicked(object? sender, EventArgs e)
        {
            this.DefaultResponse = ResponseType.Ok;
            Destroy();
        }

        #endregion

    }
}
