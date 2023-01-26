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
    /// <summary> Example Test Form for GTKSharp and Glade. </summary>
    public class Resolutions : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        internal int resolution;
#pragma warning disable 649

        /// <summary> Connects to the SendButton on the Glade Window. </summary>
        [Builder.Object]
        private Gtk.Button okBut;
        [Builder.Object]
        private Gtk.Button cancelBut;
        [Builder.Object]
        private Gtk.Entry resolutionsBox;

#pragma warning restore 649

        #endregion

        public int Resolution
        {
            get { return resolution; }
        }

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static Resolutions Create(Resolution[] ress)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Resolutions.glade", null);
            Resolutions res = new Resolutions(builder, builder.GetObject("resolutions").Handle);
            //TO DO res.resolutionsBox
            return res;
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected Resolutions(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
        }

     
        #endregion

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
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

        /// <summary> Handle Close of Form, Quit Application. </summary>
        /// <param name="sender"> Source of the event. </param>
        /// <param name="a">      Event information to send to registered event handlers. </param>
        protected void OnLocalDeleteEvent(object sender, DeleteEventArgs a)
        {
            Application.Quit();
            a.RetVal = true;
        }

        #endregion

    }
}
