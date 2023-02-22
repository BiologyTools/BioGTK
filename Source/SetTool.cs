using Gtk;
using javax.swing.text;
using System;
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
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static SetTool Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.SetTool.glade", null);
            return new SetTool(builder, builder.GetObject("setTool").Handle);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected SetTool(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            tree.RowActivated += TreeView_RowActivated;
            this.FocusActivated += SetTool_FocusActivated;
            this.DeleteEvent += SetTool_DeleteEvent;
            InitItems();
        }

        private void SetTool_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

        private void SetTool_FocusActivated(object sender, EventArgs e)
        {
            UpdateItems();
        }
        #endregion
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
