using AForge;
using BioLib;
using Gdk;
using Gtk;
using omero.gateway.model;
using org.checkerframework.checker.units.qual;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
namespace BioGTK
{
    public class OMERO : Gtk.Window
    {
        public class Image
        {
            public Gdk.Pixbuf pixbuf;
            public long dataset;
            public string name;
        }
        public static Progress prog;
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.SearchEntry searchBox;
        [Builder.Object]
        private Gtk.IconView view;
        [Builder.Object]
        private Gtk.ComboBox comboBox;
        [Builder.Object]
        private Gtk.ScrolledWindow scrollWind;
        [Builder.Object]
        private Gtk.CheckButton loadIconsBut;
        [Builder.Object]
        private Gtk.MenuItem uploadMenu;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        public static int IconWidth = 50;
        public static int IconHeight = 50;
        public static string dataset = "";
        public static long id;
        public static List<string> files = new List<string>();
        public static bool uploading = false;
        public List<Image> images = new List<Image>();
        private ListStore store;
        /// It creates a new Search object, which is a Gtk.Window, and returns it
        /// 
        /// @return A new instance of the Search class.
        public static OMERO Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/omero.glade", FileMode.Open));
            return new OMERO(builder, builder.GetObject("omeroViewer").Handle);
        }

        /* The constructor for the TextInput class. */
        protected OMERO(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            InitItems();
            this.SizeAllocated += OMERO_SizeAllocated;
            searchBox.Changed += SearchBox_Changed;
            comboBox.Changed += ComboBox_Changed;
            uploadMenu.ButtonPressEvent += UploadMenu_ButtonPressEvent;
            view.ItemActivated += View_ItemActivated;
            // Enable drag-and-drop for this window
            Gtk.Drag.DestSet(this, DestDefaults.All, new TargetEntry[]
            {
            new TargetEntry("text/uri-list", TargetFlags.OtherApp, 0)
            }, Gdk.DragAction.Copy);
            this.DragDataReceived += View_DragDataReceived;
            App.ApplyStyles(this);
        }

        private void UploadMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            files.Clear();
            FileChooserDialog filechooser = new FileChooserDialog("Choose files to import.", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            string[] sts = filechooser.Filenames;
            filechooser.Hide();
            if(BioLib.OMERO.password == "")
            {
                Login log = Login.Create();
                int status = log.Run();
                if (status != (int)ResponseType.Ok)
                {
                    return;
                }
            }
            files.AddRange(sts);
            foreach (string s in sts)
            {
                Thread ths = new Thread(StartUpload);
                ths.Start();
            }
        }


        public static void StartUpload()
        {
            foreach (var f in files)
            {
                prog = Progress.Create("Uploading", "Uploading image.",f);
                prog.Show();
                uploading = true;
                Thread th = new Thread(UpdateProgress);
                th.Start();
                fi = f;
                Thread ths = new Thread(Upload);
                ths.Start();
            }
        }
        static string fi;
        private static void Upload()
        {
            BioLib.OMERO.Upload(BioImage.OpenFile(fi), id);
            //BioLib.OMERO.Upload(BioImage.OpenFile(fi), id);
            uploading = false;
        }
        public static void UpdateProgress()
        {
            do
            {
                OMERO.prog.ProgressValue = BioLib.OMERO.progress;
                Thread.Sleep(250);
            } while (OMERO.uploading);
            OMERO.prog.Hide();
        }

