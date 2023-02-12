using Gtk;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AForge;
using System.Drawing;
using Cairo;
using Color = System.Drawing.Color;
using Gdk;

namespace BioGTK
{
    public class HistogramControl : Gtk.Window
    {

        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private MenuItem setMin;
        [Builder.Object]
        private MenuItem setMax;
        [Builder.Object]
        private MenuItem setMinAll;
        [Builder.Object]
        private MenuItem setMaxAll;
        [Builder.Object]
        private DrawingArea view;
        [Builder.Object]
        private Menu menu;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        public static HistogramControl Create(Channel channel)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Histogram.glade", null);
            return new HistogramControl(builder, builder.GetObject("histogram").Handle,channel);
        }
        protected HistogramControl(Builder builder, IntPtr handle, Channel channel) : base(handle)
        {
            _builder = builder;
            this.channel = channel;
            builder.Autoconnect(this);
            Init();
        }
        #endregion

        public Channel channel = null;
        public void Init()
        {
            if (ImageView.SelectedImage.bitsPerPixel == 8)
            {
                graphMax = 255;
                Bin = 1;
            }
            else
            {
                graphMax = ushort.MaxValue;
                Bin = 10;
            }
            this.Drawn += HistogramControl_Drawn;
            this.MotionNotifyEvent += HistogramControl_MotionNotifyEvent;
            this.ButtonPressEvent += HistogramControl_ButtonPressEvent;
            this.ScrollEvent += HistogramControl_ScrollEvent;
            setMin.ButtonPressEvent += SetMin_ButtonPressEvent;
            setMax.ButtonPressEvent += SetMax_ButtonPressEvent;
            setMinAll.ButtonPressEvent += SetMinAll_ButtonPressEvent;
            setMaxAll.ButtonPressEvent += SetMaxAll_ButtonPressEvent;
            this.AddEvents((int)
                (EventMask.ButtonPressMask
                | EventMask.ButtonReleaseMask
                | EventMask.KeyPressMask
                | EventMask.PointerMotionMask | EventMask.ScrollMask));
        }

        /// If the scroll direction is up, increase the graphMax by 50. If the scroll direction is down,
        /// decrease the graphMax by 50
        /// 
        /// @param o The object that the event is being called from.
        /// @param ScrollEventArgs 
        private void HistogramControl_ScrollEvent(object o, ScrollEventArgs args)
        {
            if(args.Event.Direction == Gdk.ScrollDirection.Up)
            {
                graphMax += 200;
            }
            else if (args.Event.Direction == Gdk.ScrollDirection.Down)
            {
                graphMax -= 200;
            }
            UpdateView();
        }

