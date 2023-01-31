using Bio;
using Gtk;
using loci.formats.gui;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    
    public class TabsView : Window
    {
        #region Properties

        private Builder _builder;
        public List<ImageView> viewers = new List<ImageView>();
        public static ImageView SelectedViewer
        {
            get { return App.viewer; }
        }
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
        private Gtk.CheckMenuItem rgbMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem filteredMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem rawMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem emissionMenu;
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
        
       /// It creates a new instance of the TabsView class, which is a class that inherits from
       /// Gtk.Window
       /// 
       /// @return A new instance of the TabsView class.
        public static TabsView Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.TabsView.glade", null);
            return new TabsView(builder, builder.GetObject("TabsView").Handle);
        }

        /* Setting up the UI. */
        protected TabsView(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            filteredMenu.Active = true;
            SetupHandlers();
        }

        #endregion

        #region Handlers

        /// This function sets up the event handlers for the menu items
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
            rgbMenu.ButtonPressEvent += RgbMenu_ButtonPressEvent;
            filteredMenu.ButtonPressEvent += FilteredMenu_ButtonPressEvent;
            rawMenu.ButtonPressEvent += RawMenu_ButtonPressEvent;
            emissionMenu.ButtonPressEvent += EmissionMenu_ButtonPressEvent;
            tabsView.SwitchPage += TabsView_SwitchPage;
            this.WindowStateEvent += TabsView_WindowStateEvent;
        }

        /// If the window is minimized, hide all the image viewers. If the window is restored, show all
        /// the image viewers
        /// 
        /// @param o The object that the event is being called from.
        /// @param WindowStateEventArgs
        /// https://developer.gnome.org/gtkmm/stable/classGtk_1_1WindowStateEvent.html
        private void TabsView_WindowStateEvent(object o, WindowStateEventArgs args)
        {
            if ((args.Event.ChangedMask & Gdk.WindowState.Iconified) != 0)
            {
                foreach (ImageView v in viewers)
                {
                    v.Hide();
                }
            }
            
            else if ((args.Event.ChangedMask & Gdk.WindowState.Maximized) == 0 && (args.Event.ChangedMask & Gdk.WindowState.Iconified) == 0)
            {
                foreach (ImageView v in viewers)
                {
                    if(!v.Visible)
                        v.Present();
                }
            }
            
        }

        /// When the user switches to a new tab, the viewer for that tab is presented
        /// 
        /// @param o The object that called the event.
        /// @param SwitchPageArgs 
        private void TabsView_SwitchPage(object o, SwitchPageArgs args)
        {
            viewers[(int)args.PageNum].Present();
        }

        /// If the emission menu is active, then deactivate it. Otherwise, activate it
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonPressEventArgs The event that is triggered when a button is pressed.
        private void EmissionMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (emissionMenu.Active)
                emissionMenu.Active = false;
            else
                emissionMenu.Active = true;
            filteredMenu.Active = false;
            rawMenu.Active = false;
            rgbMenu.Active = false;
        }
        /// If the rawMenu is active, then set it to inactive. Otherwise, set it to active
        /// 
        /// @param o the object that the event is being called on
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void RawMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (rawMenu.Active)
                rawMenu.Active = false;
            else
                rawMenu.Active = true;
            filteredMenu.Active = false;
            rgbMenu.Active = false;
            emissionMenu.Active = false;
        }
        /// If the filtered menu is active, then deactivate it. Otherwise, activate it. Deactivate all
        /// other menus
        /// 
        /// @param o the object that the event is being called on
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk3/stable/GtkWidget.html#GtkWidget-button-press-event
        private void FilteredMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (filteredMenu.Active)
                filteredMenu.Active = false;
            else
                filteredMenu.Active = true;
            rgbMenu.Active = false;
            rawMenu.Active = false;
            emissionMenu.Active = false;
        }
        /// If the rgbMenu is active, then set it to inactive. Otherwise, set it to active
        /// 
        /// @param o the object that the event is being called on
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk3/stable/GtkWidget.html#GtkWidget-button-press-event
        private void RgbMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (rgbMenu.Active)
                rgbMenu.Active = false;
            else
                rgbMenu.Active = true;
            filteredMenu.Active = false;
            rawMenu.Active = false;
            emissionMenu.Active = false;
        }

        /// It adds a new tab to the tab control
        /// 
        /// @param BioImage This is the image that you want to display.
        public void AddTab(BioImage im)
        {
            tabsView.Add(ImageView.Create(im));
        }

       /// When the TabsView is focused, set the tabsView variable in the App class to the TabsView
       /// 
       /// @param o The object that is being focused
       /// @param FocusedArgs This is a class that contains the following properties:
        private void TabsView_Focused(object o, FocusedArgs args)
        {
            App.tabsView = this;
        }

       /// It opens a file chooser dialog, and when the user selects a file, it creates a new BioImage
       /// object, creates a new ImageView object, and adds the ImageView object to the list of viewers
       /// 
       /// @param sender The object that raised the event.
       /// @param EventArgs The event arguments.
       /// 
       /// @return The response type of the dialog.
        protected void openImagesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);
            filechooser.SelectMultiple= true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage b = BioImage.OpenFile(item);
                ImageView view = ImageView.Create(b);
                viewers.Add(view);
                Label dummy = new Gtk.Label(b.file);
                dummy.Visible = false;
                tabsView.AppendPage(dummy, new Gtk.Label(b.Filename));
                view.Present();
            }
            this.ShowAll();
            filechooser.Destroy();
        }
        /// It opens a file chooser dialog, and when the user selects a file, it opens the file and adds
        /// it to the list of open images
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response type of the dialog.
        protected void openOMEImagesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage b = BioImage.OpenOME(item);
                ImageView view = ImageView.Create(b);
                viewers.Add(view);
                Label dummy = new Gtk.Label(b.file);
                dummy.Visible = false;
                tabsView.AppendPage(dummy, new Gtk.Label(b.Filename));
                view.Present();
            }
            this.ShowAll();
            filechooser.Destroy();
        }
        /// It opens a file chooser dialog, and when the user selects a file, it opens the file as a
        /// BioImage, creates an ImageView for it, and adds the ImageView to the notebook
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return A list of file names.
        protected void openOMESeriesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage[] bm = BioImage.OpenOMESeries(item);
                foreach (BioImage b in bm)
                {
                    ImageView view = ImageView.Create(b);
                    Label dummy = new Gtk.Label(b.file);
                    dummy.Visible = false;
                    tabsView.AppendPage(dummy, new Gtk.Label(b.Filename));
                }
            }
            tabsView.ShowAll();
            filechooser.Destroy();
        }
        /// It opens a file chooser dialog, and when the user selects a file, it opens the file as a
        /// series of images, and adds each image to the notebook
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response type of the dialog.
        protected void openSeriesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage[] bm = BioImage.OpenSeries(item);
                foreach (BioImage b in bm)
                {
                    ImageView view = ImageView.Create(b);
                    Label dummy = new Gtk.Label(b.file);
                    dummy.Visible = false;
                    tabsView.AppendPage(dummy, new Gtk.Label(b.Filename));
                }
            }
            tabsView.ShowAll();
            filechooser.Destroy();
        }

        /// It opens a file chooser dialog, and then adds the selected images to the currently selected
        /// viewer
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response type of the filechooser dialog.
        protected void addImagesToTabMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage b = BioImage.OpenFile(item);
                SelectedViewer.AddImage(b);
            }
            this.ShowAll();
            filechooser.Destroy();
        }
        /// It opens a file chooser dialog, and when the user selects a file, it opens the file as an
        /// OME image, and adds it to the currently selected viewer
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response type of the filechooser dialog.
        protected void addOMEImagesToTabClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
       new Gtk.FileChooserDialog("Choose file to open",
           this,
           FileChooserAction.Open,
           "Cancel", ResponseType.Cancel,
           "Open", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                BioImage b = BioImage.OpenOME(item);
                SelectedViewer.AddImage(b);
            }
            this.ShowAll();
            filechooser.Destroy();
        }

        /// It creates a file chooser dialog, and if the user selects a file, it saves the selected
        /// image to that file
        /// 
        /// @param sender The object that triggered the event.
        /// @param EventArgs This is the event that is being passed to the method.
        /// 
        /// @return The response type of the dialog.
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
        /// This function saves the selected image in the OME-TIFF format
        /// 
        /// @param sender The object that triggered the event.
        /// @param EventArgs This is the event that is being called.
        /// 
        /// @return The response type of the filechooser dialog.
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
        /// This function saves the current series of images to an OME-TIFF file
        /// 
        /// @param sender The object that triggered the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response from the filechooser dialog.
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
        /// It saves the current tab as a tiff file
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs 
        /// 
        /// @return The response type of the filechooser dialog.
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
        /// This function is called when the user clicks the "Save Series" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void saveSeriesMenuClick(object sender, EventArgs a)
        {

        }
        /// This function is called when the user clicks the "Images to Stack" button
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes that contain event data.
        protected void imagesToStackClick(object sender, EventArgs a)
        {

        }
        /// When the user clicks on the RGB menu item, the viewer's mode is set to RGB
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void rgbMenuClick(object sender, EventArgs a)
        {
            App.viewer.Mode = ImageView.ViewMode.RGBImage;
        }
        /// When the user clicks on the "Filtered" menu item, the viewer's mode is set to "Filtered"
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void filteredMenuClick(object sender, EventArgs a)
        {
            App.viewer.Mode = ImageView.ViewMode.Filtered;
        }
        /// When the user clicks on the "Raw" menu item, the viewer's mode is set to "Raw"
        /// 
        /// @param sender The object that triggered the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void rawMenuClick(object sender, EventArgs a)
        {
            App.viewer.Mode = ImageView.ViewMode.Raw;
        }
        /// When the user clicks on the emission menu item, the viewer's mode is set to emission
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void emissionMenuClick(object sender, EventArgs a)
        {
            App.viewer.Mode = ImageView.ViewMode.Emission;
        }
        /// This function is called when the user clicks on the XML menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void xmlMenuClick(object sender, EventArgs a)
        {

        }

        /// If the tools window is not open, open it
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void toolsMenuClick(object sender, EventArgs a)
        {
            if(App.tools == null)
            App.tools = Tools.Create();
            App.tools.Show();
        }
        /// This function is called when the user clicks on the "Tools" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void setToolMenuClick(object sender, EventArgs a)
        {

        }

        /// This function is called when the user clicks on the ROI Manager menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the class that contains the event data.
        protected void roiManagerMenuClick(object sender, EventArgs a)
        {
            App.roiManager.Show();
        }
        /// This function is called when a menu item is clicked
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void MenuClick(object sender, EventArgs a)
        {

        }
        /// This function is called when the user clicks on the "Export ROIs to CSV" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void exportROIsToCSVMenuClick(object sender, EventArgs a)
        {

        }
        /// This function is called when the user clicks on the "Import ROIs from CSV" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void importROIsFromCSVMenuClick(object sender, EventArgs a)
        {

        }
        /// This function is called when the user clicks on the "Export ROIs of Folder of Images" menu
        /// item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        protected void exportROIsOfFolderOfImagesMenuClick(object sender, EventArgs a)
        {

        }

        /// This function is called when the user clicks on the "Auto Threshold All" menu item.
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void autoThresholdAllMenuClick(object sender, EventArgs a)
        {

        }
        /// It creates a new instance of the ChannelsTool class, and then shows it
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void channelsToolMenuClick(object sender, EventArgs a)
        {
            App.channelsTool = ChannelsTool.Create();
            App.channelsTool.Show();
        }
        /// This function is called when the user clicks on the "Switch Red and Blue" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes that contain event data.
        protected void switchRedBlueMenuClick(object sender, EventArgs a)
        {

        }

        /// This function is called when the user clicks on the "Rotate/Flip" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void rotateFlipMenuClick(object sender, EventArgs a)
        {

        }
        /// This function is called when the user clicks on the stack tool menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void stackToolMenuClick(object sender, EventArgs a)
        {

        }

        /// The function is called when the user clicks on the "To 8-bit" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void to8BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To8Bit();
        }
        /// This function converts the selected image to 16 bit
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void to16BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To16Bit();
        }
        /// This function converts the selected image to 24 bit
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void to24BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To24Bit();
        }
        /// It converts the selected image to 32 bit
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void to32BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To32Bit();
        }
        /// This function converts the selected image to 48 bit
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void to48BitMenuClick(object sender, EventArgs a)
        {
            ImageView.SelectedImage.To48Bit();
        }

        /// It shows the filters menu.
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes that contain event data.
        protected void filtersMenuClick(object sender, EventArgs a)
        {
            App.filters.Show();
        }

        /// This function is called when the user clicks on the "Run" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void runMenuClick(object sender, EventArgs a)
        {

        }
        /// This function is called when the user clicks on the functions tool menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void functionsToolMenuClick(object sender, EventArgs a)
        {

        }
        /// This function is called when the user clicks on the console menu button
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void consoleMenuClick(object sender, EventArgs a)
        {

        }
        /// It's a function that runs when the user clicks on the "Script Runner" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments.
        protected void scriptRunnerMenuClick(object sender, EventArgs a)
        {
            App.scripting.Show();
        }

        /// It creates a new instance of the About class, and then shows it
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void AboutClick(object sender, EventArgs a)
        {
            About ab = About.Create();
            ab.Show();
        }
        #endregion

    }
}
