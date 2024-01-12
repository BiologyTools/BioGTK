using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class Search : Window
    {
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.SearchEntry searchBox;
        [Builder.Object]
        private Gtk.TreeView treeView;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// It creates a new Search object, which is a Gtk.Window, and returns it
        /// 
        /// @return A new instance of the Search class.
        public static Search Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Search.glade", FileMode.Open));
            return new Search(builder, builder.GetObject("search").Handle);
        }

        /* The constructor for the TextInput class. */
        protected Search(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            App.ApplyStyles(this);

            Gtk.TreeViewColumn coordCol = new Gtk.TreeViewColumn();
            coordCol.Title = "Command";
            Gtk.CellRendererText coordCell = new Gtk.CellRendererText();
            coordCol.PackStart(coordCell, false);
            treeView.AppendColumn(coordCol);
            coordCol.AddAttribute(coordCell, "text", 0);

            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string));
            foreach (ImageJ.Macro.Command s in ImageJ.Macro.Commands.Values)
            {
                Gtk.TreeIter iter = store.AppendValues(s.Name);
            }
            foreach (Scripting.Script s in Scripting.scripts.Values)
            {
                Gtk.TreeIter iter = store.AppendValues(s.name);
            }
            treeView.Model = store;
            treeView.RowActivated += TreeView_RowActivated;
            searchBox.Changed += SearchBox_Changed;
        }
        #endregion
        private void TreeView_RowActivated(object o, RowActivatedArgs args)
        {
            TreeIter iter;
            if (treeView.Model.GetIter(out iter, args.Path))
            {
                string itemName = (string)treeView.Model.GetValue(iter, 0);
                if(!itemName.EndsWith(".txt") || !itemName.EndsWith(".ijm") && !itemName.EndsWith(".cs"))
                {
                    ImageJ.RunOnImage("run(\"" + itemName + "\")",BioConsole.headless,BioConsole.onTab,BioConsole.useBioformats,BioConsole.resultInNewTab);
                }
                else
                    Scripting.RunByName(itemName);
            }
        }
        private void SearchBox_Changed(object sender, EventArgs e)
        {
            ScrollToItem(treeView, treeView.Model, searchBox.Buffer.Text);
        }
        static void ScrollToItem(TreeView listView, ITreeModel listStore, string itemName)
        {
            TreeIter iter;
            if (listStore.GetIterFirst(out iter))
            {
                do
                {
                    if (((string)listStore.GetValue(iter, 0)).StartsWith(itemName))
                    {
                        TreePath path = listStore.GetPath(iter);
                        listView.ScrollToCell(path, null, true, 0, 0);
                        break;
                    }
                }
                while (listStore.IterNext(ref iter));
            }
        }
    }
}
