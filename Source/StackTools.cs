using Gtk;
using System;
using System.IO;

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
        /// <returns> StackTools </returns>
        public static StackTools Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/Stacks.glade", FileMode.Open));
            return new StackTools(builder, builder.GetObject("stackTools").Handle);
        }

        /// <summary> Specialised constructor for use only by derived class. </summary>
        /// <param name="builder"> The builder. </param>
        /// <param name="handle">  The handle. </param>
        protected StackTools(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            // Create an Adjustment object to define the range and step increment
            zStartBox.Adjustment = new Adjustment(0, 0, 10000, 1, 10, 0);
            cStartBox.Adjustment = new Adjustment(0, 0, 10000, 1, 10, 0);
            tStartBox.Adjustment = new Adjustment(0, 0, 10000, 1, 10, 0);
            zEndBox.Adjustment = new Adjustment(0, 0, 10000, 1, 10, 0);
            cEndBox.Adjustment = new Adjustment(0, 0, 10000, 1, 10, 0);
            tEndBox.Adjustment = new Adjustment(0, 0, 10000, 1, 10, 0);

            stackABox.Changed += StackABox_Changed;
            stackBBox.Changed += StackBBox_Changed;
            splitChannelsBut.Clicked += SplitChannelsBut_Clicked;
            substackBut.Clicked += SubstackBut_Clicked;
            mergeZBut.Clicked += MergeZBut_Clicked;
            mergeTBut.Clicked += MergeTBut_Clicked;
            mergeBut.Clicked += MergeBut_Clicked;
            this.FocusInEvent += StackTools_FocusInEvent;
            // Set the text column to display
            var renderer = new CellRendererText();
            stackABox.PackStart(renderer, false);
            stackABox.AddAttribute(renderer, "text", 0);
            // Set the text column to display
            var renderer2 = new CellRendererText();
            stackBBox.PackStart(renderer2, false);
            stackBBox.AddAttribute(renderer2, "text", 0);
            App.ApplyStyles(this);
        }

        private void StackTools_FocusInEvent(object o, FocusInEventArgs args)
        {
            UpdateStacks();
        }
        #endregion
        /// If the user selects the same image for both A and B, then display a message box and reset
        /// the B selection to nothing
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs EventArgs is a class that contains no event data. It is used by events that
        /// do not pass state information to an event handler when an event is raised.
        /// 
        /// @return The stackABox.Active and stackBBox.Active are being returned.
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
            zEndBox.Adjustment.Upper = ImageB.SizeZ;
            cEndBox.Adjustment.Upper = ImageB.SizeC;
            tEndBox.Adjustment.Upper = ImageB.SizeT;

            zStartBox.Adjustment.Upper = ImageB.SizeZ-1;
            cStartBox.Adjustment.Upper = ImageB.SizeC-1;
            tStartBox.Adjustment.Upper = ImageB.SizeT-1;

            zEndBox.Value = ImageB.SizeZ;
            cEndBox.Value = ImageB.SizeC;
            tEndBox.Value = ImageB.SizeT;
        }

       /// If the user selects the same image for both A and B, then the program will display a message
       /// box telling the user to select a different image for either A or B
       /// 
       /// @param sender The object that raised the event.
       /// @param EventArgs System.EventArgs
       /// 
       /// @return The image stack.
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

            zStartBox.Adjustment.Upper = ImageA.SizeZ-1;
            cStartBox.Adjustment.Upper = ImageA.SizeC-1;
            tStartBox.Adjustment.Upper = ImageA.SizeT-1;

            zEndBox.Value = ImageA.SizeZ;
            cEndBox.Value = ImageA.SizeC;
            tEndBox.Value = ImageA.SizeT;
        }

        /// If the user has selected an image in each stack, then merge the two images and add the result to the
        /// tabsView
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs e
        /// 
        /// @return The return type is void.
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

        // MergeTBut_Clicked() is a function that is called when the MergeTBut button is clicked
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is a class that contains the event data.
        private void MergeTBut_Clicked(object sender, EventArgs e)
        {
            if (ImageA != null)
                App.tabsView.AddTab(BioImage.MergeT(ImageA));
        }

        /// MergeZBut_Clicked() is a function that is called when the MergeZBut button is clicked
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is a class that contains the event data.
        private void MergeZBut_Clicked(object sender, EventArgs e)
        {
            if (ImageA != null)
                App.tabsView.AddTab(BioImage.MergeZ(ImageA));
        }

        /// This function takes the image in the active tab, and creates a new image from a subset of
        /// the original image's Z, C, and T dimensions
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return A new BioImage object.
        private void SubstackBut_Clicked(object sender, EventArgs e)
        {
             if (stackABox.Active == -1)
                return;
            BioImage b = BioImage.Substack(ImageA, ImageA.series, (int)zStartBox.Value, (int)zEndBox.Value, (int)cStartBox.Value, (int)cEndBox.Value, (int)tStartBox.Value, (int)tEndBox.Value);
            UpdateStacks();
            App.tabsView.AddTab(b);
        }

        /// It takes the image in the active tab, splits it into its component channels, and creates a
        /// new tab for each channel
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The SplitChannels() method returns an array of BioImage objects.
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

        /// It takes a list of images and adds them to a dropdown menu
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
            zEndBox.Adjustment.Upper = ImageA.SizeZ;
            cEndBox.Adjustment.Upper = ImageA.SizeC;
            tEndBox.Adjustment.Upper = ImageA.SizeT;
        }
        public BioImage ImageA
        {
            get 
            {
                if (stackABox.Active != -1)
                    return Images.images[stackABox.Active];
                else
                    return Images.images[0];
            }
        }
        public BioImage ImageB
        {
            get 
            {
                if (stackBBox.Active != -1)
                    return Images.images[stackBBox.Active];
                else
                    return Images.images[0];
            }
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
