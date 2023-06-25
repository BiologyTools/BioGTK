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
using System.Threading;
using System.IO;
using ScottPlot;

namespace BioGTK
{
    public class Plot : Gtk.Window
    {
        ScottPlot.Plot model;
        //PlotModel model;
        string file;
        string name;
        Bitmap bitmap;
        List<double[]> data = new List<double[]>();
        static Dictionary<string, Plot> plots = new Dictionary<string, Plot>();

        public enum PlotType
        {
            Bar,
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
        public ScottPlot.Plot Model
        {
            get { return model; }
            set { model = value; UpdateImage(); }
        }
        public void UpdateImage()
        {
            model.SavePng(file, AllocatedWidth, AllocatedHeight);
            this.Title = name;
            pixbuf = new Pixbuf(file);
            image.QueueDraw();
        }
        private void InitScatter()
        {
            model.Clear();
            List<Coordinates> cs = new List<Coordinates>();
            for (int i = 0; i < data.Count; i++)
            {
                for (int x = 0; x < data[i].Length; x++)
                {
                    cs.Add(new ScottPlot.Coordinates(x, data[i][x]));
                }
            }
            model.Add.Scatter(cs);
        }
        private void InitBars()
        {
            model.Clear();
            int i = 0;
            List<ScottPlot.Plottables.BarSeries> seriesList = new List<ScottPlot.Plottables.BarSeries>();
            foreach (double[] items in data)
            {
                List<ScottPlot.Plottables.Bar> bars = new List<ScottPlot.Plottables.Bar>();
                foreach (double item in items)
                {
                    bars.Add(new ScottPlot.Plottables.Bar(i, item));
                }
                ScottPlot.Plottables.BarSeries series1 = new()
                {
                    Bars = bars,
                    Label = i.ToString(),
                };
                seriesList.Add(series1);
                i++;
            }
            model.Add.Bar(seriesList);
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
            return new Plot(builder, builder.GetObject("plot").Handle, vals, name, PlotType.Scatter);
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
            model = new ScottPlot.Plot();
            data.Add(vals);
            if (typ == PlotType.Scatter)
                InitScatter();
            else
            if (typ == PlotType.Bar)
                InitBars();
            if (!name.EndsWith(".png") || !name.EndsWith(".PNG"))
            file = name + ".png";
            this.Title = name;
            image.Drawn += Image_Drawn;
            image.SizeAllocated += Image_SizeAllocated;
            image.ButtonPressEvent += Image_ButtonPressEvent;
            barMenu.ButtonPressEvent += BarMenu_ButtonPressEvent;
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
            barMenu.Active = false;
            type = PlotType.Scatter;
            InitScatter();
            UpdateImage();
        }

        private void BarMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            scatterMenu.Active = false;
            type = PlotType.Bar;
            InitBars();
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
