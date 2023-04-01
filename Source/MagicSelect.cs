using AForge.Imaging.Filters;
using BioGTK;
using Gtk;
using System;

namespace Bio
{
    public partial class MagicSelect : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649

        [Builder.Object]
        private SpinButton numBox;
        [Builder.Object]
        private SpinButton minBox;
        [Builder.Object]
        private SpinButton maxBox;
        [Builder.Object]
        private ComboBox thBox;
        [Builder.Object]
        private CheckButton numericBox;

#pragma warning restore 649

        #endregion

        #region Constructors / Destructors

        /// It creates a new instance of the ColorTool class, which is a GTK# widget that allows the
        /// user to select a color
        /// 
        /// @param ColorS The color to be displayed in the color tool.
        /// 
        /// @return A new instance of the ColorTool class.
        public static MagicSelect Create(int index)
        {
            Builder builder = new Builder(null, "BioGTK.Glade.Magic.glade", null);
            return new MagicSelect(builder, builder.GetObject("magicSelect").Handle, index);
        }


        /* The constructor for the class. */
        protected MagicSelect(Builder builder, IntPtr handle, int index) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            var ths = new ListStore(typeof(string));
            // Add items to the ListStore
            ths.AppendValues("Min");
            ths.AppendValues("Median");
            ths.AppendValues("Median - Min");
            // Set the model for the ComboBox
            thBox.Model = ths;
            // Set the text column to display
            var renderer = new CellRendererText();
            thBox.PackStart(renderer, false);
            thBox.AddAttribute(renderer, "text", 0);
            thBox.Active = index;
        }

        #endregion

        private bool numeric = false;

        /* A property that returns the value of the checkbox. */
        public bool Numeric
        {
            get
            {
                return numericBox.Active;
            }
        }

        public int Threshold
        {
            get
            {
                return (int)numBox.Value;
            }
        }

        public int Min
        {
            get { return (int)minBox.Value; }
        }
        public int Max
        {
            get { return (int)maxBox.Value; }
        }
        public int Index
        {
            get
            {
                return thBox.Active;
            }
        }
    }
}
