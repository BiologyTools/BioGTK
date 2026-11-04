using Gtk;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AForge;

using Cairo;
using Color = AForge.Color;
using Gdk;
using System.IO;

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
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Histogram.glade", FileMode.Open));
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
        private const double HistogramBarOpacity = 0.55;
        private readonly Dictionary<Channel, Cairo.Color> _generatedChannelColors = new();
        /// This function initializes the histogram control
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
                Bin = 1;
            }
            view.Drawn += HistogramControl_Drawn;
            this.MotionNotifyEvent += HistogramControl_MotionNotifyEvent;
            this.ButtonPressEvent += HistogramControl_ButtonPressEvent;
            this.ScrollEvent += HistogramControl_ScrollEvent;
            this.DeleteEvent += HistogramControl_DeleteEvent;
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

        /// The function is called when the user clicks the close button on the Histogram window. The
        /// function sets the return value to true, which tells the program to hide the window instead
        /// of closing it
        /// 
        /// @param o The object that the event is being called on.
        /// @param DeleteEventArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-events-delete.html.en
        private void HistogramControl_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
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
                graphMax += 400;
            }
            else if (args.Event.Direction == Gdk.ScrollDirection.Down)
            {
                graphMax -= 400;
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

        public Cairo.Color ToCairoColor(Color c)
        {
            return new Cairo.Color((double)c.R / 255.0, (double)c.G / 255.0, (double)c.B / 255.0, (double)c.A / 255.0);
        }

        /// It draws the histogram
        /// 
        /// @param o The object that is being drawn.
        /// @param DrawnArgs This is the Cairo.Context object that you can draw on.
        private void HistogramControl_Drawn(object o, DrawnArgs args)
        {
            Cairo.Context g = args.Cr;
            var image = ImageView.SelectedImage;
            int drawingWidth = Math.Max(1, view?.AllocatedWidth ?? AllocatedWidth);
            int drawingHeight = Math.Max(1, view?.AllocatedHeight ?? AllocatedHeight);
            g.SetSourceRGB(0.82, 0.82, 0.82);
            g.Paint();

            if (!HasUsableStatistics(image))
            {
                g.SetSourceRGB(0.2, 0.2, 0.2);
                g.MoveTo(12, Math.Max(22, drawingHeight / 2));
                g.ShowText("Statistics are not available for this image.");
                g.Stroke();
                args.RetVal = true;
                return;
            }

            if (graphMax == 0)
                graphMax = ushort.MaxValue;
            g.SetSourceColor(ToCairoColor(Color.LightGray));
            //g.Restore();
            //g.Translate(-graphMin, 0);
            g.LineWidth = 1;
            string st = "";
            fx = ((float)drawingWidth) / ((float)graphMax);
            float maxmedian = 0;
            int maxChan = 0;
            for (int cc = 0; cc < image.Channels.Count; cc++)
            {
                for (int i = 0; i < image.Channels[cc].stats.Length; i++)
                {
                    Statistics channelStatistics = image.Channels[cc].stats[i];
                    if (channelStatistics == null)
                        continue;
                    float f = GetHistogramMaximum(channelStatistics.StackValues);
                    if (maxmedian < f)
                    {
                        maxmedian = f;
                        maxChan = cc;
                    }
                }
            }
            if (maxmedian <= 0)
            {
                g.SetSourceRGB(0.2, 0.2, 0.2);
                g.MoveTo(12, Math.Max(22, drawingHeight / 2));
                g.ShowText("Histogram contains no sample counts.");
                g.Stroke();
                args.RetVal = true;
                return;
            }
            fy = ((float)drawingHeight) / maxmedian;
            for (int c = 0; c < image.Channels.Count; c++)
            {
                //We draw the mouse line

                g.SetSourceColor(ToCairoColor(Color.Black));
                g.MoveTo(mouseX, 0);
                g.LineTo(mouseX, drawingHeight);
                g.Stroke();
                int gmax = graphMax;
                int gmin = graphMin;
                Channel channel = image.Channels[c];
                for (int i = 0; i < channel.range.Length; i++)
                {
                    Statistics stat;
                    int statisticsIndex = image.Channels.Count == 1 && image.RGBChannelCount > 1
                        ? Math.Clamp(i, 0, channel.stats.Length - 1)
                        : 0;
                    stat = channel.stats[statisticsIndex];
                    g.LineWidth = 1;// bin * fx;
                    Cairo.Color blackd = new Cairo.Color(0, 0, 0, 0.35);
                    if (ImageView.SelectedImage.bitsPerPixel <= 8)
                        gmax = 255;

                    Cairo.Color barColor = GetChannelBarColor(image, channel, c, i);

                    int plotWidth = drawingWidth;
                    int plotHeight = drawingHeight;
                    int valueRange = Math.Max(1, gmax - gmin);

                    if (StackHistogram && c == image.Channels.Count - 1 && i == 0 &&
                        image.Statistics?.StackValues != null)
                    {
                        g.SetSourceColor(blackd);
                        DrawBinnedHistogram(g, image.Statistics.StackValues,
                            gmin, valueRange, plotWidth, plotHeight, maxmedian);
                    }

                    // Aggregate every source bin covered by a screen pixel. The
                    // former loop sampled one isolated value per pixel and skipped
                    // nearly all peaks in a 16-bit (65,536-bin) histogram.
                    g.SetSourceColor(barColor);
                    DrawBinnedHistogram(g, stat.StackValues,
                        gmin, valueRange, plotWidth, plotHeight, maxmedian);
                    Cairo.Color markerColor = barColor;
                    markerColor.A = 1.0;
                    g.SetSourceColor(markerColor);
                    g.MoveTo((fx * channel.range[i].Max), 0);
                    g.LineTo((fx * channel.range[i].Max), drawingHeight);
                    g.Stroke();
                    g.MoveTo((fx * channel.range[i].Min), 0);
                    g.LineTo((fx * channel.range[i].Min), drawingHeight);
                    g.Stroke();
                }

            }
            
            float tick = 6;
            if (axisTicks)
            {
                g.SetSourceRGB(0, 0, 0);
                int axisRange = Math.Max(1, graphMax - graphMin);
                int majorStep = GetNiceTickStep(axisRange, 6);
                int firstTick = (int)Math.Ceiling((double)graphMin / majorStep) * majorStep;
                for (int value = firstTick; value <= graphMax; value += majorStep)
                {
                    double x = (double)(value - graphMin) * drawingWidth / axisRange;
                    g.MoveTo(x, 0);
                    g.LineTo(x, tick);
                    g.Stroke();
                    if (axisNumbers)
                    {
                        string label = value.ToString();
                        TextExtents extents = g.TextExtents(label);
                        g.MoveTo(x - (extents.Width / 2), tick + 11);
                        g.ShowText(label);
                        g.Stroke();
                    }
                }
            }
            
            int hoverBin = graphMin + (int)((long)Math.Max(0, mouseX) *
                Math.Max(1, graphMax - graphMin) / drawingWidth);
            if (image.RGBChannelCount == 3)
            {
                float red;
                float green;
                float blue;
                if (image.Channels.Count > 2)
                {
                    // Separate logical channels each carry one statistics plane.
                    red = GetHistogramValue(image.Channels[2], 0, hoverBin);
                    green = GetHistogramValue(image.Channels[1], 0, hoverBin);
                    blue = GetHistogramValue(image.Channels[0], 0, hoverBin);
                }
                else
                {
                    // Packed/planar RGB is represented by one logical channel with
                    // three statistics planes in B, G, R order.
                    red = GetHistogramValue(image.Channels[0], 2, hoverBin);
                    green = GetHistogramValue(image.Channels[0], 1, hoverBin);
                    blue = GetHistogramValue(image.Channels[0], 0, hoverBin);
                }
                st = $"({hoverBin},R:{red},G:{green},B:{blue})";
            }
            else
            {
                st = $"({hoverBin},{GetHistogramValue(image.Channels[0], 0, hoverBin)})";
            }
            g.MoveTo(mouseX, mouseY);
            g.ShowText(st);
            g.Stroke();
        }

        private static float GetHistogramMaximum(float[] values)
        {
            if (values == null || values.Length == 0)
                return 0;

            float maximum = 0;
            for (int i = 0; i < values.Length; i++)
                if (values[i] > maximum)
                    maximum = values[i];
            return maximum;
        }

        private Cairo.Color GetChannelBarColor(
            BioImage image,
            Channel imageChannel,
            int channelIndex,
            int sampleIndex)
        {
            // A single packed RGB channel contains three component histograms.
            if (image.Channels.Count == 1 && image.RGBChannelCount == 3)
            {
                return sampleIndex switch
                {
                    0 => new Cairo.Color(0.1, 0.35, 1.0, HistogramBarOpacity),
                    1 => new Cairo.Color(0.1, 0.85, 0.2, HistogramBarOpacity),
                    _ => new Cairo.Color(1.0, 0.15, 0.1, HistogramBarOpacity)
                };
            }

            if (imageChannel.Color.HasValue)
            {
                Color specified = imageChannel.Color.Value;
                return new Cairo.Color(
                    specified.R / 255.0,
                    specified.G / 255.0,
                    specified.B / 255.0,
                    HistogramBarOpacity);
            }

            if (_generatedChannelColors.TryGetValue(imageChannel, out Cairo.Color generated))
                return generated;

            generated = CreateRandomVisibleColor();
            _generatedChannelColors[imageChannel] = generated;
            return generated;
        }

        private static Cairo.Color CreateRandomVisibleColor()
        {
            double hue = Random.Shared.NextDouble() * 6.0;
            int sector = (int)Math.Floor(hue);
            double fraction = hue - sector;
            const double value = 0.9;
            const double saturation = 0.75;
            double low = value * (1.0 - saturation);
            double falling = value * (1.0 - saturation * fraction);
            double rising = value * (1.0 - saturation * (1.0 - fraction));

            return sector switch
            {
                0 => new Cairo.Color(value, rising, low, HistogramBarOpacity),
                1 => new Cairo.Color(falling, value, low, HistogramBarOpacity),
                2 => new Cairo.Color(low, value, rising, HistogramBarOpacity),
                3 => new Cairo.Color(low, falling, value, HistogramBarOpacity),
                4 => new Cairo.Color(rising, low, value, HistogramBarOpacity),
                _ => new Cairo.Color(value, low, falling, HistogramBarOpacity)
            };
        }

        private static int GetNiceTickStep(int range, int targetTickCount)
        {
            double roughStep = Math.Max(1, (double)range / Math.Max(1, targetTickCount));
            double magnitude = Math.Pow(10, Math.Floor(Math.Log10(roughStep)));
            double normalized = roughStep / magnitude;
            double nice = normalized <= 1 ? 1 : normalized <= 2 ? 2 : normalized <= 5 ? 5 : 10;
            return Math.Max(1, (int)(nice * magnitude));
        }

        private static void DrawBinnedHistogram(
            Cairo.Context context,
            float[] values,
            int graphMinimum,
            int graphRange,
            int plotWidth,
            int plotHeight,
            float histogramMaximum)
        {
            if (values == null || values.Length == 0 || histogramMaximum <= 0)
                return;

            double logMaximum = Math.Log(1.0 + histogramMaximum);
            for (int pixelX = 0; pixelX < plotWidth; pixelX++)
            {
                int start = graphMinimum + (int)((long)pixelX * graphRange / plotWidth);
                int end = graphMinimum + (int)((long)(pixelX + 1) * graphRange / plotWidth);
                start = Math.Clamp(start, 0, values.Length - 1);
                end = Math.Clamp(Math.Max(start + 1, end), 1, values.Length);

                float value = 0;
                for (int binIndex = start; binIndex < end; binIndex++)
                    if (values[binIndex] > value)
                        value = values[binIndex];

                if (value <= 0)
                    continue;

                double scaled = Math.Min(1.0, Math.Log(1.0 + value) / logMaximum);
                double y = plotHeight - (scaled * plotHeight);
                context.MoveTo(pixelX + 0.5, plotHeight);
                context.LineTo(pixelX + 0.5, y);
            }
            context.Stroke();
        }

        private static float GetHistogramValue(Channel histogramChannel, int statisticsIndex, int bin)
        {
            if (histogramChannel?.stats == null || histogramChannel.stats.Length == 0)
                return 0;

            int safeStatisticsIndex = Math.Clamp(statisticsIndex, 0, histogramChannel.stats.Length - 1);
            float[] values = histogramChannel.stats[safeStatisticsIndex]?.StackValues;
            if (values == null || values.Length == 0)
                return 0;

            return values[Math.Clamp(bin, 0, values.Length - 1)];
        }

        private static bool HasUsableStatistics(BioImage image)
        {
            if (image?.Channels == null || image.Channels.Count == 0)
                return false;

            foreach (Channel imageChannel in image.Channels)
            {
                if (imageChannel?.stats == null || imageChannel.stats.Length == 0)
                    return false;

                int requiredStats = image.Channels.Count == 1 && image.RGBChannelCount > 1
                    ? image.RGBChannelCount
                    : 1;
                if (imageChannel.stats.Length < requiredStats)
                    return false;

                for (int i = 0; i < requiredStats; i++)
                {
                    if (imageChannel.stats[i]?.StackValues == null ||
                        imageChannel.stats[i].StackValues.Length == 0)
                        return false;
                }
            }

            return true;
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
        Color SpectralColor(double l) // RGB <0,1> <- lambda l <400,700> [nm]
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
            return Color.FromArgb(255, (int)r, (int)g, (int)b);
        }
    }
}
