using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Threading;
using CSScripting;
using csscript;
using CSScriptLib;
using Gtk;
using AForge;
using BioGTK;
using System.Reflection.Metadata;

namespace BioGTK
{
    public class Scripting : Window
    {
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private TreeView scriptView;
        [Builder.Object]
        private TextView view;
        [Builder.Object]
        private TextView outputBox;
        [Builder.Object]
        private TextView errorBox;
        [Builder.Object]
        private TextView logBox;
        [Builder.Object]
        private Button scriptLoadBut;
        [Builder.Object]
        private Button saveBut;
        [Builder.Object]
        private Button runBut;
        [Builder.Object]
        private Button stopBut;
        [Builder.Object]
        private Label scriptLabel;
        [Builder.Object]
        private CheckButton headlessBox;
        [Builder.Object]
        private Notebook tabsView;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// It creates a new instance of the Scripting class, which is a class that inherits from the
        /// Gtk.Window class
        /// 
        /// @return A new instance of the Scripting class.
        public static Scripting Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Scripting.glade", FileMode.Open));
            return new Scripting(builder, builder.GetObject("scripting").Handle);
        }
       
        /* Creating a new instance of the Scripting class. */
        protected Scripting(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            scriptView.RowActivated += ScriptView_RowActivated;
            scriptView.ButtonPressEvent += ScriptView_ButtonPressEvent;
            this.KeyPressEvent += Scripting_KeyPressEvent;
            saveBut.ButtonPressEvent += SaveBut_ButtonPressEvent;
            runBut.ButtonPressEvent += runButton_Click;
            scriptLoadBut.ButtonPressEvent += scriptLoadBut_Click;
            outputBox.ButtonPressEvent += OutputBox_ButtonPressEvent;
            errorBox.ButtonPressEvent += OutputBox_ButtonPressEvent;
            logBox.ButtonPressEvent += OutputBox_ButtonPressEvent;
            stopBut.ButtonPressEvent += stopBut_Click;
            tabsView.SwitchPage += TabsView_SwitchPage;
            window = this;
            if (!Directory.Exists(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "//" + "Scripts"))
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "//" + "Scripts");
            if (!Directory.Exists(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "//" + "Tools"))
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "//" + "Tools");
            InitItems();
            scriptLabel.Text = "NewScript.cs";
            App.ApplyStyles(this);
        }

        /// When the user switches tabs, the text in the textview is updated to the current log, error,
        /// or output
        /// 
        /// @param o The object that called the event
        /// @param SwitchPageArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-notebook-switch-page.html.en
        private void TabsView_SwitchPage(object o, SwitchPageArgs args)
        {
            logBox.Buffer.Text = log;
            errorBox.Buffer.Text = error;
            outputBox.Buffer.Text = output;
        }

        /// This function is called when the button is pressed. It sets the text of the textview to the
        /// text of the string
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs The event arguments for the button press event.
        private void OutputBox_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            logBox.Buffer.Text = log;
            errorBox.Buffer.Text = error;
            outputBox.Buffer.Text = output;
        }

        /// > When the user clicks on the ScriptView, refresh the items
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk-sharp/stable/Gtk.ButtonPressEventArgs.html
        private void ScriptView_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            RefreshItems();
        }

        /// The SaveBut_ButtonPressEvent function is called when the Save button is pressed
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs The event arguments for the button press event.
        private void SaveBut_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Save();
        }

        /// If the user presses the "s" key while holding down the control key, then call the Save()
        /// function
        /// 
        /// @param o The object that the event is being called from.
        /// @param KeyPressEventArgs This is the event that is triggered when a key is pressed.
        private void Scripting_KeyPressEvent(object o, KeyPressEventArgs args)
        {
            if (args.Event.Key == Gdk.Key.s && args.Event.State == Gdk.ModifierType.ControlMask)
            {
                Save();
            }
        }

       /// When a row is activated, the first argument is the name of the script, and if the script
       /// exists, it is set as the selected item.
       /// 
       /// @param o The object that called the event
       /// @param RowActivatedArgs This is a class that contains the arguments that are passed to the
       /// event handler.
        private void ScriptView_RowActivated(object o, RowActivatedArgs args)
        {
            TreeView treeView = (TreeView)o;
            TreePath path = args.Path;
            TreeIter iter;

            // Get the TreeIter for the selected row
            if (treeView.Model.GetIter(out iter, path))
            {
                // Get the value of the "Text" column for the selected row
                string text = (string)treeView.Model.GetValue(iter, 0);
                if (scripts.ContainsKey(text))
                {
                    selectedItem = scripts[text];
                    view.Buffer.Text = selectedItem.scriptString;
                    scriptLabel.Text = selectedItem.name;
                }
            }
        }
        #endregion

        /* Creating a static class called Window, and a static string called log. */
        public static Window window;
        /* Declaring a static variable called log. */
        public static string log;
        public static string output;
        public static string error;
        public static string ImageJPath = Fiji.ImageJPath;
        public static Dictionary<string,Script> scripts = new Dictionary<string,Script>();
        public static void LogLine(string s)
        {
            log += s + Environment.NewLine;
        }
        /* It takes a string, adds using statements, wraps it in a class, and then runs it */
        public class Script
        {
            public string name;
            public string file;
            public string scriptString;
            public bool exit = false;
            public dynamic script;
            public object obj;
            public string output = "";
            public bool done = false;
            public static List<string> usings = new List<string>();
            public Exception ex = null;
            public Thread thread;
            public ScriptType type = ScriptType.script;
            /* Creating a new script object. */
            public Script(string file, string scriptStr)
            {
                name = System.IO.Path.GetFileName(file);
                scriptString = scriptStr;
                if (file.EndsWith(".txt") || file.EndsWith(".ijm"))
                    type = ScriptType.imagej;
            }
            /* Reading the file and storing the file name, file path, and file contents in the
            variables name, file, and scriptString. */
            public Script(string file)
            {
                name = System.IO.Path.GetFileName(file);
                scriptString = File.ReadAllText(file);
                this.file = file;
                if (file.EndsWith(".txt") || file.EndsWith(".ijm"))
                    type = ScriptType.imagej;
            }
            /// It creates a new thread and starts it. 
            /// 
            /// The thread is started by calling the RunScript function. 
            /// @param Script The script you want to run.
            public static void Run(Script rn)
            {
                scriptName = rn.name;
                Thread t = new Thread(new ThreadStart(RunScript));
                t.Start();
            }
            private static string scriptName = "";
            private static string str = "";
            /// It runs a script in a separate thread
            private static void RunScript()
            {
                Script rn = scripts[scriptName];
                rn.ex = null;
                if (rn.type == ScriptType.imagej)
                {
                    try
                    {
                        rn.done = false;
                        Fiji.RunString(rn.scriptString,"", false);
                        rn.done = true;
                    }
                    catch (Exception e)
                    {
                        rn.ex = e;
                    }
                }
                else
                {
                    try
                    {
                        rn.done = false;
                        rn.script = CSScript.Evaluator.LoadCode(rn.scriptString);
                        rn.obj = rn.script.Load();
                        rn.output = rn.obj.ToString();
                        rn.done = true;
                        
                    }
                    catch (Exception e)
                    {
                        rn.ex = e;
                        error = e.Message + Environment.NewLine + e.StackTrace;
                    }
                }
            }
            /// It takes a string, adds using statements, wraps it in a class, and then runs
            /// it.
            /// 
            /// @param st The string to be executed
            /// 
            /// @return The return value of the last statement in the script.
            public static object RunString(string st)
            {
                try
                {
                    string loader =
                  @"//css_reference " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + @".dll
                    using System;
                    using Gtk;
                    using System.Threading;
                    using BioGTK;
                    using AForge;";
                    foreach (string s in usings)
                    {
                        loader += usings + Environment.NewLine;
                    }
                    loader += @"
                    public class Loader
                    {
                        public object Load()
                        {" +
                            st + @";
                            return true;
                        }
                    }";
                    dynamic script = CSScript.Evaluator.LoadCode(loader);
                    return script.Load();
                }
                catch (Exception e)
                {
                    MessageDialog md = new MessageDialog(window,
                DialogFlags.DestroyWithParent, MessageType.Info,
                ButtonsType.Close, e.Message + ", " + e.Source);
                    return e;
                }
            }
            /// It creates a new thread and starts it
            public void Run()
            {
                if (!scripts.ContainsKey(name))
                    scripts.Add(name, this);
                scriptName = this.name;
                thread = new Thread(new ThreadStart(RunScript));
                thread.Start();
            }
            /// It stops the thread
            public void Stop()
            {
                exit = true;
            }
            /// If the thread is not null, return the name and the thread state. Otherwise, return the
            /// name
            /// 
            /// @return The name of the thread and the state of the thread.
            public override string ToString()
            {
                if (thread != null)
                {
                    return name.ToString() + ", " + thread.ThreadState.ToString();
                }
                else
                    return name.ToString();
            }
        }
        /* It's a class that represents a mouse event */
        /* It's a class that holds the state of the mouse */
        public class State
        {
            public Event type;
            /// > This function returns a new State object with the type set to Event.Up, the point set
            /// to the point passed in, and the buttons set to the buttons passed in
            /// 
            /// @param PointD A point with double precision.
            /// @param mb Mouse button
            /// 
            /// @return A new state object is being returned.
            public static State GetUp(PointD pf,uint mb)
            {
                
                State st = new State();
                st.type = Event.Up;
                st.p = pf;
                st.buts = mb;
                return st;
            }
            /// > This function returns a new state object with the type set to Event.Move, the point
            /// set to the point passed in, and the mouse buttons set to the mouse buttons passed in
            /// 
            /// @param PointD A point with double precision.
            /// @param mb Mouse button
            /// 
            /// @return A new state object with the type of event, the point, and the mouse button.
            public static State GetMove(PointD pf, uint mb)
            {
                State st = new State();
                st.type = Event.Move;
                st.p = pf;
                st.buts = mb;
                return st;
            }

            /// It returns a new State object with the type set to Down, the point set to a new PointD
            /// object, and the buttons set to 
            /// 
            /// @return A new State object is being returned.
            public static State GetDown(PointD pf, uint mb)
            {
                State st = new State();
                st.type = Event.Down;
                st.p = pf;
                st.buts = mb;
                return st;
            }

            /// It returns a new State object with the type set to None, the point set to a new PointD
            /// object, and the buttons set to 0
            /// 
            /// @return A new State object is being returned.
            public static State GetNone()
            {
                State st = new State();
                st.type = Event.None;
                st.p = new PointD();
                st.buts = 0;
                return st;
            }

            public PointD p;
            public uint buts;
            public bool processed = false;
            public Gdk.Key? key;
            public override string ToString()
            {
                return type.ToString() + " ,(" + p.X.ToString() + ", " + p.Y.ToString() + "), " + buts.ToString();
            }

        }
        /* Defining an enum. */
        public enum Event
        {
            Down,
            Up,
            Move,
            None
        }
        /* Defining an enum. */
        public enum ScriptType
        {
            tool,
            script,
            imagej
        }
        Script selectedItem;
        private Script SelectedItem
        {
            get
            {
                return selectedItem;
            }
        }
        private static State state;
        /// It returns the state of the game.
        /// 
        /// @return The state of the game.
        public static State GetState()
        {
            return state;
        }

        public static bool Exit(string name)
        {
            if(scripts.ContainsKey(name))
            {
                return scripts[name].exit;
            }
            return false;
        }

        /// If the state is null, return. If the state is not null, set the state to the new state. If
        /// the state is not null and the new state is the same as the old state, set the processed flag
        /// to true
        /// 
        /// @param State The state of the game.
        /// 
        /// @return The state of the current object.
        public static void UpdateState(State s)
        {
            if (s == null)
                return;
            if (state == null)
                state = s;
            if (s.p.X == state.p.X && s.p.Y == state.p.Y && s.type == state.type)
            {
                state.processed = true;
            }
            else
            state = s;
        }
        /// It loads all the scripts in the Scripts and Tools folders into the TreeView
        public void InitItems()
        {
            Gtk.TreeViewColumn typeCol = new Gtk.TreeViewColumn();
            typeCol.Title = "Script";
            Gtk.CellRendererText typeCell = new Gtk.CellRendererText();
            typeCol.PackStart(typeCell, true);
            scriptView.AppendColumn(typeCol);
            typeCol.AddAttribute(typeCell, "text", 0);

            Gtk.TreeViewColumn statCol = new Gtk.TreeViewColumn();
            statCol.Title = "Status";
            Gtk.CellRendererText statCell = new Gtk.CellRendererText();
            statCol.PackStart(statCell, true);
            scriptView.AppendColumn(statCol);
            statCol.AddAttribute(statCell, "text", 1);

            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string));
            scriptView.Model = store;
            scripts.Clear();
            string st = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
            foreach (string file in Directory.GetFiles(st + "/Scripts"))
            {
                if (!scripts.ContainsKey(System.IO.Path.GetFileName(file)))
                {
                    Script sc = new Script(file, File.ReadAllText(file));
                    if(sc.thread == null)
                        store.AppendValues(sc.name, ThreadState.Unstarted.ToString());
                    else
                        store.AppendValues(sc.name, sc.thread.ThreadState.ToString());
                    scripts.Add(sc.name, sc);
                }
            }
            foreach (string file in Directory.GetFiles(st + "/Tools"))
            {
                if (file.EndsWith(".cs"))
                {
                    if (!scripts.ContainsKey(System.IO.Path.GetFileName(file)))
                    {
                        Script sc = new Script(file, File.ReadAllText(file));
                        if (sc.thread == null)
                            store.AppendValues(sc.name, ThreadState.Unstarted.ToString());
                        else
                            store.AppendValues(sc.name, sc.thread.ThreadState.ToString());
                        scripts.Add(sc.name, sc);
                    }
                }
            }
        }

        /// It takes the list of scripts and puts them into a tree view
        public void RefreshItems()
        {
            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string));
            foreach (Script sc in scripts.Values)
            {
                if (sc.thread == null)
                    store.AppendValues(sc.name, ThreadState.Unstarted.ToString());
                else
                    store.AppendValues(sc.name, sc.thread.ThreadState.ToString());
            }
            scriptView.Model = store;
        }
        /*
        public void RefreshStatus()
        {
            errorBox.Buffer.Text = "";
            RefreshItems();
            foreach (ListViewItem item in scriptView.SelectedItems)
            {
                Script s = (Script)item.Tag;
                //We update item text to show Script status.
                item.Text = s.ToString();
                outputBox.Buffer.Text = s.output;
                if (s.ex != null)
                {
                    string[] sps = s.ex.Message.Split('>');
                    //ListViewItem it = new ListViewItem(s.ex.ToString());
                    for (int i = 1; i < sps.Length; i++)
                    {
                        ListViewItem er = new ListViewItem(sps[i]);
                        er.Tag = s.ex;
                        errorView.Items.Add(er);
                    }
                    
                }
            }
            foreach (ListViewItem item in scriptView.SelectedItems)
            {
                Script s = (Script)item.Tag;
                //We update item text to show Script status.
                item.Text = s.ToString();
            }
            logBox.Buffer.Text = log;
            //We scroll to end of text so we see latest log output.
            if (logBox.SelectionStart != logBox.Buffer.Text.Length)
            {
                logBox.SelectionStart = logBox.Buffer.Text.Length;
                logBox.ScrollToCaret();
            }
        }
        */
        /// Runs a script file by name
        /// 
        /// @param file The filename of the script you want to run.
        public static void RunByName(string name)
        {
            scripts[name].Run();
        }
        /// It runs a script file
        /// 
        /// @param file The file path of the script you want to run.
        public void RunScriptFile(string file)
        {
            Script sc = new Script(file);
            scripts.Add(sc.name,sc);
            RefreshItems();
            RunByName(sc.name);
        }
        /// It creates a new script object, adds it to the dictionary, and then runs it
        /// 
        /// @param file The file path to the script.
        public static void RunScript(string file)
        {
            Script sc = new Script(file);
            scripts.Add(sc.name, sc);
            RunByName(sc.name);
        }
        /// It runs a string as a Bio script
        /// 
        /// @param st The string to run.
        public static object RunString(string st)
        {
            return Script.RunString(st);
        }
        /// It runs the script that is currently selected in the list
        /// 
        /// @return The output of the script is being returned.
        public void Run()
        {
            log = "";
            outputBox.Buffer.Text = "";
            logBox.Buffer.Text = "";
            if (SelectedItem == null)
                return;
            //We run this script
            Script sc = SelectedItem;
            sc.scriptString = view.Buffer.Text;
            sc.output = "";
            sc.Run();
            outputBox.Buffer.Text = sc.output;
            logBox.Buffer.Text = log;
            RefreshItems();
        }
        /// We stop the script.
        /// 
        /// @return The SelectedItem is being returned.
        public void Stop()
        {
            if (SelectedItem == null)
                return;
            //We stop this script
            SelectedItem.Stop();
            RefreshItems();
        }
        /// It runs the program.
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.       
        private void runToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Run();
        }
        /// It opens the folder where the scripts are stored.
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void openScriptFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string path = System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "\\Scripts";
            System.Diagnostics.Process.Start("explorer.exe", path);
        }
        /// It refreshes the items in the list view.
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void refreshToolStripMenuItem_Click(object sender, EventArgs e)
        {
            RefreshItems();
        }
        /*
        private void scriptView_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (scriptView.SelectedItems.Count == 0)
                return;
            ListViewItem item = scriptView.SelectedItems[0];
            Script s = (Script)item.Tag;
            textBox.Buffer.Text = s.scriptString;
            scriptLabel.Text = s.name;
        }
        */
        /// If the script ends with .ijm, then run it in ImageJ, otherwise run it in C#
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void runButton_Click(object sender, ButtonPressEventArgs args)
        {
            if (scriptLabel.Text.EndsWith(".ijm"))
            {
                Fiji.RunString(view.Buffer.Text, ImageView.SelectedImage.ID, headlessBox.Active);
            }
            else
                Run();
        }

        /// It opens a file dialog, reads the file, and adds it to a dictionary
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The file name of the file that was selected.
        private void scriptLoadBut_Click(object sender, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
    new Gtk.FileChooserDialog("Choose script file to open",
        this,
        FileChooserAction.Open,
        "Cancel", ResponseType.Cancel,
        "Save", ResponseType.Accept);
            filechooser.SetCurrentFolder(System.IO.Path.GetDirectoryName(Environment.ProcessPath));
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;

            Script script = new Script(filechooser.Filename, File.ReadAllText(filechooser.Filename));
            script.name = System.IO.Path.GetFileName(filechooser.Filename);
            view.Buffer.Text = script.scriptString;
            scriptLabel.Text = System.IO.Path.GetFileName(filechooser.Filename);

            scripts.Add(script.name, script);
            RefreshItems();
        }

        /// It creates a file chooser dialog, sets the current folder to the directory of the current
        /// process, and if the user clicks the save button, it writes the contents of the text view to
        /// the file
        /// 
        /// @return The file name of the file that was selected.
        private void Save()
        {
            Gtk.FileChooserDialog filechooser =
    new Gtk.FileChooserDialog("Choose script filename to save",
        this,
        FileChooserAction.Save,
        "Cancel", ResponseType.Cancel,
        "Save", ResponseType.Accept);
            filechooser.SetCurrentFolder(System.IO.Path.GetDirectoryName(Environment.ProcessPath));
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            scriptLabel.Text = System.IO.Path.GetFileName(filechooser.Filename);
            File.WriteAllText(filechooser.Filename, view.Buffer.Text);
            if(!scripts.ContainsKey(scriptLabel.Text))
            {
                scripts.Add(scriptLabel.Text,new Script(filechooser.Filename, view.Buffer.Text));
            }
            RefreshItems();
            filechooser.Destroy();
        }

        /// It stops the timer and resets the timer to 0
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        private void stopBut_Click(object sender, ButtonPressEventArgs args)
        {
            Stop();
        }

        /*
        private void errorBox_SelectionChanged(object sender, EventArgs e)
        {
            Script sc = (Script)scriptView.SelectedItems[0].Tag;
            Exception ex = sc.ex;
        }

        private void errorView_SelectedIndexChanged(object sender, EventArgs e)
        {
            Exception ex = (Exception)errorView.SelectedItems[0].Tag;
            string exs = ex.Message.Substring(ex.Message.IndexOf('('), ex.Message.IndexOf(')'));
            string ls = exs.Substring(1, exs.IndexOf(',')-1);
            int line = int.Parse(ls);
            string c = exs.Substring(exs.IndexOf(',') + 1, exs.IndexOf(")") - exs.IndexOf(',') - 1);
            int cr = int.Parse(c);
        }
        */
    }
}
