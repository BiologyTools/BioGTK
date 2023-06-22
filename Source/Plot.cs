using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AForge;
using Gdk;
using GLib;
using Gtk;
using OxyPlot;
using System.Threading;
using System.IO;
using OxyPlot.Series;
using OxyPlot.Axes;

namespace BioGTK
{
    public class Plot : Gtk.Window
    {
        PlotModel model;
        string file;
        string name;
        Bitmap bitmap;
        List<double[]> data = new List<double[]>();
        static Dictionary<string, Plot> plots = new Dictionary<string, Plot>();

        public enum PlotType
        {
            Bar,
            Signal,
            Scatter
        }
        PlotType type;
        public PlotType Type
        {
            get { return type; }
            set { type = value; }
        }
        public static Dictionary<string,Plot> Plots
        {
            get { return plots; }
        }
        public List<double[]> Data
        {
            get { return data; }
            set { data = value; UpdateImage(); }
        }
        public Bitmap Image
        {
            get { return bitmap; }
            set { bitmap = value; }
        }
        public void UpdateImage()
        {
            OxyPlot.SkiaSharp.PngExporter.Export(model, file, AllocatedWidth, AllocatedHeight);
            this.Title = name;
            pixbuf = new Pixbuf(file);
            image.QueueDraw();
        }

        #region Properties
        Pixbuf pixbuf;
        private Builder _builder;
#pragma warning disable 649
        [Builder.Object]
        private Gtk.DrawingArea image;
        [Builder.Object]
        private Gtk.MenuItem saveImageMenu;
        [Builder.Object]
        private Gtk.MenuItem saveCSVMenu;

        [Builder.Object]
        private Gtk.CheckMenuItem barMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem signalMenu;
        [Builder.Object]
        private Gtk.CheckMenuItem scatterMenu;

#pragma warning restore 649
        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the Plot class.
        /// 
        /// @return A new instance of the Plot class.
        public static Plot Create(double[] vals, string name)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Plot.glade", null);
            return new Plot(builder, builder.GetObject("plot").Handle, vals, name, PlotType.Bar);
        }
        /// It creates a new instance of the Plot class.
        /// 
        /// @return A new instance of the Plot class.
        public static Plot Create(double[] vals, string name, PlotType typ)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Plot.glade", null);
            return new Plot(builder, builder.GetObject("plot").Handle, vals, name, typ);
        }

        /* It's the constructor of the class. */
        protected Plot(Builder builder, IntPtr handle, double[] vals, string name, PlotType typ) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            model = new PlotModel { Title = name };
            data.Add(vals);
            double maxy = 0;
            double maxx = 0;
            for (int s = 0; s < data.Count; s++)
            {
                var ser = new LineSeries()
                {
                    Color = OxyColors.Blue,
                    MarkerType = MarkerType.Circle,
                    MarkerSize = 4,
                    MarkerStroke = OxyColors.Black,
                    MarkerFill = OxyColors.Black,
                    MarkerStrokeThickness = 1.0
                };
                ser.Title = name;
                for (int i = 0; i < data.Count; i++)
                {
                    if (data[i].Length > maxy)
                        maxy = data[i].Length;
                    for (int x = 0; x < data[i].Length; x++)
                    {
                        ser.Points.Add(new DataPoint(x, data[i][x]));
                        if(data[i][x] > maxx)
                            maxx = data[i][x];
                    }
                }
                model.Series.Add(ser);
            }
            if (!name.EndsWith(".png") || !name.EndsWith(".PNG"))
            file = name + ".png";
            this.Title = name;
            image.Drawn += Image_Drawn;
            image.SizeAllocated += Image_SizeAllocated;
            image.ButtonPressEvent += Image_ButtonPressEvent;
            barMenu.ButtonPressEvent += BarMenu_ButtonPressEvent;
            signalMenu.ButtonPressEvent += SignalMenu_ButtonPressEvent;
            scatterMenu.ButtonPressEvent += ScatterMenu_ButtonPressEvent;
            saveImageMenu.ButtonPressEvent += SaveImageMenu_ButtonPressEvent;
            saveCSVMenu.ButtonPressEvent += SaveCSVMenu_ButtonPressEvent;
            this.DeleteEvent += About_DeleteEvent;
            this.name = name;
            selected = this;
            if(plots.ContainsKey(name))
                plots[name] = this;
            else
                plots.Add(name,this);
        }

        private void SaveCSVMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
       new Gtk.FileChooserDialog("Save CSV Plot data.",
           this,
           FileChooserAction.Save,
           "Cancel", ResponseType.Cancel,
           "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;

            string s = "";
            foreach (double[] item in data)
            {
                for (int i = 0; i < data.Count; i++)
                {
                    s += i + ",";
                }
                s += Environment.NewLine;
                foreach (double d in item)
                {
                    s += d + "," + Environment.NewLine;
                }
            }
            File.WriteAllText(filechooser.Filename, s);
            filechooser.Destroy();
            
        }

        private void SaveImageMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            Gtk.FileChooserDialog filechooser =
       new Gtk.FileChooserDialog("Save PNG Image of Plot.",
           this,
           FileChooserAction.Save,
           "Cancel", ResponseType.Cancel,
           "Save", ResponseType.Accept);
            if (filechooser.Run() != (int)ResponseType.Accept)
                return;
            pixbuf.Save(filechooser.Filename,"png");
            filechooser.Destroy();
            
        }

        private void ScatterMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            signalMenu.Active = false;
            barMenu.Active = false;
            type = PlotType.Scatter;
            UpdateImage();
        }

        private void SignalMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            scatterMenu.Active = false;
            barMenu.Active = false;
            type = PlotType.Signal;
            UpdateImage();
        }

        private void BarMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            signalMenu.Active = false;
            scatterMenu.Active = false;
            type = PlotType.Bar;
            UpdateImage();
        }

        private void Image_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            selected = this;
            Recorder.AddLine("Plot.SelectedPlot = Plot.Plots[" + name + "];");
        }

        System.Threading.Thread th;
        static Plot selected;
        public static Plot SelectedPlot
        {
            get { return selected; }
        }
        static void ShowPlot()
        {
            selected.Show();
        }

        private void Image_SizeAllocated(object o, SizeAllocatedArgs args)
        {
            UpdateImage();
        }

        private void About_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            plots.Remove(name);
            Hide();
        }

        private void Image_Drawn(object o, DrawnArgs e)
        {
            Pixbuf pf = pixbuf.ScaleSimple(image.AllocatedWidth, image.AllocatedHeight, InterpType.Bilinear);
            Gdk.CairoHelper.SetSourcePixbuf(e.Cr, pf, 0, 0);
            e.Cr.Paint();
            e.Cr.Stroke();
        }

        #endregion

    }
}
