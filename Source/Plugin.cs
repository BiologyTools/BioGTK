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
            void Execute();
        }
    }
    
    public static class Plugins
    {
        public static void Initialize()
        {
            foreach (string s in Directory.GetFiles("Plugins"))
            {
                if (!s.EndsWith(".dll") || Plugin.Plugins.ContainsKey(Path.GetFileName(s)))
                    continue;
                // Load the plugin assembly
                Assembly pluginAssembly = Assembly.LoadFile(Environment.CurrentDirectory + "/" + s);
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
                else
                {
                    Console.WriteLine("No plugin found in the assembly: " + s);
                }
            }
        }
        
    }

}
