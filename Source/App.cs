using Bio;
using Gtk;
using OpenSlideGTK;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public static class App
    {
        public static TabsView tabsView;
        public static Tools tools;
        public static ImageView viewer;
        public static ColorTool color;
        public static Progress progress;
        public static About about;
        public static NodeView nodeView;
        public static FiltersView filters;
        public static ApplyFilter applyFilter;
        public static ChannelsTool channelsTool;
        public static ROIManager roiManager;
        public static Scripting scripting;
        public static BioConsole console;
        public static Functions funcs;
        public static StackTools stack;
        public static SetTool setTool;
        public static Resolutions resolutions;
        public static Recorder recorder;
        public static SAMTool samTool;
        public static Updater updater;
        private static int selectedIndex;
        static bool useGPU = true, useVips = false, useImageSharp = false;
        public static bool UseGPU
        {
            get
            {
                return useGPU;
            }
            set
            {
                useGPU = value;
                useVips = !value;
                useImageSharp = !value;
                UpdateStitching();
            }
        }
        public static bool UseVips
        {
            get
            {
                return useVips;
            }
            set
            {
                useVips = value;
                useImageSharp = !value;
                useGPU = !value;
                UpdateStitching();
            }
        }
        public static bool UseImageSharp
        {
            get
            {
                return useImageSharp;
            }
            set
            {
                useImageSharp = value;
                useVips = !value;
                useGPU = !value;
                UpdateStitching();
            }
        }
        private static void UpdateStitching()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                useGPU = false;
                OpenSlideBase.useGPU = false;
                //SlideBase.useGPU = false;
                OpenSlideBase.UseVips = false;
                SlideBase.UseVips = false;
                return;
            }
            OpenSlideBase.useGPU = useGPU;
            //SlideBase.useGPU = useGPU;
            OpenSlideBase.UseVips = useVips;
            SlideBase.UseVips = useVips;
 
        }
        public static bool ToConsole
        {
            get
            {
                return BioLib.Recorder.outputConsole;
            }
            set
            {
                BioLib.Recorder.outputConsole = value;
            }
        }
        static bool useFiji = true;
        public static bool UseFiji
        {
            get
            {
                if (OperatingSystem.IsMacOS())
                {
                    return true;
                }
                else
                {
                    if(Fiji.ImageJPath.Contains("Fiji"))
                        return true;
                    else
                        return false;
                }
            }
        }
        /// This function creates a file chooser dialog that allows the user to select the location of
        /// the ImageJ executable
        /// 
        /// @return A boolean value.
        public static bool SetFijiOrImageJPath()
        {
            bool ifb;
            BioLib.Settings.Load();
            string st = BioLib.Settings.GetSettings("ImageJPath");
            if (st != "")
            {
                if (st.Contains("Fiji"))
                {
                    BioLib.Fiji.ImageJPath = st;
                }
                else
                    BioLib.ImageJ.ImageJPath = st;
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
            if(filechooser.Filename.Contains("Fiji"))
                Fiji.ImageJPath = filechooser.Filename;
            else
                ImageJ.ImageJPath = filechooser.Filename;
            BioLib.Settings.AddSettings("ImageJPath", filechooser.Filename);
            filechooser.Destroy();
            BioLib.Settings.Save();
            return true;
        }
        /// Initialize() is a function that initializes the BioImage Suite Web
        public static void Initialize()
        {
            Console.WriteLine("Initializing components.");
            SetFijiOrImageJPath();
            if (Fiji.ImageJPath == "")
            {
                ImageJ.Initialize(ImageJ.ImageJPath);
                BioImage.Initialize(ImageJ.ImageJPath);
            }
            else
            {
                Fiji.Initialize(Fiji.ImageJPath);
                BioImage.Initialize(Fiji.ImageJPath);
            }
            Console.WriteLine("Loading settings.");
            BioLib.Settings.Load();
            updater = Updater.Create();
            tools = Tools.Create();
            filters = FiltersView.Create();
            roiManager = ROIManager.Create();
            scripting = Scripting.Create();
            funcs = Functions.Create();
            progress = Progress.Create("","","");
            //channelsTool = ChannelsTool.Create();
            console = BioConsole.Create();
            stack = StackTools.Create();
            about = About.Create();
            setTool = SetTool.Create();
            recorder = Recorder.Create();
            //color = ColorTool.Create();
            UpdateStitching();
            BioLib.Settings.Load();
        }

        public static void ApplyStyles(Widget widget)
        {
       
            if (widget != null)
            {
                if (widget is not Button || widget is not SpinButton)
                {
                    widget.ModifyBg(StateType.Normal, new Gdk.Color(49, 91, 138));
                    widget.ModifyFg(StateType.Normal, new Gdk.Color(255, 255, 255));
                }
            }
            try
            {
                if (widget is Container)
                {
                    foreach (var child in ((Container)widget).Children)
                    {
                        if(child is not Button || child is not SpinButton)
                        ApplyStyles(child);
                    }
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message.ToString());
            }
            if(widget is Window)
            {
                var window = (Window)widget;
                window.Icon = new Gdk.Pixbuf("bio.png");
                window.DeleteEvent += Window_DeleteEvent;
            }
        }

        private static void Window_DeleteEvent(object o, DeleteEventArgs args)
        {
            Window w = (Window)o;
            w.Destroy();
        }

        public static void SelectWindow(string name)
        {
            TabsView tbs = App.tabsView;
            
            int c = tbs.GetViewerCount();
            for (int v = 0; v < c; v++)
            {
                int cc = tbs.GetViewer(v).Images.Count;
                for (int im = 0; im < cc; im++)
                {
                    if (tbs.GetViewer(v).Images[im].Filename == Path.GetFileName(name))
                    {
                        tbs.SetTab(v);
                        BioLib.Recorder.Record("App.SelectWindow(\"" + name + "\")");
                        ImageView.SelectedImage = tbs.GetViewer(v).Images[im];
                        viewer = tbs.GetViewer(v);
                        viewer.Present();
                        return;
                    }
                }
            }
        }

        public static void CloseWindow(string name)
        {
            BioLib.Recorder.Record("App.CloseWindow(\"" + name + "\")");
            TabsView tbs = App.tabsView;
            int c = tbs.GetViewerCount();
            if (name == "ROI Manager")
                App.roiManager.Close();
            else
            if (name == "Stack Tool")
                App.stack.Close();
            else
            if (name == "Channels Tool")
                App.channelsTool.Close();
            else
            if (name == "Scripting")
                App.scripting.Close();
            else
            if (name == "SAM Tool")
                App.samTool.Close();
            else
            if (name == "Recorder")
                App.recorder.Close();
            else
            if (name == "Tools")
                App.tools.Close();
            else
            if (name == "Color Tool")
                App.color.Destroy();
            else
                for (int v = 0; v < c; v++)
                {
                    int cc = tbs.GetViewer(v).Images.Count;
                    for (int im = 0; im < cc; im++)
                    {
                        ImageView vi = tbs.GetViewer(v);
                        if (vi.Images[im].Filename == Path.GetFileName(name))
                        {
                            tbs.RemoveTab(name);
                            vi.Close();
                            vi.Destroy();
                            return;
                        }
                        else if (vi.Title == name)
                        {
                            tbs.RemoveTab(name);
                            vi.Close();
                            vi.Destroy();
                            return;
                        }
                    }
                }
            
        }

        public static void Rename(string text)
        {
            App.tabsView.RenameTab(ImageView.SelectedImage.Filename, text);
            ImageView.SelectedImage.Rename(text);
            ImageView v = App.tabsView.GetViewer(ImageView.SelectedImage.Filename);
            v.SetTitle(ImageView.SelectedImage.Filename);
            BioLib.Recorder.AddLine("App.Rename(\"" + text + "\");",false);
        }

        /// The function FindItem takes a Menu object and a label as input, and returns the MenuItem
        /// object with the matching label, if found.
        /// 
        /// @param Menu The parameter "w" is of type Menu, which represents a menu widget. It is the
        /// menu in which we want to find the item.
        /// @param label The label is a string that represents the label of the MenuItem we are
        /// searching for.
        /// 
        /// @return The method is returning a MenuItem object or null.
        static MenuItem FindItem(Menu w,string label)
        {
            foreach (Widget item in w.Children)
            {
                //We use a try block incase this widget is a menu instead of a menuitem.
                try
                {
                    Menu m = (Menu)item;
                    for (int i = 0; i < m.Children.Length; i++)
                    {
                        //We again use a try block incase this is a Menu instead of a menuitem
                        try
                        {
                            MenuItem it = (MenuItem)m.Children[i];
                            if (it.Label == label)
                            {
                                return it;
                            }
                        }
                        catch (Exception)
                        {

                        }
                    }
                }
                catch (Exception)
                {
                    MenuItem it = (MenuItem)item;
                    if (it.Label == label)
                    {
                        return it;
                    }
                }
            }
            return null;
        }
        
        /// The function `GetMainMenuItem` takes a path and returns the corresponding `Widget` and a
        /// boolean indicating whether it is a menu or not.
        /// 
        /// @param path The path is a string that represents the location of a main menu item in a menu
        /// bar. It is formatted as a series of menu item labels separated by forward slashes ("/"). For
        /// example, "File/Open/Recent" represents the "Recent" menu item under the "Open" menu item
        /// under the
        /// @param Widget The `Widget` parameter `wid` is an output parameter that will store the found
        /// `MenuItem` or `Menu` widget based on the given `path`.
        /// @param Menu The "Menu" parameter is a boolean variable that indicates whether the main menu
        /// item is a menu or a menu item. If it is true, it means the main menu item is a menu, and if
        /// it is false, it means the main menu item is a menu item.
        /// 
        /// @return The method is returning the main menu item specified by the given path. The main
        /// menu item can be either a MenuItem or a Menu.
        private static void GetMainMenuItem(string path, out Widget wid, out bool Menu)
        {
            wid = null;
            Menu = false;
            string[] s = path.Split('/');
            MenuBar w = tabsView.MainMenu;
            for (int i = 0; i < s.Length; i++)
            {
                foreach (Widget item in w.Children)
                {
                    //We use a try block incase this widget is a MenuItem instead of Menu.
                    try
                    {
                        MenuItem m = FindItem((Menu)item, s[i]);
                        if(m != null && i == s.Length -1)
                        {
                            wid = m;
                            Menu = true;
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        MenuItem mi = (MenuItem)item;
                        if (mi.Label == s[i])
                        {
                            wid = mi;
                            Menu = false;
                            return;
                        }
                    }
                }
            }
            if(wid == null)
            GetMainImageJMenuItem(path, out wid, out Menu);
        }

        /// The function `GetMainMenuItem` takes a path and returns the corresponding `Widget` and a
        /// boolean indicating whether it is a menu or not.
        /// 
        /// @param path The path is a string that represents the location of a main menu item in a menu
        /// bar. It is formatted as a series of menu item labels separated by forward slashes ("/"). For
        /// example, "File/Open/Recent" represents the "Recent" menu item under the "Open" menu item
        /// under the
        /// @param Widget The `Widget` parameter `wid` is an output parameter that will store the found
        /// `MenuItem` or `Menu` widget based on the given `path`.
        /// @param Menu The "Menu" parameter is a boolean variable that indicates whether the main menu
        /// item is a menu or a menu item. If it is true, it means the main menu item is a menu, and if
        /// it is false, it means the main menu item is a menu item.
        /// 
        /// @return The method is returning the main menu item specified by the given path. The main
        /// menu item can be either a MenuItem or a Menu.
        private static void GetMainImageJMenuItem(string path, out Widget wid, out bool Menu)
        {
            string[] s = path.Split('/');
            MenuBar w = tabsView.ImageJMenu;
            for (int i = 0; i < s.Length; i++)
            {
                foreach (Widget item in w.Children)
                {
                    //We use a try block incase this widget is a MenuItem instead of Menu.
                    try
                    {
                        MenuItem m = FindItem((Menu)item, s[i]);
                        if (m != null && i == s.Length - 1)
                        {
                            wid = m;
                            Menu = true;
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        MenuItem mi = (MenuItem)item;
                        if (mi.Label == s[i] && i == s.Length - 1)
                        {
                            wid = mi;
                            Menu = false;
                            return;
                        }
                    }
                }
            }
            wid = null;
            Menu = false;
        }

        /// The function `GetContextMenuItem` takes a path and returns the corresponding widget and a
        /// boolean indicating if it is a menu or not.
        /// 
        /// @param path The path parameter is a string that represents the path to a specific context
        /// menu item. It is in the format of a forward-slash-separated string, where each segment
        /// represents a level in the menu hierarchy. For example, "File/Edit/Copy" would represent the
        /// "Copy" item in the "
        /// @param Widget The `Widget` parameter is an output parameter that will store the found widget
        /// (either a `Menu` or a `MenuItem`) based on the given `path`.
        /// @param Menu The `Menu` parameter is a boolean variable that indicates whether the `wid` is a
        /// `Menu` or a `MenuItem`. If `Menu` is `true`, it means `wid` is a `Menu`, otherwise it is a
        /// `MenuItem`.
        /// 
        /// @return The method is returning the widget (wid) and a boolean value (Menu) indicating
        /// whether the widget is a menu or not.
        private static void GetContextMenuItem(string path, out Widget wid, out bool Menu)
        {
            string[] s = path.Split('/');
            Menu w = App.viewer.contextMenu;
            for (int i = 0; i < s.Length; i++)
            {
                foreach (Widget item in w.Children)
                {
                    //We use a try block incase this widget is a MenuItem instead of Menu.
                    try
                    {
                        MenuItem m = FindItem((Menu)item, s[i]);
                        if (m != null && i == s.Length - 1)
                        {
                            wid = m;
                            Menu = true;
                            return;
                        }
                    }
                    catch (Exception)
                    {
                        MenuItem mi = (MenuItem)item;
                        if (mi.Label == s[i] && i == s.Length - 1)
                        {
                            wid = mi;
                            Menu = false;
                            return;
                        }
                    }
                }
            }
            wid = null;
            Menu = false;
        }

        /// The AddMenu function adds a new menu item to an existing menu structure based on the
        /// specified path.
        /// 
        /// @param path The path parameter is a string that represents the file path of the menu item
        /// that needs to be added.
        /// 
        /// @return The method does not have a return type, so it does not return anything.
        public static void AddMenu(string path)
        {
            if (path == null || path == "")
                return;
            //We get the Menu containing the MenuItem specified by the path.
            Widget w;
            bool menu;
            GetMainMenuItem(System.IO.Path.GetDirectoryName(path), out w, out menu);
            if(!menu && w != null)
            {
                MenuItem m = (MenuItem)w;
                if(m.Submenu != null)
                {
                    Menu me = (Menu)m.Submenu;
                    MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                    mi.ButtonPressEvent += ItemClicked;
                    me.Append(mi);
                    me.ShowAll();
                    return;
                }
                else
                {
                    //If the item we need to add a child item to, is not already a Menu we need to make it a menu
                    Menu me = new Menu();
                    MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                    mi.ButtonPressEvent += ItemClicked;
                    me.Append(mi);
                    m.Submenu = me;
                    me.ShowAll();
                    return;
                }
            }
            else if(menu && w!=null)
            {
                //Since this is already a Menu we can just append the item to it.
                Menu m = (Menu)w;
                MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                mi.ButtonPressEvent += ItemClicked;
                m.Append(mi);
            }

            GetMainImageJMenuItem(System.IO.Path.GetDirectoryName(path), out w, out menu);
            if (!menu && w != null)
            {
                MenuItem m = (MenuItem)w;
                if (m.Submenu != null)
                {
                    Menu me = (Menu)m.Submenu;
                    MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                    mi.ButtonPressEvent += ItemClicked;
                    me.Append(mi);
                    me.ShowAll();
                }
                else
                {
                    //If the item we need to add a child item to, is not already a Menu we need to make it a menu
                    Menu me = new Menu();
                    MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                    mi.ButtonPressEvent += ItemClicked;
                    me.Append(mi);
                    m.Submenu = me;
                    me.ShowAll();
                }
            }
            else if (menu && w != null)
            {
                //Since this is already a Menu we can just append the item to it.
                Menu m = (Menu)w;
                MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                mi.ButtonPressEvent += ItemClicked;
                m.Append(mi);
            }
        }
        /// The function `AddContextMenu` adds a context menu item to a specified path in a C#
        /// application.
        /// 
        /// @param path The path parameter is a string that represents the path of a file or directory.
        /// 
        /// @return The method does not have a return type, so nothing is being returned.
        public static void AddContextMenu(string path)
        {
            if (path == null || path == "")
                return;
            string[] s = path.Split('/');
            //We get the Menu containing the MenuItem specified by the path.

            Widget w;
            bool menu;
            GetContextMenuItem(System.IO.Path.GetDirectoryName(path), out w, out menu);
            if (!menu && w != null)
            {
                MenuItem m = (MenuItem)w;
                if (m.Submenu != null)
                {
                    Menu me = (Menu)m.Submenu;
                    MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                    mi.ButtonPressEvent += ItemClicked;
                    me.Append(mi);
                    me.ShowAll();
                }
                else
                {
                    //If the item we need to add a child item to, is not already a Menu we need to make it a menu
                    Menu me = new Menu();
                    MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                    mi.ButtonPressEvent += ItemClicked;
                    me.Append(mi);
                    m.Submenu = me;
                    me.ShowAll();
                }
            }
            else if (!menu && w == null)
            {
                MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                mi.ButtonPressEvent += ItemClicked;
                viewer.contextMenu.Append(mi);
                viewer.contextMenu.ShowAll();
            }
        }
        /// The function "ItemClicked" is an event handler that is triggered when a menu item is
        /// clicked, and it performs a specific function based on the label of the clicked menu item.
        /// 
        /// @param o The "o" parameter is of type object and represents the object that triggered the
        /// event. In this case, it is a MenuItem object.
        /// @param ButtonPressEventArgs ButtonPressEventArgs is an event argument class that contains
        /// information about a button press event. It typically includes properties such as the button
        /// that was pressed, the timestamp of the event, and any modifiers (e.g., Ctrl, Shift) that
        /// were pressed along with the button.
        private static void ItemClicked(object o, ButtonPressEventArgs args)
        {
            MenuItem ts = (MenuItem)o;
            if (Function.Functions.ContainsKey(ts.Label))
            {
                Function f = Function.Functions[ts.Label];
                f.PerformFunction(true);
            }
            else
            {
                if (ts.Label.EndsWith(".dll"))
                {
                    try
                    {
                        Plugin.Plugins[ts.Label].Execute(new string[] { });
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message.ToString());
                    }
                }
                else if(ts.Label.EndsWith(".onnx") || ts.Label.EndsWith(".pt"))
                {
                    try
                    {
                        ML.ML.Run(ts.Label, ImageView.SelectedImage);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message.ToString());
                    }
                }
            }
        }
    }
}
