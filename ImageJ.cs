using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;

namespace BioGTK
{
    public class ImageJ
    {
        public static string ImageJPath;
        public static List<Process> processes = new List<Process>();
        private static Random rng = new Random();
        public static void RunMacro(string file, string param)
        {
            if(ImageJPath == "")
            {
                if (!App.SetImageJPath())
                    return;
            }
            file.Replace("/", "\\");
            Process pr = new Process();
            pr.StartInfo.FileName = ImageJPath;
            pr.StartInfo.Arguments = "-macro " + file + " " + param;
            pr.Start();
            processes.Add(pr);
            Recorder.AddLine("ImageJ.RunMacro(" + file + "," + '"' + param + '"' + ");");
        }
        public static void RunString(string con, string param, bool headless)
        {

            if (ImageJPath == "")
            {
                if (!App.SetImageJPath())
                    return;
            }
            Process pr = new Process();
            pr.StartInfo.FileName = ImageJPath;
            string te = rng.Next(0, 9999999).ToString();
            string p = Environment.CurrentDirectory + "\\" + te + ".txt";
            p.Replace("/", "\\");
            File.WriteAllText(p,con);
            if(headless)
                pr.StartInfo.Arguments = "--headless -macro " + p + " " + param;
            else
                pr.StartInfo.Arguments = "-macro " + p + " " + param;
            pr.Start();
            File.Delete(Path.GetDirectoryName(ImageJPath) + "/done.txt");
            processes.Add(pr);
            do
            {
                if (File.Exists(Path.GetDirectoryName(ImageJPath) + "/done.txt"))
                {
                    do
                    {
                        try
                        {
                            File.Delete(Path.GetDirectoryName(ImageJPath) + "/done.txt");
                        }
                        catch (Exception)
                        {
                        
                        }
                    } while (File.Exists(Path.GetDirectoryName(ImageJPath) + "/done.txt"));
                    pr.Kill();
                    break;
                }
            } while (!pr.HasExited);
            File.Delete(p);
        }
        public static void RunOnImage(string con, bool headless, bool onTab, bool bioformats)
        {
            if (ImageJPath == "")
            {
                if (!App.SetImageJPath())
                    return;
            }
            string filename = "";
            string dir = Path.GetDirectoryName(ImageView.SelectedImage.file);

            if (ImageView.SelectedImage.ID.EndsWith(".ome.tif"))
            {
                filename = Path.GetFileNameWithoutExtension(ImageView.SelectedImage.ID);
                filename = filename.Remove(filename.Length - 4, 4);
            }
            else
                filename = Path.GetFileNameWithoutExtension(ImageView.SelectedImage.ID);
            string file = dir + "\\" + filename + "-temp" + ".ome.tif";
            file = file.Replace("\\", "/");
            string st =
            "run(\"Bio-Formats Importer\", \"open=\" + getArgument + \" autoscale color_mode=Default open_all_series display_rois rois_import=[ROI manager] view=Hyperstack stack_order=XYCZT\"); " + con +
            "run(\"Bio-Formats Exporter\", \"save=" + file + " export compression=Uncompressed\"); " +
            "dir = getDir(\"startup\"); " +
            "File.saveString(\"done\", dir + \"/done.txt\");";
            if(bioformats)
                st =
                "open(getArgument); " + con +
                "run(\"Bio-Formats Exporter\", \"save=" + file + " export compression=Uncompressed\"); " +
                "dir = getDir(\"startup\"); " +
                "File.saveString(\"done\", dir + \"/done.txt\");";
            //We save the image as a temp image as otherwise imagej won't export due to file access error.
            RunString(st, ImageView.SelectedImage.file, headless);

            if (!File.Exists(file))
                return;

            string ffile = dir + "/" + filename + ".ome.tif";
            File.Delete(ffile);
            File.Copy(file, ffile);
            File.Delete(file);
            App.tabsView.AddTab(BioImage.OpenFile(ffile));
            App.viewer.UpdateImage();
            App.viewer.UpdateView();
            
            Recorder.AddLine("RunOnImage(\"" + con + "\"," + headless + "," + onTab + ");");
        }
        public static void Initialize(string path)
        {
            ImageJPath = path;
        }
    }
}
