using Gtk;
using javax.swing.text;
using System;
using System.IO;
using System.Threading;

namespace BioGTK
{
    public partial class SetTool : Window
    {
        #region Properties
        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        int line = 0;
#pragma warning disable 649
        [Builder.Object]
        private TreeView tree;
        [Builder.Object]
        private Menu contextMenu;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// "Create a new instance of the SetTool class, using the Builder class to load the glade file
        /// and get the handle of the main window."
        /// 
        /// The Builder class is a class that is used to load the glade file and get the handle of the
        /// main window
        /// 
        /// @return A new instance of the SetTool class.
        public static SetTool Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/SetTool.glade", FileMode.Open));
            return new SetTool(builder, builder.GetObject("setTool").Handle);
        }
        /* The constructor of the class. */
        protected SetTool(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            tree.RowActivated += TreeView_RowActivated;
            this.FocusActivated += SetTool_FocusActivated;
            this.DeleteEvent += SetTool_DeleteEvent;
            InitItems();
        }

        /// The function is called when the user clicks the close button on the window. It sets the
        /// return value of the event to true, which tells the window to close
        /// 
        /// @param o The object that triggered the event.
        /// @param DeleteEventArgs The event arguments.
        private void SetTool_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

        /// When the user clicks on the "Focus" button, the function "SetTool_FocusActivated" is called
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        private void SetTool_FocusActivated(object sender, EventArgs e)
        {
            UpdateItems();
        }
        #endregion
        /// It creates two columns in the treeview, and then adds the text from the first and second
        /// columns of the liststore to the first and second columns of the treeview.
        public void InitItems()
        {
            Gtk.TreeViewColumn coordCol = new Gtk.TreeViewColumn();
            coordCol.Title = "Tool";
            Gtk.CellRendererText coordCell = new Gtk.CellRendererText();
            coordCol.PackStart(coordCell, true);

            Gtk.TreeViewColumn expCol = new Gtk.TreeViewColumn();
            expCol.Title = "State";
            Gtk.CellRendererText expCell = new Gtk.CellRendererText();
            expCol.PackStart(expCell, true);

            tree.AppendColumn(coordCol);
            tree.AppendColumn(expCol);

            coordCol.AddAttribute(coordCell, "text", 0);
            expCol.AddAttribute(expCell, "text", 1);

            UpdateItems();
        }
        /// It updates the treeview with the current state of the scripts
        public void UpdateItems()
        {
            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string), typeof(string));

            foreach (Scripting.Script s in Scripting.scripts.Values)
            {
                if (s.thread == null)
                    store.AppendValues(s.name, ThreadState.Unstarted.ToString());
                else
                    store.AppendValues(s.name, s.thread.ThreadState.ToString());
            }
            tree.Model = store;
        }

        /// When a row is activated, get the value of the first column, and if it's a script, run it
        /// 
        /// @param o The object that the event was called on.
        /// @param RowActivatedArgs
        /// https://developer.gnome.org/gtk3/stable/GtkTreeView.html#GtkTreeView-row-activated
        /// 
        /// @return The TreeView.Model.GetValue(iter, 0) is returning the value of the first column of
        /// the selected row.
        private void TreeView_RowActivated(object o, RowActivatedArgs args)
        {
            TreeView treeView = (TreeView)o;
            TreePath path = args.Path;
            TreeIter iter;
            string s;
            // Get the TreeIter for the selected row
            if (treeView.Model.GetIter(out iter, path))
            {
                // Get the value of the "Text" column for the selected row
                s = (string)treeView.Model.GetValue(iter, 0);
            }
            else
                return;
            if (Scripting.scripts.ContainsKey(s))
            {
                Scripting.Script sc = Scripting.scripts[s];
                if (sc.thread == null)
                    sc.Run();
                else
                if(sc.thread.ThreadState == System.Threading.ThreadState.Running || sc.thread.ThreadState!= System.Threading.ThreadState.WaitSleepJoin)
                {
                    sc.Stop();
                }
                else
                {
                    sc.Run();
                }
            }
            UpdateItems();
        }
    }
}
