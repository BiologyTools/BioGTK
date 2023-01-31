using Bio;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public static class App
    {
        public static TabsView tabsView = null;
        public static Tools tools = null;
        public static ImageView viewer = null;
        public static ColorTool color = null;
        public static NodeView nodeView = null;
        public static FiltersView filters = null;
        public static ApplyFilter applyFilter = null;
        public static ChannelsTool channelsTool = null;
        public static ROIManager roiManager = null;
        public static Scripting scripting = null;

        /// Initialize() is a function that initializes the BioImage Suite Web
        public static void Initialize()
        {
            BioImage.Initialize();
            tools = Tools.Create();
            filters = FiltersView.Create();
            roiManager = ROIManager.Create();
            scripting = Scripting.Create();
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

    }
}
