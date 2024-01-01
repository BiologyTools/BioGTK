using BioGTK;
using Gtk;
using Pango;
using System;
using System.Collections.Generic;
using System.IO;

namespace BioGTK
{
    public class BioConsole : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        private string pred = "";
        private List<string> preds = new List<string>();
        public static bool onTab = false;
        public static bool useBioformats = true;
        public static bool headless = false;
        public static bool resultInNewTab = false;
        int line = 0;
#pragma warning disable 649
        [Builder.Object]
        private Label predLabel;
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
        private CheckButton resultsBox;
        [Builder.Object]
        private RadioButton selRadioBut;
        [Builder.Object]
        private RadioButton tabRadioBut;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
      
        /// Create a new BioConsole object using the Glade file "BioGTK.Glade.BioConsole.glade"
        /// @return A new instance of the BioConsole class.
        public static BioConsole Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/BioConsole.glade", FileMode.Open));
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
            resultsBox.Clicked += ResultsBox_Clicked;
            selRadioBut.Clicked += SelRadioBox_Clicked;
            tabRadioBut.Clicked += TabRadioBox_Clicked;
            consoleBox.Buffer.Changed += Buffer_Changed;
            consoleBox.KeyPressEvent += Console_KeyPressEvent;
            this.DeleteEvent += BioConsole_DeleteEvent;
        }

        private int Measure(string s)
        {
            // Create a Pango layout using the label's context
            using (var layout = new Pango.Layout(predLabel.PangoContext))
            {
                // Set the text and font description of the layout
                layout.SetText(s);
                layout.FontDescription = FontDescription.FromString("Sans 12"); // Specify your desired font here
                // Get the size of the layout
                int width, height;
                layout.GetPixelSize(out width, out height);
                return width;
            }
        }

        bool skip = false;
        string chs = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
        private void Buffer_Changed(object sender, EventArgs e)
        {
            if (consoleBox.Buffer.CharCount == 0)
                return;
            if(skip)
            {
                skip = false;
                return;
            }
            pred = pred.Replace("\t", "").Replace("\r","").Replace("\n","");
            if (pred.Contains(' '))
            pred = consoleBox.Buffer.Text.Remove(0,consoleBox.Buffer.Text.LastIndexOf(' '));
            else
                pred = consoleBox.Buffer.Text.Replace("\t", "").Replace("\r", "").Replace("\n", "");
            bool ends = false;
            char ch = ' ';
            int i = 0;
            int ind = -1;
            foreach (Char cs in pred)
            {
                ends = false;
                foreach (Char c in chs)
                {
                    if (cs == c)
                    {
                        ends = true;
                        ch = c;
                    }
                }
                if (!ends)
                    ind = i;
                i++;
            }
            if (ind!=-1)
                pred = pred.Remove(0, ind+1);
            
            preds.Clear();
            i = 0;
            ind = -1;
            foreach(var cs in ImageJ.Macro.Commands)
            {
                if (cs.Value.Name.StartsWith(pred))
                    preds.Add(cs.Value.Name);
            }
            foreach (var cs in ImageJ.Macro.Functions)
            {
                if (cs.Value[0].Name.StartsWith(pred))
                    preds.Add(cs.Value[0].Name);
            }
            predLabel.Text = "";
            foreach(var item in preds)
            {
                if (this.AllocatedWidth > Measure(predLabel.Text))
                {
                    predLabel.Text += item + ", ";
                }
                else break;
            }
            if(consoleBox.Buffer.Text.EndsWith('\t'))
            {
                string s = consoleBox.Buffer.Text.TrimEnd('\t');
                skip = true;
                consoleBox.Buffer.Text = s.Remove(s.Length - pred.Length, pred.Length) + preds[0];
            }
        }

        /// If the user clicks the checkbox, then the resultInNewTab variable is set to the value of the
        /// checkbox
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void ResultsBox_Clicked(object sender, EventArgs e)
        {
            resultInNewTab = resultsBox.Active;
        }

        /// The BioConsole_DeleteEvent function is called when the user clicks the close button on the
        /// BioConsole window. The function hides the BioConsole window and returns true to the
        /// DeleteEventArgs object
        /// 
        /// @param o The object that fired the event.
        /// @param DeleteEventArgs This is the event that is being passed to the event handler.
        private void BioConsole_DeleteEvent(object o, DeleteEventArgs args)
        {
            Hide();
            args.RetVal = true;
        }

        /// If the user presses the "w" key, the line variable is incremented and the textBox is set to
        /// the last line of the consoleBox.
        /// If the user presses the "s" key, the line variable is decremented and the textBox is set to
        /// the last line of the consoleBox
        /// 
        /// @param o The object that the event is being called from.
        /// @param KeyPressEventArgs This is the event that is triggered when a key is pressed.
        private void Console_KeyPressEvent(object o, KeyPressEventArgs args)
        {
            if (args.Event.Key == Gdk.Key.w)
            {
                line++;
                string[] s = consoleBox.Buffer.Text.Split(Environment.NewLine);
                textBox.Buffer.Text = s[s.Length - 1 - line];
            }
            if (args.Event.Key == Gdk.Key.s)
            {
                line--;
                string[] s = consoleBox.Buffer.Text.Split(Environment.NewLine);
                textBox.Buffer.Text = s[s.Length - 1 - line];
            }
        }

       /// If the tabRadioBut is active, then onTab is true
       /// 
       /// @param sender The object that raised the event.
       /// @param EventArgs The event arguments.
        private void TabRadioBox_Clicked(object sender, EventArgs e)
        {
            onTab = tabRadioBut.Active;
        }

        /// If the user clicks on the "Select" radio button, the variable "onTab" is set to true
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void SelRadioBox_Clicked(object sender, EventArgs e)
        {
            onTab = selRadioBut.Active;
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
            ImageJ.RunOnImage(textBox.Buffer.Text, headless, onTab, useBioformats, resultsBox.Active);
            consoleBox.Buffer.Text += textBox.Buffer.Text + Environment.NewLine;
            textBox.Buffer.Text = "";
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
