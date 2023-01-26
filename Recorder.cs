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
    public class Recorder : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        public static string log = "";
#pragma warning disable 649

        /// <summary> Connects to the SendButton on the Glade Window. </summary>
        [Builder.Object]
        private Gtk.Button clearBut;
        [Builder.Object]
        private Gtk.Entry textBox;

#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static Recorder Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Recorder.glade", null);
            return new Recorder(builder, builder.GetObject("recorderWindow").Handle);
        }

        public static void AddLine(string s)
        {
            log += s + Environment.NewLine;
        }
        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected Recorder(Builder builder, IntPtr handle) : base(handle)
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
            clearBut.Clicked += ClearBut_Clicked;
        }

        private void ClearBut_Clicked(object? sender, EventArgs e)
        {
            textBox.Text = "";
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
