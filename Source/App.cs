using Bio;
using Gtk;
using org.checkerframework.checker.units.qual;
using sun.tools.tree;
using System;
using System.Collections.Generic;
using System.Linq;
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

        /// Initialize() is a function that initializes the BioImage Suite Web
        public static void Initialize()
        {
            BioImage.Initialize();
            tools = Tools.Create();
            filters = FiltersView.Create();
            roiManager = ROIManager.Create();
            scripting = Scripting.Create();
            funcs = Functions.Create();
            //channelsTool = ChannelsTool.Create();
            console = BioConsole.Create();
            stack = StackTools.Create();
            about = About.Create();
            setTool = SetTool.Create();
            recorder = Recorder.Create();
            //color = ColorTool.Create();
            Settings.Load();
            ImageJ.ImageJPath = Settings.GetSettings("ImageJPath");
        }

        /// This function creates a file chooser dialog that allows the user to select the location of
        /// the ImageJ executable
        /// 
        /// @return A boolean value.
        public static bool SetImageJPath()
        {
            Gtk.FileChooserDialog filechooser =
    new Gtk.FileChooserDialog("Select ImageJ Executable Location",Scripting.window,
        FileChooserAction.Save,
        "Cancel", ResponseType.Cancel,
        "Save", ResponseType.Accept);
            filechooser.SetCurrentFolder(System.IO.Path.GetDirectoryName(Environment.ProcessPath));
            if (filechooser.Run() != (int)ResponseType.Accept)
                return false;
            ImageJ.ImageJPath = filechooser.Filename;
            Settings.AddSettings("ImageJPath", filechooser.Filename);
            Settings.Save();
            return true;
        }

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
        
        private static void GetMainMenuItem(string path, out Widget wid, out bool Menu)
        {
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
            else if(menu && w!=null)
            {
                //Since this is already a Menu we can just append the item to it.
                Menu m = (Menu)w;
                MenuItem mi = new MenuItem(System.IO.Path.GetFileName(path));
                mi.ButtonPressEvent += ItemClicked;
                m.Append(mi);
            }
        }
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
        private static void ItemClicked(object o, ButtonPressEventArgs args)
        {
            MenuItem ts = (MenuItem)o;
            if (Function.Functions.ContainsKey(ts.Label))
            {
                Function f = Function.Functions[ts.Label];
                f.PerformFunction(true);
            }
        }
    }
}
