using Gtk;
using Gdk;
using System;
using Bio;
using System.IO;

namespace BioGTK
{
    public class PlateTool : Gtk.Window
    {
        #region Properties
        BioImage.WellPlate.Well.Sample selected = null;
        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.DrawingArea pictureBox;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        public static PlateTool Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Plate.glade", FileMode.Open));
            PlateTool v = new PlateTool(builder, builder.GetObject("plate").Handle);
            v.Show();
            return v;
        }

        protected PlateTool(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            pictureBox.Drawn += PictureBox_Drawn;
            pictureBox.ButtonPressEvent += PictureBox_ButtonPressEvent;
            pictureBox.AddEvents((int)
                (EventMask.ButtonPressMask
                | EventMask.ButtonReleaseMask
                | EventMask.KeyPressMask
                | EventMask.PointerMotionMask | EventMask.ScrollMask));
            App.ApplyStyles(this);
        }

        private void PictureBox_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if(args.Event.Button == 1)
            {
                int l = 15;
                int i = 0;
                foreach (BioImage.WellPlate.Well w in ImageView.SelectedImage.Plate.Wells)
                {
                    int r = 0;
                    foreach (BioImage.WellPlate.Well.Sample s in w.Samples)
                    {
                        Rectangle re = new Rectangle(r, (w.Column - 1) * l, l, l);
                        if(re.IntersectsWith(new Rectangle((int)args.Event.X, (int)args.Event.Y,1,1)))
                        {
                            ImageView.SelectedImage.Level = s.Index;
                            selected = s;
                            App.viewer.UpdateImage();
                            App.viewer.UpdateView();
                            pictureBox.QueueDraw();
                        }
                        r += l;
                    }
                    i++;
                }
            }
        }

        private void PictureBox_Drawn(object o, DrawnArgs args)
        {
            int l = 15;
            int i = 0;
            foreach (BioImage.WellPlate.Well w in ImageView.SelectedImage.Plate.Wells)
            {
                int r = 0;
                foreach (BioImage.WellPlate.Well.Sample s in w.Samples)
                {
                    if (s.Index != ImageView.SelectedImage.Level)
                    {
                        args.Cr.SetSourceColor(new Cairo.Color(0, 0, 0));
                        args.Cr.Rectangle(r, (w.Column - 1) * l, l, l);
                        args.Cr.Stroke();
                    }
                    r+=l;
                }
                i++;
            }
            foreach (BioImage.WellPlate.Well w in ImageView.SelectedImage.Plate.Wells)
            {
                int r = 0;
                foreach (BioImage.WellPlate.Well.Sample s in w.Samples)
                {
                    if (s.Index == ImageView.SelectedImage.Level)
                    {
                        args.Cr.SetSourceColor(new Cairo.Color(255, 0, 0));
                        args.Cr.Rectangle(r, (w.Column - 1) * l, l, l);
                        args.Cr.Stroke();
                    }
                    r += l;
                }
                i++;
            }
        }
        #endregion

    }
}
