﻿using Gtk;
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

namespace BioGTK
{
    public class HistogramControl : Widget
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
            setMin.ButtonPressEvent += SetMin_ButtonPressEvent;
            setMax.ButtonPressEvent += SetMax_ButtonPressEvent;
            setMinAll.ButtonPressEvent += SetMinAll_ButtonPressEvent;
            setMaxAll.ButtonPressEvent += SetMaxAll_ButtonPressEvent;
        }

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

        private void SetMax_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.channelsTool.SelectedChannel.range[App.channelsTool.SelectedSample].Max = (int)MouseValX;
            this.QueueDraw();
            App.viewer.UpdateImage();
        }

        private void SetMin_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            App.channelsTool.SelectedChannel.range[App.channelsTool.SelectedSample].Min = (int)MouseValX;
            this.QueueDraw();
            App.viewer.UpdateImage();
        }

        public void HistogramControl_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            mouseX = (int)args.Event.X;
            mouseY = (int)args.Event.Y;
            this.QueueDraw();
            if(args.Event.Button == 3)
            menu.Popup();
        }

        public void HistogramControl_MotionNotifyEvent(object o, MotionNotifyEventArgs args)
        {
            mouseValX = (float)args.Event.X / fx;
            mouseValY = (float)args.Event.Y / fy;
        }

        private void HistogramControl_Drawn(object o, DrawnArgs args)
        {
            Cairo.Context g = args.Cr;
            if (graphMax == 0)
                graphMax = ushort.MaxValue;
            g.SetSourceColor(ImageView.FromColor(System.Drawing.Color.LightGray));
            g.Restore();
            g.Translate(-graphMin, 0);
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
                    if (channel.Emission != 0)
                    {
                        pen = ImageView.FromColor(SpectralColor(channel.Emission));
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
                    //We draw the mouse line
                    g.SetSourceColor(ImageView.FromColor(System.Drawing.Color.Black));
                    g.MoveTo(mouseX, 0);
                    g.LineTo(mouseX, this.AllocatedHeight);
                    g.StrokePreserve();
                    int gmax = graphMax;
                    if (ImageView.SelectedImage.bitsPerPixel <= 8)
                        gmax = 255;


                    float sumbins = 0;
                    float sumbin = 0;
                    int binind = 0;
                    int bininds = 0;
                    PointF? prevs = null;
                    PointF? prev = null;
                    float f = gmax / this.AllocatedWidth;
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
                                    g.StrokePreserve();
                                }
                                g.SetSourceColor(black);
                                g.MoveTo(fx * x, this.AllocatedHeight);
                                g.LineTo(fx * x, yy);
                                g.StrokePreserve();
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
                            g.SetSourceColor(pen);
                            g.MoveTo(fx * x, this.AllocatedHeight);
                            g.LineTo(fx * x, this.AllocatedHeight - (fy * (sumbins / bininds)));
                            g.StrokePreserve();
                            if (prev != null)
                            {
                                g.SetSourceColor(pend);
                                g.MoveTo(prev.Value.X,prev.Value.Y);
                                g.LineTo(fx * x, this.AllocatedHeight - (fy * (sumbins / bininds)));
                                g.StrokePreserve();
                            }
                            prev = new PointF(fx * x, this.AllocatedHeight - (fy * (sumbins / bininds)));
                            bininds = 0;
                            sumbins = 0;
                        }
                        binind++;
                        bininds++;

                    }
                    g.SetSourceColor(pend);
                    g.MoveTo((fx * channel.range[i].Max), 0);
                    g.LineTo((fx * channel.range[i].Max), this.AllocatedHeight);
                    g.StrokePreserve();
                    g.MoveTo((fx * channel.range[i].Min), 0);
                    g.LineTo((fx * channel.range[i].Min), this.AllocatedHeight);
                    g.StrokePreserve();
                }

            }
            /*
            float tick = 6;
            if (axisTicks)
            {
                if (axisNumbers)
                {
                    for (float x = 0; x < graphMax; x += 2000)
                    {
                       
                        SizeF s = g.MeasureString(x.ToString(), SystemFonts.DefaultFont);
                        g.DrawString(x.ToString(), SystemFonts.DefaultFont, Brushes.Black, (fx * x) - (s.Width / 2), tick + 6);
                        g.DrawLine(Pens.Black, new PointF((fx * x), 0), new PointF((fx * x), tick + 3));
                    }
                    for (float x = 0; x < graphMax; x += 1000)
                    {
                        g.DrawLine(Pens.Black, new PointF((fx * x), 0), new PointF((fx * x), tick));
                    }
                }
                if (graphMax <= 16383)
                    for (float x = 0; x < graphMax; x += 100)
                    {
                        g.DrawLine(Pens.Black, new PointF((fx * x), 0), new PointF((fx * x), tick));
                    }
                if (graphMax <= 255)
                {
                    for (float x = 0; x < graphMax; x += 50)
                    {
                        g.DrawLine(Pens.Black, new PointF((fx * x), 0), new PointF((fx * x), 4));
                    }
                    for (float x = 0; x < graphMax; x += 10)
                    {
                        g.DrawLine(Pens.Black, new PointF((fx * x), 0), new PointF((fx * x), 2));
                    }
                }
            }
            */
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
            g.StrokePreserve();
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
        private System.Drawing.Graphics g;

        public void UpdateChannel(Channel c)
        {
            channel = c;
        }
        public void UpdateView()
        {
            view.QueueDraw();
        }

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
