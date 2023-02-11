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
    
    public class Recorder : Gtk.Window
    {
        #region Properties
        private Builder _builder;
        public static string log = "";
#pragma warning disable 649

        [Builder.Object]
        private Gtk.Button clearBut;
        [Builder.Object]
        private Gtk.TextView textBox;

#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// > Create a new instance of the Recorder class
        /// 
        /// @return A new instance of the Recorder class.
        public static Recorder Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Recorder.glade", null);
            return new Recorder(builder, builder.GetObject("recorderWindow").Handle);
        }

       /// It adds a line to the log
       /// 
       /// @param s The string to add to the log.
        public static void AddLine(string s)
        {
            log += s + Environment.NewLine;
        }
        /* The constructor for the class. */
        protected Recorder(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
        }

     
        #endregion

        #region Handlers

        /// It sets up the event handlers for the delete event and the clear button.
        protected void SetupHandlers()
        {
            DeleteEvent += OnLocalDeleteEvent;
            clearBut.Clicked += ClearBut_Clicked;
            this.FocusActivated += Recorder_FocusActivated;
        }

        private void Recorder_FocusActivated(object sender, EventArgs e)
        {
            textBox.Buffer.Text = log;
        }

        /// This function clears the textbox when the clear button is clicked
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        private void ClearBut_Clicked(object? sender, EventArgs e)
        {
            textBox.Buffer.Text = "";
            log = "";
        }

        /// When the user clicks the X button on the window, the application will quit.
        /// 
        /// @param sender The object that raised the event.
        /// @param DeleteEventArgs This is the event arguments that are passed to the event handler.
        protected void OnLocalDeleteEvent(object sender, DeleteEventArgs a)
        {
            Application.Quit();
            a.RetVal = true;
        }

        #endregion

    }
}