        private void View_DragDataReceived(object o, DragDataReceivedArgs args)
        {
            files.Clear();
            // Convert the received data to a string
            string receivedData = System.Text.Encoding.UTF8.GetString(args.SelectionData.Data);

            // Parse the URIs (file paths)
            string[] uris = receivedData.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string uri in uris)
            {
                if (Uri.TryCreate(uri, UriKind.Absolute, out Uri fileUri) && fileUri.IsFile)
                {
                    string filePath = fileUri.LocalPath;
                    files.Add(filePath);
                }
            }
            StartUpload();
            args.RetVal = true; // Indicate the drop was handled
        }

        private void View_ItemActivated(object o, ItemActivatedArgs args)
        {
            BioLib.OMERO.ReConnect();
            TreePath path = args.Path;
            store.GetIter(out TreeIter iter, path);
            string selectedItem;
            if (loadIconsBut.Active)
                selectedItem = (string)store.GetValue(iter, 1);
            else
                selectedItem = (string)store.GetValue(iter, 0);
            string[] sts = selectedItem.Split(' ');
            foreach (var item in images)
            {
                if (item.name.Contains(sts[0]))
                {
                    BioImage bm = BioLib.OMERO.GetImage(sts[0], item.dataset);
                    App.tabsView.AddTab(bm);
                }
            }
        }

        private void OMERO_SizeAllocated(object o, SizeAllocatedArgs args)
        {
            view.HeightRequest = scrollWind.AllocatedHeight;
        }

        private void ComboBox_Changed(object sender, EventArgs e)
        {
            TreeIter iter;
            comboBox.Model.GetIterFromString(out iter, comboBox.Active.ToString());
            string selectedItem = (string)comboBox.Model.GetValue(iter, 0);
            string[] sts = selectedItem.Split(' ');
            dataset = sts[0];
            id = long.Parse(sts[1]);
            InitIcons();
        }
        #endregion
        private void InitIcons()
        {
            List<DatasetData> dbs = BioLib.OMERO.GetDatasetsData();
            if (loadIconsBut.Active)
            {
                var listStore = new ListStore(typeof(Gdk.Pixbuf), typeof(string));
                view.PixbufColumn = 0;
                view.TextColumn = 1;
                images.Clear();

                if (comboBox.Active != -1)
                {
                    TreeIter iter;
                    comboBox.Model.GetIterFromString(out iter, comboBox.Active.ToString());
                    string selectedItem = (string)comboBox.Model.GetValue(iter, 0);
                    string[] sts = selectedItem.Split(' ');
                    List<string> str = BioLib.OMERO.GetDatasetFiles(id);
                    Dictionary<long, Pixbuf> dict = BioLib.OMERO.GetThumbnails(str.ToArray(), IconWidth, IconHeight);
                    if (dict != null)
                        foreach (var item in dict)
                        {
                            Image image = new Image();
                            image.pixbuf = item.Value;
                            image.dataset = id;
                            image.name = BioLib.OMERO.GetNameFromID(item.Key);
                            images.Add(image);
                        }
                }
                foreach (var im in images)
                {
                    listStore.AppendValues(im.pixbuf, im.name);
                }
                view.Model = listStore;
                store = listStore;
            }
            else
            {
                var listStore = new ListStore(typeof(string));
                view.TextColumn = 0;
                images.Clear();

                if (comboBox.Active != -1)
                    foreach (var db in dbs)
                    {
                        TreeIter iter;
                        comboBox.Model.GetIterFromString(out iter, comboBox.Active.ToString());
                        string selectedItem = (string)comboBox.Model.GetValue(iter, 0);
                        string[] sts = selectedItem.Split(' ');
                        if (db.getId() == long.Parse(sts[1]))
                            continue;
                        List<long> str = BioLib.OMERO.GetDatasetIds(db.getId());
                        if (str.Count == 0)
                            BioLib.OMERO.ReConnect();
                        foreach (var item in str)
                        {
                            Image image = new Image();
                            //.pixbuf = item.Value;
                            image.dataset = db.getId();
                            image.name = BioLib.OMERO.GetNameFromID(item);
                            images.Add(image);
                        }
                    }
                foreach (var im in images)
                {
                    listStore.AppendValues(im.name);
                }
                view.Model = listStore;
                store = listStore;
            }
        }
        public void InitItems()
        {
            var comStore = new ListStore(typeof(string));
            List<DatasetData> dbs = BioLib.OMERO.GetDatasetsData();
            foreach (var im in dbs)
            {
                comStore.AppendValues(im.getName() + " " + im.getId());
            }
            comboBox.Model = comStore;

        }
        public void UpdateItems(string filt)
        {
            var listStore = new ListStore(typeof(Gdk.Pixbuf), typeof(string));
            view.PixbufColumn = 0;
            view.TextColumn = 1;
            foreach (var im in images)
            {
                if(im.name.Contains(filt) || filt == "")
                listStore.AppendValues(im.pixbuf, im.name);
            }
            view.Model = listStore;
        }

        private void SearchBox_Changed(object sender, EventArgs e)
        {
            UpdateItems(searchBox.Buffer.Text);
        }
        
    }
}
