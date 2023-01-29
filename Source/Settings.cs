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
        static Dictionary<string,string> Default = new Dictionary<string,string>();
        public static string GetSettings(string name)
        {
            if(Default.ContainsKey(name)) return Default[name];
            return "";
        }
        public static void AddSettings(string name,string val)
        {
            Default.Add(name, val);
        }
        static string path = System.IO.Path.GetDirectoryName(Environment.ProcessPath);
        public static void Save()
        {
            string val = "";
            foreach (var item in Default)
            {
                val += item.Key + "=" + item.Value + Environment.NewLine;
            }
            File.WriteAllText(path + "/Settings.txt", val);
        }
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
