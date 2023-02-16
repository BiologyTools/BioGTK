using Gtk;
using System;

namespace BioGTK
{
    public class StackTools : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;
        int line = 0;
#pragma warning disable 649
        [Builder.Object]
        private Button splitChannelsBut;
        [Builder.Object]
        private Button substackBut;
        [Builder.Object]
        private Button mergeZBut;
        [Builder.Object]
        private Button mergeTBut;
        [Builder.Object]
        private Button mergeBut;
        [Builder.Object]
        private ComboBox stackABox;
        [Builder.Object]
        private ComboBox stackBBox;
        [Builder.Object]
        private SpinButton zStartBox;
        [Builder.Object]
        private SpinButton cStartBox;
        [Builder.Object]
        private SpinButton tStartBox;
        [Builder.Object]
        private SpinButton zEndBox;
        [Builder.Object]
        private SpinButton cEndBox;
        [Builder.Object]
        private SpinButton tEndBox;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        /// <summary> Default Shared Constructor. </summary>
        /// <returns> A TestForm1. </returns>
        public static StackTools Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Stacks.glade", null);
            return new StackTools(builder, builder.GetObject("stackTools").Handle);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected StackTools(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            stackABox.Changed += StackABox_Changed;
            stackBBox.Changed += StackBBox_Changed;
            splitChannelsBut.Clicked += SplitChannelsBut_Clicked;
            substackBut.Clicked += SubstackBut_Clicked;
            mergeZBut.Clicked += MergeZBut_Clicked;
            mergeTBut.Clicked += MergeTBut_Clicked;
            mergeBut.Clicked += MergeBut_Clicked;
            this.DeleteEvent += StackTools_DeleteEvent;
        }

        private void StackTools_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }
        #endregion
        private void StackBBox_Changed(object sender, EventArgs e)
        {
            if (stackABox.Active == -1)
                return;
            if (stackBBox.Active == -1)
                return;
            if (stackABox.Active == stackBBox.Active)
            {
                //Same image selected for A & B
                MessageDialog messageDialog = new MessageDialog(
                  null,
                  DialogFlags.DestroyWithParent,
                  MessageType.Info,
                  ButtonsType.Ok,
                  "Same image selected for A & B. Change either A stack or B stack.");
                messageDialog.Run();
                messageDialog.Destroy();
                stackBBox.Active = -1;
            }
        }

        private void StackABox_Changed(object sender, EventArgs e)
        {
            if (stackABox.Active == -1)
                return;
            if (stackABox.Active == stackBBox.Active)
            {
                //Same image selected for A & B
                MessageDialog messageDialog = new MessageDialog(
                  null,
                  DialogFlags.DestroyWithParent,
                  MessageType.Info,
                  ButtonsType.Ok,
                  "Same image selected for A & B. Change either A stack or B stack.");
                messageDialog.Run();
                messageDialog.Destroy();
                stackBBox.Active = -1;
            }
            zEndBox.Adjustment.Upper = ImageA.SizeZ;
            cEndBox.Adjustment.Upper = ImageA.SizeC;
            tEndBox.Adjustment.Upper = ImageA.SizeT;
            zEndBox.Value = ImageA.SizeZ;
            cEndBox.Value = ImageA.SizeC;
            tEndBox.Value = ImageA.SizeT;
        }

        private void MergeBut_Clicked(object sender, EventArgs e)
        {
             if (stackABox.Active == -1)
                return;
            if (stackBBox.Active == -1)
                return;
            BioImage b = BioImage.MergeChannels(ImageA, ImageB);
            App.tabsView.AddTab(b);
            UpdateStacks();
        }

        private void MergeTBut_Clicked(object sender, EventArgs e)
        {
            if (ImageA != null)
                App.tabsView.AddTab(BioImage.MergeT(ImageA));
        }

        private void MergeZBut_Clicked(object sender, EventArgs e)
        {
            if (ImageA != null)
                App.tabsView.AddTab(BioImage.MergeZ(ImageA));
            
        }

        private void SubstackBut_Clicked(object sender, EventArgs e)
        {
             if (stackABox.Active == -1)
                return;
            BioImage b = BioImage.Substack(ImageA, ImageA.series, (int)zStartBox.Value, (int)zEndBox.Value, (int)cStartBox.Value, (int)cEndBox.Value, (int)tStartBox.Value, (int)tEndBox.Value);
            App.tabsView.AddTab(b);
            UpdateStacks();
        }

        private void SplitChannelsBut_Clicked(object sender, EventArgs e)
        {
            if (stackABox.Active == -1)
                return;
            BioImage[] bms = ImageA.SplitChannels();
            for (int i = 0; i < bms.Length; i++)
            {
                App.tabsView.AddTab(bms[i]);
            }
            UpdateStacks();
        }

        public void UpdateStacks()
        {
            var imgs = new ListStore(typeof(string));
            // Add items to the ListStore
            foreach (BioImage b in Images.images)
            {
                imgs.AppendValues(b.Filename);
            }
            // Set the model for the ComboBox
            stackABox.Model = imgs;
            stackBBox.Model = imgs;
            // Set the text column to display
            var renderer = new CellRendererText();
            stackABox.PackStart(renderer, false);
            stackABox.AddAttribute(renderer, "text", 0);
            // Set the text column to display
            var renderer2 = new CellRendererText();
            stackABox.PackStart(renderer2, false);
            stackABox.AddAttribute(renderer2, "text", 0);
        }
        public BioImage ImageA
        {
            get { return Images.images[stackABox.Active]; }
        }
        public BioImage ImageB
        {
            get { return Images.images[stackBBox.Active]; }
        }

        private void StackTools_Activated(object sender, EventArgs e)
        {
            UpdateStacks();
        }

        private void setMaxToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ImageA != null)
                zEndBox.Value = ImageA.SizeZ;
        }

        private void setMaxCToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ImageA != null)
                cEndBox.Value = ImageA.SizeC;
        }

        private void setMaxTToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (ImageA != null)
                tEndBox.Value = ImageA.SizeT;
        }
    }
}
