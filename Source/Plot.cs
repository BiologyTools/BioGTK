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
using org.checkerframework.checker.units.qual;
using ScottPlot;
using System.Threading;

namespace BioGTK
{
    public class Plot : Gtk.Window
    {
        public ScottPlot.Plot plot;
        string file;
        string name;
        Bitmap bitmap;
        List<double[]> data = new List<double[]>();
        static Dictionary<string, Plot> plots = new Dictionary<string, Plot>();
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
            plot = new ScottPlot.Plot(AllocatedWidth, AllocatedHeight);
            foreach (double[] val in data) 
            {
                plot.AddBar(val);
            }
            file = plot.SaveFig(name + ".png");
            this.Title = name;
            pixbuf = new Pixbuf(file);
        }

        #region Properties
        Pixbuf pixbuf;
        private Builder _builder;
#pragma warning disable 649
        [Builder.Object]
        private Gtk.DrawingArea image;
        [Builder.Object]
        private Gtk.Label label;
#pragma warning restore 649
        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the Plot class.
        /// 
        /// @return A new instance of the Plot class.
        public static Plot Create(double[] vals, string name)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Plot.glade", null);
            return new Plot(builder, builder.GetObject("plot").Handle, vals, name);
        }


        /* It's the constructor of the class. */
        protected Plot(Builder builder, IntPtr handle, double[] vals, string name) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            this.Title = name;
            image.Drawn += Image_Drawn;
            image.SizeAllocated += Image_SizeAllocated;
            this.DeleteEvent += About_DeleteEvent;
            data.Add(vals);
            this.name = name;
            selected = this;
            if(plots.ContainsKey(name))
                plots[name] = this;
            else
                plots.Add(name,this);
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
            selected.Present();
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
