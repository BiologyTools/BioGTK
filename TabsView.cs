using Bio;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    /// <summary> Example Test Form for GTKSharp and Glade. </summary>
    public class TabsView : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649

        [Builder.Object]
        private MenuItem openImagesMenu;
        [Builder.Object]
        private MenuItem openOMEImagesMenu;
        [Builder.Object]
        private MenuItem openOMESeriesMenu;
        [Builder.Object]
        private MenuItem openSeriesMenu;
        [Builder.Object]
        private MenuItem addImagesToTabMenu;
        [Builder.Object]
        private MenuItem addOMEImagesToTab;
        [Builder.Object]
        private MenuItem saveSelectedTiff;
        [Builder.Object]
        private MenuItem saveSelectedOME;
        [Builder.Object]
        private MenuItem saveTabOME;
        [Builder.Object]
        private MenuItem saveTabTiffMenu;
        [Builder.Object]
        private MenuItem saveSeriesMenu;
        [Builder.Object]
        private MenuItem imagesToStack;

        [Builder.Object]
        private MenuItem rgbMenu;
        [Builder.Object]
        private MenuItem filteredMenu;
        [Builder.Object]
        private MenuItem rawMenu;
        [Builder.Object]
        private MenuItem emissionMenu;
        [Builder.Object]
        private MenuItem xmlMenu;

        [Builder.Object]
        private MenuItem toolsMenu;
        [Builder.Object]
        private MenuItem setToolMenu;

        [Builder.Object]
        private MenuItem roiManagerMenu;
        [Builder.Object]
        private MenuItem exportROIsToCSVMenu;
        [Builder.Object]
        private MenuItem importROIsFromCSVMenu;
        [Builder.Object]
        private MenuItem exportROIsOfFolderOfImagesMenu;

        [Builder.Object]
        private MenuItem autoThresholdAllMenu;
        [Builder.Object]
        private MenuItem channelsToolMenu;
        [Builder.Object]
        private MenuItem switchRedBlueMenu;

        [Builder.Object]
        private MenuItem rotateFlipMenu;
        [Builder.Object]
        private MenuItem stackToolMenu;


        [Builder.Object]
        private MenuItem to8BitMenu;
        [Builder.Object]
        private MenuItem to16BitMenu;
        [Builder.Object]
        private MenuItem to24BitMenu;
        [Builder.Object]
        private MenuItem to32BitMenu;
        [Builder.Object]
        private MenuItem to48BitMenu;

        [Builder.Object]
        private MenuItem filtersMenu;

        [Builder.Object]
        private MenuItem runMenu;
        [Builder.Object]
        private MenuItem functionsToolMenu;
        [Builder.Object]
        private MenuItem consoleMenu;
        [Builder.Object]
        private MenuItem scriptRunnerMenu;

        [Builder.Object]
        private MenuItem aboutMenu;

        [Builder.Object]
        private Notebook tabsView;

