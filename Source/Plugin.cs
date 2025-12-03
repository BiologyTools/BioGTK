using AForge;
using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using static BioGTK.Plugin;

namespace BioGTK
{
    public class Plugin
    {
        public static Dictionary<string, IPlugin> Plugins = new Dictionary<string, IPlugin>();
        public interface IPlugin
        {
            string Name { get; }
            string MenuPath { get; }
            bool ContextMenu { get; }
            void Execute(string[] args);
            void KeyUpEvent(object o, KeyReleaseEventArgs e);
            void KeyDownEvent(object o, KeyPressEventArgs e);
            void ScrollEvent(object o, ScrollEventArgs args);
            void Render(object sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e);
            void MouseMove(object o, PointD e, MotionNotifyEventArgs buts);
            void MouseUp(object o, PointD e, ButtonReleaseEventArgs buts);
            void MouseDown(object o, PointD e, ButtonPressEventArgs buts);
        }
    }
    
    public static class Plugins
    {
        public static void Initialize()
        {
            foreach (string s in Directory.GetFiles(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/Plugins"))
            {
                try
                {
                    if (!s.EndsWith(".dll") || Plugin.Plugins.ContainsKey(Path.GetFileName(s)))
                        continue;
                    string dir = System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/Plugins/" + Path.GetFileName(s);
                    dir.Replace("\\", "/");
                    // Load the plugin assembly
                    Assembly pluginAssembly = Assembly.LoadFile(dir);
                    // Find the type which implements the IPlugin interface
                    var pluginType = pluginAssembly.GetTypes().FirstOrDefault(t => typeof(IPlugin).IsAssignableFrom(t) && !t.IsInterface);
                    if (pluginType != null)
                    {
                        // Create an instance of this type
                        IPlugin pluginInstance = Activator.CreateInstance(pluginType) as IPlugin;
                        Plugin.Plugins.Add(Path.GetFileName(s), pluginInstance);
                        if (pluginInstance.ContextMenu)
                            App.AddContextMenu(pluginInstance.MenuPath);
                        else
                            App.AddMenu(pluginInstance.MenuPath);                   
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e.Message.ToString());
                }
                
            }
        }
        public static void KeyUpEvent(object o, KeyReleaseEventArgs e)
        {
            foreach (IPlugin p in Plugin.Plugins.Values)
            {
                p.KeyUpEvent(o, e);
            }
        }
        public static void KeyDownEvent(object o, KeyPressEventArgs e)
        {
            foreach (IPlugin p in Plugin.Plugins.Values)
            {
                p.KeyDownEvent(o, e);
            }
        }
        public static void ScrollEvent(object o, ScrollEventArgs args)
        {
            foreach (IPlugin p in Plugin.Plugins.Values)
            {
                p.ScrollEvent(o, args);
            }
        }
        public static void Render(object o, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
        {
            foreach (IPlugin p in Plugin.Plugins.Values)
            {
                ((IPlugin)p).Render(o, e);
            }
        }
        public static void MouseMove(object o, PointD e, MotionNotifyEventArgs buts)
        {
            foreach (IPlugin p in Plugin.Plugins.Values)
            {
                p.MouseMove(o, e, buts);
            }
        }
        public static void MouseUp(object o, PointD e, ButtonReleaseEventArgs buts)
        {
            foreach (IPlugin p in Plugin.Plugins.Values)
            {
                p.MouseUp(o, e, buts);
            }
        }
        public static void MouseDown(object o, PointD e, ButtonPressEventArgs buts)
        {
            foreach (IPlugin p in Plugin.Plugins.Values)
            {
                p.MouseDown(o, e, buts);
            }
        }
    }

}
