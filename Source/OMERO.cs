using AForge;
using BioLib;
using com.sun.corba.se.spi.orb;
using Gdk;
using Gtk;
using javax.swing.text;
using omero.gateway.model;
using org.springframework.orm.jpa.vendor;
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
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        public static int IconWidth = 50;
        public static int IconHeight = 50;
        public string database = "";
        public List<Image> images = new List<Image>();
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
            App.ApplyStyles(this);
            InitItems();
            this.SizeAllocated += OMERO_SizeAllocated;
            searchBox.Changed += SearchBox_Changed;
            comboBox.Changed += ComboBox_Changed;
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
            database = sts[0];
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
                    foreach (var db in dbs)
                    {
                        TreeIter iter;
                        comboBox.Model.GetIterFromString(out iter, comboBox.Active.ToString());
                        string selectedItem = (string)comboBox.Model.GetValue(iter, 0);
                        string[] sts = selectedItem.Split(' ');
                        if (db.getId() == long.Parse(sts[1]))
                            continue;
                        List<string> str = BioLib.OMERO.GetDatasetFiles(db.getId());
                        Dictionary<long, Pixbuf> dict = BioLib.OMERO.GetThumbnails(str.ToArray(), IconWidth, IconHeight);
                        foreach (var item in dict)
                        {
                            Image image = new Image();
                            image.pixbuf = item.Value;
                            image.dataset = item.Key;
                            image.name = BioLib.OMERO.GetNameFromID(item.Key);
                            images.Add(image);
                        }
                    }
                foreach (var im in images)
                {
                    listStore.AppendValues(im.pixbuf, im.name);
                }
                view.Model = listStore;
                // Handle item selection
                view.ItemActivated += (o, args) =>
                {
                    TreePath path = args.Path;
                    listStore.GetIter(out TreeIter iter, path);
                    string selectedItem = (string)listStore.GetValue(iter, 1);
                    string[] sts = selectedItem.Split(' ');
                    long id = long.Parse(sts[1]);
                    BioImage bm = BioLib.OMERO.GetImage(id);
                    App.tabsView.AddTab(bm);
                };
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
                        foreach (var item in str)
                        {
                            Image image = new Image();
                            //.pixbuf = item.Value;
                            image.dataset = item;
                            image.name = BioLib.OMERO.GetNameFromID(item);
                            images.Add(image);
                        }
                    }
                foreach (var im in images)
                {
                    listStore.AppendValues(im.name);
                }
                view.Model = listStore;
                // Handle item selection
                view.ItemActivated += (o, args) =>
                {
                    TreePath path = args.Path;
                    listStore.GetIter(out TreeIter iter, path);
                    string selectedItem = (string)listStore.GetValue(iter, 1);
                    string[] sts = selectedItem.Split(' ');
                    long id = long.Parse(sts[1]);
                    BioImage bm = BioLib.OMERO.GetImage(id);
                    App.tabsView.AddTab(bm);
                };
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
            InitIcons();
        }

        private void SearchBox_Changed(object sender, EventArgs e)
        {
            UpdateItems(searchBox.Buffer.Text);
        }
        
    }
}