#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static TabsView Create()
        {
            try
            {
                Builder builder = new Builder(null, "BioGTK.Glade.TabsView.glade", null);
                return new TabsView(builder, builder.GetObject("TabsView").Handle);
            }
            catch (Exception e)
            {

                throw;
            }
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected TabsView(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
        }

        #endregion

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            openImagesMenu.ButtonPressEvent += openImagesMenuClick;
            openOMEImagesMenu.ButtonPressEvent += openOMEImagesMenuClick;
            openOMESeriesMenu.ButtonPressEvent += openOMESeriesMenuClick;
            openSeriesMenu.ButtonPressEvent += openSeriesMenuClick;
            addImagesToTabMenu.ButtonPressEvent += addImagesToTabMenuClick;
            addOMEImagesToTab.ButtonPressEvent += openSeriesMenuClick;
            saveSelectedTiff.ButtonPressEvent += saveSelectedTiffClick;
            saveSelectedOME.ButtonPressEvent += saveSelectedOMEClick;
            saveTabOME.ButtonPressEvent += saveTabOMEClick;
            saveTabTiffMenu.ButtonPressEvent += saveTabTiffMenuClick;
            saveSeriesMenu.ButtonPressEvent += saveSeriesMenuClick;
            imagesToStack.ButtonPressEvent += imagesToStackClick;

            rgbMenu.ButtonPressEvent += rgbMenuClick;
            filteredMenu.ButtonPressEvent += filteredMenuClick;
            rawMenu.ButtonPressEvent += rawMenuClick;
            emissionMenu.ButtonPressEvent += emissionMenuClick;
            xmlMenu.ButtonPressEvent += xmlMenuClick;

            toolsMenu.ButtonPressEvent += toolsMenuClick;
            setToolMenu.ButtonPressEvent += setToolMenuClick;

            roiManagerMenu.ButtonPressEvent += roiManagerMenuClick;
            exportROIsToCSVMenu.ButtonPressEvent += exportROIsToCSVMenuClick;
            importROIsFromCSVMenu.ButtonPressEvent += importROIsFromCSVMenuClick;
            exportROIsOfFolderOfImagesMenu.ButtonPressEvent += exportROIsOfFolderOfImagesMenuClick;

            autoThresholdAllMenu.ButtonPressEvent += autoThresholdAllMenuClick;
            channelsToolMenu.ButtonPressEvent += channelsToolMenuClick;
            switchRedBlueMenu.ButtonPressEvent += switchRedBlueMenuClick;

            rotateFlipMenu.ButtonPressEvent += rotateFlipMenuClick;
            stackToolMenu.ButtonPressEvent += stackToolMenuClick;

            to8BitMenu.ButtonPressEvent += to8BitMenuClick;
            to16BitMenu.ButtonPressEvent += to16BitMenuClick;
            to24BitMenu.ButtonPressEvent += to24BitMenuClick;
            to32BitMenu.ButtonPressEvent += to32BitMenuClick;
            to48BitMenu.ButtonPressEvent += to48BitMenuClick;

            filtersMenu.ButtonPressEvent += filtersMenuClick;

            runMenu.ButtonPressEvent += runMenuClick;
            functionsToolMenu.ButtonPressEvent += functionsToolMenuClick;
            consoleMenu.ButtonPressEvent += consoleMenuClick;
            scriptRunnerMenu.ButtonPressEvent += scriptRunnerMenuClick;

            aboutMenu.ButtonPressEvent += AboutClick;

            this.Focused += TabsView_Focused;
        }

        public void AddTab(BioImage im)
        {
            tabsView.Add(ImageView.Create(im));
        }

        private void TabsView_Focused(object o, FocusedArgs args)
        {
            App.tabsView = this;
        }

        protected void openImagesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);

            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            
            BioImage bm = BioImage.OpenFile(filechooser.Filename);
            ImageView view = ImageView.Create(bm);
            tabsView.AppendPage(view, new Gtk.Label(bm.Filename));
            tabsView.Show();
            tabsView.ShowAll();
            view.GoToImage();
            filechooser.Destroy();
        }
        protected void openOMEImagesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);

            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage bm = BioImage.OpenOME(item);
                ImageView view = ImageView.Create(bm);
                tabsView.AppendPage(view, new Gtk.Label(bm.Filename));
            }
            tabsView.ShowAll();
            filechooser.Destroy();
        }
        protected void openOMESeriesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);

            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage[] bm = BioImage.OpenOMESeries(item);
                foreach (BioImage b in bm)
                {
                    ImageView view = ImageView.Create(b);
                    tabsView.AppendPage(view, new Gtk.Label(b.Filename));
                }
            }
            tabsView.ShowAll();
            filechooser.Destroy();
        }
        protected void openSeriesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);

            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage[] bm = BioImage.OpenSeries(item);
                foreach (BioImage b in bm)
                {
                    ImageView view = ImageView.Create(b);
                    tabsView.AppendPage(view, new Gtk.Label(b.Filename));
                }
            }
            tabsView.ShowAll();
            filechooser.Destroy();
        }

        protected void addImagesToTabMenuClick(object sender, EventArgs a)
        {

        }
        protected void addOMEImagesToTabClick(object sender, EventArgs a)
        {

        }

        protected void saveSelectedTiffClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
       new Gtk.FileChooserDialog("Choose the file to open",
           this,
           FileChooserAction.Save,
           "Cancel", ResponseType.Cancel,
           "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            BioImage.Save(filechooser.Filename,ImageView.SelectedImage.ID);
            filechooser.Destroy();
        }
        protected void saveSelectedOMEClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
      new Gtk.FileChooserDialog("Choose the file to open",
          this,
          FileChooserAction.Save,
          "Cancel", ResponseType.Cancel,
          "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            BioImage.SaveOME(filechooser.Filename, ImageView.SelectedImage.ID);
            filechooser.Destroy();
        }
        protected void saveTabOMEClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
      new Gtk.FileChooserDialog("Choose the file to open",
          this,
          FileChooserAction.Save,
          "Cancel", ResponseType.Cancel,
          "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            List<string> list = new List<string>();
            foreach (BioImage b in App.viewer.Images)
            {
                list.Add(b.ID);
            }
            BioImage.SaveOMESeries(list.ToArray(), filechooser.Filename, true);
            filechooser.Destroy();
        }
        protected void saveTabTiffMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
     new Gtk.FileChooserDialog("Choose the file to open",
         this,
         FileChooserAction.Save,
         "Cancel", ResponseType.Cancel,
         "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            List<string> list = new List<string>();
            foreach (BioImage b in App.viewer.Images)
            {
                list.Add(b.ID);
            }
            BioImage.SaveSeries(list.ToArray(), filechooser.Filename);
            filechooser.Destroy();
        }
        protected void saveSeriesMenuClick(object sender, EventArgs a)
        {

        }
        protected void imagesToStackClick(object sender, EventArgs a)
        {

        }
        protected void rgbMenuClick(object sender, EventArgs a)
        {
            App.viewer.Mode = ImageView.ViewMode.RGBImage;
        }
        protected void filteredMenuClick(object sender, EventArgs a)
        {
            App.viewer.Mode = ImageView.ViewMode.Filtered;
        }
        protected void rawMenuClick(object sender, EventArgs a)
        {
            App.viewer.Mode = ImageView.ViewMode.Raw;
        }
        protected void emissionMenuClick(object sender, EventArgs a)
        {
            App.viewer.Mode = ImageView.ViewMode.Emission;
        }
        protected void xmlMenuClick(object sender, EventArgs a)
        {

        }

        protected void toolsMenuClick(object sender, EventArgs a)
        {
            if(App.tools == null)
            App.tools = Tools.Create();
            App.tools.Show();
        }
        protected void setToolMenuClick(object sender, EventArgs a)
        {

        }

        protected void roiManagerMenuClick(object sender, EventArgs a)
        {
            App.roiManager.Show();
        }
        protected void MenuClick(object sender, EventArgs a)
        {

        }
        protected void exportROIsToCSVMenuClick(object sender, EventArgs a)
        {

        }
        protected void importROIsFromCSVMenuClick(object sender, EventArgs a)
        {

        }
        protected void exportROIsOfFolderOfImagesMenuClick(object sender, EventArgs a)
        {

        }

        protected void autoThresholdAllMenuClick(object sender, EventArgs a)
        {

        }
        protected void channelsToolMenuClick(object sender, EventArgs a)
        {
            App.channelsTool = ChannelsTool.Create();
            App.channelsTool.Show();
        }
        protected void switchRedBlueMenuClick(object sender, EventArgs a)
        {

        }

        protected void rotateFlipMenuClick(object sender, EventArgs a)
        {

        }
        protected void stackToolMenuClick(object sender, EventArgs a)
        {

        }

        protected void to8BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To8Bit();
        }
        protected void to16BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To16Bit();
        }
        protected void to24BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To24Bit();
        }
        protected void to32BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To32Bit();
        }
        protected void to48BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To48Bit();
        }

        protected void filtersMenuClick(object sender, EventArgs a)
        {
            App.filters.Show();
        }

        protected void runMenuClick(object sender, EventArgs a)
        {

        }
        protected void functionsToolMenuClick(object sender, EventArgs a)
        {

        }
        protected void consoleMenuClick(object sender, EventArgs a)
        {

        }
        protected void scriptRunnerMenuClick(object sender, EventArgs a)
        {
            App.scripting.Show();
        }

        protected void AboutClick(object sender, EventArgs a)
        {
            About ab = About.Create();
            ab.Show();
        }
        #endregion

    }
}
