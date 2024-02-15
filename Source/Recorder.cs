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
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Recorder.glade", FileMode.Open));
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
            App.ApplyStyles(this);
        }

     
        #endregion

        #region Handlers

        /// It sets up the event handlers for the delete event and the clear button.
        protected void SetupHandlers()
        {
            clearBut.Clicked += ClearBut_Clicked;
            this.FocusInEvent += Recorder_FocusActivated;
        }

        /// The function is called when the user clicks the close button on the recorder window. It
        /// hides the recorder window instead of closing it
        /// 
        /// @param o The object that raised the event.
        /// @param DeleteEventArgs The event arguments.
        private void Recorder_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

       /// When the user clicks on the textbox, the textbox is populated with the log
       /// 
       /// @param sender The object that raised the event.
       /// @param EventArgs The event arguments.
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

        #endregion

    }
}
