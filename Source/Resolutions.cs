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
    
    public class Resolutions : Gtk.Window
    {
        #region Properties

        
        private Builder _builder;
        internal int resolution;
#pragma warning disable 649

        
        [Builder.Object]
        private Gtk.Button okBut;
        [Builder.Object]
        private Gtk.Button cancelBut;
        [Builder.Object]
        private Gtk.Entry resolutionsBox;

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
        public static Resolutions Create(Resolution[] ress)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Resolutions.glade", null);
            Resolutions res = new Resolutions(builder, builder.GetObject("resolutions").Handle);
            //TO DO res.resolutionsBox
            return res;
        }

        /* A constructor. */
        protected Resolutions(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
        }

     
        #endregion

        #region Handlers

        /// It sets up the event handlers for the delete event and the ok and cancel buttons.
        protected void SetupHandlers()
        {
            DeleteEvent += OnLocalDeleteEvent;
            okBut.Clicked += OkBut_Clicked;
            cancelBut.Clicked += CancelBut_Clicked;
        }

        private void CancelBut_Clicked(object? sender, EventArgs e)
        {
           
        }

        private void OkBut_Clicked(object? sender, EventArgs e)
        {
            
        }

        protected void OnLocalDeleteEvent(object sender, DeleteEventArgs a)
        {
            a.RetVal = true;
            Hide();
        }

        #endregion

    }
}
