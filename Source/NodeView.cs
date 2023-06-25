using AForge;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class NodeView : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private MenuItem newTabsViewMenu;
        [Builder.Object]
        private MenuItem aboutMenu;
        [Builder.Object]
        private TreeView tree;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static NodeView Create(string[] args)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.NodeView.glade", null);
            return new NodeView(builder, builder.GetObject("nodeView").Handle,args);
        }
        ListStore store;
        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected NodeView(Builder builder, IntPtr handle, string[] args) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            App.Initialize();
            App.nodeView = this;
            SetupHandlers();
            foreach (string item in args)
            {
                BioImage.OpenFile(item);
            }
            InitItems();
        }

        /// It takes a list of images, and for each image, it takes a list of bitmaps, and for each
        /// bitmap, it adds a row to the treeview with the bitmap's coordinate, exposure, and delta
        public void InitItems()
        {
            Gtk.TreeViewColumn coordCol = new Gtk.TreeViewColumn();
            coordCol.Title = "Coordinate";
            Gtk.CellRendererText coordCell = new Gtk.CellRendererText();
            coordCol.PackStart(coordCell, true);

            Gtk.TreeViewColumn expCol = new Gtk.TreeViewColumn();
            expCol.Title = "Exposure";
            Gtk.CellRendererText expCell = new Gtk.CellRendererText();
            expCol.PackStart(expCell, true);

            Gtk.TreeViewColumn deltaCol = new Gtk.TreeViewColumn();
            deltaCol.Title = "Delta";
            Gtk.CellRendererText deltaCell = new Gtk.CellRendererText();
            deltaCol.PackStart(deltaCell, true);

            
            tree.AppendColumn(coordCol);
            tree.AppendColumn(expCol);
            tree.AppendColumn(deltaCol);

            coordCol.AddAttribute(coordCell, "text", 0);
            expCol.AddAttribute(expCell, "text", 1);
            deltaCol.AddAttribute(deltaCell, "text", 2);

            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string), typeof(string));

            foreach (BioImage b in Images.images)
            {
                Gtk.TreeIter iter = store.AppendValues(System.IO.Path.GetFileName(b.Filename));
                foreach (AForge.Bitmap bit in b.Buffers)
                {
                    store.AppendValues(iter, bit.Coordinate.ToString(), bit.Plane.Exposure, bit.Plane.Delta);
                }
            }
            tree.Model = store;
            
        }
        /// It takes a list of images, and for each image, it takes a list of bitmaps, and for each
        /// bitmap, it adds a row to the treeview
        public void UpdateItems()
        {
            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string), typeof(string));

            foreach (BioImage b in Images.images)
            {
                Gtk.TreeIter iter = store.AppendValues(System.IO.Path.GetFileName(b.Filename));
                foreach (AForge.Bitmap bit in b.Buffers)
                {
                    if(bit.Plane != null)
                        store.AppendValues(iter, bit.Coordinate.ToString(), bit.Plane.Exposure, bit.Plane.Delta);
                    else
                        store.AppendValues(iter, bit.Coordinate.ToString(), 0, 0);
                }
            }
            tree.Model = store;

        }
        #endregion

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            DeleteEvent += OnLocalDeleteEvent;
            newTabsViewMenu.ButtonPressEvent += newTabsViewClick;
            aboutMenu.ButtonPressEvent += aboutClick;
            
        }

        /// <summary> Handle Close of Form, Quit Application. </summary>
        /// <param name="sender"> Source of the event. </param>
        /// <param name="a">      Event information to send to registered event handlers. </param>
        protected void OnLocalDeleteEvent(object sender, DeleteEventArgs a)
        {
            Application.Quit();
            a.RetVal = true;
        }

        /// <summary> Handle Click of Button. </summary>
        /// <param name="sender"> Source of the event. </param>
        /// <param name="a">      Event information to send to registered event handlers. </param>
        protected void newTabsViewClick(object sender, EventArgs a)
        {
            BioGTK.TabsView tabsView = BioGTK.TabsView.Create();
            tabsView.Show();
            
        }
        /// <summary> Handle Click of Button. </summary>
        /// <param name="sender"> Source of the event. </param>
        /// <param name="a">      Event information to send to registered event handlers. </param>
        protected void aboutClick(object sender, EventArgs a)
        {
            BioGTK.About about = BioGTK.About.Create();
            about.Show();
        }
        #endregion

    }
}
