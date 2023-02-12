using BioGTK;
using Gtk;
using System;

namespace BioGTK
{
    public class BioConsole : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        public static bool onTab = false;
        public static bool useBioformats = false;
        public static bool headless = false;
        int line = 0;
#pragma warning disable 649
        [Builder.Object]
        private Button imagejBut;
        [Builder.Object]
        private Button runBut;
        [Builder.Object]
        private TextView textBox;
        [Builder.Object]
        private TextView consoleBox;
        [Builder.Object]
        private CheckButton headlessBox;
        [Builder.Object]
        private CheckButton bioformatsBox;
        [Builder.Object]
        private RadioButton selRadioBut;
        [Builder.Object]
        private RadioButton tabRadioBut;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static BioConsole Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Console.glade", null);
            return new BioConsole(builder, builder.GetObject("console").Handle);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected BioConsole(Builder builder, IntPtr handle) : base(handle)
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
            runBut.Clicked += RunBut_Clicked;
            imagejBut.Clicked += ImagejBut_Clicked;
            headlessBox.Clicked += HeadlessBox_Clicked;
            bioformatsBox.Clicked += BioformatsBox_Clicked;
            selRadioBut.Clicked += SelRadioBox_Clicked;
            tabRadioBut.Clicked += TabRadioBox_Clicked;
            this.KeyPressEvent += Console_KeyPressEvent;
            this.DeleteEvent += BioConsole_DeleteEvent;
        }

        private void BioConsole_DeleteEvent(object o, DeleteEventArgs args)
        {
            Hide();
            args.RetVal = false;
        }

        private void Console_KeyPressEvent(object o, KeyPressEventArgs args)
        {
            if (args.Event.Key == Gdk.Key.Up)
            {
                line++;
                string[] s = consoleBox.Buffer.Text.Split(Environment.NewLine);
                textBox.Buffer.Text = s[s.Length - 1 - line];
            }
            if (args.Event.Key == Gdk.Key.Down)
            {
                line--;
                string[] s = consoleBox.Buffer.Text.Split(Environment.NewLine);
                textBox.Buffer.Text = s[s.Length - 1 - line];
            }
        }

        private void TabRadioBox_Clicked(object sender, EventArgs e)
        {
            onTab = tabRadioBut.Active;
        }

        private void SelRadioBox_Clicked(object sender, EventArgs e)
        {
            onTab = selRadioBut.Active;
        }

        private void BioformatsBox_Clicked(object sender, EventArgs e)
        {
            useBioformats = bioformatsBox.Active;
        }

        private void HeadlessBox_Clicked(object sender, EventArgs e)
        {
            headless = headlessBox.Active;
        }

        /// The function takes the text from the textbox, passes it to the ImageJ plugin, and then
        /// updates the image
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The ImageJ.RunOnImage method returns a string.
        private void ImagejBut_Clicked(object sender, EventArgs e)
        {
            if (ImageView.SelectedImage == null)
                return;
            ImageJ.RunOnImage(textBox.Buffer.Text, headless, onTab, useBioformats);
            consoleBox.Buffer.Text += textBox.Buffer.Text + Environment.NewLine;
            textBox.Buffer.Text = "";
            string filename = "";

            if (ImageView.SelectedImage.ID.EndsWith(".ome.tif"))
            {
                filename = System.IO.Path.GetFileNameWithoutExtension(ImageView.SelectedImage.ID);
                filename = filename.Remove(filename.Length - 4, 4);
            }
            else
                filename = System.IO.Path.GetFileNameWithoutExtension(ImageView.SelectedImage.ID);
            string file = System.IO.Path.GetDirectoryName(ImageView.SelectedImage.ID) + "/" + filename + ".ome.tif";
            if (ImageView.SelectedImage.ID.EndsWith(".ome.tif"))
                ImageView.SelectedImage.Update();
            else
                App.tabsView.AddTab(BioImage.OpenOME(file));
        }

        /// It runs the code in the textbox and prints the output to the console
        /// 
        /// @param sender The object that called the event.
        /// @param EventArgs The event arguments.
        private void RunBut_Clicked(object sender, EventArgs e)
        {
            object o = Scripting.Script.RunString(textBox.Buffer.Text);
            consoleBox.Buffer.Text += textBox.Buffer.Text + Environment.NewLine + o.ToString() + Environment.NewLine;
            textBox.Buffer.Text = "";
        }

        #endregion

        
    }
}
