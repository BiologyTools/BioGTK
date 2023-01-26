using AForge;
using BioGTK;
using Gtk;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bio
{
    public partial class ROIManager : Window
    {
        #region Properties

        /// <summary> Used to load in the glade file resource as a window. </summary>
        private Builder _builder;

#pragma warning disable 649
        [Builder.Object]
        private TreeView roiView;
        [Builder.Object]
        private SpinButton xBox;
        [Builder.Object]
        private SpinButton yBox;
        [Builder.Object]
        private SpinButton wBox;
        [Builder.Object]
        private SpinButton hBox;
        [Builder.Object]
        private SpinButton zBox;
        [Builder.Object]
        private SpinButton cBox;
        [Builder.Object]
        private SpinButton tBox;
        [Builder.Object]
        private SpinButton widthBox;
        [Builder.Object]
        private SpinButton rBox;
        [Builder.Object]
        private SpinButton gBox;
        [Builder.Object]
        private SpinButton bBox;
        [Builder.Object]
        private Label typeLabel;
        [Builder.Object]
        private Entry textBox;
        [Builder.Object]
        private Entry idBox;
        [Builder.Object]
        private SpinButton pointBox;
        [Builder.Object]
        private SpinButton pointXBox;
        [Builder.Object]
        private SpinButton pointYBox;
        [Builder.Object]
        private SpinButton selBox;
        [Builder.Object]
        private FontButton fontBut;
        [Builder.Object]
        private CheckButton boundsBox;
        [Builder.Object]
        private CheckButton showTextBox;
        [Builder.Object]
        private CheckButton showRBox;
        [Builder.Object]
        private CheckButton showGBox;
        [Builder.Object]
        private CheckButton showBBox;
        [Builder.Object]
        private Label imageNameLabel;
        [Builder.Object]
        private Menu menu;
#pragma warning restore 649

        #endregion

        #region Constructors / Destructors
        public static Dictionary<string, ROI> rois = new Dictionary<string, ROI>();
        public static ROIManager Create()
        {
            Builder builder = new Builder(null, "BioGTK.Glade.ROIManager.glade", null);
            return new ROIManager(builder, builder.GetObject("roiManager").Handle);
        }
        protected ROIManager(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);

            UpdateAnnotationList();
            xBox.ChangeValue += xBox_ValueChanged;
            yBox.ChangeValue += yBox_ValueChanged;
            wBox.ChangeValue += wBox_ValueChanged;
            hBox.ChangeValue += hBox_ValueChanged;
            rBox.ChangeValue += rBox_ValueChanged;
            gBox.ChangeValue += gBox_ValueChanged;
            bBox.ChangeValue += bBox_ValueChanged;
            zBox.ChangeValue += zBox_ValueChanged;
            cBox.ChangeValue += cBox_ValueChanged;
            tBox.ChangeValue += tBox_ValueChanged;
            widthBox.ChangeValue += strokeWBox_ValueChanged;
            pointXBox.ChangeValue += pointXBox_ValueChanged;
            pointYBox.ChangeValue += pointYBox_ValueChanged;
            pointBox.ChangeValue += pointBox_ValueChanged;
            selBox.ChangeValue += selectBoxSize_ValueChanged;
            widthBox.ChangeValue += strokeWBox_ValueChanged;
            textBox.Changed += textBox_TextChanged;
            idBox.Changed += idBox_TextChanged;
            boundsBox.Clicked += showBoundsBox_ActiveChanged;
            showTextBox.Clicked += showTextBox_ActiveChanged;
            showRBox.Clicked += showRBox_ActiveChanged;
            showGBox.Clicked += showGBox_ActiveChanged;
            showBBox.Clicked += showBBox_ActiveChanged;
            
            //roiView.Selection.Changed += roiView_SelectedIndexChanged;
            roiView.RowActivated += RoiView_RowActivated;
            roiView.ActivateOnSingleClick = true;

            xBox.Adjustment.Upper = PointD.MaxX;
            yBox.Adjustment.Upper = PointD.MaxY;
            wBox.Adjustment.Upper = PointD.MaxX;
            hBox.Adjustment.Upper = PointD.MaxY;
            rBox.Adjustment.Upper = byte.MaxValue;
            gBox.Adjustment.Upper = byte.MaxValue;
            bBox.Adjustment.Upper = byte.MaxValue;
            zBox.Adjustment.Upper = 10000;
            cBox.Adjustment.Upper = 10000;
            tBox.Adjustment.Upper = 10000;
            widthBox.Adjustment.Upper = 100;
            pointBox.Adjustment.Upper = 100000;
            pointXBox.Adjustment.Upper = PointD.MaxX;
            pointYBox.Adjustment.Upper = PointD.MaxY;

        }
        #endregion

        public ROI anno = new ROI();

        private void RoiView_RowActivated(object o, RowActivatedArgs args)
        {
            string id = (string)args.Args[1];
            if(rois.ContainsKey(id)) 
            {
                anno = rois[id];
                if(App.viewer!=null)
                App.viewer.SetCoordinate(anno.coord.Z, anno.coord.C, anno.coord.T);

                if(anno.type == ROI.Type.Line || anno.type == ROI.Type.Polygon ||
                   anno.type == ROI.Type.Polyline)
                {
                    xBox.Sensitive = false;
                    yBox.Sensitive = false;
                    wBox.Sensitive = false;
                    hBox.Sensitive = false;
                }
                else
                {
                    xBox.Sensitive = true;
                    yBox.Sensitive = true;
                    wBox.Sensitive = true;
                    hBox.Sensitive = true;
                }
                if(anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                {
                    pointBox.Sensitive = false;
                    pointXBox.Sensitive = false;
                    pointYBox.Sensitive = false;
                }
                else
                {
                    pointBox.Sensitive = true;
                    pointXBox.Sensitive = true;
                    pointYBox.Sensitive = true;
                }
                xBox.Value = anno.X;
                yBox.Value = anno.Y;
                wBox.Value = anno.W;
                hBox.Value = anno.H;
                zBox.Value = anno.coord.Z;
                cBox.Value = anno.coord.C;
                tBox.Value = anno.coord.T;
                rBox.Value = anno.strokeColor.R;
                gBox.Value = anno.strokeColor.G;
                bBox.Value = anno.strokeColor.B;
                widthBox.Value = anno.strokeWidth;
                idBox.Text = anno.id;
                textBox.Text = anno.Text;
                typeLabel.Text = anno.type.ToString();
                UpdatePointBox();
            }

        }
        public void InitItems()
        {
            Gtk.TreeViewColumn typeCol = new Gtk.TreeViewColumn();
            typeCol.Title = "Type";
            Gtk.CellRendererText typeCell = new Gtk.CellRendererText();
            typeCol.PackStart(typeCell, true);

            Gtk.TreeViewColumn idCol = new Gtk.TreeViewColumn();
            idCol.Title = "ID";
            Gtk.CellRendererText idCell = new Gtk.CellRendererText();
            idCol.PackStart(idCell, true);

            Gtk.TreeViewColumn textCol = new Gtk.TreeViewColumn();
            textCol.Title = "Text";
            Gtk.CellRendererText textCell = new Gtk.CellRendererText();
            textCol.PackStart(textCell, true);

            Gtk.TreeViewColumn rectCol = new Gtk.TreeViewColumn();
            rectCol.Title = "Bounds";
            Gtk.CellRendererText rectCell = new Gtk.CellRendererText();
            textCol.PackStart(rectCell, true);


            roiView.AppendColumn(typeCol);
            roiView.AppendColumn(idCol);
            roiView.AppendColumn(textCol);
            roiView.AppendColumn(rectCol);

            typeCol.AddAttribute(typeCell, "text", 0);
            idCol.AddAttribute(idCell, "text", 1);
            textCol.AddAttribute(textCell, "text", 2);
            rectCol.AddAttribute(rectCell, "text", 3);

            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string), typeof(string), typeof(string));

            foreach (BioImage b in Images.images)
            {
                Gtk.TreeIter iter = store.AppendValues(System.IO.Path.GetFileName(b.Filename));
                foreach (ROI r in b.Annotations)
                {
                    store.AppendValues(iter, r.type.ToString(), r.id, r.Text, r.Rect.ToString());
                }
            }
            roiView.Model = store;

        }
        public void UpdateAnnotationList()
        {
            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string), typeof(string), typeof(string));

            foreach (BioImage b in Images.images)
            {
                Gtk.TreeIter iter = store.AppendValues(System.IO.Path.GetFileName(b.Filename));
                foreach (ROI r in b.Annotations)
                {
                    rois.Add(r.id, r);
                    store.AppendValues(iter, r.type.ToString(), r.id, r.Text, r.Rect.ToString());
                }
            }
            roiView.Model = store;
        }

        private void UpdateView()
        {
            App.viewer.UpdateView();
        }
        public void updateROI(int index, ROI an)
        {
            if (ImageView.SelectedImage == null)
                return;
            ImageView.SelectedImage.Annotations[index] = an;
            UpdateView();
        }
        private void xBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.X = (double)xBox.Value;
            UpdateView();
        }
        private void yBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.Y = (double)yBox.Value;
            UpdateView();
        }
        private void wBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            if(anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                anno.W = (double)wBox.Value;
            UpdateView();
        }
        private void hBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            if (anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                anno.H = (double)hBox.Value;
            UpdateAnnotationList();
            UpdateView();
        }
        private void sBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            UpdateView();
        }
        private void zBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.coord.Z = (int)zBox.Value;
            UpdateView();
        }
        private void cBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.coord.C = (int)cBox.Value;
            UpdateView();
        }
        private void tBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.coord.T = (int)cBox.Value;
            UpdateView();
        }
        private void rBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.strokeColor = Color.FromArgb((byte)rBox.Value, anno.strokeColor.G, anno.strokeColor.B);
            UpdateView();
        }
        private void gBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.strokeColor = Color.FromArgb(anno.strokeColor.R, (byte)gBox.Value, anno.strokeColor.B);
            UpdateView();
        }
        private void bBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.strokeColor = Color.FromArgb(anno.strokeColor.R, anno.strokeColor.G, (byte)bBox.Value);
            UpdateView();
        }
        private void textBox_TextChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.Text = textBox.Text;
            UpdateView();
        }
        private void idBox_TextChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.id = idBox.Text;
            UpdateView();
        }
        private void ROIManager_Activated(object sender, EventArgs e)
        {
            if (ImageView.SelectedImage == null)
                return;
            string n = System.IO.Path.GetFileName(ImageView.SelectedImage.ID);
            if (imageNameLabel.Text != n)
                imageNameLabel.Text = n;
            UpdateAnnotationList();
        }

        private void addButton_Click(object sender, EventArgs e)
        {
            ImageView.SelectedImage.Annotations.Add(anno);
            UpdateView();
        }
        public static bool showBounds;
        public static bool showText;
        public static bool showR;
        public static bool showG;
        public static bool showB;
        private void showBoundsBox_ActiveChanged(object sender, EventArgs e)
        {
            showBounds = boundsBox.Active;
            UpdateView();
        }
        private void showTextBox_ActiveChanged(object sender, EventArgs e)
        {
            showText = showTextBox.Active;
            UpdateView();
        }
        private void pointXBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            if (anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                return;
            anno.UpdatePoint(new PointD((double)pointXBox.Value, (double)pointYBox.Value),(int)pointBox.Value);
            UpdateView();
        }
        private void pointYBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            if (anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                return;
            anno.UpdatePoint(new PointD((double)pointXBox.Value, (double)pointYBox.Value), (int)pointBox.Value);
            UpdateView();
        }
        public bool autoUpdate = true;
        public void UpdatePointBox()
        {
            if (anno == null)
                return;
            PointD d = anno.GetPoint((int)pointBox.Value);
            pointXBox.Value = (int)d.X;
            pointYBox.Value = (int)d.Y;
        }
        private void pointBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            UpdatePointBox();
        }
        private void strokeWBox_ValueChanged(object sender, ChangeValueArgs args)
        {
            if (anno == null)
                return;
            anno.strokeWidth = (int)widthBox.Value;
            UpdateView();
        }
        private void selectBoxSize_ValueChanged(object sender, ChangeValueArgs args)
        {
            ROI.selectBoxSize = (int)widthBox.Value;
            UpdateView();
        }
        private void showRBox_ActiveChanged(object sender, EventArgs e)
        {
            if (App.viewer == null)
                return;
            App.viewer.showRROIs = showRBox.Active;
            UpdateView();
        }
        private void showGBox_ActiveChanged(object sender, EventArgs e)
        {
            if (App.viewer == null)
                return;
            App.viewer.showGROIs = showGBox.Active;
            UpdateView();
        }
        private void showBBox_ActiveChanged(object sender, EventArgs e)
        {
            if (App.viewer == null)
                return;
            App.viewer.showBROIs = showBBox.Active;
            UpdateView();
        }
        /*
        private void deleteToolStripMenuItem_Click(object sender, EventArgs e)
        {
            for (int i = 0; i < roiView.SelectedItems.Count; i++)
            {
                ImageView.SelectedImage.Annotations.Remove((ROI)roiView.SelectedItems[i].Tag);
            }
            UpdateAnnotationList();
            UpdateView();
        }

        private void copyToolStripMenuItem_Click(object sender, EventArgs e)
        {
            List<ROI> annotations = new List<ROI>();
            ROI an = (ROI)roiView.SelectedItems[0].Tag;
            Clipboard.(BioImage.ROIToString(an));
        }
        */
        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

    }
}
