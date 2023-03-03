using Gtk;
using Gdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

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
            Builder builder = new Builder(null, "BioGTK.Glade.Resolutions.glade", null);
            Resolutions res = new Resolutions(builder, builder.GetObject("resolutions").Handle, re);
            //TO DO res.resolutionsBox
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

        private void Resolutions_DeleteEvent(object o, DeleteEventArgs args)
        {
            Hide();
            args.RetVal = true;
        }

        private void ResolutionsBox_Changed(object sender, EventArgs e)
        {
            resolution = resolutionsBox.ValueAsInt;
            label.Text = ress[resolution].ToString();
        }

        private void OkBut_Clicked(object? sender, EventArgs e)
        {
            this.DefaultResponse = ResponseType.Ok;
            Destroy();
        }

        #endregion

    }
}