        /// This function sets the maximum value of the selected channel to the current mouse position
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void SetMaxAll_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            foreach (Channel c in ImageView.SelectedImage.Channels)
            {
                for (int i = 0; i < c.range.Length; i++)
                {
                    c.range[i].Max = (int)MouseValX;
                }
            }
            this.QueueDraw();
            App.viewer.UpdateImage();
        }

        /// This function sets the minimum value of all channels to the current mouse position
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void SetMinAll_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            foreach (Channel c in ImageView.SelectedImage.Channels)
            {
                for (int i = 0; i < c.range.Length; i++)
                {
                    c.range[i].Min = (int)MouseValX;
                }
            }
            this.QueueDraw();
            App.viewer.UpdateImage();
        }

        /// When the user clicks on the graph, the function gets the x-value of the mouse click and sets
        /// the maximum value of the selected sample to that value
        /// 
        /// @param o The object that the event is attached to.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void SetMax_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.channelsTool.SelectedChannel.range[App.channelsTool.SelectedSample].Max = (int)MouseValX;
            this.QueueDraw();
            App.viewer.UpdateImage();
        }

        /// When the user clicks on the graph, the function sets the minimum value of the selected
        /// sample to the x-coordinate of the mouse
        /// 
        /// @param o The object that the event is being called from.
        /// @param ButtonPressEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-button.html.en
        private void SetMin_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.channelsTool.SelectedChannel.range[App.channelsTool.SelectedSample].Min = (int)MouseValX;
            this.QueueDraw();
            App.viewer.UpdateImage();
        }

        /// This function is called when the user presses a button on the mouse
        /// 
        /// @param o The object that the event is being called on.
        /// @param ButtonPressEventArgs 
        public void HistogramControl_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            mouseX = (int)args.Event.X;
            mouseY = (int)args.Event.Y;
            this.QueueDraw();
            if(args.Event.Button == 3)
            menu.Popup();
        }

       /// The function takes the mouse position and divides it by the scaling factor to get the actual
       /// value of the mouse position
       /// 
       /// @param o The object that the event is being called from.
       /// @param MotionNotifyEventArgs This is the event that is triggered when the mouse is moved.
        public void HistogramControl_MotionNotifyEvent(object o, MotionNotifyEventArgs args)
        {
            mouseValX = (float)args.Event.X / fx;
            mouseValY = (float)args.Event.Y / fy;
        }

        /// It draws the histogram
        /// 
        /// @param o The object that is being drawn.
        /// @param DrawnArgs This is the Cairo.Context object that you can draw on.
        private void HistogramControl_Drawn(object o, DrawnArgs args)
        {
            Cairo.Context g = args.Cr;
            if (graphMax == 0)
                graphMax = ushort.MaxValue;
            g.SetSourceColor(ImageView.FromColor(System.Drawing.Color.LightGray));
            g.Restore();
            g.Translate(-graphMin, 0);
            g.LineWidth = 1;
            string st = "";
            fx = ((float)this.AllocatedWidth) / ((float)graphMax);
            float maxmedian = 0;
            int maxChan = 0;
            for (int cc = 0; cc < ImageView.SelectedImage.Channels.Count; cc++)
            {
                for (int i = 0; i < ImageView.SelectedImage.Channels[cc].stats.Length; i++)
                {
                    float f = ImageView.SelectedImage.Channels[cc].stats[i].StackMedian;
                    if (maxmedian < f)
                    {
                        maxmedian = f;
                        maxChan = cc;
                    }
                }
            }
            fy = ((float)this.AllocatedHeight) / maxmedian;
            for (int c = 0; c < ImageView.SelectedImage.Channels.Count; c++)
            {
                //We draw the mouse line
                g.SetSourceColor(ImageView.FromColor(System.Drawing.Color.Black));
                g.MoveTo(mouseX, 0);
                g.LineTo(mouseX, this.AllocatedHeight);
                g.Stroke();
                int gmax = graphMax;
                Channel channel = ImageView.SelectedImage.Channels[c];
                for (int i = 0; i < channel.range.Length; i++)
                {
                    Statistics stat;
                    if (ImageView.SelectedImage.RGBChannelCount == 1)
                        stat = channel.stats[0];
                    else
                        stat = channel.stats[i];
                    g.LineWidth = bin * fx;
                    Cairo.Color black = ImageView.FromColor(System.Drawing.Color.FromArgb(35, 0, 0, 0));
                    Cairo.Color blackd = ImageView.FromColor(System.Drawing.Color.FromArgb(150, 0, 0, 0));
                    Cairo.Color pen = new Cairo.Color();
                    Cairo.Color pend = new Cairo.Color();
                    int dark = 200;
                    int light = 50;
                    
                    
                    if (ImageView.SelectedImage.bitsPerPixel <= 8)
                        gmax = 255;

                    if (channel.Emission != 0)
                    {
                        Cairo.Color cc = ImageView.FromColor(SpectralColor(channel.Emission));
                        cc.A = 0.2;
                        pen = cc;
                        cc.A = 0.6;
                        pend = cc;
                    }
                    else
                    {
                        if (i == 0)
                        {
                            pen = ImageView.FromColor(System.Drawing.Color.FromArgb(light, 255, 0, 0));
                            pend = ImageView.FromColor(System.Drawing.Color.FromArgb(dark, 255, 0, 0));
                        }
                        else if (i == 1)
                        {
                            pen = ImageView.FromColor(System.Drawing.Color.FromArgb(light, 0, 255, 0));
                            pend = ImageView.FromColor(System.Drawing.Color.FromArgb(dark, 0, 255, 0));
                        }
                        else
                        {
                            pen = ImageView.FromColor(System.Drawing.Color.FromArgb(light, 0, 0, 255));
                            pend = ImageView.FromColor(System.Drawing.Color.FromArgb(dark, 0, 0, 255));
                        }
                    }

                    float sumbins = 0;
                    float sumbin = 0;
                    int binind = 0;
                    int bininds = 0;
                    PointF? prevs = null;
                    PointF? prev = null;
                    float f = (float)gmax / (float)this.AllocatedWidth;
                    g.SetSourceColor(pen);
                    for (float x = 0; x < gmax; x+=f)
                    {
                        if (StackHistogram && c == ImageView.SelectedImage.Channels.Count - 1)
                        {
                            //Lets draw the stack histogram.
                            float val = (float)ImageView.SelectedImage.Statistics.StackValues[(int)x];
                            sumbin += val;
                            if (binind == bin)
                            {
                                float v = sumbin / binind;
                                float yy = this.AllocatedHeight - (fy * v);
                                if (prevs != null)
                                {
                                    g.SetSourceColor(blackd);
                                    g.MoveTo(prevs.Value.X, prevs.Value.Y);
                                    g.LineTo(fx * x, yy);
                                    g.Stroke();
                                }
                                g.SetSourceColor(black);
                                g.MoveTo(fx * x, this.AllocatedHeight);
                                g.LineTo(fx * x, yy);
                                g.Stroke();
                                prevs = new PointF(fx * x, yy);
                                binind = 0;
                                sumbin = 0;
                            }
                        }
                        //Lets draw the channel histogram on top of the stack histogram.
                        float rv = stat.StackValues[(int)x];
                        sumbins += rv;
                        if (bininds == bin)
                        {
                            g.SetSourceColor(pend);
                            g.MoveTo(fx * x, this.AllocatedHeight);
                            g.LineTo(fx * x, this.AllocatedHeight - (fy * (sumbins / bininds)));
                            g.Stroke();
                            if (prev != null)
                            {
                                g.MoveTo(prev.Value.X,prev.Value.Y);
                                g.LineTo(fx * x, this.AllocatedHeight - (fy * (sumbins / bininds)));
                                g.Stroke();
                            }
                            prev = new PointF(fx * x, this.AllocatedHeight - (fy * (sumbins / bininds)));
                            bininds = 0;
                            sumbins = 0;
                        }
                        binind++;
                        bininds++;

                    }
                    g.SetSourceColor(ImageView.FromColor(SpectralColor(channel.Emission)));
                    g.MoveTo((fx * channel.range[i].Max), 0);
                    g.LineTo((fx * channel.range[i].Max), this.AllocatedHeight);
                    g.Stroke();
                    g.MoveTo((fx * channel.range[i].Min), 0);
                    g.LineTo((fx * channel.range[i].Min), this.AllocatedHeight);
                    g.Stroke();
                }

            }
            
            float tick = 6;
            if (axisTicks)
            {
                g.SetSourceRGB(0, 0, 0);
                if (axisNumbers)
                {
                    for (float x = 0; x < graphMax; x += 2000)
                    {
                        g.MoveTo((fx * x), 0);
                        g.LineTo((fx * x), tick + 3);
                        g.Stroke();
                    }
                    for (float x = 0; x < graphMax; x += 1000)
                    {
                        TextExtents ex = g.TextExtents(x.ToString());
                        g.MoveTo((fx * x) - (ex.Width / 2), tick + 10);
                        g.ShowText(x.ToString());
                        g.Stroke();
                        g.MoveTo((fx * x), 0);
                        g.LineTo((fx * x), tick);
                        g.Stroke();
                    }
                }
                if (graphMax <= 16383)
                    for (float x = 0; x < graphMax; x += 100)
                    {
                        g.MoveTo((fx * x), 0);
                        g.LineTo((fx * x), tick);
                        g.Stroke();
                    }
                if (graphMax <= 255)
                {
                    for (float x = 0; x < graphMax; x += 50)
                    {
                        g.MoveTo((fx * x), 0);
                        g.LineTo((fx * x), 4);
                        g.Stroke();
                    }
                    for (float x = 0; x < graphMax; x += 10)
                    {
                        g.MoveTo((fx * x), 0);
                        g.LineTo((fx * x), 2);
                        g.Stroke();
                    }
                }
            }
            
            if (ImageView.SelectedImage.RGBChannelCount == 3)
            {
                if (ImageView.SelectedImage.Channels.Count > 2)
                {
                    st =
                        "(" + (mouseX / fx).ToString() +
                        ",R:" + ImageView.SelectedImage.Channels[2].stats[2].StackValues[(int)(mouseX / fx)].ToString() +
                        ",G:" + ImageView.SelectedImage.Channels[1].stats[1].StackValues[(int)(mouseX / fx)].ToString() +
                        ",B:" + ImageView.SelectedImage.Channels[0].stats[0].StackValues[(int)(mouseX / fx)].ToString() + ")";
                }
                else
                {
                    st =
                        "(" + (mouseX / fx).ToString() +
                        ",R:" + ImageView.SelectedImage.Channels[0].stats[2].StackValues[(int)(mouseX / fx)].ToString() +
                        ",G:" + ImageView.SelectedImage.Channels[0].stats[1].StackValues[(int)(mouseX / fx)].ToString() +
                        ",B:" + ImageView.SelectedImage.Channels[0].stats[0].StackValues[(int)(mouseX / fx)].ToString() + ")";
                }
            }
            else
            {
                int x = (int)(mouseX / fx);
                if (ImageView.SelectedImage.bitsPerPixel < 8 && ImageView.SelectedImage.Channels[0].stats[0].StackValues.Length < 255)
                    st = "(" + (mouseX / fx).ToString() + "," + ImageView.SelectedImage.Channels[0].stats[0].StackValues[x].ToString() + ")";

            }
            g.MoveTo(mouseX, mouseY);
            g.ShowText(st);
            g.Stroke();
        }
        private float bin = 10;
        public float Bin
        {
            get
            {
                return bin;
            }
            set
            {
                bin = value;
            }
        }
        private int min = 0;
        public float Min
        {
            get { return min; }
            set 
            { 
                if(channel!=null)
                    channel.range[App.channelsTool.SelectedSample].Min = (int)value;
                min = (int)value;
            }
        }
        private int max = 0;
        public float Max
        {
            get { return max; }
            set
            {
                if (channel != null)
                    channel.range[App.channelsTool.SelectedSample].Max = (int)value;
                max = (int)value;
            }
        }
        private int graphMax = ushort.MaxValue;
        public int GraphMax
        {
            get { return graphMax; }
            set { graphMax = value; }
        }
        private int graphMin = 0;
        public int GraphMin
        {
            get { return graphMin; }
            set { graphMin = value; }
        }
        private bool stackHistogram = true;
        public bool StackHistogram
        {
            get
            {
                return stackHistogram;
            }
            set
            {
                stackHistogram = value;
            }
        }
        private int mouseX = 0;
        private int mouseY = 0;
        public int MouseX
        {
            get
            {
                return mouseX;
            }
        }
        public int MouseY
        {
            get
            {
                return mouseX;
            }
        }
        private float mouseValX = 0;
        private float mouseValY = 0;
        public float MouseValX
        {
            get
            {
                return mouseValX;
            }
        }
        public float MouseValY
        {
            get
            {
                return mouseValY;
            }
        }

        private bool axisNumbers = true;
        public bool AxisNumbers
        {
            get { return axisNumbers;}
            set { axisNumbers = value; }
        }

        private bool axisTicks = true;
        public bool AxisTicks
        {
            get { return axisTicks; }
            set { axisTicks = value; }
        }

        private float fx = 0;
        private float fy = 0;
        private AForge.Bitmap bm;

        /// This function updates the channel variable with the channel that is passed in
        /// 
        /// @param Channel The channel object that contains the channel information.
        public void UpdateChannel(Channel c)
        {
            channel = c;
        }
        /// > The function UpdateView() is called when the user clicks the button. It calls the function
        /// QueueDraw() which is a function of the Gtk.DrawingArea widget. This function tells the
        /// widget to redraw itself
        public void UpdateView()
        {
            view.QueueDraw();
        }

        /// > The function takes a wavelength in nanometers and returns a color in RGB
        /// 
        /// @param l wavelength in nanometers
        /// 
        /// @return A color.
        System.Drawing.Color SpectralColor(double l) // RGB <0,1> <- lambda l <400,700> [nm]
        {
            double t;
            double r = 0;
            double g = 0;
            double b = 0;
            if ((l >= 400.0) && (l < 410.0)) { t = (l - 400.0) / (410.0 - 400.0); r = +(0.33 * t) - (0.20 * t * t); }
            else if ((l >= 410.0) && (l < 475.0)) { t = (l - 410.0) / (475.0 - 410.0); r = 0.14 - (0.13 * t * t); }
            else if ((l >= 545.0) && (l < 595.0)) { t = (l - 545.0) / (595.0 - 545.0); r = +(1.98 * t) - (t * t); }
            else if ((l >= 595.0) && (l < 650.0)) { t = (l - 595.0) / (650.0 - 595.0); r = 0.98 + (0.06 * t) - (0.40 * t * t); }
            else if ((l >= 650.0) && (l < 700.0)) { t = (l - 650.0) / (700.0 - 650.0); r = 0.65 - (0.84 * t) + (0.20 * t * t); }
            if ((l >= 415.0) && (l < 475.0)) { t = (l - 415.0) / (475.0 - 415.0); g = +(0.80 * t * t); }
            else if ((l >= 475.0) && (l < 590.0)) { t = (l - 475.0) / (590.0 - 475.0); g = 0.8 + (0.76 * t) - (0.80 * t * t); }
            else if ((l >= 585.0) && (l < 639.0)) { t = (l - 585.0) / (639.0 - 585.0); g = 0.84 - (0.84 * t); }
            if ((l >= 400.0) && (l < 475.0)) { t = (l - 400.0) / (475.0 - 400.0); b = +(2.20 * t) - (1.50 * t * t); }
            else if ((l >= 475.0) && (l < 560.0)) { t = (l - 475.0) / (560.0 - 475.0); b = 0.7 - (t) + (0.30 * t * t); }
            r *= 255;
            g *= 255;
            b *= 255;
            return System.Drawing.Color.FromArgb(255, (int)r, (int)g, (int)b);
        }
    }
}
