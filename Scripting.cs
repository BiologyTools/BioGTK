using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
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

namespace BioGTK
{
    public class Scripting : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
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
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        public static Scripting Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Scripting.glade", null);
            return new Scripting(builder, builder.GetObject("scripting").Handle);
        }
       
        protected Scripting(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            scriptView.RowActivated += ScriptView_RowActivated;
            this.KeyPressEvent += Scripting_KeyPressEvent;
            window = this;
            if (!Directory.Exists(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "//" + "Scripts"))
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "//" + "Scripts");
            if (!Directory.Exists(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "//" + "Tools"))
                Directory.CreateDirectory(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "//" + "Tools");
            InitItems();
            scriptLabel.Text = "NewScript.cs";
        }

        private void Scripting_KeyPressEvent(object o, KeyPressEventArgs args)
        {
            if (args.Event.Key == Gdk.Key.s && args.Event.State == Gdk.ModifierType.ControlMask)
            {
                Save();
            }
        }

        private void ScriptView_RowActivated(object o, RowActivatedArgs args)
        {
            string s = (string)args.Args[0];
            if(scripts.ContainsKey(s))
                selectedItem = scripts[s];
        }
        #endregion

        public static Window window;
        public static string log;
        public static string ImageJPath = ImageJ.ImageJPath;
        public static Dictionary<string,Script> scripts = new Dictionary<string,Script>();
        public static void LogLine(string s)
        {
            log += s + Environment.NewLine;
        }
        public static Dictionary<string, Script> Scripts = new Dictionary<string, Script>();
        public class Script
        {
            public string name;
            public string file;
            public string scriptString;
            public dynamic script;
            public object obj;
            public string output = "";
            public bool done = false;
            public static List<string> usings = new List<string>();
            public Exception ex = null;
            public Thread thread;
            public ScriptType type = ScriptType.script;
            public Script(string file, string scriptStr)
            {
                name = System.IO.Path.GetFileName(file);
                scriptString = scriptStr;
                if (file.EndsWith(".txt") || file.EndsWith(".ijm"))
                    type = ScriptType.imagej;
            }
            public Script(string file)
            {
                name = System.IO.Path.GetFileName(file);
                scriptString = File.ReadAllText(file);
                this.file = file;
                if (file.EndsWith(".txt") || file.EndsWith(".ijm"))
                    type = ScriptType.imagej;
            }
            public static void Run(Script rn)
            {
                scriptName = rn.name;
                Thread t = new Thread(new ThreadStart(RunScript));
                t.Start();
            }
            private static string scriptName = "";
            private static string str = "";
            private static void RunScript()
            {
                Script rn = Scripts[scriptName];
                rn.ex = null;
                if (rn.type == ScriptType.imagej)
                {
                    try
                    {
                        rn.done = false;
                        ImageJ.RunString(rn.scriptString,"", false);
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
                    }
                }
            }
            public static object RunString(string st)
            {
                try
                {
                    string loader =
                  @"//css_reference " + System.Reflection.Assembly.GetExecutingAssembly().GetName().Name + @".dll
                    using System;
                    using System.Windows.Forms;
                    using System.Drawing;
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
            public void Run()
            {
                if (!Scripts.ContainsKey(name))
                    Scripts.Add(name, this);
                scriptName = this.name;
                thread = new Thread(new ThreadStart(RunScript));
                thread.Start();
            }
            public void Stop()
            {
                if(thread!=null)
                thread.Abort();
            }
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
        public class State
        {
            public Event type;
            public static State GetUp(PointD pf,uint mb)
            {
                
                State st = new State();
                st.type = Event.Up;
                st.p = pf;
                st.buts = mb;
                return st;
            }
            public static State GetDown(PointD pf, uint mb)
            {
                State st = new State();
                st.type = Event.Down;
                st.p = pf;
                st.buts = mb;
                return st;
            }
            public static State GetMove(PointD pf, uint mb)
            {
                State st = new State();
                st.type = Event.Move;
                st.p = pf;
                st.buts = mb;
                return st;
            }
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
            public override string ToString()
            {
                return type.ToString() + " ,(" + p.X.ToString() + ", " + p.Y.ToString() + "), " + buts.ToString();
            }

        }
        public enum Event
        {
            Down,
            Up,
            Move,
            None
        }
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
        public static State GetState()
        {
            return state;
        }
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
            Scripts.Clear();
            string st = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
            foreach (string file in Directory.GetFiles(st + "/Scripts"))
            {
                if (!Scripts.ContainsKey(System.IO.Path.GetFileName(file)))
                {
                    Script sc = new Script(file, File.ReadAllText(file));
                    store.AppendValues(sc.name, sc.thread.ThreadState);
                    Scripts.Add(sc.name, sc);
                }
            }
            foreach (string file in Directory.GetFiles(st + "/Tools"))
            {
                if (file.EndsWith(".cs"))
                {
                    if (!Scripts.ContainsKey(System.IO.Path.GetFileName(file)))
                    {
                        Script sc = new Script(file, File.ReadAllText(file));
                        store.AppendValues(sc.name,sc.thread.ThreadState);
                        Scripts.Add(sc.name, sc);
                    }
                }
            }
        }

        public void RefreshItems()
        {
            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string));
            foreach (Script s in Scripts.Values)
            {
                store.AppendValues(s.name, s.thread.ThreadState);
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
        public void RunScriptFile(string file)
        {
            Script sc = new Script(file);
            Scripts.Add(sc.name,sc);
            RefreshItems();
            RunByName(sc.name);
        }
        public static void RunScript(string file)
        {
            Script sc = new Script(file);
            Scripts.Add(sc.name, sc);
            RunByName(sc.name);
        }
        public static void RunString(string st)
        {
            Script.RunString(st);
        }
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
            
        }
        public void Stop()
        {
            if (SelectedItem == null)
                return;
            //We stop this script
            SelectedItem.Stop();
        }
        public static void RunByName(string name)
        {
            Scripts[name].Run();
        }

        private void runToolStripMenuItem_Click(object sender, EventArgs e)
        {
            Run();
        }

        private void openScriptFolderToolStripMenuItem_Click(object sender, EventArgs e)
        {
            string path = System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "\\Scripts";
            System.Diagnostics.Process.Start("explorer.exe", path);
        }

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
        private void runButton_Click(object sender, EventArgs e)
        {
            if (scriptLabel.Text.EndsWith(".ijm"))
            {
                ImageJ.RunString(view.Buffer.Text, ImageView.SelectedImage.ID, headlessBox.Active);
            }
            else
                Run();
        }

        private void scriptLoadBut_Click(object sender, EventArgs e)
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

            Scripts.Add(script.name, script);
            RefreshItems();
        }

        private void Save()
        {
            Gtk.FileChooserDialog filechooser =
    new Gtk.FileChooserDialog("Choose script file to open",
        this,
        FileChooserAction.Save,
        "Cancel", ResponseType.Cancel,
        "Save", ResponseType.Accept);
            filechooser.SetCurrentFolder(System.IO.Path.GetDirectoryName(Environment.ProcessPath));
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            scriptLabel.Text = System.IO.Path.GetFileName(filechooser.Filename);
            File.WriteAllText(filechooser.Filename, view.Buffer.Text);
        }

        private void stopBut_Click(object sender, EventArgs e)
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
