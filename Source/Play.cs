using AForge;
using Gtk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BioGTK
{
    public class Play : Window
    {
        #region Properties
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private SpinButton startZ;
        [Builder.Object]
        private SpinButton startC;
        [Builder.Object]
        private SpinButton startT;
        [Builder.Object]
        private SpinButton endZ;
        [Builder.Object]
        private SpinButton endC;
        [Builder.Object]
        private SpinButton endT;
        [Builder.Object]
        private SpinButton fpsZ;
        [Builder.Object]
        private SpinButton fpsC;
        [Builder.Object]
        private SpinButton fpsT;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// The function creates a new instance of the Play class using a Builder object and a Glade
        /// file.
        /// 
        /// @return The method is returning an instance of the Play class.
        public static Play Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Play.glade", null);
            return new Play(builder, builder.GetObject("play").Handle);
        }
        /* The constructor for the Play class. */
        protected Play(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            SetupHandlers();
            
            startZ.Adjustment.Upper = ImageView.SelectedImage.SizeZ - 1;
            startC.Adjustment.Upper = ImageView.SelectedImage.SizeC - 1;
            startT.Adjustment.Upper = ImageView.SelectedImage.SizeT - 1;
            endZ.Adjustment.Upper = ImageView.SelectedImage.SizeZ - 1;
            endC.Adjustment.Upper = ImageView.SelectedImage.SizeC - 1;
            endT.Adjustment.Upper = ImageView.SelectedImage.SizeT - 1;
            fpsZ.Adjustment.Upper = 60;
            fpsC.Adjustment.Upper = 60;
            fpsT.Adjustment.Upper = 60;
            startZ.Adjustment.StepIncrement = 1;
            startC.Adjustment.StepIncrement = 1;
            startT.Adjustment.StepIncrement = 1;
            endZ.Adjustment.StepIncrement = 1;
            endC.Adjustment.StepIncrement = 1;
            endT.Adjustment.StepIncrement = 1;
            fpsZ.Adjustment.StepIncrement = 1;
            fpsC.Adjustment.StepIncrement = 1;
            fpsT.Adjustment.StepIncrement = 1;
            fpsZ.Value = 1;
            fpsC.Value = 1;
            fpsT.Value = 1;
            startZ.Value = ImageView.startz;
            startC.Value = ImageView.startc;
            startT.Value = ImageView.startt;
            endZ.Value = ImageView.endz;
            endC.Value = ImageView.endc;
            endT.Value = ImageView.endt;
        }

        #endregion

        #region Handlers

        /// <summary> Sets up the handlers. </summary>
        protected void SetupHandlers()
        {
            startZ.Changed += StartZ_Changed;
            startC.Changed += StartC_Changed;
            startT.Changed += StartT_Changed;
            endZ.Changed += EndZ_Changed;
            endC.Changed += EndC_Changed;
            endT.Changed += EndT_Changed;
            fpsZ.Changed += FpsZ_Changed;
            fpsC.Changed += FpsC_Changed;
            fpsT.Changed += FpsT_Changed;
            this.FocusActivated += Play_FocusActivated;
        }

        private void Play_FocusActivated(object sender, EventArgs e)
        {

        }

        private void FpsT_Changed(object sender, EventArgs e)
        {
            ImageView.waitt = (int)(1000 / fpsT.Value);   
        }

        private void FpsC_Changed(object sender, EventArgs e)
        {
            ImageView.waitc = (int)(1000 / fpsC.Value);
        }

        private void FpsZ_Changed(object sender, EventArgs e)
        {
            ImageView.waitz = (int)(1000 / fpsZ.Value);
        }

        private void EndT_Changed(object sender, EventArgs e)
        {
            ImageView.endt = (int)endT.Value;
        }

        private void EndC_Changed(object sender, EventArgs e)
        {
            ImageView.endc = (int)endC.Value;
        }

        private void EndZ_Changed(object sender, EventArgs e)
        {
            ImageView.endz = (int)endZ.Value;
        }

        private void StartT_Changed(object sender, EventArgs e)
        {
            ImageView.startt = (int)startT.Value;
        }

        private void StartC_Changed(object sender, EventArgs e)
        {
            ImageView.startc = (int)startC.Value;
        }

        private void StartZ_Changed(object sender, EventArgs e)
        {
            ImageView.startz = (int)startZ.Value;
        }

        #endregion

    }
}
