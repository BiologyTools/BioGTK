using AForge;
using Bio;
using Gtk;
using ij.plugin.filter;
using ikvm.runtime;
using java.nio.file;
using sun.applet.resources;
using System;
using System.Collections;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    
    public class TabsView : Window
    {
        #region Properties
        private Builder _builder;
        private List<ImageView> viewers = new List<ImageView>();
        public ImageView GetViewer(int i)
        {
            return viewers[i];
        }
        public ImageView? GetViewer(string name)
        {
            for (int v = 0; v < viewers.Count; v++)
            {
                for (int i = 0; i < viewers.Count; i++)
                {
                    if (viewers[v].Images[i].Filename == name)
                        return viewers[v];
                } 
            }
            return null;
        }
        public int GetViewerCount()
        {
            return viewers.Count;
        }
        public void RemoveViewer(ImageView v)
        {
            viewers.Remove(v);
        }
        public void AddViewer(ImageView v)
        {
            viewers.Add(v);
        }

#pragma warning disable 649
        [Builder.Object]
        public MenuBar ImageJMenu;
        [Builder.Object]
        private MenuItem recentMenu;
        [Builder.Object]
        private MenuItem commandsMenu;
        [Builder.Object]
        private MenuItem runMenu;
        [Builder.Object]
        private MenuItem searchMenu;
        [Builder.Object]
        private MenuItem updateMenu;
        [Builder.Object]
        private MenuItem renameMenu;
        [Builder.Object]
        public MenuBar MainMenu;
        [Builder.Object]
        private MenuItem openImagesMenu;
        [Builder.Object]
        private MenuItem openOMEImagesMenu;
        [Builder.Object]
        private MenuItem openOMESeriesMenu;
        [Builder.Object]
        private MenuItem openSeriesMenu;
        [Builder.Object]
        private MenuItem openQuPathProject;
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
        private MenuItem savePyramidalMenu;
        [Builder.Object]
        private MenuItem saveQuPathProject;
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
        private MenuItem toolsMenu;
        [Builder.Object]
        private MenuItem setToolMenu;
        [Builder.Object]
        private MenuItem runSAMMenu;
        [Builder.Object]
        private MenuItem plateToolMenu;

        [Builder.Object]
        private MenuItem roiManagerMenu;
        [Builder.Object]
        private MenuItem exportROIsToCSVMenu;
        [Builder.Object]
        private MenuItem importROIsFromCSVMenu;
        [Builder.Object]
        private MenuItem exportROIsOfFolderOfImagesMenu;
        [Builder.Object]
        private MenuItem importROIsFromImageJMenu;
        [Builder.Object]
        private MenuItem exportROIsFromImageJMenu;
        [Builder.Object]
        private MenuItem importROIQuPath;
        [Builder.Object]
        private MenuItem exportROIQuPath;

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
        private MenuItem focusMenu;
        [Builder.Object]
        private MenuItem extractRegionMenu;

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
        private MenuItem toFloatMenu;
        [Builder.Object]
        private MenuItem toShortMenu;

        [Builder.Object]
        private MenuItem filtersMenu;

        [Builder.Object]
        private MenuItem functionsToolMenu;
        [Builder.Object]
        private MenuItem consoleMenu;
        [Builder.Object]
        private MenuItem scriptRunnerMenu;
        [Builder.Object]
        private MenuItem scriptRecorderMenu;

        [Builder.Object]
        private MenuItem aboutMenu;

        [Builder.Object]
        private Notebook tabsView;

        [Builder.Object]
        private Menu contextMenu;
        [Builder.Object]
        private MenuItem tabClose;
        [Builder.Object]
        private MenuItem tabDuplicate;
        [Builder.Object]
        private MenuItem runMicroSAMMenu;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the TabsView class, which is a class that inherits from
        /// Gtk.Window
        /// 
        /// @return A new instance of the TabsView class.
        public static TabsView Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/TabsView.glade", FileMode.Open));
            return new TabsView(builder, builder.GetObject("TabsView").Handle);
        }
        Menu recent = new Menu();
        /* Setting up the UI. */
        protected TabsView(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            App.tabsView = this;
            builder.Autoconnect(this);
            filteredMenu.Active = true;
            SetupHandlers();
            progress = Progress.Create(title, "", "");
            Function.InitializeMainMenu();
            filechooser =
        new Gtk.FileChooserDialog("Choose file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            Menu m = new Menu();
            foreach (RotateFlipType flip in (RotateFlipType[])Enum.GetValues(typeof(RotateFlipType)))
            {
                MenuItem mi = new MenuItem(flip.ToString());
                mi.ButtonPressEvent += RotateFlip_ButtonPressEvent;
                m.Append(mi);
            }
            rotateFlipMenu.Submenu = m;
            rotateFlipMenu.ShowAll();
            string a = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
            Menu me = new Menu();
            foreach (char c in a)
            {
                MenuItem mi = new MenuItem(c.ToString());
                Menu men = new Menu();
                foreach (Fiji.Macro.Command command in Fiji.Macro.Commands.Values)
                {
                    if (command.Name.StartsWith(c))
                    {
                        MenuItem menuItem = new MenuItem(command.Name.ToString());
                        menuItem.ButtonPressEvent += CommandMenuItem_ButtonPressEvent;
                        men.Append(menuItem);
                    }
                }
                mi.Submenu = men;
                me.Append(mi);
            }
            commandsMenu.Submenu = me;
            commandsMenu.ShowAll();
            recentMenu.Submenu = recent;

            App.ApplyStyles(this);
            Menu rm = new Menu();
            foreach(Scripting.Script s in Scripting.scripts.Values)
            {
                MenuItem mi = new MenuItem(s.name);
                mi.ButtonPressEvent += Mi_ButtonPressEvent;
                rm.Append(mi);
            }
            //runMenu.ShowAll();
            Plugins.Initialize();
            ML.ML.Initialize();
        }

        /// This function creates a file chooser dialog that allows the user to select the location of
        /// the ImageJ executable
        /// 
        /// @return A boolean value.
        public static bool SetImageJPath()
        {
            string st = BioLib.Settings.GetSettings("ImageJPath");
            if (st != "")
            {
                Fiji.ImageJPath = BioLib.Settings.GetSettings("ImageJPath");
                return true;
            }
            string title = "Select ImageJ Executable Location";
            if (OperatingSystem.IsMacOS())
                title = "Select ImageJ Executable Location (Fiji.app/Contents/MacOS/ImageJ-macosx)";
            Gtk.FileChooserDialog filechooser =
    new Gtk.FileChooserDialog(title, Scripting.window,
        FileChooserAction.Open,
        "Cancel", ResponseType.Cancel,
        "Save", ResponseType.Accept);
            filechooser.SetCurrentFolder(System.IO.Path.GetDirectoryName(Environment.ProcessPath));
            if (filechooser.Run() != (int)ResponseType.Accept)
                return false;
            Fiji.ImageJPath = filechooser.Filename;
            BioLib.Settings.AddSettings("ImageJPath", filechooser.Filename);
            filechooser.Destroy();
            BioLib.Settings.Save();
            return true;
        }
        private void Mi_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            MenuItem m = (MenuItem)o;
            if (m.Label.EndsWith(".ijm") || m.Label.EndsWith(".txt") && !m.Label.EndsWith(".cs"))
            {
                string file = Fiji.ImageJPath + m.Label;
                string ma = File.ReadAllText(file);
                Fiji.RunOnImage(ImageView.SelectedImage,ma, BioConsole.headless, BioConsole.onTab, BioConsole.useBioformats, BioConsole.resultInNewTab);
            }
            else
                Scripting.RunByName(m.Label);
            bool con = false;
            foreach (MenuItem item in recent.Children)
            {
                if (item.Label == m.Label)
                    con = true;
            }
            if (!con)
                recent.Append(m);
            recentMenu.ShowAll();
        }

        private async void CommandMenuItem_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (ImageView.SelectedImage == null) return;
            MenuItem m = (MenuItem)o;
            BioImage bm = await Fiji.RunOnImage(ImageView.SelectedImage, "run(\"" + m.Label + "\");", BioConsole.headless, BioConsole.onTab, BioConsole.useBioformats, BioConsole.resultInNewTab);
            MenuItem mi = new MenuItem(m.Label);
            mi.ButtonPressEvent += CommandMenuItem_ButtonPressEvent;
            bool con = false;
            foreach (MenuItem item in recent.Children)
            {
                if (item.Label == m.Label)
                {
                    con = true;
                    break;
                }
            }
            if(!con)
            recent.Append(mi);
            recentMenu.ShowAll();
            AddTab(bm);
        }

        /// When the user clicks on a menu item, the selected image is rotated or flipped according to
        /// the menu item's label
        /// 
        /// @param o The object that called the event
        /// @param ButtonPressEventArgs The event arguments for the button press event.
        /// 
        /// @return The return type is void.
        private void RotateFlip_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if(ImageView.SelectedImage==null) return;
            MenuItem m = (MenuItem)o;
            RotateFlipType r = Enum.Parse<RotateFlipType>(m.Label);
            ImageView.SelectedImage.RotateFlip(r);
            App.viewer.UpdateImage();
            App.viewer.UpdateView();
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
            openQuPathProject.ButtonPressEvent += openQuPathProjectMenuClick;
            addImagesToTabMenu.ButtonPressEvent += addImagesToTabMenuClick;
            addOMEImagesToTab.ButtonPressEvent += addOMEImagesToTabClick;
            saveSelectedTiff.ButtonPressEvent += saveSelectedTiffClick;
            saveSelectedOME.ButtonPressEvent += saveSelectedOMEClick;
            saveTabOME.ButtonPressEvent += saveTabOMEClick;
            saveTabTiffMenu.ButtonPressEvent += saveTabTiffMenuClick;
            saveSeriesMenu.ButtonPressEvent += saveSeriesMenuClick;
            savePyramidalMenu.ButtonPressEvent += SavePyramidalMenu_ButtonPressEvent;
            saveQuPathProject.ButtonPressEvent += saveQuPathProject_ButtonPressEvent;
            imagesToStack.ButtonPressEvent += imagesToStackClick;

            rgbMenu.ButtonPressEvent += RgbMenu_ButtonPressEvent;
            filteredMenu.ButtonPressEvent += FilteredMenu_ButtonPressEvent;
            rawMenu.ButtonPressEvent += RawMenu_ButtonPressEvent;
            emissionMenu.ButtonPressEvent += EmissionMenu_ButtonPressEvent;

            toolsMenu.ButtonPressEvent += toolsMenuClick;
            setToolMenu.ButtonPressEvent += setToolMenuClick;
            runSAMMenu.ButtonPressEvent += RunSAMMenu_ButtonPressEvent;
            runMicroSAMMenu.ButtonPressEvent += RunMicroSAMMenu_ButtonPressEvent;
            plateToolMenu.ButtonPressEvent += PlateToolMenu_ButtonPressEvent;
            extractRegionMenu.ButtonPressEvent += ExtractRegionMenu_ButtonPressEvent;

            roiManagerMenu.ButtonPressEvent += roiManagerMenuClick;
            exportROIsToCSVMenu.ButtonPressEvent += exportROIsToCSVMenuClick;
            importROIsFromCSVMenu.ButtonPressEvent += importROIsFromCSVMenuClick;
            exportROIsOfFolderOfImagesMenu.ButtonPressEvent += exportROIsOfFolderOfImagesMenuClick;
            importROIsFromImageJMenu.ButtonPressEvent += ImportROIsFromImageJMenu_ButtonPressEvent;
            exportROIsFromImageJMenu.ButtonPressEvent += ExportROIsFromImageJMenu_ButtonPressEvent;
            importROIQuPath.ButtonPressEvent += ImportROIQuPath_ButtonPressEvent;
            exportROIQuPath.ButtonPressEvent += ExportROIQuPath_ButtonPressEvent;

            autoThresholdAllMenu.ButtonPressEvent += autoThresholdAllMenuClick;
            channelsToolMenu.ButtonPressEvent += channelsToolMenuClick;
            switchRedBlueMenu.ButtonPressEvent += switchRedBlueMenuClick;

            stackToolMenu.ButtonPressEvent += stackToolMenuClick;
            focusMenu.ButtonPressEvent += FocusMenu_ButtonPressEvent;

            to8BitMenu.ButtonPressEvent += to8BitMenuClick;
            to16BitMenu.ButtonPressEvent += to16BitMenuClick;
            to24BitMenu.ButtonPressEvent += to24BitMenuClick;
            to32BitMenu.ButtonPressEvent += to32BitMenuClick;
            to48BitMenu.ButtonPressEvent += to48BitMenuClick;
            toFloatMenu.ButtonPressEvent += ToFloatMenu_ButtonPressEvent;
            toShortMenu.ButtonPressEvent += ToShortMenu_ButtonPressEvent;

            filtersMenu.ButtonPressEvent += filtersMenuClick;
            functionsToolMenu.ButtonPressEvent += functionsToolMenuClick;
            consoleMenu.ButtonPressEvent += consoleMenuClick;
            scriptRunnerMenu.ButtonPressEvent += scriptRunnerMenuClick;
            scriptRecorderMenu.ButtonPressEvent += ScriptRecorderMenu_ButtonPressEvent;

            aboutMenu.ButtonPressEvent += AboutClick;

            tabClose.ButtonPressEvent += TabClose_ButtonPressEvent;
            tabDuplicate.ButtonPressEvent += TabDuplicate_ButtonPressEvent;
            this.FocusInEvent += TabsView_FocusInEvent;
            rgbMenu.ButtonPressEvent += RgbMenu_ButtonPressEvent;
            filteredMenu.ButtonPressEvent += FilteredMenu_ButtonPressEvent;
            rawMenu.ButtonPressEvent += RawMenu_ButtonPressEvent;
            emissionMenu.ButtonPressEvent += EmissionMenu_ButtonPressEvent;
            tabsView.SwitchPage += TabsView_SwitchPage;
            tabsView.ButtonPressEvent += TabsView_ButtonPressEvent;
            this.WindowStateEvent += TabsView_WindowStateEvent;
            this.DeleteEvent += TabsView_DeleteEvent;

            searchMenu.ButtonPressEvent += SearchMenu_ButtonPressEvent;
            updateMenu.ButtonPressEvent += UpdateMenu_ButtonPressEvent;
            renameMenu.ButtonPressEvent += RenameMenu_ButtonPressEvent;
        }

        private void RunMicroSAMMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            SAMTool sam = SAMTool.Create(true);
            if (sam.init)
                sam.Show();
            App.samTool = sam;
        }

        public void RenameTab(string name, string newName)
        {
            int i = 0;
            foreach (Widget item in tabsView.Children)
            {
                // Get the tab widget for the page and set the new title
                Label l = (Label)tabsView.GetTabLabel(tabsView.GetNthPage(i));
                if (l.Text == name)
                {
                    l.Text = newName;
                    return;
                }
            }
        }
        void RenameNotebookTab(int pageIndex, string newTitle)
        {
            if (pageIndex >= 0 && pageIndex < tabsView.NPages)
            {
                // Get the tab widget for the page and set the new title
                Widget tabLabel = tabsView.GetTabLabel(tabsView.GetNthPage(pageIndex));
                if (tabLabel is Label label)
                {
                    label.Text = newTitle;
                }
            }
        }
        private void TabsView_FocusInEvent(object o, FocusInEventArgs args)
        {
            App.tabsView = this;
        }

        private void RenameMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            TextInput ti = TextInput.Create();
            ti.Show();
            if ((int)ti.Run() == (int)ResponseType.Ok)
            {
                App.Rename(ti.Text);
            }
        }

        private void TabsView_DeleteEvent(object o, DeleteEventArgs args)
        {
            foreach (var v in viewers)
            {
                foreach (var b in v.Images) 
                {
                    Images.RemoveImage(b);
                }
                v.Close();
            }
        }

        private async void ExtractRegionMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            BioImage b = ImageView.SelectedImage.Copy();
            String name = System.IO.Path.GetFileNameWithoutExtension(ImageView.SelectedImage.Filename).Replace(".ome","");
            int c = Images.GetImageCountByName(name);
            b.ID = name + "-" + c + ".ome.tif";
            var bms = await b.GetSlice((int)b.PyramidalOrigin.X, (int)b.PyramidalOrigin.Y, b.PyramidalSize.Width, b.PyramidalSize.Height, b.Resolution);
            Point3D loc = b.Volume.Location;
            Point3D p = new Point3D(b.StageSizeX + b.PyramidalOrigin.X, b.StageSizeY + b.PyramidalOrigin.Y, b.StageSizeZ + b.SizeZ);
            Resolution res = new Resolution(b.PyramidalSize.Width, b.PyramidalSize.Height, b.Buffers[0].PixelFormat,b.PhysicalSizeX,b.PhysicalSizeY,b.PhysicalSizeZ,p.X,p.Y,p.Z);
            b.Resolutions.Clear();
            b.Resolutions.Add(res);
            b.Type = BioImage.ImageType.stack;
            b.Buffers.Clear();
            b.Buffers.AddRange(bms.ToArray());
            Images.AddImage(b);
            AddTab(b);
        }

        private void ToShortMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            ImageView.SelectedImage.ToShort();
        }

        private void ToFloatMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            ImageView.SelectedImage.ToFloat();
        }

        private void UpdateMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.updater.Show();
        }

        private void PlateToolMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            PlateTool pl = PlateTool.Create();
        }

        private void ExportROIQuPath_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose QuPath ROI(.geojson) file to save to.",
            this,
            FileChooserAction.Save,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            QuPath.SaveROI(filechooser.Filename, ImageView.SelectedImage);
            filechooser.Hide();
        }

        private void ImportROIQuPath_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose QuPath ROI(.geojson) file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                ROI[] rois = QuPath.ReadROI(item,ImageView.SelectedImage);
                foreach (ROI r in rois)
                {
                    ImageView.SelectedImage.Annotations.Add(r);
                }
            }
            filechooser.Hide();
        }

        private void SearchMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Search search = Search.Create();
            search.Show();
        }

        private void TabsView_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            contextMenu.Popup();
        }

        private void TabDuplicate_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            List<BioImage> bm = new List<BioImage>();
            foreach (BioImage item in App.viewer.Images)
            {
                bm.Add(item.Copy());
            }
            for (int i = 1; i < bm.Count; i++)
            {
                Images.AddImage(bm[i]);
                AddTab(bm[i]);
            }
        }

        private void TabClose_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            foreach (BioImage item in App.viewer.Images)
            {
                Images.RemoveImage(item);
                RemoveTab(item.Filename);
            }
        }

        private void RunSAMMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            SAMTool sam = SAMTool.Create(false);
            if(sam.init)
            sam.Show();
            App.samTool = sam;
        }

        private async void SavePyramidalMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Set Pyramidal filename to save",
            this,
            FileChooserAction.Save,
            "Cancel", ResponseType.Cancel,
            "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            ComboPicker picker = ComboPicker.Create(typeof(NetVips.Enums.ForeignTiffCompression));
            picker.Title = "Select Compression";
            picker.Show();
            if(picker.Run() != (int)ResponseType.Ok)
                return;
            NumberPicker numpicker = NumberPicker.Create(0,100,0);
            numpicker.Title = "Select Compression Level in %";
            if (numpicker.Run() != (int)ResponseType.Ok)
                return;
            await BioImage.SavePyramidalAsync(App.viewer.Images.ToArray(), filechooser.Filename, (NetVips.Enums.ForeignTiffCompression)picker.SelectedIndex, (int)numpicker.SelectedValue);
        }

        private void FocusMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (ImageView.SelectedImage == null) return;
            ZCT co = App.viewer.GetCoordinate();
            int f = BioImage.FindFocus(ImageView.SelectedImage, co.C, co.T);
            ZCT z = ImageView.SelectedImage.Buffers[f].Coordinate;
            App.viewer.SetCoordinate(z.Z, z.C, z.T);
        }

        /// When the user clicks on the Script Recorder menu item, the Script Recorder window is shown
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtk3/stable/GtkButton.html#GtkButton-clicked
        private void ScriptRecorderMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.recorder = Recorder.Create();
            App.recorder.Show();
            App.recorder.Present();
        }

        /// It saves all the ROIs in the current image to a file
        /// 
        /// @param o The object that triggered the event.
        /// @param ButtonPressEventArgs This is the event that is triggered when the button is pressed.
        /// 
        /// @return The response type of the filechooser dialog.
        private void ExportROIsFromImageJMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Set ROI filename to save",
            this,
            FileChooserAction.Save,
            "Cancel", ResponseType.Cancel,
            "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            int i = 1;
            foreach (ROI item in ImageView.SelectedImage.Annotations)
            {
                string f = filechooser.Filename;
                if (!f.EndsWith(".roi"))
                    f += i + ".roi";
                else
                {
                    f = f.Replace(".roi","") + i + ".roi";
                }
                Fiji.RoiEncoder.save(ImageView.SelectedImage, item, f);
                i++;
            }
            filechooser.Hide();
        }

        /// It opens a file chooser dialog, and when the user selects a file, it opens the file as an
        /// ROI, and adds it to the selected image
        /// 
        /// @param o The object that triggered the event.
        /// @param ButtonPressEventArgs This is the event that is triggered when the button is pressed.
        /// 
        /// @return A ROI object.
        private void ImportROIsFromImageJMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Choose ROI file to open",
            this,
            FileChooserAction.Open,
            "Cancel", ResponseType.Cancel,
            "Open", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            foreach (string item in filechooser.Filenames)
            {
                ROI roi = Fiji.RoiDecoder.open(item, ImageView.SelectedImage.PhysicalSizeX, ImageView.SelectedImage.PhysicalSizeY, ImageView.SelectedImage.Volume.Location.X, ImageView.SelectedImage.Volume.Location.Y);
                ImageView.SelectedImage.Annotations.Add(roi);
            }
            filechooser.Hide();
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
            if(viewers.Count == 0) return;
            viewers[(int)args.PageNum].Present();
            App.viewer = viewers[(int)args.PageNum];
            ImageView.SelectedImage = App.viewer.Images[0];
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
            App.viewer.Mode = ImageView.ViewMode.Emission;
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
            App.viewer.Mode = ImageView.ViewMode.Raw;
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
            App.viewer.Mode = ImageView.ViewMode.Filtered;
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
            App.viewer.Mode = ImageView.ViewMode.RGBImage;
        }
        /// <summary>
        /// Sets the current tab index.
        /// </summary>
        /// <param name="i"></param>
        public void SetTab(int i)
        {
            tabsView.Page = i;
        }
        /// It adds a new tab to the tab control
        /// 
        /// @param BioImage This is the image that you want to display.
        public void AddTab(BioImage im)
        {
            try
            {
                Application.Invoke(delegate
                {
                    for (int vi = 0; vi < viewers.Count; vi++)
                    {
                        for (int i = 0; i < viewers[vi].Images.Count; i++)
                        {
                            if (viewers[vi].Images[i].Filename == im.Filename)
                                return;
                        }
                    }
                    ImageView v = ImageView.Create(im);
                    viewers.Add(v);
                    Label dummy = new Gtk.Label(System.IO.Path.GetDirectoryName(im.file.Replace("\\", "/")) + "/" + im.Filename);
                    dummy.Text = im.file.Replace("\\", "/") + "/" + im.Filename;
                    dummy.Visible = false;
                    Label l = new Gtk.Label(System.IO.Path.GetFileName(im.Filename));
                    l.Text = System.IO.Path.GetFileName(im.Filename);
                    tabsView.AppendPage(dummy, l);                
                    Console.WriteLine("New tab added for " + im.Filename);
                    App.nodeView.UpdateItems();
                    tabsView.ShowAll();
                    v.Show();
                });
            }
            catch (Exception e)
            {
                Console.WriteLine(e.ToString());
            }
        }
        Gtk.FileChooserDialog filechooser;
        /// It opens a file chooser dialog, and when the user selects a file, it creates a new BioImage
       /// object, creates a new ImageView object, and adds the ImageView object to the list of viewers
       /// 
       /// @param sender The object that raised the event.
       /// @param EventArgs The event arguments.
       /// 
       /// @return The response type of the dialog.
        protected async void openImagesMenuClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Choose file to open", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            string[] sts = filechooser.Filenames;
            filechooser.Hide();
            foreach (string item in sts)
            {
                StartProgress("Open File", "Opening");
                await BioImage.OpenAsync(item,false,true,true,0);
                BioImage im = Images.GetImage(item);
                AddTab(im);
                StopProgress();
            }
        }
        /// It opens a file chooser dialog, and when the user selects a file, it opens the file and adds
        /// it to the list of open images
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response type of the dialog.
        protected async void openOMEImagesMenuClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Choose OME file to open", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            foreach (string item in filechooser.Filenames)
            {
                StartProgress("Open OME", "Opening");
                await BioImage.OpenAsync(item, true, true, true,0);
                BioImage im = Images.GetImage(item);
                AddTab(im);
                StopProgress();
            }
        }
        /// It opens a file chooser dialog, and when the user selects a file, it opens the file as a
        /// BioImage, creates an ImageView for it, and adds the ImageView to the notebook
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return A list of file names.
        protected async void openOMESeriesMenuClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Choose OME Series file to open.", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            foreach (string item in filechooser.Filenames)
            {
                StartProgress("Open OME", "Opening");
                int s = BioImage.GetSeriesCount(item);
                for (int i = 0; i < s; i++)
                {
                    await BioImage.OpenAsync(item, true, true, true, i);
                    BioImage im = Images.GetImage(item);
                    AddTab(im);
                }
            }
            tabsView.ShowAll();
            
        }
        /// It opens a file chooser dialog, and when the user selects a file, it opens the file as a
        /// series of images, and adds each image to the notebook
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response type of the dialog.
        protected async void openSeriesMenuClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Choose series file to open", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            foreach (string item in filechooser.Filenames)
            {
                StartProgress("Open OME Series", "Opening");
                await BioImage.OpenAsync(item, false, true, true, 0);
                BioImage im = Images.GetImage(item);
                AddTab(im);
                StopProgress();
            }
            tabsView.Show();
        }
        private void openQuPathProjectMenuClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Choose QuPath project file to open", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            QuPath.Project pr = QuPath.OpenProject(filechooser.Filename);
            foreach (var item in pr.Images)
            {
                string file = item.ServerBuilder.Uri.Replace("file:/", "");
                BioImage bm = BioImage.OpenFile(file);
                AddTab(bm);
                string[] rs = Directory.GetFiles(System.IO.Path.GetDirectoryName(file));
                foreach (string s in rs)
                {
                    if (s.EndsWith(".geojson") && s.Contains(System.IO.Path.GetFileNameWithoutExtension(file)))
                    {
                        ROI[] rois = QuPath.ReadROI(s, bm);
                        bm.Annotations.AddRange(rois);
                    }
                }
            }
        }
        private void saveQuPathProject_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
        new Gtk.FileChooserDialog("Set QuPath project file to save.",
            this,
            FileChooserAction.Save,
            "Cancel", ResponseType.Cancel,
            "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            List<BioImage[]> bms = new List<BioImage[]>();
            foreach (var item in viewers)
            {
                List<BioImage> bs = new List<BioImage>();
                foreach (var b in item.Images)
                {
                    bs.Add(b);
                }
                bms.Add(bs.ToArray());
            }
            QuPath.Project.SaveProject(filechooser.Filename, bms);
        }
        /// It opens a file chooser dialog, and then adds the selected images to the currently selected
        /// viewer
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response type of the filechooser dialog.
        protected async void addImagesToTabMenuClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Choose file to add to current tab.", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            foreach (string item in filechooser.Filenames)
            {
                StartProgress("Add Image to Tab", "Opening");
                await BioImage.OpenAsync(item, false, false, true, 0);
                BioImage im = Images.GetImage(item);
                AddTab(im);
                StopProgress();
            }
            this.ShowAll();
        }
        /// It opens a file chooser dialog, and when the user selects a file, it opens the file as an
        /// OME image, and adds it to the currently selected viewer
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response type of the filechooser dialog.
        protected async void addOMEImagesToTabClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Choose OME file to add to current tab.", this, FileChooserAction.Open, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            foreach (string item in filechooser.Filenames)
            {
                StartProgress("Add Image to Tab", "Opening");
                await BioImage.OpenAsync(item, true, false, true, 0);
                BioImage im = Images.GetImage(item);
                AddTab(im);
                StopProgress();
            }
            this.Show();
        }
        private static bool done = false;
        private static Progress progress;
        private static string title;
        private static void ProgressUpdate()
        {
            do
            {
                progress.Title = title;
                progress.Status = BioImage.Status;
                progress.ProgressValue = BioImage.Progress;
                Thread.Sleep(250);
            } while (!done);
        }
        private static void StartProgress(string titl, string status)
        {
            done = false;
            title = titl;
            BioImage.Progress = 0;
            progress.Status = status;
            progress.Title = title;
            if (BioImage.Status == "" || BioImage.Status == null)
                progress.Status = status;
            else
                progress.Status = BioImage.Status;
            progress.ProgressValue = BioImage.Progress;
            progress.Show();
            Thread th = new Thread(ProgressUpdate);
            th.Start();
        }
        private static void StopProgress()
        {
            done = true;
            progress.Hide();
        }
        /// It creates a file chooser dialog, and if the user selects a file, it saves the selected
        /// image to that file
        /// 
        /// @param sender The object that triggered the event.
        /// @param EventArgs This is the event that is being passed to the method.
        /// 
        /// @return The response type of the dialog.
        protected async void saveSelectedTiffClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Save Selected Image To TIFF", this, FileChooserAction.Save, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            StartProgress("Save Selected Tiff","Saving");
            await BioImage.SaveAsync(filechooser.Filename,ImageView.SelectedImage.Filename, 0, false);
            StopProgress();
        }
        /// This function saves the selected image in the OME-TIFF format
        /// 
        /// @param sender The object that triggered the event.
        /// @param EventArgs This is the event that is being called.
        /// 
        /// @return The response type of the filechooser dialog.
        protected async void saveSelectedOMEClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Save Selected OME Image to File", this, FileChooserAction.Save, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            StartProgress("Save Selected OME", "Saving");
            await BioImage.SaveAsync(filechooser.Filename, ImageView.SelectedImage.Filename, 0, true);
            StopProgress();
        }
        /// This function saves the current series of images to an OME-TIFF file
        /// 
        /// @param sender The object that triggered the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The response from the filechooser dialog.
        protected async void saveTabOMEClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Save Tab as an OME image.", this, FileChooserAction.Save, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            StartProgress("Save Selected OME", "Saving");
            await BioImage.SaveSeriesAsync(App.viewer.Images.ToArray(), filechooser.Filename, true);
            StopProgress();
        }
        /// It saves the current tab as a tiff file
        /// 
        /// @param sender The object that sent the event.
        /// @param EventArgs 
        /// 
        /// @return The response type of the filechooser dialog.
        protected async void saveTabTiffMenuClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Save Tab as an image.", this, FileChooserAction.Save, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            StartProgress("Save Selected Tiff", "Saving");
            await BioImage.SaveSeriesAsync(App.viewer.Images.ToArray(), filechooser.Filename, false);     
            StopProgress();
        }
        /// This function is called when the user clicks the "Save Series" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected async void saveSeriesMenuClick(object sender, EventArgs a)
        {
            filechooser = new FileChooserDialog("Save TIFF series.", this, FileChooserAction.Save, "Cancel", ResponseType.Cancel, "OK", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            StartProgress("Save Selected Tiff", "Saving");
            await BioImage.SaveSeriesAsync(App.viewer.Images.ToArray(), filechooser.Filename, true);
            StopProgress();
        }
        /// This function is called when the user clicks the "Images to Stack" button
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes that contain event data.
        protected void imagesToStackClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser =
    new Gtk.FileChooserDialog("Choose images to convert to stack.",
        this,
        FileChooserAction.Save,
        "Cancel", ResponseType.Cancel,
        "Save", ResponseType.Accept);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            filechooser.Hide();
            BioImage b = BioImage.ImagesToStack(filechooser.Filenames, true);
            AddTab(b);
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
            App.tools.Present();
        }
        /// This function is called when the user clicks on the "Tools" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void setToolMenuClick(object sender, EventArgs a)
        {
            App.setTool = SetTool.Create();
            App.setTool.Show();
            App.setTool.Present();
        }

        /// This function is called when the user clicks on the ROI Manager menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the class that contains the event data.
        protected void roiManagerMenuClick(object sender, EventArgs a)
        {
            App.roiManager = ROIManager.Create();   
            App.roiManager.Show();
            App.roiManager.Present();
        }

        /// This function is called when the user clicks on the "Export ROIs to CSV" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void exportROIsToCSVMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser = new Gtk.FileChooserDialog("Set filename for export.",
           this,
           FileChooserAction.Save,
           "Cancel", ResponseType.Cancel,
           "Ok", ResponseType.Ok);
            filechooser.SelectMultiple = false;
            if (filechooser.Run() != (int)ResponseType.Ok)
                return;
            BioImage.ExportROIsCSV(filechooser.Filename, ImageView.SelectedImage.Annotations);
            filechooser.Hide();
        }
        /// This function is called when the user clicks on the "Import ROIs from CSV" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void importROIsFromCSVMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog filechooser = new Gtk.FileChooserDialog("Choose the file to open.",
          this,
          FileChooserAction.Open,
          "Cancel", ResponseType.Cancel,
          "Ok", ResponseType.Ok);
            filechooser.SelectMultiple = true;
            if (filechooser.Run() != (int)ResponseType.Ok)
                return;
            foreach (string item in filechooser.Filenames)
            {
                ImageView.SelectedImage.Annotations.AddRange(BioImage.ImportROIsCSV(item));
            }
            filechooser.Hide();
        }
        /// This function is called when the user clicks on the "Export ROIs of Folder of Images" menu
        /// item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        protected void exportROIsOfFolderOfImagesMenuClick(object sender, EventArgs a)
        {
            Gtk.FileChooserDialog folderchooser = new Gtk.FileChooserDialog("Set folder of images for export.",
          this,
          FileChooserAction.SelectFolder,
          "Cancel", ResponseType.Cancel,
          "Ok", ResponseType.Ok);
            folderchooser.SelectMultiple = true;
            if (folderchooser.Run() != (int)ResponseType.Ok)
                return;
            string folder = folderchooser.Filename;
            folderchooser.Destroy();
            Gtk.FileChooserDialog filechooser = new Gtk.FileChooserDialog("Set filename for CSV to export.",
           this,
           FileChooserAction.Save,
           "Cancel", ResponseType.Cancel,
           "Ok", ResponseType.Ok);
            filechooser.SelectMultiple = false;
            if (filechooser.Run() != (int)ResponseType.Ok)
                return;
            string file = filechooser.Filename;
            filechooser.Hide();
            BioImage.ExportROIFolder(folder, file);
        }

        /// This function is called when the user clicks on the "Auto Threshold All" menu item.
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void autoThresholdAllMenuClick(object sender, EventArgs a)
        {
            BioImage.AutoThreshold(ImageView.SelectedImage, true);
            if (ImageView.SelectedImage.bitsPerPixel > 8)
                ImageView.SelectedImage.StackThreshold(true);
            else
                ImageView.SelectedImage.StackThreshold(false);
            App.viewer.UpdateImage();
            App.viewer.UpdateView();
        }
        /// It creates a new instance of the ChannelsTool class, and then shows it
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void channelsToolMenuClick(object sender, EventArgs a)
        {
            if (App.viewer == null)
                return;
            App.channelsTool = ChannelsTool.Create();
            App.channelsTool.Show();
            App.channelsTool.Present();
        }
        /// This function is called when the user clicks on the "Switch Red and Blue" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes that contain event data.
        protected void switchRedBlueMenuClick(object sender, EventArgs a)
        {
            foreach (AForge.Bitmap bf in ImageView.SelectedImage.Buffers)
            {
                bf.SwitchRedBlue();
            }
            App.viewer.UpdateImage();
            App.viewer.UpdateView();
        }

        /// This function is called when the user clicks on the stack tool menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        protected void stackToolMenuClick(object sender, EventArgs a)
        {
            App.stack = StackTools.Create();
            App.stack.Show();
            App.stack.Present();
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
            App.filters = FiltersView.Create();
            App.filters.Show();
            App.filters.Present();
        }

        /// This function is called when the user clicks on the functions tool menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void functionsToolMenuClick(object sender, EventArgs a)
        {
            App.funcs = Functions.Create();
            App.funcs.Show();
            App.funcs.Present();
        }
        /// This function is called when the user clicks on the console menu button
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments that are passed to the event handler.
        protected void consoleMenuClick(object sender, EventArgs a)
        {
            App.console = BioConsole.Create();
            App.console.Show();
            App.console.Present();
        }
        /// It's a function that runs when the user clicks on the "Script Runner" menu item
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is the event arguments.
        protected void scriptRunnerMenuClick(object sender, EventArgs a)
        {
            App.scripting = Scripting.Create();
            App.scripting.Show();
            App.scripting.Present();
        }

        /// It creates a new instance of the About class, and then shows it
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        protected void AboutClick(object sender, EventArgs a)
        {
            App.about = About.Create();
            App.about.Show();
            App.about.Present();
        }

        /// This function removes a tab from the tab control
        /// 
        /// @param tabName The name of the tab to remove.
        public void RemoveTab(string tabName)
        {
            int i = 0;
            foreach (Widget item in tabsView.Children)
            {
                Gtk.Label l = item as Gtk.Label;
                string name = System.IO.Path.GetFileName(l.LabelMarkup);
                if (name == tabName)
                {
                    ImageView iv = viewers[i];
                    for (int v = 0; v < iv.Images.Count; v++)
                    {
                        Images.RemoveImage(iv.Images[v]);
                    }
                    tabsView.Remove(item);
                    viewers.RemoveAt(i);
                    App.nodeView.UpdateItems();
                    return;
                }
                i++;
            }
        }
        /// Open's a file in a new tab.
        /// 
        /// @param tabName The filename of the image to add to tabcontrol
        public void Open(string file)
        {
            BioImage b = BioImage.OpenFile(file);
            ImageView view = ImageView.Create(b);
            if (!viewers.Contains(App.viewer))
                viewers.Add(view);
            Label dummy = new Gtk.Label(b.Filename);
            dummy.Text = b.Filename;
            dummy.UseMarkup = false;
            dummy.Visible = false;
            Label l = new Gtk.Label(b.Filename);
            l.Text = b.Filename;
            l.UseMarkup = false;
            tabsView.AppendPage(dummy, l);
            view.Present();
        }
        #endregion

    }
}
