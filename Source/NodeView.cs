using AForge;
using Gtk;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class NodeView : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        private readonly Queue<string> pendingImagePaths = new Queue<string>();
        private bool openingImages;
        private Application macApplication;

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

        /// <summary>Creates the main window and queues the supplied image paths for opening.</summary>
        public static NodeView Create(string[] args)
        {
            using var gladeStream = new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/NodeView.glade", FileMode.Open, FileAccess.Read, FileShare.Read);
            Builder builder = new Builder(gladeStream);
            var node = new NodeView(builder, builder.GetObject("nodeView").Handle, args);
            if (AppStorage.IsPackagedMacApp)
                node.RegisterMacDocumentOpen();
            return node;
        }
        ListStore store;
        GLWindow window;
        /* The code you provided is the constructor for the `NodeView` class. */
        protected NodeView(Builder builder, IntPtr handle, string[] args) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            string st = AppStorage.DirectoryPath;
            //Let's make sure the directories we need for startup exist.
            Console.WriteLine("Reading start up folders.");
            System.IO.Directory.CreateDirectory(st + "/Scripts");
            System.IO.Directory.CreateDirectory(st + "/Functions");
            System.IO.Directory.CreateDirectory(st + "/Tools");
            System.IO.Directory.CreateDirectory(st + "/Plugins");
            System.IO.Directory.CreateDirectory(st + "/Models");
            App.nodeView = this;
            App.Initialize();
            SetupHandlers();
            Console.WriteLine("Parsing arguments.");
            App.tabsView = TabsView.Create();
            Console.WriteLine("Initializing nodes.");    
            InitItems();
            App.MoveToMain(this);
            App.ApplyStyles(this);
            Console.WriteLine("NodeView initialized.");
            Shown += (_, _) => ScheduleImageOpen();
            QueueImagePaths(args);
        }

        private void RegisterMacDocumentOpen()
        {
            // Quartz installs the Finder open-document delegate when this app registers.
            macApplication = new Application("com.BioGTK",
                GLib.ApplicationFlags.HandlesOpen | GLib.ApplicationFlags.NonUnique)
            {
                RegisterSession = true
            };
            // GtkSharp's OpenedArgs.Files exposes one IFile instead of the native GFile array.
            macApplication.AddSignalHandler("open",
                new EventHandler<GLib.SignalArgs>(OnMacDocumentOpen), typeof(GLib.SignalArgs));
            macApplication.Register(null);
        }

        private void OnMacDocumentOpen(object sender, GLib.SignalArgs args)
        {
            var files = (IntPtr)args.Args[0];
            var count = (int)args.Args[1];
            var paths = new string[count];
            for (int i = 0; i < count; i++)
            {
                IntPtr file = Marshal.ReadIntPtr(files, i * IntPtr.Size);
                // The URI keeps Unicode filenames intact across the native boundary.
                paths[i] = GLib.FileAdapter.GetObject(file, false).Uri.AbsoluteUri;
            }
            QueueImagePaths(paths);
        }

        private void QueueImagePaths(IEnumerable<string> files)
        {
            foreach (string file in files)
            {
                if (!string.IsNullOrWhiteSpace(file))
                    pendingImagePaths.Enqueue(file);
            }
            ScheduleImageOpen();
        }

        private void ScheduleImageOpen()
        {
            if (!Visible || openingImages || pendingImagePaths.Count == 0)
                return;

            openingImages = true;
            GLib.Idle.Add(() =>
            {
                OpenPendingImages();
                return false;
            });
        }

        private async void OpenPendingImages()
        {
            while (pendingImagePaths.TryDequeue(out string file))
            {
                if (file.StartsWith("file:", StringComparison.OrdinalIgnoreCase) &&
                    Uri.TryCreate(file, UriKind.Absolute, out Uri uri) && uri.IsFile)
                {
                    file = uri.LocalPath;
                }

                try
                {
                    if (!await App.tabsView.OpenPyramidRootsAsTabsAsync(file))
                        throw new IOException("No image was found.");
                }
                catch (Exception ex)
                {
                    using var dialog = new MessageDialog(this, DialogFlags.Modal,
                        MessageType.Error, ButtonsType.Ok, "Could not open {0}\n{1}", file, ex.Message);
                    dialog.Title = "Open Image Failed";
                    dialog.Run();
                }
            }
            openingImages = false;
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
