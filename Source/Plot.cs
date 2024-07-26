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
        /// The function updates an image by saving it as a PNG file, setting the window title, creating
        /// a new Pixbuf object from the file, and redrawing the image.
        public void UpdateImage()
        {
            model.SavePng(file, AllocatedWidth, AllocatedHeight);
            this.Title = name;
            pixbuf = new Pixbuf(file);
            image.QueueDraw();
        }
        /// The function initializes a scatter plot by adding data points to the model.
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
        /// The function initializes a bar chart by creating a list of bar series and adding them to the
        /// model.
        private void InitBars()
        {
            model.Clear();
            foreach (double[] items in data)
            {
                int i = 0;
                List<Bar> bars = new List<Bar>();
                foreach (double item in items)
                {
                    Bar b = new Bar();
                    b.Value = item;
                    b.Position = i;
                    bars.Add(b);
                    i++;
                }
                model.Add.Bars(bars);
            }
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
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Plot.glade", FileMode.Open));
            return new Plot(builder, builder.GetObject("plot").Handle, vals, name, PlotType.Scatter);
        }
        /// It creates a new instance of the Plot class.
        /// 
        /// @return A new instance of the Plot class.
        public static Plot Create(double[] vals, string name, PlotType typ)
        {
            Builder builder = new Builder(new FileStream("Glade/Plot.glade", FileMode.Open));
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
            App.ApplyStyles(this);
        }

        /// The function saves data in CSV format to a user-selected file.
        /// 
        /// @param o The parameter "o" is the object that triggered the event. In this case, it is the
        /// object that represents the button that was pressed.
        /// @param ButtonPressEventArgs ButtonPressEventArgs is an event argument that provides
        /// information about a button press event. It contains properties such as the button that was
        /// pressed, the event's coordinates, and modifiers (e.g., Shift, Control) that were active
        /// during the event.
        /// 
        /// @return The method is not returning anything.
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

        /// The function allows the user to save a PNG image of a plot by selecting a file location
        /// using a file chooser dialog.
        /// 
        /// @param o The "o" parameter is the object that triggered the event. In this case, it is the
        /// SaveImageMenu button that was clicked.
        /// @param ButtonPressEventArgs ButtonPressEventArgs is an event argument that contains
        /// information about a button press event, such as the button that was pressed and the
        /// coordinates of the event.
        /// 
        /// @return If the "Save" button is clicked in the file chooser dialog, the response type
        /// "Accept" is returned. Otherwise, if the "Cancel" button is clicked or the dialog is closed,
        /// the response type "Cancel" is returned.
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

        /// The ScatterMenu_ButtonPressEvent function sets the plot type to scatter, initializes the
        /// scatter plot, and updates the image.
        /// 
        /// @param o The "o" parameter is the object that triggered the event. In this case, it is the
        /// ScatterMenu button that was pressed.
        /// @param ButtonPressEventArgs ButtonPressEventArgs is an event argument class that contains
        /// information about a button press event. It provides properties and methods to access details
        /// such as the button that was pressed, the position of the press, and any modifiers that were
        /// active during the press.
        private void ScatterMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            barMenu.Active = false;
            type = PlotType.Scatter;
            InitScatter();
            UpdateImage();
        }

        /// The function sets the plot type to "Bar", initializes the bars, and updates the image.
        /// 
        /// @param o The "o" parameter is the object that triggered the event. In this case, it is the
        /// button that was pressed to trigger the event.
        /// @param ButtonPressEventArgs ButtonPressEventArgs is an event argument class that contains
        /// information about a button press event. It provides properties and methods to access details
        /// such as the button that was pressed, the event timestamp, and the event modifiers (e.g.,
        /// Shift, Control, etc.).
        private void BarMenu_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            scatterMenu.Active = false;
            type = PlotType.Bar;
            InitBars();
            UpdateImage();
        }

        /// The Image_ButtonPressEvent function sets the selected plot to the current plot and adds a
        /// line to the Recorder.
        /// 
        /// @param o The "o" parameter is the object that triggered the event. In this case, it is the
        /// image button that was pressed.
        /// @param ButtonPressEventArgs ButtonPressEventArgs is an event argument class that contains
        /// information about a button press event. It provides properties such as Button, which
        /// represents the button that was pressed, and EventTime, which represents the time at which
        /// the event occurred.
        private void Image_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            selected = this;
            BioLib.Recorder.AddLine("Plot.SelectedPlot = Plot.Plots[" + name + "];",false);
        }

        System.Threading.Thread th;
        static Plot selected;
        public static Plot SelectedPlot
        {
            get { return selected; }
        }
        /// The ShowPlot function displays the selected plot.
        static void ShowPlot()
        {
            selected.Show();
        }

        /// The function "Image_SizeAllocated" updates the image based on the allocated size.
        /// 
        /// @param o The "o" parameter is the object that triggered the event. In this case, it is the
        /// object that the event handler is attached to, which is likely an instance of a class.
        /// @param SizeAllocatedArgs SizeAllocatedArgs is a class that contains information about the
        /// allocated size of a widget. It is used to pass this information to event handlers or methods
        /// that need to perform actions based on the allocated size of the widget.
        private void Image_SizeAllocated(object o, SizeAllocatedArgs args)
        {
            UpdateImage();
        }

        /// The function "About_DeleteEvent" removes a plot from a collection and hides the current
        /// window.
        /// 
        /// @param o The "o" parameter is an object that represents the sender of the event. It is
        /// typically used to refer to the object that raised the event.
        /// @param DeleteEventArgs DeleteEventArgs is an event argument class that is used to pass
        /// information about a delete event. It contains properties and methods that provide
        /// information about the event and allow you to control the event's behavior.
        private void About_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            plots.Remove(name);
            Hide();
        }

        /// The function `Image_Drawn` scales a `Pixbuf` and sets it as the source for a `CairoContext`,
        /// then paints and strokes the context.
        /// 
        /// @param o The "o" parameter is the object that triggered the event. In this case, it is the
        /// object that the event handler is attached to.
        /// @param DrawnArgs DrawnArgs is an event argument class that contains information about the
        /// drawing event. It typically includes a Cairo context (Cr) that can be used to perform
        /// drawing operations, such as setting the source image and painting/stroking on the context.
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
