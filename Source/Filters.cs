using AForge;
using Bio;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class FiltersView : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private TreeView tree;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static FiltersView Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Filters.glade", null);
            return new FiltersView(builder, builder.GetObject("filters").Handle);
        }
        ListStore store;
        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected FiltersView(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            InitItems();
            tree.RowActivated += Tree_RowActivated;
            this.DeleteEvent += FiltersView_DeleteEvent;
        }

        private void FiltersView_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }

        /// It creates a new instance of the ApplyFilter class, passing in the filter that was selected
        /// in the treeview, and then shows the ApplyFilter window
        /// 
        /// @param o The object that the event was fired from.
        /// @param RowActivatedArgs 
        private void Tree_RowActivated(object o, RowActivatedArgs args)
        {
            if (args.Path.Indices.Length == 1)
                return;
            Filt f = Filters.GetFilter(args.Path.Indices[0], args.Path.Indices[1]);
            if (f == null)
                return;
            if (f.type == Filt.Type.Base)
            {
                App.applyFilter = ApplyFilter.Create(f, false);
            }
            if (f.type == Filt.Type.Base2)
            {
                App.applyFilter = ApplyFilter.Create(f,true);
            }
            else
            if (f.type == Filt.Type.InPlace)
            {
                App.applyFilter = ApplyFilter.Create(f, false);
            }
            else
            if (f.type == Filt.Type.InPlace2)
            {
                App.applyFilter = ApplyFilter.Create(f, true);
            }
            else
            if (f.type == Filt.Type.InPlacePartial)
            {
                App.applyFilter = ApplyFilter.Create(f, false);
            }
            else
            if (f.type == Filt.Type.Resize)
            {
                App.applyFilter = ApplyFilter.Create(f, false);
            }
            else
            if (f.type == Filt.Type.Rotate)
            {
                App.applyFilter = ApplyFilter.Create(f, false);
            }
            else
            if (f.type == Filt.Type.Transformation)
            {
                App.applyFilter = ApplyFilter.Create(f, false);
            }
            else
            if (f.type == Filt.Type.Copy)
            {
                App.applyFilter = ApplyFilter.Create(f, false);
            }
            App.applyFilter.Show();
            App.applyFilter.Present();
        }
        
        /// It creates a treeview with two columns, the first column is the filter type, the second
        /// column is the filter name
        public void InitItems()
        {
            tree.Model = null;
            tree.ActivateOnSingleClick = false;
            Gtk.TreeViewColumn coordCol = new Gtk.TreeViewColumn();
            coordCol.Title = "Filter";
            Gtk.CellRendererText coordCell = new Gtk.CellRendererText();
            coordCol.PackStart(coordCell, true);
            tree.AppendColumn(coordCol);
            coordCol.AddAttribute(coordCell, "text", 0);
            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string));
            Filters.Init();
            foreach (Filt.Type f in (Filt.Type[])Enum.GetValues(typeof(Filt.Type)))
            {
                Gtk.TreeIter iter = store.AppendValues(f.ToString());
                foreach (Filt fil in Filters.filters)
                {
                    if(fil.type == f)
                    {
                        store.AppendValues(iter, fil.name);
                    }
                }
            }
            tree.Model = store;
        }
        /// It takes a list of filters and creates a treeview with the filters grouped by type
        public void UpdateItems()
        {
            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string));
            foreach (Filt.Type f in (Filt.Type[])Enum.GetValues(typeof(Filt.Type)))
            {
                Gtk.TreeIter iter = store.AppendValues(f.ToString());
                foreach (Filt fil in Filters.filters)
                {
                    if (fil.type == f)
                    {
                        store.AppendValues(iter, fil.name);
                    }
                }
            }
        }
        #endregion

    }
}
