using AForge;
using BioGTK;
using CSScripting;
using Gtk;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.IO;
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
        private CheckButton showMasksBox;
        [Builder.Object]
        private Menu menu;
        [Builder.Object]
        private MenuItem menuDelete;
        [Builder.Object]
        private MenuItem menuCopy;
        [Builder.Object]
        private MenuItem menuPaste;
#pragma warning restore 649

        #endregion
        private static Random rng = new Random();
        #region Constructors / Destructors
        public BioImage image;
        private static Dictionary<string, ROI> rts = new Dictionary<string, ROI>();
        public static Dictionary<string, ROI> rois
        {
            get
            {
                foreach (var item in ImageView.SelectedImage.Annotations)
                {
                    if(!rts.ContainsKey(item.id))
                    {
                        if (item.id == "" || item.id == null)
                            item.id = rng.Next(0,999999).ToString();
                        rts.Add(item.id, item);
                    }
                }
                return rts;
            }
            set 
            {
                rts = value;
            }
        }
        public static ROIManager Create()
        {
            Builder builder = new Builder(new FileStream(System.IO.Path.GetDirectoryName(Environment.ProcessPath) + "/" + "Glade/ROIManager.glade", FileMode.Open));
            return new ROIManager(builder, builder.GetObject("roiManager").Handle);
        }
        /* The above code is initializing the ROIManager class. */
        protected ROIManager(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);
            xBox.ValueChanged += xBox_ValueChanged;
            yBox.ValueChanged += yBox_ValueChanged;
            wBox.ValueChanged += wBox_ValueChanged;
            hBox.ValueChanged += hBox_ValueChanged;
            rBox.ValueChanged += rBox_ValueChanged;
            gBox.ValueChanged += gBox_ValueChanged;
            bBox.ValueChanged += bBox_ValueChanged;
            zBox.ValueChanged += zBox_ValueChanged;
            cBox.ValueChanged += cBox_ValueChanged;
            tBox.ValueChanged += tBox_ValueChanged;
            widthBox.ValueChanged += strokeWBox_ValueChanged;
            pointXBox.ValueChanged += pointXBox_ValueChanged;
            pointYBox.ValueChanged += pointYBox_ValueChanged;
            pointBox.ValueChanged += pointBox_ValueChanged;
            selBox.ValueChanged += selectBoxSize_ValueChanged;
            widthBox.ValueChanged += strokeWBox_ValueChanged;
            textBox.Changed += textBox_TextChanged;
            idBox.Changed += idBox_TextChanged;
            boundsBox.Clicked += showBoundsBox_ActiveChanged;
            showTextBox.Clicked += showTextBox_ActiveChanged;
            showRBox.Clicked += showRBox_ActiveChanged;
            showGBox.Clicked += showGBox_ActiveChanged;
            showBBox.Clicked += showBBox_ActiveChanged;
            showMasksBox.Clicked += ShowMasksBox_Clicked;
            this.DeleteEvent += ROIManager_DeleteEvent;
            menuDelete.ButtonPressEvent += MenuDelete_ButtonPressEvent;
            menuCopy.ButtonPressEvent += MenuCopy_ButtonPressEvent;
            menuPaste.ButtonPressEvent += MenuPaste_ButtonPressEvent;

            //roiView.Selection.Changed += roiView_SelectedIndexChanged;
            roiView.RowActivated += RoiView_RowActivated;
            this.FocusInEvent += ROIManager_FocusInEvent;
            this.FocusActivated += ROIManager_Activated;
            roiView.ButtonPressEvent += RoiView_ButtonPressEvent;

            xBox.Adjustment.Upper = PointD.MaxX;
            xBox.Adjustment.StepIncrement= 0.1;
            xBox.Adjustment.PageIncrement= 1;
            yBox.Adjustment.Upper = PointD.MaxY;
            yBox.Adjustment.StepIncrement = 0.1;
            yBox.Adjustment.PageIncrement = 1;
            wBox.Adjustment.Upper = PointD.MaxX;
            wBox.Adjustment.StepIncrement = 0.1;
            wBox.Adjustment.PageIncrement = 1;
            hBox.Adjustment.Upper = PointD.MaxY;
            xBox.Adjustment.StepIncrement = 0.1;
            xBox.Adjustment.PageIncrement = 1;
            rBox.Adjustment.Upper = byte.MaxValue;
            rBox.Adjustment.StepIncrement = 1;
            rBox.Adjustment.PageIncrement = 1;
            gBox.Adjustment.Upper = byte.MaxValue;
            gBox.Adjustment.StepIncrement = 1;
            gBox.Adjustment.PageIncrement = 1;
            bBox.Adjustment.Upper = byte.MaxValue;
            bBox.Adjustment.StepIncrement = 1;
            bBox.Adjustment.PageIncrement = 1;
            zBox.Adjustment.Upper = 10000;
            zBox.Adjustment.StepIncrement = 1;
            zBox.Adjustment.PageIncrement = 1;
            cBox.Adjustment.Upper = 10000;
            cBox.Adjustment.StepIncrement = 1;
            cBox.Adjustment.PageIncrement = 1;
            tBox.Adjustment.Upper = 10000;
            tBox.Adjustment.StepIncrement = 1;
            tBox.Adjustment.PageIncrement = 1;
            widthBox.Adjustment.Upper = 100;
            widthBox.Adjustment.StepIncrement = 1;
            widthBox.Adjustment.PageIncrement = 1;
            pointBox.Adjustment.Upper = 100000;
            pointBox.Adjustment.StepIncrement = 1;
            pointBox.Adjustment.PageIncrement = 1;
            pointXBox.Adjustment.Upper = PointD.MaxX;
            pointXBox.Adjustment.StepIncrement = 0.1;
            pointXBox.Adjustment.PageIncrement = 1;
            pointYBox.Adjustment.Upper = PointD.MaxY;
            pointYBox.Adjustment.StepIncrement = 0.1;
            pointYBox.Adjustment.PageIncrement = 1;
            selBox.Value = ROI.selectBoxSize;
            selBox.Adjustment.Upper = 100;
            selBox.Adjustment.StepIncrement = 1;
            roiView.ActivateOnSingleClick = true;
            InitItems();
            App.ApplyStyles(this);
        }

        private void RoiView_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (args.Event.Button == 3)
                menu.Popup();
        }

        private List<ROI> copys = new List<ROI>();
        /// It takes the selected ROIs and copies them to the clipboard
        public void CopySelection()
        {
            copys.Clear();
            string s = "";
            List<ROI> rois = new List<ROI>();
            rois.AddRange(ImageView.SelectedImage.AnnotationsR);
            rois.AddRange(ImageView.SelectedImage.AnnotationsG);
            rois.AddRange(ImageView.SelectedImage.AnnotationsB);
            foreach (ROI item in rois)
            {
                if (item.Selected)
                {
                    copys.Add(item);
                    s += BioImage.ROIToString(item);
                }
            }
            Clipboard clipboard = Clipboard.Get(Gdk.Selection.Clipboard);
            clipboard.Text = s;
        }
        /// The function takes the text from the clipboard and splits it into lines. Each line is then
        /// converted into an ROI object and added to the list of annotations
        public void PasteSelection()
        {
            Clipboard clipboard = Clipboard.Get(Gdk.Selection.Clipboard);
            string text = clipboard.WaitForText();
            string[] sts = text.Split(BioImage.NewLine);
            foreach (string line in sts)
            {
                if (line.Length > 8)
                {
                    ROI an = BioImage.StringToROI(line);
                    //We set the coordinates of the ROI's we are pasting
                    an.coord = App.viewer.GetCoordinate();
                    ImageView.SelectedImage.Annotations.Add(an);
                }
            }
            UpdateView();
        }


        private void MenuPaste_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            PasteSelection();
        }

        private void MenuCopy_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            CopySelection();
        }

        private void MenuDelete_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (anno != null && ImageView.SelectedImage != null)
            {
                // Remove the selected annotation
                ImageView.SelectedImage.Annotations.Remove(anno);
                UpdateAnnotationList();
            }
        }

        private void ShowMasksBox_Clicked(object sender, EventArgs e)
        {
            App.viewer.ShowMasks = showMasksBox.Active;
            UpdateView();
        }

        private void ROIManager_DeleteEvent(object o, DeleteEventArgs args)
        {
            args.RetVal = true;
            Hide();
        }
        
        /// When the ROIManager gets focus, update the annotation list.
        /// 
        /// @param o The object that raised the event.
        /// @param FocusInEventArgs

        private void ROIManager_FocusInEvent(object o, FocusInEventArgs args)
        {
            UpdateAnnotationList();
        }


        #endregion

        public ROI anno = new ROI();

        /// When a row is activated in the ROI view, the corresponding ROI is selected and its
        /// properties are displayed in the property view
        /// 
        /// @param o the object that the event is being called from
        /// @param RowActivatedArgs
        /// https://developer.gnome.org/gtkmm-tutorial/stable/sec-treeview-signals.html.en
        /// 
        /// @return The ROI object.
        private void RoiView_RowActivated(object o, RowActivatedArgs args)
        {
            TreeIter iter;
            if (roiView.Model.GetIter(out iter, args.Path))
            {
                string type = (string)roiView.Model.GetValue(iter, 0);
                ROI.Type t;
                if (Enum.TryParse<ROI.Type>(type,out t))
                {
                    string id = (string)roiView.Model.GetValue(iter, 1);
                    string text = (string)roiView.Model.GetValue(iter, 2);
                    string bounds = (string)roiView.Model.GetValue(iter, 3);
                    if (rois.ContainsKey(id))
                    {
                        anno = rois[id];
                        if (App.viewer != null)
                            App.viewer.SetCoordinate(anno.coord.Z, anno.coord.C, anno.coord.T);
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
                        imageNameLabel.Text = ImageView.SelectedImage.Filename;
                        UpdatePointBox();
                        if (ImageView.SelectedImage.isPyramidal)
                        {
                            App.viewer.PyramidalOrigin = new PointD((int)(anno.X), (int)(anno.Y));
                            App.viewer.UpdateView(true);
                        }
                    }
                    else return;
                }
                else return;
            }
            else
                return;
        }
        /// It creates a treeview with 4 columns, and then populates the treeview with the data from the
        /// Images class
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
            rectCol.PackStart(rectCell, true);


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
                    store.AppendValues(iter, r.type.ToString(), r.id, r.Text, r.BoundingBox.ToString());
                }
            }
            roiView.Model = store;
            
        }
        /// It takes a list of images, and for each image, it takes a list of annotations, and for each
        /// annotation, it adds the annotation to a dictionary, and then adds the annotation to a
        /// treeview
        public void UpdateAnnotationList()
        {
            rois.Clear();
            Gtk.TreeStore store = new Gtk.TreeStore(typeof(string), typeof(string), typeof(string), typeof(string));
            foreach (BioImage b in Images.images)
            {
                Gtk.TreeIter iter = store.AppendValues(System.IO.Path.GetFileName(b.Filename));
                foreach (ROI r in b.Annotations)
                {
                    if (r.id == "")
                    {
                        store.AppendValues(iter, r.type.ToString(), rng.Next(), r.Text, r.BoundingBox.ToString());
                    }
                    else
                    {
                        if(!rois.ContainsKey(r.id))
                        rois.Add(r.id, r);
                        store.AppendValues(iter, r.type.ToString(), r.id, r.Text, r.BoundingBox.ToString());
                    }
                }
            }
            roiView.Model = store;
        }

        /// > UpdateView() is a function that updates the view of the viewer
        private void UpdateView()
        {
            App.viewer.UpdateView();
        }
        /// > Update the ROI at the specified index with the specified ROI
        /// 
        /// @param index the index of the annotation to be updated
        /// @param ROI The ROI object to be updated
        /// 
        /// @return The ROI object is being returned.
        public void updateROI(int index, ROI an)
        {
            if (ImageView.SelectedImage == null)
                return;
            ImageView.SelectedImage.Annotations[index] = an;
            UpdateView();
        }
        /// When the value of the X text box changes, the X value of the annotation is updated
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The value of the X property of the annotation.
        private void xBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.BoundingBox = new RectangleD(xBox.Value, anno.Y, anno.W, anno.H); 
            UpdateView();
        }
        /// When the value of the yBox is changed, the value of the yBox is assigned to the Y property
        /// of the annotation
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The value of the Y coordinate of the annotation.
        private void yBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.BoundingBox = new RectangleD(anno.X, yBox.Value, anno.W, anno.H);
            UpdateView();
        }
        /// When the value of the width box changes, the width of the annotation is updated
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The value of the width of the annotation.
        private void wBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            if(anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                anno.BoundingBox = new RectangleD(anno.X, anno.Y, wBox.Value, anno.H);
            UpdateView();
        }
        /// This function is called when the value of the hBox is changed
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The value of the hBox.Value property.
        private void hBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            if (anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                anno.BoundingBox = new RectangleD(anno.X, anno.Y, anno.W, hBox.Value);
            UpdateView();
        }
        /// This function is called when the value of the checkbox is changed
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs e
        /// 
        /// @return The value of the selected item in the listbox.
        private void sBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            //anno.serie = (int)sBox.Value;
            UpdateView();
        }
        /// When the value of the Z coordinate changes, update the annotation's Z coordinate and update
        /// the view
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The value of the Z coordinate of the annotation.
        private void zBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.coord.Z = (int)zBox.Value;
            UpdateView();
        }
        /// When the value of the C coordinate changes, update the annotation's C coordinate and update
        /// the view
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The value of the C coordinate of the annotation.
        private void cBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.coord.C = (int)cBox.Value;
            UpdateView();
        }
       /// When the user changes the value of the time slider, the annotation's time coordinate is
       /// updated and the view is updated
       /// 
       /// @param sender The object that raised the event.
       /// @param EventArgs 
       /// 
       /// @return The value of the annotation.
        private void tBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.coord.T = (int)cBox.Value;
            UpdateView();
        }
       /// When the value of the red color box changes, the red color value of the annotation is changed
       /// to the value of the red color box
       /// 
       /// @param sender The object that raised the event.
       /// @param EventArgs The event arguments.
       /// 
       /// @return The color of the annotation.
        private void rBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.strokeColor = Color.FromArgb((byte)rBox.Value, anno.strokeColor.G, anno.strokeColor.B);
            UpdateView();
        }
        /// When the value of the green trackbar changes, the green value of the annotation's stroke
        /// color is updated to the new value
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The value of the slider.
        private void gBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.strokeColor = Color.FromArgb(anno.strokeColor.R, (byte)gBox.Value, anno.strokeColor.B);
            UpdateView();
        }
       /// When the value of the blue color slider changes, the blue color value of the annotation is
       /// updated
       /// 
       /// @param sender The object that raised the event.
       /// @param EventArgs The event arguments.
       /// 
       /// @return The value of the slider is being returned.
        private void bBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.strokeColor = Color.FromArgb(anno.strokeColor.R, anno.strokeColor.G, (byte)bBox.Value);
            UpdateView();
        }
        /// When the text in the textbox changes, the text in the annotation changes, and the view is
        /// updated
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        /// 
        /// @return The textbox is being returned.
        private void textBox_TextChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.Text = textBox.Text;
            UpdateView();
        }
        /// When the text in the idBox changes, the id of the annotation is set to the text in the idBox
        /// 
        /// @param sender The control that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes that contain event data,
        /// and provides a value to use with events that do not include event data.
        /// 
        /// @return The idBox.Text is being returned.
        private void idBox_TextChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.id = idBox.Text;
            UpdateView();
        }
        /// When the ROI Manager is activated, if the selected image is not null, the name of the image
        /// is displayed in the imageNameLabel
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The image name.
        private void ROIManager_Activated(object sender, EventArgs e)
        {
            if (ImageView.SelectedImage == null)
                return;
            string n = System.IO.Path.GetFileName(ImageView.SelectedImage.ID);
            if (imageNameLabel.Text != n)
                imageNameLabel.Text = n;
            UpdateAnnotationList();
        }

        /// The function adds the annotation to the image and updates the view
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void addButton_Click(object sender, EventArgs e)
        {
            ImageView.SelectedImage.Annotations.Add(anno);
            UpdateView();
        }
        public static bool showBounds = true;
        public static bool showText = false;
        public static bool showR = true;
        public static bool showG = true;
        public static bool showB = true;
        /// When the checkbox is clicked, the showBounds variable is set to the value of the checkbox
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs This is a class that contains the event data.
        private void showBoundsBox_ActiveChanged(object sender, EventArgs e)
        {
            showBounds = boundsBox.Active;
            UpdateView();
        }
        /// When the checkbox is clicked, the value of the checkbox is stored in the variable showText
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes that contain event data.
        private void showTextBox_ActiveChanged(object sender, EventArgs e)
        {
            showText = showTextBox.Active;
            UpdateView();
        }
        /// When the user changes the value of the pointXBox, the function checks if the annotation is a
        /// rectangle or ellipse. If it is, the function returns. If it is not, the function updates the
        /// point of the annotation and updates the view
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The value of the pointXBox.Value is being returned.
        private void pointXBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            if (anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                return;
            anno.UpdatePoint(new PointD((double)pointXBox.Value, (double)pointYBox.Value),(int)pointBox.Value);
            UpdateView();
        }
        /// When the user changes the value of the pointYBox, the function updates the pointYBox value
        /// to the new value
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The value of the pointXBox.Value and pointYBox.Value
        private void pointYBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            if (anno.type == ROI.Type.Rectangle || anno.type == ROI.Type.Ellipse)
                return;
            anno.UpdatePoint(new PointD((double)pointXBox.Value, (double)pointYBox.Value), (int)pointBox.Value);
            UpdateView();
        }
        public bool autoUpdate = true;
        /// This function updates the point box with the current point
        /// 
        /// @return The point of the annotation.
        public void UpdatePointBox()
        {
            if (anno == null)
                return;
            PointD d = anno.GetPoint((int)pointBox.Value);
            pointXBox.Value = (int)d.X;
            pointYBox.Value = (int)d.Y;
        }
        /// When the value of the pointBox changes, update the pointBox
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments.
        private void pointBox_ValueChanged(object sender, EventArgs e)
        {
            UpdatePointBox();
        }
        /// When the stroke width is changed, the stroke width of the annotation is changed to the value
        /// of the stroke width box
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        /// 
        /// @return The value of the stroke width.
        private void strokeWBox_ValueChanged(object sender, EventArgs e)
        {
            if (anno == null)
                return;
            anno.strokeWidth = (int)widthBox.Value;
            UpdateView();
        }
        /// When the value of the numeric up down box changes, the value of the numeric up down box is
        /// assigned to the variable selectBoxSize
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The event arguments that are passed to the event handler.
        private void selectBoxSize_ValueChanged(object sender, EventArgs e)
        {
            ROI.selectBoxSize = (int)widthBox.Value;
            UpdateView();
        }
        /// If the checkbox is checked, then the viewer will show the RROIs
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The value of the checkbox.
        private void showRBox_ActiveChanged(object sender, EventArgs e)
        {
            if (App.viewer == null)
                return;
            App.viewer.showRROIs = showRBox.Active;
            UpdateView();
        }
        /// If the checkbox is checked, then the viewer will show the GROIs
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs 
        /// 
        /// @return The value of the checkbox.
        private void showGBox_ActiveChanged(object sender, EventArgs e)
        {
            if (App.viewer == null)
                return;
            App.viewer.showGROIs = showGBox.Active;
            UpdateView();
        }
        /// If the viewer is not null, set the viewer's showBROIs to the value of the checkbox, and
        /// update the view
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        /// 
        /// @return The value of the property.
        private void showBBox_ActiveChanged(object sender, EventArgs e)
        {
            if (App.viewer == null)
                return;
            App.viewer.showBROIs = showBBox.Active;
            UpdateView();
        }
       
        /// This function saves the current file
        /// 
        /// @param sender The object that raised the event.
        /// @param EventArgs The EventArgs class is the base class for classes containing event data.
        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {

        }

    }
}
