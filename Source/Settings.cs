using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using ucar.nc2.ncml;

namespace BioGTK
{
    public static class Settings
    {
        /* Creating a new dictionary with a string as the key and a string as the value. */
        static Dictionary<string,string> Default = new Dictionary<string,string>();
        /// If the dictionary contains the key, return the value, otherwise return an empty string
        /// 
        /// @param name The name of the setting you want to get.
        /// 
        /// @return The value of the key in the dictionary.
        public static string GetSettings(string name)
        {
            if(Default.ContainsKey(name)) return Default[name];
            return "";
        }
        /// It adds a new setting to the settings file
        /// 
        /// @param name The name of the setting.
        /// @param val The value of the setting
        public static void AddSettings(string name,string val)
        {
            Default.Add(name, val);
        }
        static string path = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
        /// It takes the values in the dictionary and writes them to a file
        public static void Save()
        {
            string val = "";
            foreach (var item in Default)
            {
                val += item.Key + "=" + item.Value + Environment.NewLine;
            }
            File.WriteAllText(path + "/Settings.txt", val);
        }
        /// It reads a file and adds the contents to a dictionary
        /// 
        /// @return The settings are being returned.
        public static void Load()
        {
            if (!File.Exists(path + "/Settings.txt"))
                return;
            string[] sts = File.ReadAllLines(path + "/Settings.txt");
            foreach (string item in sts)
            {
                string[] st = item.Split('=');
                Default.Add(st[0], st[1]);
            }
        }
    }
}
