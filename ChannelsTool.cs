using AForge;
using Gtk;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;

namespace BioGTK
{
    public class ChannelsTool : Gtk.Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Box box;
        [Builder.Object]
        private Button applyBut;
        [Builder.Object]
        private Button resetBut;
        [Builder.Object]
        private ComboBox channelsBox;
        [Builder.Object]
        private SpinButton sampleBox;
        [Builder.Object]
        private ComboBox maxUintBox;
        [Builder.Object]
        private SpinButton minBox;
        [Builder.Object]
        private SpinButton maxBox;
        [Builder.Object]
        private Button setMinAllBut;
        [Builder.Object]
        private Button setMaxAllBut;
        [Builder.Object]
        private Entry fluorBox;
        [Builder.Object]
        private SpinButton emissionBox;
        [Builder.Object]
        private SpinButton excitationBox;
        [Builder.Object]
        private SpinButton binBox;
        [Builder.Object]
        private SpinButton graphMinBox;
        [Builder.Object]
        private SpinButton graphMax;
        [Builder.Object]
        private ComboBox maxUintBox2;

#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        public static ChannelsTool Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.ChannelsTool.glade", null);
            return new ChannelsTool(builder, builder.GetObject("chanTool").Handle);
        }
        protected ChannelsTool(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            App.channelsTool = this;
            var store = new ListStore(typeof(string));
            foreach (Channel item in Channels)
            {
                // Add items to the ListStore
                store.AppendValues(item.ToString());
                channelsBox.Active = 0;
            }
            channelsBox.Model = store;
            // Set the text column to display
            var renderer = new CellRendererText();
            channelsBox.PackStart(renderer, false);
            channelsBox.AddAttribute(renderer, "text", 0);

            UpdateGUI();

            minBox.Value = (int)Channels[0].stats[0].StackMin;
            maxBox.Value = (int)Channels[0].stats[0].StackMax;
            hist = HistogramControl.Create(Channels[0]);
            hist.GraphMax = (int)maxBox.Value;
            graphMax.Value = (int)maxBox.Value;
            box.Add(hist);

            minBox.ButtonPressEvent += minBox_ValueChanged;
            //meanStackBox.Clicked += MeanStackBox_Clicked;
            setMinAllBut.ButtonPressEvent += setMinAllBut_Click;
            setMaxAllBut.ButtonPressEvent += setMaxAllBut_Click;
            minBox.ValueChanged+= minBox_ValueChanged;
            maxBox.ValueChanged+= maxBox_ValueChanged;
            fluorBox.Changed += fluorBox_TextChanged;
            emissionBox.ValueChanged += emissionBox_ValueChanged;
            excitationBox.ValueChanged += excitationBox_ValueChanged;
            binBox.ValueChanged += binBox_ValueChanged;
            graphMinBox.ValueChanged += minGraphBox_ValueChanged;
            graphMax.ValueChanged += maxGraphBox_ValueChanged;
            applyBut.ButtonPressEvent += applyBut_Click;
            resetBut.ButtonPressEvent += ResetButton_ButtonPressEvent;
            this.ButtonPressEvent += ChannelsTool_MouseDown;
            this.FocusActivated += ChannelsTool_Activated;


            ShowAll();
        }

        private void UpdateGUI()
        {
            if (SelectedChannel.BitsPerPixel > 8)
            {
                minBox.Adjustment.Upper = ushort.MaxValue;
                maxBox.Adjustment.Upper = ushort.MaxValue;
                graphMax.Adjustment.Upper = ushort.MaxValue;
                graphMinBox.Adjustment.Upper = ushort.MaxValue;
            }
            else
            {
                minBox.Adjustment.Upper = byte.MaxValue;
                maxBox.Adjustment.Upper = byte.MaxValue;
                graphMax.Adjustment.Upper = byte.MaxValue;
                graphMinBox.Adjustment.Upper = byte.MaxValue;
            }

            emissionBox.Adjustment.Upper = 1000;
            excitationBox.Adjustment.Upper = 1000;
            binBox.Adjustment.Upper = 1000;
            sampleBox.Adjustment.Upper = SelectedChannel.SamplesPerPixel;

        }

        private void ResetButton_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (ImageView.SelectedImage.bitsPerPixel > 8)
                ImageView.SelectedImage.StackThreshold(true);
            else
                ImageView.SelectedImage.StackThreshold(false);
            App.viewer.UpdateView();
        }
        /*
        private void View_ScrollEvent(object o, ScrollEventArgs args)
        {
            int i = 100;
            if (maxGraphBox.Value < 255)
                i = 10;
            if (e.Delta > 0)
            {
                if (graphMax.Value + i < graphMax.Adjustment.Upper)
                {
                    graphMax.Value += i;
                    graphMax.QueueDraw();
                    return;
                }
            }
            else
            if (graphMax.Value - i > graphMax.Adjustment.Upper)
            {
                graphMax.Value -= i;
                graphMax.QueueDraw();
                return;
            }
        }
        */
        #endregion
        private HistogramControl hist = null;

        public List<Channel> Channels
        {
            get
            {
                return ImageView.SelectedImage.Channels;
            }
        }
        public Channel SelectedChannel
        {
            get
            {
                if (channelsBox.Active != -1)
                    return Channels[channelsBox.Active];
                else
                    return Channels[0];
            }
        }
        public int SelectedSample
        {
            get
            {
                return (int)sampleBox.Value;
            }
            set
            {
                sampleBox.Value = value;
            }
        }
        public void UpdateItems()
        {
            channelsBox.Model = null;
            var store = new ListStore(typeof(string));
            // Add items to the ListStore
            foreach (Channel item in Channels)
            {
                store.AppendValues(item.Name);
            }
            // Set the model for the ComboBox
            channelsBox.Model = store;
            // Set the text column to display
            var renderer = new CellRendererText();
            channelsBox.PackStart(renderer, false);
            channelsBox.AddAttribute(renderer, "text", 0);

        }

        private void minBox_ValueChanged(object sender, EventArgs e)
        {
            if (channelsBox.Active == -1)
                return;
            if (hist != null)
            {
                hist.UpdateChannel(Channels[channelsBox.Active]);
                hist.UpdateView();
                hist.Min = (int)minBox.Value;
            }
            SelectedChannel.range[(int)sampleBox.Value].Min = (int)minBox.Value;
            App.viewer.UpdateImage();
            App.viewer.UpdateView();
        }
        private void maxBox_ValueChanged(object sender, EventArgs e)
        {
            if (channelsBox.Active == -1)
                return;
            if (hist != null)
            {
                hist.UpdateChannel(Channels[channelsBox.Active]);
                hist.QueueDraw();
                hist.Max = (int)maxBox.Value;
            }
            SelectedChannel.range[(int)sampleBox.Value].Min = (int)minBox.Value;
            App.viewer.UpdateImage();
            App.viewer.UpdateView();
        }
        private void channelsBox_ActiveChanged(object sender, EventArgs e)
        {
            if (channelsBox.Active == -1)
                return;
            sampleBox.Value = 0;
            sampleBox.Adjustment.Upper = SelectedChannel.range.Length - 1;
            if (minBox.Adjustment.Upper < SelectedChannel.range[(int)sampleBox.Value].Min || maxBox.Adjustment.Upper < SelectedChannel.range[(int)sampleBox.Value].Max)
            {
                minBox.Value = 0;
                maxBox.Value = ushort.MaxValue;
            }
            else
            {
                minBox.Value = Channels[channelsBox.Active].range[(int)sampleBox.Value].Min;
                maxBox.Value = Channels[channelsBox.Active].range[(int)sampleBox.Value].Max;
            }
            if (hist != null)
            {
                //hist.Statistics = Channels[channelsBox.Active].statistics;
                hist.UpdateChannel(SelectedChannel);
                hist.UpdateView();
            }
            fluorBox.Text = SelectedChannel.Fluor;
            emissionBox.Value = SelectedChannel.Emission;
            excitationBox.Value = SelectedChannel.Excitation;
            App.viewer.UpdateView();
        }

        private void maxUintBox_ActiveChanged(object sender, EventArgs e)
        {
            TreeIter it;
            maxUintBox.GetActiveIter(out it);
            var selectedItem = (string)maxUintBox.Model.GetValue(it, 0);

            int i = int.Parse((string)selectedItem, System.Globalization.CultureInfo.InvariantCulture);
            if(i<=maxBox.Adjustment.Upper)
            maxBox.Value = i;

        }

        private void setMaxAllBut_Click(object o, ButtonPressEventArgs args)
        {
            foreach (Channel c in Channels)
            {
                for (int i = 0; i < c.range.Length; i++)
                {
                    c.range[i].Max = (int)maxBox.Value;
                }
            }
            App.viewer.UpdateView();
        }

        private void setMinAllBut_Click(object o, ButtonPressEventArgs args)
        {
            foreach (Channel c in Channels)
            {
                for (int i = 0; i < c.range.Length; i++)
                {
                    c.range[i].Min = (int)minBox.Value;
                }
            }
            App.viewer.UpdateView();
        }

        private void ChannelsTool_Activated(object? sender, EventArgs e)
        {
            sampleBox.Adjustment.Upper = SelectedChannel.SamplesPerPixel - 1;
            for (int i = 0; i < SelectedChannel.range.Length; i++)
            {
                SelectedChannel.range[i].Min = (int)minBox.Value;
            }
            minBox.Value = SelectedChannel.range[(int)sampleBox.Value].Min;
            maxBox.Value = SelectedChannel.range[(int)sampleBox.Value].Max;
            UpdateItems();
            hist.UpdateView();

        }

        private void maxUintBox2_ActiveChanged(object sender, EventArgs e)
        {
            TreeIter it;
            maxUintBox.GetActiveIter(out it);
            var selectedItem = (string)maxUintBox.Model.GetValue(it, 0);
            int i = int.Parse((string)selectedItem, System.Globalization.CultureInfo.InvariantCulture);
            if(i <= graphMax.Adjustment.Upper)
                graphMax.Value = i;
            if (i == 255)
            {
                graphMax.Value = 255;
                binBox.Value = 1;
            }
        }

        private void minGraphBox_ValueChanged(object sender, EventArgs e)
        {
            if (hist != null)
            {
                hist.GraphMin = (int)graphMinBox.Value;
                hist.UpdateView();
            }
        }

        private void maxGraphBox_ValueChanged(object sender, EventArgs e)
        {
            if (hist != null)
            {
                hist.GraphMax = (int)graphMax.Value;
                hist.UpdateView();
            }
        }
        private void binBox_ValueChanged(object sender, EventArgs e)
        {
            if (hist != null)
            {
                hist.Bin = (int)binBox.Value;
                hist.UpdateView();
            }
        }

        private void ChannelsTool_MouseDown(object sender, ButtonPressEventArgs e)
        {
            
        }
        private void setMinToolStripMenuItem_Click(object o, ButtonPressEventArgs args)
        {
            minBox.Value = (int)hist.MouseValX;
        }

        private void setMaxToolStripMenuItem_Click(object o, ButtonPressEventArgs args)
        {
            maxBox.Value = (int)hist.MouseValX;
        }

        private void applyBut_Click(object o, ButtonPressEventArgs args)
        {
            ImageView.SelectedImage.Bake(ImageView.SelectedImage.RChannel.RangeR, ImageView.SelectedImage.GChannel.RangeG, ImageView.SelectedImage.BChannel.RangeB);
        }

        private void minToolStripMenuItem_Click(object o, ButtonPressEventArgs args)
        {
            minBox.Value = SelectedChannel.stats[channelsBox.Active].StackMin;
        }

        private void maxToolStripMenuItem_Click(object o, ButtonPressEventArgs args)
        {
            minBox.Value = SelectedChannel.stats[channelsBox.Active].StackMax;
        }

        private void medianToolStripMenuItem_Click(object o, ButtonPressEventArgs args)
        {
            minBox.Value = SelectedChannel.stats[channelsBox.Active].StackMedian;
        }

        private void meanToolStripMenuItem_Click(object o, ButtonPressEventArgs args)
        {
            minBox.Value = SelectedChannel.stats[channelsBox.Active].StackMean;
        }

        private void toolStripMenuItem1_Click(object o, ButtonPressEventArgs args)
        {
            maxBox.Value = SelectedChannel.stats[channelsBox.Active].StackMin;
        }

        private void toolStripMenuItem2_Click(object o, ButtonPressEventArgs args)
        {
            maxBox.Value = SelectedChannel.stats[channelsBox.Active].StackMax;
        }
        private void toolStripMenuItem3_Click(object o, ButtonPressEventArgs args)
        {
            maxBox.Value = SelectedChannel.stats[channelsBox.Active].StackMedian;
        }
        private void toolStripMenuItem4_Click(object o, ButtonPressEventArgs args)
        {
            maxBox.Value = SelectedChannel.stats[channelsBox.Active].StackMean;
        }

        private void updateBut_Click(object o, ButtonPressEventArgs args)
        {
            if (ImageView.SelectedImage.bitsPerPixel > 8)
            {
                ImageView.SelectedImage.StackThreshold(true);
                if (ImageView.SelectedImage.RGBChannelCount == 1)
                {
                    maxBox.Value = ImageView.SelectedImage.Channels[channelsBox.Active].stats[0].StackMax;
                    minBox.Value = ImageView.SelectedImage.Channels[channelsBox.Active].stats[0].StackMin;
                }
                else
                {
                    maxBox.Value = ImageView.SelectedImage.Channels[channelsBox.Active].stats[channelsBox.Active].StackMax;
                    minBox.Value = ImageView.SelectedImage.Channels[channelsBox.Active].stats[channelsBox.Active].StackMin;
                }
            }
            else
            {
                ImageView.SelectedImage.StackThreshold(false);
            }
            App.viewer.UpdateImage();
        }

        private void sampleBox_ValueChanged(object sender, EventArgs e)
        {
            if (channelsBox.Active == -1)
                channelsBox.Active = 0;
            minBox.Value = Channels[channelsBox.Active].range[(int)sampleBox.Value].Min;
            maxBox.Value = Channels[channelsBox.Active].range[(int)sampleBox.Value].Max;
            if (hist != null)
            {
                //hist.Statistics = Channels[channelsBox.Active].statistics;
                hist.UpdateChannel(SelectedChannel);
                hist.QueueDraw();
            }
            App.viewer.UpdateView();
        }

        private void excitationBox_ValueChanged(object sender, EventArgs e)
        {
            SelectedChannel.Excitation = (int)excitationBox.Value;
        }

        private void emissionBox_ValueChanged(object sender, EventArgs e)
        {
            SelectedChannel.Emission = (int)emissionBox.Value;
        }

        private void fluorBox_TextChanged(object sender, EventArgs e)
        {
            SelectedChannel.Fluor = fluorBox.Text;
        }
    }
}
