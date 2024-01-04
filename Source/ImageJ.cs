using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using System.IO;
using CSScripting;
using Gtk;
using AForge;
using BioGTK;

namespace BioGTK
{
    public static class ImageJ
    {
        public static class Macro
        {
            public class Command
            {
                public string Name {  get; set; }
                public string Arguments { get; set; }
                public string Description { get; set; }
                public Command(string name, string args, string description)
                {
                    Name = name;
                    Arguments = args;
                    Description = description;
                }
                public override string ToString()
                {
                    return Name.ToString();
                }
            }
            public class Function
            {
                public string Name { get; set; }
                public string Arguments { get; set; }
                public string Description { get; set; }
                public Function(string name, string args, string description)
                {
                    Name = name;
                    Arguments = args;
                    Description = description;
                }
                public override string ToString()
                {
                    return Name.ToString();
                }
            }
            public static Dictionary<string,Command> Commands = new Dictionary<string,Command>();
            public static Dictionary<string, List<Function>> Functions = new Dictionary<string, List<Function>>();
            internal static void Initialize()
            {
                string[] sts = File.ReadAllLines("macro-functions.txt");
                foreach (string s in sts)
                {
                    string com = "";
                    string args = "";
                    string doc = "";
                    bool indoc = false, inargs = false;
                    if (!s.StartsWith('#'))
                    {
                        for (int i = 0; i < s.Length; i++)
                        {
                            if (!inargs)
                            {
                                if (s[i] != '(')
                                    com += s[i];
                                else
                                    inargs = true;
                                if (s[i] == ' ')
                                {
                                    inargs = true;
                                    indoc = true;
                                }
                            }
                            else
                            if (!indoc)
                                if (s[i] != ')')
                                    args += s[i];
                                else
                                    indoc = true;
                            else
                                doc += s[i];
                        }
                        if (!Functions.ContainsKey(com))
                            Functions.Add(com, new List<Function>() { new Function(com,args, doc) });
                        else
                            Functions[com].Add(new Function(com, args, doc));
                    }
                }
                string[] cs = File.ReadAllLines("macro-commands.csv");
                foreach (string s in cs)
                {
                    string[] v = s.Split(',');
                    Commands.Add(v[0], new Command(v[0], v[1], ""));
                }
            }
        }
        static bool init = false;
        public static bool Initialized { get { return init; } private set { } }
        public static string ImageJPath;
        public static List<Process> processes = new List<Process>();
        public static List<Macro.Command> Macros = new List<Macro.Command>();
        private static Random rng = new Random();
        /// It runs a macro in ImageJ
        /// 
        /// @param file the path to the macro file
        /// @param param The parameter to pass to the macro.
        /// 
        /// @return The macro is being returned.
        public static void RunMacro(string file, string param)
        {
            if (!Initialized)
            {
                if (!Initialize(true))
                    return;
            }
            file.Replace("/", "\\");
            Process pr = new Process();
            pr.StartInfo.FileName = ImageJPath;
            pr.StartInfo.Arguments = "-macro " + file + " " + param;
            pr.Start();
            processes.Add(pr);
            Recorder.AddLine("ImageJ.RunMacro(" + file + "," + '"' + param + '"' + ");");
        }
        /// It runs a macro in ImageJ
        /// 
        /// @param con The macro code
        /// @param param The parameters to pass to the macro.
        /// @param headless Whether or not to run ImageJ in headless mode.
        /// 
        /// @return Nothing.
        public static void RunString(string con, string param, bool headless)
        {
            if (!Initialized)
            {
                if (!Initialize(true))
                    return;
            }
            Process pr = new Process();
            pr.StartInfo.FileName = ImageJPath;
            string te = rng.Next(0, 9999999).ToString();
            string p = Path.GetDirectoryName(Environment.ProcessPath) + "/" + te;
            if (OperatingSystem.IsMacOS())
            {
                Console.WriteLine(p);
                pr.StartInfo.UseShellExecute = true;
            }
            File.WriteAllText(p, con);
            if (headless)
                pr.StartInfo.Arguments = "--headless -macro " + p + " " + param;
            else
                pr.StartInfo.Arguments = "-macro " + p + " " + param;
            pr.Start();
            string donedir = Path.GetDirectoryName(Environment.ProcessPath);
            donedir = donedir.Replace("\\", "/");
            File.Delete(Path.GetDirectoryName(Environment.ProcessPath) + "/done.txt");
            processes.Add(pr);
            do
            {
                if (File.Exists(donedir + "/done.txt"))
                {
                    do
                    {
                        try
                        {
                            File.Delete(donedir + "/done.txt");
                        }
                        catch (Exception)
                        {

                        }
                    } while (File.Exists(donedir + "/done.txt"));
                    pr.Kill();
                    break;
                }
            } while (!pr.HasExited);
            File.Delete(p);
        }
        /// It runs a macro on the current image, saves the result as a new image, and then opens the
        /// new image in a new tab
        /// 
        /// @param con The ImageJ macro to run on the image.
        /// @param headless Whether to run ImageJ in headless mode.
        /// @param onTab If true, the imacro will be run on tab. If false, the macro will be run on selected image.
        /// @param bioformats If true, the image is opened using the bioformats plugin. If false, the
        /// image is opened using the default imagej open command.
        /// 
        /// @return The image is being returned as a new tab.
        public static void RunOnImage(string con, int index, bool headless, bool onTab, bool bioformats, bool resultInNewTab)
        {
            if (!Initialized)
            {
                if (!Initialize(true))
                    return;
            }
            string filename = "";
            string dir = Path.GetDirectoryName(ImageView.SelectedImage.file);
            dir = dir.Replace("\\", "/");
            if (ImageView.SelectedImage.ID.EndsWith(".ome.tif"))
            {
                filename = Path.GetFileNameWithoutExtension(ImageView.SelectedImage.ID);
                filename = filename.Remove(filename.Length - 4, 4);
            }
            else
                filename = Path.GetFileNameWithoutExtension(ImageView.SelectedImage.ID);
            string file = dir + "/" + filename + "-temp.ome.tif";
            if(!bioformats)
                file = dir + "/" + filename + ".tif";
            string donepath = Path.GetDirectoryName(Environment.ProcessPath);
            donepath = donepath.Replace("\\", "/");
            string op = dir + "/" + ImageView.SelectedImage.ID.Replace("\\", "/");
            if(!File.Exists(op))
            {
                BioImage.SaveOME(ImageView.SelectedImage, op);
            }
            string st =
            "run(\"Bio-Formats Importer\", \"open=" + op + " autoscale color_mode=Default open_all_series display_rois rois_import=[ROI manager] view=Hyperstack stack_order=XYCZT\"); " + con +
            "run(\"Bio-Formats Exporter\", \"save=" + file + " export compression=Uncompressed\"); " +
            "dir = \"" + donepath + "\"" +
            "File.saveString(\"done\", dir + \"/done.txt\");";
            if (!bioformats)
                st =
                "open(getArgument); " + con +
                "saveAs(\"Tiff\",\"" + file + "\"); " +
                "dir = \"" + donepath + "\"" +
                "File.saveString(\"done\", dir + \"/done.txt\");";
            if (File.Exists(file) && bioformats)
                File.Delete(file);
            RunString(st, dir + "/" + ImageView.SelectedImage.ID, headless);
            if (!File.Exists(file))
                return;
            //If not in images we add it to a new tab.
            if (Images.GetImage(file) == null)
            {
                BioImage bm = BioImage.OpenFile(file, index, false, false);
                bm.ID = Path.GetFileName(file).Replace("-temp","");
                bm.Filename = bm.ID;
                bm.file = file;
                Images.AddImage(bm,true);
            }
            else
            {
                BioImage b = BioImage.OpenFile(file, index, onTab, false);
                b.ID = ImageView.SelectedImage.ID;
                b.Filename = ImageView.SelectedImage.Filename;
                b.file = dir + "/" + ImageView.SelectedImage.ID;
                Images.UpdateImage(b);
                App.viewer.Images[App.viewer.SelectedIndex] = b;
            }
            //If using bioformats we delete the temp file.
            if(bioformats)
            File.Delete(file);
            // update image on main UI thread
            if (App.viewer != null)
            {
                Application.Invoke(delegate
                {
                    App.viewer.UpdateImage();
                    App.viewer.UpdateView();
                });
            }
            Recorder.AddLine("ImageJ.RunOnImage(\"" + con + "\"," + index + "," + headless + "," + onTab + "," + bioformats + "," + resultInNewTab + ");");
        }
        public static void RunOnImage(string s)
        {
            RunOnImage(s, BioConsole.headless, BioConsole.onTab, BioConsole.useBioformats, BioConsole.resultInNewTab);
        }
        public static void RunOnImage(string con, bool headless, bool onTab, bool bioformats, bool resultInNewTab)
        {
            RunOnImage(con,0,headless,onTab,bioformats,resultInNewTab);
        }
        /// This function is used to initialize the path of the ImageJ.exe file
        /// 
        /// @param path The path to the ImageJ executable.
        public static bool Initialize(bool imagej)
        {
            if (!imagej)
                return false;
            if (!SetImageJPath())
                return false;
            Macro.Initialize();
            string[] ds = Directory.GetFiles(Path.GetDirectoryName(ImageJPath) + "/macros");
            foreach (string s in ds)
            {
                if(s.EndsWith(".ijm") || s.EndsWith(".txt"))
                Macros.Add(new Macro.Command(Path.GetFileName(s), "", ""));
            }
            return true;
        }

        /// This function creates a file chooser dialog that allows the user to select the location of
        /// the ImageJ executable
        /// 
        /// @return A boolean value.
        public static bool SetImageJPath()
        {
            if(Settings.GetSettings("ImageJPath")!="")
            {
                Initialized = true;
                ImageJPath = Settings.GetSettings("ImageJPath");
            }
            string title = "Select ImageJ Executable Location";
            if (OperatingSystem.IsMacOS())
                title = "Select ImageJ Executable Location (Fiji.app/Contents/MacOS/ImageJ-macosx)";
            Gtk.FileChooserDialog filechooser =
    new Gtk.FileChooserDialog(title, Scripting.window,
        FileChooserAction.Open,
        "Cancel", ResponseType.Cancel,
        "Save", ResponseType.Accept);
            filechooser.SetCurrentFolder(System.IO.Path.GetDirectoryName(Environment.ProcessPath));
            if (filechooser.Run() != (int)ResponseType.Accept)
                return false;
            ImageJ.ImageJPath = filechooser.Filename;
            filechooser.Destroy();
            Settings.AddSettings("ImageJPath", filechooser.Filename);
            Settings.Save();
            Initialized = true;
            return true;
        }

        public class RoiDecoder
        {
            #region Params
            // offsets
            public static int VERSION_OFFSET = 4;
            public static int TYPE = 6;
            public static int TOP = 8;
            public static int LEFT = 10;
            public static int BOTTOM = 12;
            public static int RIGHT = 14;
            public static int N_COORDINATES = 16;
            public static int X1 = 18;
            public static int Y1 = 22;
            public static int X2 = 26;
            public static int Y2 = 30;
            public static int XD = 18;
            public static int YD = 22;
            public static int WIDTHD = 26;
            public static int HEIGHTD = 30;
            public static int SIZE = 18;
            public static int STROKE_WIDTH = 34;
            public static int SHAPE_ROI_SIZE = 36;
            public static int STROKE_COLOR = 40;
            public static int FILL_COLOR = 44;
            public static int SUBTYPE = 48;
            public static int OPTIONS = 50;
            public static int ARROW_STYLE = 52;
            public static int FLOAT_PARAM = 52; //ellipse ratio or rotated rect width
            public static int POINT_TYPE = 52;
            public static int ARROW_HEAD_SIZE = 53;
            public static int ROUNDED_RECT_ARC_SIZE = 54;
            public static int POSITION = 56;
            public static int HEADER2_OFFSET = 60;
            public static int COORDINATES = 64;
            // header2 offsets
            public static int C_POSITION = 4;
            public static int Z_POSITION = 8;
            public static int T_POSITION = 12;
            public static int NAME_OFFSET = 16;
            public static int NAME_LENGTH = 20;
            public static int OVERLAY_LABEL_COLOR = 24;
            public static int OVERLAY_FONT_SIZE = 28; //short
            public static int GROUP = 30;  //byte
            public static int IMAGE_OPACITY = 31;  //byte
            public static int IMAGE_SIZE = 32;  //int
            public static int FLOAT_STROKE_WIDTH = 36;  //float
            public static int ROI_PROPS_OFFSET = 40;
            public static int ROI_PROPS_LENGTH = 44;
            public static int COUNTERS_OFFSET = 48;

            // subtypes
            public static int TEXT = 1;
            public static int ARROW = 2;
            public static int ELLIPSE = 3;
            public static int IMAGE = 4;
            public static int ROTATED_RECT = 5;

            // options
            public static int SPLINE_FIT = 1;
            public static int DOUBLE_HEADED = 2;
            public static int OUTLINE = 4;
            public static int OVERLAY_LABELS = 8;
            public static int OVERLAY_NAMES = 16;
            public static int OVERLAY_BACKGROUNDS = 32;
            public static int OVERLAY_BOLD = 64;
            public static int SUB_PIXEL_RESOLUTION = 128;
            public static int DRAW_OFFSET = 256;
            public static int ZERO_TRANSPARENT = 512;
            public static int SHOW_LABELS = 1024;
            public static int SCALE_LABELS = 2048;
            public static int PROMPT_BEFORE_DELETING = 4096; //points
            public static int SCALE_STROKE_WIDTH = 8192;

            // types
            private int polygon = 0, rect = 1, oval = 2, line = 3, freeline = 4, polyline = 5, noRoi = 6,
                freehand = 7, traced = 8, angle = 9, point = 10;

            private byte[] data;
            private string path;
            private MemoryStream ins;
            private string name;
            private int size;
            #endregion

            /** Constructs an RoiDecoder using a file path. */
            public RoiDecoder(string path)
            {
                this.path = path;
            }

            /** Constructs an RoiDecoder using a byte array. */
            public RoiDecoder(byte[] bytes, string name)
            {
                ins = new MemoryStream(bytes);
                this.name = name;
                this.size = bytes.Length;
            }

            /** Opens the BioGTK.ROI at the specified path. Returns null if there is an error. */
            public static BioGTK.ROI open(string path)
            {
                BioGTK.ROI roi = null;
                RoiDecoder rd = new RoiDecoder(path);
                roi = rd.getRoi();
                return roi;
            }

            /** Returns the ROI. */
            public BioGTK.ROI getRoi()
            {
                BioGTK.ROI roi = new BioGTK.ROI();
                data = File.ReadAllBytes(path);
                size = data.Length;
                if (getByte(0) != 73 || getByte(1) != 111)  //"Iout"
                    throw new IOException("This is not an ImageJ ROI");
                int version = getShort(VERSION_OFFSET);
                int type = getByte(TYPE);
                int subtype = getShort(SUBTYPE);
                int top = getShort(TOP);
                int left = getShort(LEFT);
                int bottom = getShort(BOTTOM);
                int right = getShort(RIGHT);
                int width = right - left;
                int height = bottom - top;
                int n = getUnsignedShort(N_COORDINATES);
                if (n == 0)
                    n = getInt(SIZE);
                int options = getShort(OPTIONS);
                int position = getInt(POSITION);
                int hdr2Offset = getInt(HEADER2_OFFSET);
                int channel = 0, slice = 0, frame = 0;
                int overlayLabelColor = 0;
                int overlayFontSize = 0;
                int group = 0;
                int imageOpacity = 0;
                int imageSize = 0;
                bool subPixelResolution = (options & SUB_PIXEL_RESOLUTION) != 0 && version >= 222;
                bool drawOffset = subPixelResolution && (options & DRAW_OFFSET) != 0;
                bool scaleStrokeWidth = true;
                if (version >= 228)
                    scaleStrokeWidth = (options & SCALE_STROKE_WIDTH) != 0;

                bool subPixelRect = version >= 223 && subPixelResolution && (type == rect || type == oval);
                double xd = 0.0, yd = 0.0, widthd = 0.0, heightd = 0.0;
                if (subPixelRect) {
                    xd = getFloat(XD);
                    yd = getFloat(YD);
                    widthd = getFloat(WIDTHD);
                    heightd = getFloat(HEIGHTD);
                    roi.subPixel = true;
                }

                if (hdr2Offset > 0 && hdr2Offset + IMAGE_SIZE + 4 <= size)
                {
                    channel = getInt(hdr2Offset + C_POSITION);
                    slice = getInt(hdr2Offset + Z_POSITION);
                    frame = getInt(hdr2Offset + T_POSITION);
                    overlayLabelColor = getInt(hdr2Offset + OVERLAY_LABEL_COLOR);
                    overlayFontSize = getShort(hdr2Offset + OVERLAY_FONT_SIZE);
                    imageOpacity = getByte(hdr2Offset + IMAGE_OPACITY);
                    imageSize = getInt(hdr2Offset + IMAGE_SIZE);
                    group = getByte(hdr2Offset + GROUP);
                }

                if (name != null && name.EndsWith(".roi"))
                    name = name.Substring(0, name.Length - 4);
                bool isComposite = getInt(SHAPE_ROI_SIZE) > 0;


                /*
                if (isComposite)
                {
                    roi = getShapeRoi();
                    if (version >= 218)
                        getStrokeWidthAndColor(roi, hdr2Offset, scaleStrokeWidth);
                    roi.coord.Z = position;
                    if (channel > 0 || slice > 0 || frame > 0)
                    {
                        roi.coord.C = channel; roi.coord.Z = slice; roi.coord.T = frame;
                    }
                    decodeOverlayOptions(roi, version, options, overlayLabelColor, overlayFontSize);
                    if (version >= 224)
                    {
                        string props = getRoiProps();
                        if (props != null)
                            roi.properties = props;
                    }
                    if (version >= 228 && group > 0)
                        roi.serie = group;
                    return roi;
                }
                */
                switch (type)
                {
                    case 1: //Rect
                        if (subPixelRect)
                            roi = BioGTK.ROI.CreateRectangle(new AForge.ZCT(slice-1, channel - 1, frame - 1), xd, yd, widthd, heightd);
                        else
                            roi = BioGTK.ROI.CreateRectangle(new AForge.ZCT(slice - 1, channel - 1, frame - 1), left, top, width, height);
                        int arcSize = getShort(ROUNDED_RECT_ARC_SIZE);
                        if (arcSize > 0)
                            throw new NotSupportedException("Type rounded rectangle not supported.");
                        break;
                    case 2: //Ellipse
                        if (subPixelRect)
                            roi = BioGTK.ROI.CreateEllipse(new AForge.ZCT(slice - 1, channel - 1, frame - 1), xd, yd, widthd, heightd);
                        else
                            roi = BioGTK.ROI.CreateEllipse(new AForge.ZCT(slice - 1, channel - 1, frame - 1), left, top, width, height);
                        break;
                    case 3: //Line
                        float x1 = getFloat(X1);
                        float y1 = getFloat(Y1);
                        float x2 = getFloat(X2);
                        float y2 = getFloat(Y2);

                        if (subtype == ARROW)
                        {
                            throw new NotSupportedException("Type arrow not supported.");
                            /*
                            roi = new Arrow(x1, y1, x2, y2);
                            ((Arrow)roi).setDoubleHeaded((options & DOUBLE_HEADED) != 0);
                            ((Arrow)roi).setOutline((options & OUTLINE) != 0);
                            int style = getByte(ARROW_STYLE);
                            if (style >= Arrow.FILLED && style <= Arrow.BAR)
                                ((Arrow)roi).setStyle(style);
                            int headSize = getByte(ARROW_HEAD_SIZE);
                            if (headSize >= 0 && style <= 30)
                                ((Arrow)roi).setHeadSize(headSize);
                            */
                        }
                        else
                        {
                            roi = ROI.CreateLine(new AForge.ZCT(slice, channel, frame), new AForge.PointD(x1, y1), new AForge.PointD(x2, y2));
                            //roi.setDrawOffset(drawOffset);
                        }

                        break;
                    case 0:
                    case 5:
                    case 6:
                    case 7:
                    case 8:
                    case 9:
                    case 10:
                        //IJ.log("type: "+type);
                        //IJ.log("n: "+n);
                        //ij.IJ.log("rect: "+left+","+top+" "+width+" "+height);
                        if (n == 0 || n < 0) break;
                        int[] x = new int[n];
                        int[] y = new int[n];
                        float[] xf = null;
                        float[] yf = null;
                        int base1 = COORDINATES;
                        int base2 = base1 + 2 * n;
                        int xtmp, ytmp;
                        for (int i = 0; i < n; i++)
                        {
                            xtmp = getShort(base1 + i * 2);
                            if (xtmp < 0) xtmp = 0;
                            ytmp = getShort(base2 + i * 2);
                            if (ytmp < 0) ytmp = 0;
                            x[i] = left + xtmp;
                            y[i] = top + ytmp;
                        }
                        if (subPixelResolution)
                        {
                            xf = new float[n];
                            yf = new float[n];
                            base1 = COORDINATES + 4 * n;
                            base2 = base1 + 4 * n;
                            for (int i = 0; i < n; i++)
                            {
                                xf[i] = getFloat(base1 + i * 4);
                                yf[i] = getFloat(base2 + i * 4);
                            }
                        }
                        if (type == point)
                        {
                            //TODO implement non subpizel ROI
                            if (subPixelResolution)
                            {
                                roi.AddPoints(xf, yf);
                            }
                            else
                                roi.AddPoints(x, y);
                            if (version >= 226)
                            {
                                //((PointRoi)roi).setPointType(getByte(POINT_TYPE));
                                roi.strokeWidth = getShort(STROKE_WIDTH);
                            }
                            //if ((options & SHOW_LABELS) != 0 && !ij.Prefs.noPointLabels)
                            //    ((PointRoi)roi).setShowLabels(true);
                            //if ((options & PROMPT_BEFORE_DELETING) != 0)
                            //    ((PointRoi)roi).promptBeforeDeleting(true);
                            roi.type = ROI.Type.Point;
                            break;
                        }
                        if (type == polygon)
                            roi.type = ROI.Type.Polygon;
                        else if (type == freehand)
                        {
                            roi.type = ROI.Type.Freeform;
                            if (subtype == ELLIPSE || subtype == ROTATED_RECT)
                            {
                                throw new NotSupportedException("ROI type not supported.");
                                /*
                                double ex1 = getFloat(X1);
                                double ey1 = getFloat(Y1);
                                double ex2 = getFloat(X2);
                                double ey2 = getFloat(Y2);
                                double param = getFloat(FLOAT_PARAM);
                                if (subtype == ROTATED_RECT)
                                    roi = new RotatedRectRoi(ex1, ey1, ex2, ey2, param);
                                else
                                    roi = new EllipseRoi(ex1, ey1, ex2, ey2, param);
                                break;
                                */
                            }
                        }
                        else if (type == traced)
                            roi.type = ROI.Type.Polyline;
                        else if (type == polyline)
                            roi.type = ROI.Type.Polyline;
                        else if (type == freeline)
                            roi.type = ROI.Type.Polyline;
                        else if (type == angle)
                            roi.type = ROI.Type.Point;
                        else
                            roi.type = ROI.Type.Freeform;
                        if (subPixelResolution)
                        {
                            roi.AddPoints(xf, yf);
                            //roi = new PolygonRoi(xf, yf, n, roiType);
                            //roi.setDrawOffset(drawOffset);
                        }
                        else
                            roi.AddPoints(x, y);
                        break;
                    default:
                        throw new IOException("Unrecognized ROI type: " + type);
                }
                if (roi == null)
                    return null;
                roi.roiName = getRoiName();

                // read stroke width, stroke color and fill color (1.43i or later)
                if (version >= 218)
                {
                    getStrokeWidthAndColor(roi, hdr2Offset, scaleStrokeWidth);
                    /*
                    if (type == point)
                        roi.setStrokeWidth(0);
                    bool splineFit = (options & SPLINE_FIT) != 0;
                    if (splineFit && roi instanceof PolygonRoi)
				            ((PolygonRoi)roi).fitSpline();
                    */
                }

                if (version >= 218 && subtype == TEXT)
                {
                    getTextRoi(roi, version);
                    roi.type = ROI.Type.Label;
                }
                /*
                if (version >= 221 && subtype == IMAGE)
                    roi = getImageRoi(roi, imageOpacity, imageSize, options);
                
                if (version >= 224)
                {
                    string props = getRoiProps();
                    if (props != null)
                        roi.setProperties(props);
                }

                if (version >= 227)
                {
                    int[] counters = getPointCounters(n);
                    if (counters != null && (roi instanceof PointRoi))
				            ((PointRoi)roi).setCounters(counters);
                }
                */
                // set group (1.52t or later)
                if (version >= 228 && group > 0)
                    roi.serie = group;

                roi.coord.Z = position;
                if (channel > 0 || slice > 0 || frame > 0)
                    roi.coord = new AForge.ZCT(slice - 1, channel - 1, frame - 1); //-1 because our ROI coordinates are 0 based
                //decodeOverlayOptions(roi, version, options, overlayLabelColor, overlayFontSize);

                //We convert pixel to subpixel
                if (!roi.subPixel)
                {
                    for (int i = 0; i < roi.PointsD.Count; i++)
                    {
                        AForge.PointD pd = ImageView.SelectedImage.ToStageSpace(roi.PointsD[i]);
                        roi.PointsD[i] = pd;
                        roi.UpdateBoundingBox();
                    }
                }
                if (roi.type == ROI.Type.Polygon || roi.type == ROI.Type.Freeform)
                    roi.closed = true;
                return roi;
            }
            /*
            void decodeOverlayOptions(BioGTK.ROI roi, int version, int options, int color, int fontSize)
            {
                
                Overlay proto = new Overlay();
                proto.drawLabels((options & OVERLAY_LABELS) != 0);
                proto.drawNames((options & OVERLAY_NAMES) != 0);
                proto.drawBackgrounds((options & OVERLAY_BACKGROUNDS) != 0);
                if (version >= 220 && color != 0)
                    proto.setLabelColor(new Color(color));
                bool bold = (options & OVERLAY_BOLD) != 0;
                bool scalable = (options & SCALE_LABELS) != 0;
                if (fontSize > 0 || bold || scalable)
                {
                    proto.setLabelFont(new Font("SansSerif", bold ? Font.BOLD : Font.PLAIN, fontSize), scalable);
                }
                roi.setPrototypeOverlay(proto);
                
            }
            */
            void getStrokeWidthAndColor(BioGTK.ROI roi, int hdr2Offset, bool scaleStrokeWidth)
            {
                double strokeWidth = getShort(STROKE_WIDTH);
                if (hdr2Offset > 0)
                {
                    double strokeWidthD = getFloat(hdr2Offset + FLOAT_STROKE_WIDTH);
                    if (strokeWidthD > 0.0)
                        strokeWidth = strokeWidthD;
                }
                if (strokeWidth > 0.0)
                {
                    roi.strokeWidth = strokeWidth;
                }
                int strokeColor = getInt(STROKE_COLOR);
                if (strokeColor != 0)
                {
                    byte[] bts = BitConverter.GetBytes(strokeColor);
                    AForge.Color c = AForge.Color.FromArgb(bts[0], bts[1], bts[2], bts[3]);
                    roi.strokeColor = c;
                }
                int fillColor = getInt(FILL_COLOR);
                if (fillColor != 0)
                {
                    byte[] bts = BitConverter.GetBytes(strokeColor);
                    AForge.Color c = AForge.Color.FromArgb(bts[0], bts[1], bts[2], bts[3]);
                    roi.fillColor = c;
                }
            }
            /*
            public BioGTK.ROI getShapeRoi()
            {
		        int type = getByte(TYPE);
		        if (type!=rect)
			        throw new NotSupportedException("Invalid composite ROI type");
                int top = getShort(TOP);
                int left = getShort(LEFT);
                int bottom = getShort(BOTTOM);
                int right = getShort(RIGHT);
                int width = right - left;
                int height = bottom - top;
                int n = getInt(SHAPE_ROI_SIZE);

                BioGTK.ROI roi = new ROI();
                float[] shapeArray = new float[n];
                int bas = COORDINATES;
                for (int i = 0; i < n; i++)
                {
                    shapeArray[i] = getFloat(bas);
                    bas += 4;
                }
                roi = new ShapeRoi(shapeArray);
                roi.setName(getRoiName());
                return roi;
            }
	        */
            void getTextRoi(BioGTK.ROI roi, int version)
            {
                AForge.Rectangle r = roi.BoundingBox.ToRectangleInt();
                int hdrSize = 64;
                int size = getInt(hdrSize);
                int styleAndJustification = getInt(hdrSize + 4);
                int style = styleAndJustification & 255;
                int justification = (styleAndJustification >> 8) & 3;
                bool drawStringMode = (styleAndJustification & 1024) != 0;
                int nameLength = getInt(hdrSize + 8);
                int textLength = getInt(hdrSize + 12);
                char[] name = new char[nameLength];
                char[] text = new char[textLength];
                for (int i = 0; i < nameLength; i++)
                    name[i] = (char)getShort(hdrSize + 16 + i * 2);
                for (int i = 0; i < textLength; i++)
                    text[i] = (char)getShort(hdrSize + 16 + nameLength * 2 + i * 2);
                double angle = version >= 225 ? getFloat(hdrSize + 16 + nameLength * 2 + textLength * 2) : 0f;
                //Font font = new Font(new string(name), style, size);
                roi.family = new string(name);
                roi.Text = new string(text);
                roi.slant = Cairo.FontSlant.Normal;
                roi.weight = Cairo.FontWeight.Normal;
                roi.fontSize = size;
                /*
                if (roi.subPixel)
                {
                    RectangleD fb = roi.Rect;
                    roi2 = new TextRoi(fb.getX(), fb.getY(), fb.getWidth(), fb.getHeight(), new string(text), font);
                }
                else
                    roi2 = new TextRoi(r.x, r.y, r.width, r.height, new string(text), font);

                roi.strokeColor
                roi2.setFillColor(roi.getFillColor());
                roi2.setName(getRoiName());
                roi2.setJustification(justification);
                roi2.setDrawStringMode(drawStringMode);
                roi2.setAngle(angle);
                return roi2;
                */
            }

            string getRoiName()
            {
                string fileName = name;
                int hdr2Offset = getInt(HEADER2_OFFSET);
                if (hdr2Offset == 0)
                    return fileName;
                int offset = getInt(hdr2Offset + NAME_OFFSET);
                int Length = getInt(hdr2Offset + NAME_LENGTH);
                if (offset == 0 || Length == 0)
                    return fileName;
                if (offset + Length * 2 > size)
                    return fileName;
                char[] namem = new char[Length];
                for (int i = 0; i < Length; i++)
                    namem[i] = (char)getShort(offset + i * 2);
                return new string(namem);
            }

            string getRoiProps()
            {
                int hdr2Offset = getInt(HEADER2_OFFSET);
                if (hdr2Offset == 0)
                    return null;
                int offset = getInt(hdr2Offset + ROI_PROPS_OFFSET);
                int Length = getInt(hdr2Offset + ROI_PROPS_LENGTH);
                if (offset == 0 || Length == 0)
                    return null;
                if (offset + Length * 2 > size)
                    return null;
                char[] props = new char[Length];
                for (int i = 0; i < Length; i++)
                    props[i] = (char)getShort(offset + i * 2);
                return new string(props);
            }

            int[] getPointCounters(int n)
            {
                int hdr2Offset = getInt(HEADER2_OFFSET);
                if (hdr2Offset == 0)
                    return null;
                int offset = getInt(hdr2Offset + COUNTERS_OFFSET);
                if (offset == 0)
                    return null;
                if (offset + n * 4 > data.Length)
                    return null;
                int[] counters = new int[n];
                for (int i = 0; i < n; i++)
                    counters[i] = getInt(offset + i * 4);
                return counters;
            }


            int getByte(int bas)
            {
                return data[bas] & 255;
            }

            int getShort(int bas)
            {
                int b0 = data[bas] & 255;
                int b1 = data[bas + 1] & 255;
                int n = (short)((b0 << 8) + b1);
                if (n < -5000)
                    n = (b0 << 8) + b1; // assume n>32767 and unsigned
                return n;
            }

            int getUnsignedShort(int bas)
            {
                int b0 = data[bas] & 255;
                int b1 = data[bas + 1] & 255;
                return (b0 << 8) + b1;
            }

            int getInt(int bas)
            {
                int b0 = data[bas] & 255;
                int b1 = data[bas + 1] & 255;
                int b2 = data[bas + 2] & 255;
                int b3 = data[bas + 3] & 255;
                return ((b0 << 24) + (b1 << 16) + (b2 << 8) + b3);
            }

            float getFloat(int bas)
            {
                return BitConverter.Int32BitsToSingle(getInt(bas));
            }

            /** Opens an ROI from a byte array. */
            public static BioGTK.ROI openFromByteArray(byte[] bytes)
            {
                BioGTK.ROI roi = null;
                if (bytes == null || bytes.Length == 0)
                    return roi;
                try
                {
                    RoiDecoder decoder = new RoiDecoder(bytes, null);
                    roi = decoder.getRoi();
                }
                catch (IOException e)
                {
                    return null;
                }
                return roi;
            }

        }

        /// The function `GetImageJType` takes in a `ROI` object and returns an integer representing the
        /// type of the ROI in ImageJ.
        /// 
        /// @param ROI The ROI parameter is an object of type ROI, which represents a region of interest
        /// in an image. It has a property called "type" which indicates the type of the ROI.
        /// 
        /// @return The method is returning an integer value that represents the ImageJ type of the
        /// given ROI.
        static int GetImageJType(ROI roi)
        {
            //private int polygon = 0, rect = 1, oval = 2, line = 3, freeline = 4, polyline = 5, noRoi = 6, freehand = 7,
            //    traced = 8, angle = 9, point = 10;
            switch (roi.type)
            {
                case ROI.Type.Rectangle:
                    return 1;
                case ROI.Type.Point:
                    return 10;
                case ROI.Type.Line:
                    return 3;
                case ROI.Type.Polygon:
                    return 0;
                case ROI.Type.Polyline:
                    return 5;
                case ROI.Type.Freeform:
                    return 7;
                case ROI.Type.Ellipse:
                    return 2;
                case ROI.Type.Label:
                default:
                    return 0;
            }
        }

        /// The function "GetPointsXY" takes a ROI object and returns the X and Y coordinates of its
        /// points as arrays.
        /// 
        /// @param ROI The ROI parameter is an object that represents a region of interest. It contains
        /// a collection of points (PointsD) that define the boundary of the region.
        /// @param xp An array of integers representing the x-coordinates of the points in the ROI.
        /// @param yp The `yp` parameter is an output parameter of type `int[]`. It is used to return
        /// the y-coordinates of the points in the `ROI` object.
        static void GetPointsXY(ROI roi, out int[] xp, out int[] yp)
        {
            int[] x = new int[roi.PointsD.Count];
            int[] y = new int[roi.PointsD.Count];
            for (int i = 0; i < roi.PointsD.Count; i++)
            {
                PointD pd = ImageView.SelectedImage.ToImageSpace(roi.PointsD[i]);
                x[i] = (int)pd.X;
                y[i] = (int)pd.Y;
            }
            xp = x;
            yp = y;

        }

        /// The function "GetXY" takes a region of interest (ROI) and returns the corresponding X and Y
        /// coordinates in image space.
        /// 
        /// @param ROI The ROI parameter is of type ROI, which is likely a custom class representing a
        /// region of interest. It contains information about the position and size of the region.
        /// @param x An output parameter that will store the X coordinate of the ROI in image space.
        /// @param y The "y" parameter is an output parameter that will hold the y-coordinate of the ROI
        /// (Region of Interest) after the method is called.
        static void GetXY(ROI roi,out float x, out float y)
        {
            PointD pd = ImageView.SelectedImage.ToImageSpace(new PointD(roi.X,roi.Y));
            x = (float)pd.X;
            y = (float)pd.Y;
        }
        /// The function "GetWH" takes a ROI (region of interest) and returns the width and height of
        /// the ROI in terms of image size.
        /// 
        /// @param ROI The ROI parameter is an object that represents a region of interest. It likely
        /// contains information such as the position (x, y) and size (width, height) of the region.
        /// @param w The width of the ROI (Region of Interest) in the selected image.
        /// @param h The "h" parameter is an output parameter of type float. It is used to store the
        /// height value calculated in the method.
        static void GetWH(ROI roi, out float w, out float h)
        {
            w = (float)ImageView.SelectedImage.ToImageSizeX(roi.W);
            h = (float)ImageView.SelectedImage.ToImageSizeY(roi.H);
        }
        /// The function rightMove takes an integer value and a position as input, and returns the value
        /// after performing a right shift operation by the specified position.
        /// 
        /// @param value The value is an integer that represents the number you want to perform a right
        /// move on.
        /// @param pos The "pos" parameter represents the number of positions to move the bits to the
        /// right.
        /// 
        /// @return the value after performing a right shift operation.
        static int rightMove(int value, int pos)
        {
            if (pos != 0)
            {
                int mask = 0x7fffffff;
                value >>= 1;
                value &= mask;
                value >>= pos - 1;
            }
            return value;
        }
        public class RoiEncoder
        {
            static int HEADER_SIZE = 64;
            static int HEADER2_SIZE = 64;
            static int VERSION = 228; // v1.52t (roi groups, scale stroke width)
            private string path;
            private FileStream f;
            private int polygon = 0, rect = 1, oval = 2, line = 3, freeline = 4, polyline = 5, noRoi = 6, freehand = 7,
                traced = 8, angle = 9, point = 10;
            private byte[] data;
            private string roiName;
            private int roiNameSize;
            private string roiProps;
            private int roiPropsSize;
            private int countersSize;
            private int[] counters;
            private bool subres = true;

            /** Creates an RoiEncoder using the specified path. */
            public RoiEncoder(String path)
            {
                this.path = path;
            }

            /** Creates an RoiEncoder using the specified OutputStream. */
            public RoiEncoder(FileStream f)
            {
                this.f = f;
            }

            /** Saves the specified ROI as a file, returning 'true' if successful. */
            public static bool save(ROI roi, String path)
            {
                RoiEncoder re = new RoiEncoder(path);
                try
                {
                    re.write(roi);
                }
                catch (IOException e)
                {
                    return false;
                }
                return true;
            }

            /** Save the Roi to the file of stream. */
            public void write(ROI roi)
            {
                if (f != null) 
                {
                    write(roi, f);
                } 
                else
                {
                    f = new FileStream(path,FileMode.Create);
                    write(roi, f);
                    f.Close();
                }
            }
            
            /** Saves the specified ROI as a byte array. 
            public static byte[] saveAsByteArray(ROI roi)
            {
                if (roi == null) return null;
                byte[] bytes = null;
                try
                {
                    MemoryStream outs = new MemoryStream(4096);
                    RoiEncoder encoder = new RoiEncoder(path);
                    encoder.write(roi);
			        outs.close();
                    bytes = out.toByteArray();
                }
                catch (IOException e)
                {
                    return null;
                }
                return bytes;
            }
            */
            /// The function "write" saves the properties and coordinates of a region of interest (ROI)
            /// to a file stream.
            /// 
            /// @param ROI The ROI (Region of Interest) is an object that represents a selected area or
            /// shape in an image. It contains information about the type of ROI (e.g., rectangle,
            /// polygon, line), its position and size, and any additional properties or data associated
            /// with it.
            /// @param FileStream FileStream is a class that represents a stream of bytes that can be
            /// written to a file. It is used to write the data of the ROI (Region of Interest) to a
            /// file.
            /// 
            /// @return The code snippet does not have a return statement, so it does not return
            /// anything.
            void write(ROI roi, FileStream f)
            {
                RectangleD r = roi.Rect;
                //if (r.width > 60000 || r.height > 60000 || r.x > 60000 || r.y > 60000)
                //    roi.enableSubPixelResolution();
                //int roiType = GetImageJType(roi);
                int type = GetImageJType(roi);
                int options = 0;
                //if (roi.getScaleStrokeWidth())
                //    options |= RoiDecoder.SCALE_STROKE_WIDTH;
                roiName = roi.Text;
                if (roiName != null)
                    roiNameSize = roiName.Length * 2;
                else
                    roiNameSize = 0;

                roiProps = roi.properties;
                if (roiProps != null)
                    roiPropsSize = roiProps.Length * 2;
                else
                    roiPropsSize = 0;
                /*
                switch (roiType) {
                    case Roi.POLYGON: type = polygon; break;
                    case Roi.FREEROI: type = freehand; break;
                    case Roi.TRACED_ROI: type = traced; break;
                    case Roi.OVAL: type = oval; break;
                    case Roi.LINE: type = line; break;
                    case Roi.POLYLINE: type = polyline; break;
                    case Roi.FREELINE: type = freeline; break;
                    case Roi.ANGLE: type = angle; break;
                    case Roi.COMPOSITE: type = rect; break; // shape array size (36-39) will be >0 to indicate composite type
                    case Roi.POINT: type = point; break;
                    default: type = rect; break;
                }
                */
                /*
                if (roiType == Roi.COMPOSITE) {
                    saveShapeRoi(roi, type, f, options);
                    return;
                }
                */
                int n = 0;
                int[]
                x = null, y = null;
                float[]
                xf = null, yf = null;
                int floatSize = 0;
                //if (roi instanceof PolygonRoi) {
                //PolygonRoi proi = (PolygonRoi)roi;
                //Polygon p = proi.getNonSplineCoordinates();
                n = roi.PointsD.Count; //p.npoints;
                //x = p.xpoints;
                //y = p.ypoints;
                GetPointsXY(roi, out x, out y);
                if (subres)
                {
                    /*
                    if (proi.isSplineFit())
                        fp = proi.getNonSplineFloatPolygon();
                    else
                        fp = roi.getFloatPolygon();
                    if (n == fp.npoints)
                    {
                        options |= RoiDecoder.SUB_PIXEL_RESOLUTION;
                        if (roi.getDrawOffset())
                            options |= RoiDecoder.DRAW_OFFSET;
                        xf = fp.xpoints;
                        yf = fp.ypoints;
                        floatSize = n * 8;
                    }
                    */
                }
                

                countersSize = 0;
                /*
                if (roi instanceof PointRoi) {
                    counters = ((PointRoi)roi).getCounters();
                    if (counters != null && counters.length >= n)
                        countersSize = n * 4;
                }
                */
                data = new byte[HEADER_SIZE + HEADER2_SIZE + n * 4 + floatSize + roiNameSize + roiPropsSize + countersSize];
                data[0] = 73; data[1] = 111; data[2] = 117; data[3] = 116; // "Iout"
                putShort(RoiDecoder.VERSION_OFFSET, VERSION);
                data[RoiDecoder.TYPE] = (byte)type;
                float px, py, pw, ph;
                GetXY(roi, out px, out py);
                GetWH(roi, out pw, out ph);
                putShort(RoiDecoder.TOP, (int)py);
                putShort(RoiDecoder.LEFT, (int)px);
                putShort(RoiDecoder.BOTTOM, (int)(py + ph));
                putShort(RoiDecoder.RIGHT, (int)(px + pw));
                if (subres && (type == rect || type == oval))
                {
                    //FloatPolygon p = null;
                    /*
                    if (roi instanceof OvalRoi)
				            p = ((OvalRoi)roi).getFloatPolygon4();
                        else
                    {
                        int d = roi.getCornerDiameter();
                        if (d > 0)
                        {
                            roi.setCornerDiameter(0);
                            p = roi.getFloatPolygon();
                            roi.setCornerDiameter(d);
                        }
                        else
                            p = roi.getFloatPolygon();
                    }
                        */
                    if (roi.PointsD.Count == 4)
                    {
                        putFloat(RoiDecoder.XD, (float)roi.PointsImage[0].X);
                        putFloat(RoiDecoder.YD, (float)roi.PointsImage[0].Y);
                        //putFloat(RoiDecoder.WIDTHD, p.xpoints[1] - roi.PointsD[0]);
                        //putFloat(RoiDecoder.HEIGHTD, p.ypoints[2] - p.ypoints[1]);
                        putFloat(RoiDecoder.WIDTHD, (float)roi.PointsImage[1].X - (float)roi.PointsImage[0].X);
                        putFloat(RoiDecoder.HEIGHTD, (float)roi.PointsImage[2].Y - (float)roi.PointsImage[1].Y);
                        options |= RoiDecoder.SUB_PIXEL_RESOLUTION;
                        putShort(RoiDecoder.OPTIONS, options);
                    }
                }
                if (n > 65535 && type != point)
                {
                    if (type == polygon || type == freehand || type == traced)
                    {
                        //String name = roi.Text;
                        //roi = new ShapeRoi(roi);
                        //if (name != null) roi.setName(name);
                        saveShapeRoi(roi, rect, f, options);
                        return;
                    }
                    //ij.IJ.beep();
                    //ij.IJ.log("Non-polygonal selections with more than 65k points cannot be saved.");
                    n = 65535;
                }
                if (type == point && n > 65535)
                    putInt(RoiDecoder.SIZE, n);
                else
                    putShort(RoiDecoder.N_COORDINATES, n);
                putInt(RoiDecoder.POSITION, roi.coord.Z);

                /*
                if (type == rect)
                {
                    int arcSize = roi.getCornerDiameter();
                    if (arcSize > 0)
                        putShort(RoiDecoder.ROUNDED_RECT_ARC_SIZE, arcSize);
                }
                */

                if(type == line) //(roi instanceof Line) 
                {
                    //Line line = (Line)roi;
                    putFloat(RoiDecoder.X1, (float)roi.PointsImage[0].X);
                    putFloat(RoiDecoder.Y1, (float)roi.PointsImage[0].Y);
                    putFloat(RoiDecoder.X2, (float)roi.PointsImage[1].X);
                    putFloat(RoiDecoder.Y2, (float)roi.PointsImage[1].Y);
                    /*
                    if (roi instanceof Arrow) {
                        putShort(RoiDecoder.SUBTYPE, RoiDecoder.ARROW);
                        if (((Arrow)roi).getDoubleHeaded())
                            options |= RoiDecoder.DOUBLE_HEADED;
                        if (((Arrow)roi).getOutline())
                            options |= RoiDecoder.OUTLINE;
                        putShort(RoiDecoder.OPTIONS, options);
                        putByte(RoiDecoder.ARROW_STYLE, ((Arrow)roi).getStyle());
                        putByte(RoiDecoder.ARROW_HEAD_SIZE, (int)((Arrow)roi).getHeadSize());
                    } else
                    {
                        if (roi.getDrawOffset())
                            options |= RoiDecoder.SUB_PIXEL_RESOLUTION + RoiDecoder.DRAW_OFFSET;
                    }
                    */
                }

                if (type == point) {
                    //PointRoi point = (PointRoi)roi;
                    putByte(RoiDecoder.POINT_TYPE, 1);//point.getPointType());
                    putShort(RoiDecoder.STROKE_WIDTH, (int)roi.strokeWidth);
                    /*
                    if (point.getShowLabels())
                        options |= RoiDecoder.SHOW_LABELS;
                    if (point.promptBeforeDeleting())
                        options |= RoiDecoder.PROMPT_BEFORE_DELETING;
                    */
                }

                if (type == oval) 
                {
                    /*
                    double[] p = null;
                    if (roi instanceof RotatedRectRoi) {
                        putShort(RoiDecoder.SUBTYPE, RoiDecoder.ROTATED_RECT);
                        p = ((RotatedRectRoi)roi).getParams();
                    } else
                    {
                        */
                        putShort(RoiDecoder.SUBTYPE, RoiDecoder.ELLIPSE);
                    //p = ((EllipseRoi)roi).getParams();
                    //}
                    float fx, fy, fw, fh;
                    GetXY(roi, out fx, out fy);
                    GetWH(roi, out fw, out fh);
                    putFloat(RoiDecoder.X1, fx);
                    putFloat(RoiDecoder.Y1, fy);
                    putFloat(RoiDecoder.X2, fw);
                    putFloat(RoiDecoder.Y2, fh);
                    //putFloat(RoiDecoder.FLOAT_PARAM, (float)p[4]);
                }

                // save stroke width, stroke color and fill color (1.43i or later)
                if (VERSION >= 218)
                {
                    saveStrokeWidthAndColor(roi);
                    /*
                    if ((roi instanceof PolygonRoi) && ((PolygonRoi)roi).isSplineFit()) {
                        options |= RoiDecoder.SPLINE_FIT;
                        putShort(RoiDecoder.OPTIONS, options);
                    }
                    */
                }

                if (roi.type == ROI.Type.Label)//(n == 0 && roi instanceof TextRoi)
			            saveTextRoi(roi);
                /*
                else if (n == 0 && roi instanceof ImageRoi)
			        options = saveImageRoi((ImageRoi)roi, options);
                */
                //else
                putHeader2(roi, HEADER_SIZE + n * 4 + floatSize);

                if (n > 0)
                {
                    int base1 = 64;
                    int base2 = base1 + 2 * n;
                    for (int i = 0; i < n; i++)
                    {
                        putShort(base1 + i * 2, (int)(x[i] - px));
                        putShort(base2 + i * 2, (int)(y[i] - py));
                    }
                    if (xf != null)
                    {
                        base1 = 64 + 4 * n;
                        base2 = base1 + 4 * n;
                        for (int i = 0; i < n; i++)
                        {
                            putFloat(base1 + i * 4, xf[i]);
                            putFloat(base2 + i * 4, yf[i]);
                        }
                    }
                }

                //saveOverlayOptions(roi, options);
                f.Write(data);
            }

            /// The function saves the stroke width and color of a region of interest (ROI) in a
            /// specific format.
            /// 
            /// @param ROI The ROI parameter is an object that represents a region of interest. It
            /// contains information about the stroke width, stroke color, and fill color of the region.
            void saveStrokeWidthAndColor(ROI roi)
            {
                //BasicStroke stroke = roi.getStroke();
                //if (stroke != null)
                    putShort(RoiDecoder.STROKE_WIDTH, (int)roi.strokeWidth);
                Color strokeColor = roi.strokeColor;
                int intColor = (strokeColor.R << 16) | (strokeColor.G << 8) | (strokeColor.B);
                putInt(RoiDecoder.STROKE_COLOR, 0);
                Color fillColor = roi.fillColor;
                int intFillColor = (fillColor.R << 16) | (fillColor.G << 8) | (fillColor.B);
                putInt(RoiDecoder.FILL_COLOR, 0);
            }

            /// The function `saveShapeRoi` saves a shape region of interest (ROI) to a file stream in a
            /// specific format.
            /// 
            /// @param ROI The `ROI` parameter is an object that represents a region of interest. It
            /// contains information about the shape and position of the region.
            /// @param type The "type" parameter is an integer that represents the type of the ROI. It
            /// is used to determine how the ROI should be saved and interpreted.
            /// @param FileStream FileStream is a class in C# that represents a stream of bytes to read
            /// from or write to a file. It is used to handle file input/output operations. In the given
            /// code, it is used to write the data to a file.
            /// @param options The "options" parameter is an integer that represents various options for
            /// saving the shape ROI. It is used to specify additional information or settings related
            /// to the saving process. The specific meaning and usage of the options parameter would
            /// depend on the context and the implementation of the saveShapeRoi() method.
            void saveShapeRoi(ROI roi, int type, FileStream f, int options)
            {
                //float[] shapeArray = ((ShapeRoi)roi).getShapeAsArray();
                //if (shapeArray == null) return;
                //BufferedOutputStream bout = new BufferedOutputStream(f);

                data = new byte[HEADER_SIZE + HEADER2_SIZE + roiNameSize + roiPropsSize];//shapeArray.length * 4 + roiNameSize + roiPropsSize];
                data[0] = 73; data[1] = 111; data[2] = 117; data[3] = 116; // "Iout"

                putShort(RoiDecoder.VERSION_OFFSET, VERSION);
                data[RoiDecoder.TYPE] = (byte)type;

                float x, y, w, h;
                GetXY(roi, out x,out y);
                GetWH(roi, out w, out h);
                putShort(RoiDecoder.TOP, (int)y);
                putShort(RoiDecoder.LEFT, (int)x);
                putShort(RoiDecoder.BOTTOM, (int)(y + h));
                putShort(RoiDecoder.RIGHT, (int)(x + w));
                putInt(RoiDecoder.POSITION, roi.coord.Z);
                ///putShort(16, n);
                //putInt(36, shapeArray.Length); // non-zero segment count indicate composite type
                if (VERSION >= 218)
                    saveStrokeWidthAndColor(roi);
                //saveOverlayOptions(roi, options);

                // handle the actual data: data are stored segment-wise, i.e.,
                // the type of the segment followed by 0-6 control point coordinates.
                /*
                int bas = 64;
                for (int i = 0; i < shapeArray.Length; i++)
                {
                    putFloat(bas, shapeArray[i]);
                    bas += 4;
                }
                */
                int hdr2Offset = HEADER_SIZE;// + shapeArray.Length * 4;
                //ij.IJ.log("saveShapeRoi: "+HEADER_SIZE+"  "+shapeArray.length);
                putHeader2(roi, hdr2Offset);
                f.Write(data, 0, data.Length);
                f.Flush();
            }

            /*
            void saveOverlayOptions(ROI roi, int options)
            {
                Overlay proto = roi.getPrototypeOverlay();
                if (proto.getDrawLabels())
                    options |= RoiDecoder.OVERLAY_LABELS;
                if (proto.getDrawNames())
                    options |= RoiDecoder.OVERLAY_NAMES;
                if (proto.getDrawBackgrounds())
                    options |= RoiDecoder.OVERLAY_BACKGROUNDS;
                Font font = proto.getLabelFont();
                if (font != null && font.getStyle() == Font.BOLD)
                    options |= RoiDecoder.OVERLAY_BOLD;
                if (proto.scalableLabels())
                    options |= RoiDecoder.SCALE_LABELS;
                putShort(RoiDecoder.OPTIONS, options);
            }
            */
            /// The function `saveTextRoi` saves the properties of a text region of interest (ROI) into
            /// a byte array.
            /// 
            /// @param ROI The `ROI` parameter is an object that represents a region of interest. It
            /// contains information about the font, size, style, text, and other properties of the
            /// region of interest.
            void saveTextRoi(ROI roi)
            {
                //Font font = roi.getCurrentFont();
                string fontName = roi.family;
                int size = (int)roi.fontSize;
                int drawStringMode = 0; //roi.getDrawStringMode() ? 1024 : 0;
                int style = 0;//font.getStyle() + roi.getJustification() * 256 + drawStringMode;
                string text = roi.roiName;
                float angle = 0;
                int angleLength = 4;
                int fontNameLength = fontName.Length;
                int textLength = text.Length;
                int textRoiDataLength = 16 + fontNameLength * 2 + textLength * 2 + angleLength;
                byte[] data2 = new byte[HEADER_SIZE + HEADER2_SIZE + textRoiDataLength + roiNameSize + roiPropsSize];
                Array.Copy(data, 0, data2, 0, HEADER_SIZE);
                data = data2;
                putShort(RoiDecoder.SUBTYPE, RoiDecoder.TEXT);
                putInt(HEADER_SIZE, size);
                putInt(HEADER_SIZE + 4, style);
                putInt(HEADER_SIZE + 8, fontNameLength);
                putInt(HEADER_SIZE + 12, textLength);
                for (int i = 0; i < fontNameLength; i++)
                    putShort(HEADER_SIZE + 16 + i * 2, fontName.ElementAt(i));
                for (int i = 0; i < textLength; i++)
                    putShort(HEADER_SIZE + 16 + fontNameLength * 2 + i * 2, text.ElementAt(i));
                int hdr2Offset = HEADER_SIZE + textRoiDataLength;
                //ij.IJ.log("saveTextRoi: "+HEADER_SIZE+"  "+textRoiDataLength+"  "+fontNameLength+"  "+textLength);
                putFloat(hdr2Offset - angleLength, angle);
                putHeader2(roi, hdr2Offset);
            }
            /*
            private int saveImageRoi(ROI roi, int options)
            {
                byte[] bytes = roi.getSerializedImage();
                int imageSize = bytes.length;
                byte[] data2 = new byte[HEADER_SIZE + HEADER2_SIZE + imageSize + roiNameSize + roiPropsSize];
                System.arraycopy(data, 0, data2, 0, HEADER_SIZE);
                data = data2;
                putShort(RoiDecoder.SUBTYPE, RoiDecoder.IMAGE);
                for (int i = 0; i < imageSize; i++)
                    putByte(HEADER_SIZE + i, bytes[i] & 255);
                int hdr2Offset = HEADER_SIZE + imageSize;
                double opacity = roi.getOpacity();
                putByte(hdr2Offset + RoiDecoder.IMAGE_OPACITY, (int)(opacity * 255.0));
                putInt(hdr2Offset + RoiDecoder.IMAGE_SIZE, imageSize);
                if (roi.getZeroTransparent())
                    options |= RoiDecoder.ZERO_TRANSPARENT;
                putHeader2(roi, hdr2Offset);
                return options;
            }
            */
            /// The function "putHeader2" is used to set various properties of a Region of Interest
            /// (ROI) object, such as its position, label color, font size, stroke width, and group.
            /// 
            /// @param ROI The ROI parameter is an object of type ROI, which represents a region of
            /// interest in an image. It contains information about the position and size of the ROI, as
            /// well as other properties such as the stroke color, stroke width, and font size.
            /// @param hdr2Offset The `hdr2Offset` parameter is an integer that represents the offset
            /// position in the header where the information for the second header should be stored.
            void putHeader2(ROI roi, int hdr2Offset)
            {
                //ij.IJ.log("putHeader2: "+hdr2Offset+" "+roiNameSize+"  "+roiName);
                putInt(RoiDecoder.HEADER2_OFFSET, hdr2Offset);
                putInt(hdr2Offset + RoiDecoder.C_POSITION, roi.coord.C + 1);
                putInt(hdr2Offset + RoiDecoder.Z_POSITION, roi.coord.Z + 1);
                putInt(hdr2Offset + RoiDecoder.T_POSITION, roi.coord.T + 1);
                //Overlay proto = roi.getPrototypeOverlay();
                Color overlayLabelColor = roi.strokeColor; //proto.getLabelColor();
                int intColor = (overlayLabelColor.R << 16) | (overlayLabelColor.G << 8) | (overlayLabelColor.B);
                //if (overlayLabelColor != null)
                putInt(hdr2Offset + RoiDecoder.OVERLAY_LABEL_COLOR, 0);
                //Font font = proto.getLabelFont();
                //if (font != null)
                    putShort(hdr2Offset + RoiDecoder.OVERLAY_FONT_SIZE, (int)roi.fontSize);
                if (roiNameSize > 0)
                    putName(roi, hdr2Offset);
                double strokeWidth = roi.strokeWidth;
                //if (roi.getStroke() == null)
                //    strokeWidth = 0.0;
                putFloat(hdr2Offset + RoiDecoder.FLOAT_STROKE_WIDTH, (float)strokeWidth);
                if (roiPropsSize > 0)
                    putProps(roi, hdr2Offset);
                if (countersSize > 0)
                    putPointCounters(roi, hdr2Offset);
                putByte(hdr2Offset + RoiDecoder.GROUP, roi.serie);//roi.getGroup());
            }

            /// The function "putName" takes a ROI object and an offset value, and sets the name and
            /// length of the ROI in the header.
            /// 
            /// @param ROI The ROI parameter is an object of type ROI. It is used to access the
            /// properties and methods of the ROI object within the putName method.
            /// @param hdr2Offset The `hdr2Offset` parameter is an integer value representing the offset
            /// of the header2 in a data structure or file. It is used to calculate the offset for
            /// storing the name of the ROI (Region of Interest) in the data structure or file.
            void putName(ROI roi, int hdr2Offset)
            {
                int offset = hdr2Offset + HEADER2_SIZE;
                int nameLength = roiNameSize / 2;
                putInt(hdr2Offset + RoiDecoder.NAME_OFFSET, offset);
                putInt(hdr2Offset + RoiDecoder.NAME_LENGTH, nameLength);
                for (int i = 0; i < nameLength; i++)
                    putShort(offset + i * 2, roiName.ElementAt(i));
            }

            /// The function "putProps" takes a ROI object and an offset value, and updates the ROI
            /// properties in the header based on the given offset and ROI object.
            /// 
            /// @param ROI The ROI parameter is an object of type ROI. It is used to pass information
            /// about a region of interest.
            /// @param hdr2Offset The `hdr2Offset` parameter is an integer value representing the offset
            /// of the header2 in memory. It is used to calculate the offset for storing the ROI
            /// properties.
            void putProps(ROI roi, int hdr2Offset)
            {
                int offset = hdr2Offset + HEADER2_SIZE + roiNameSize;
                int roiPropsLength = roiPropsSize / 2;
                putInt(hdr2Offset + RoiDecoder.ROI_PROPS_OFFSET, offset);
                putInt(hdr2Offset + RoiDecoder.ROI_PROPS_LENGTH, roiPropsLength);
                for (int i = 0; i < roiPropsLength; i++)
                    putShort(offset + i * 2, roiProps.ElementAt(i));
            }

            /// The function "putPointCounters" updates the counters in a region of interest (ROI) by
            /// copying the values from an array to a specific offset in memory.
            /// 
            /// @param ROI The ROI parameter is an object of type ROI, which likely represents a region
            /// of interest in an image. It may contain information such as the coordinates, size, and
            /// properties of the region.
            /// @param hdr2Offset The `hdr2Offset` parameter is the offset value for the second header
            /// in the data structure. It is used to calculate the position where the point counters
            /// will be stored.
            void putPointCounters(ROI roi, int hdr2Offset)
            {
                int offset = hdr2Offset + HEADER2_SIZE + roiNameSize + roiPropsSize;
                putInt(hdr2Offset + RoiDecoder.COUNTERS_OFFSET, offset);
                for (int i = 0; i < countersSize / 4; i++)
                    putInt(offset + i * 4, counters[i]);
                countersSize = 0;
            }

            /// The function "putByte" assigns a byte value to a specific index in an array.
            /// 
            /// @param bas The parameter "bas" is an integer that represents the base address or index
            /// of the array "data" where the byte value will be stored.
            /// @param v The parameter "v" is an integer value that represents the value to be stored in
            /// the byte array.
            void putByte(int bas, int v)
            {
                data[bas] = (byte)v;
            }

            /// The function "putShort" takes two integer parameters, "bas" and "v", and stores the
            /// value of "v" in the "data" array at index "bas" and "bas + 1" after performing a right
            /// shift operation on "v" by 8 bits.
            /// 
            /// @param bas The parameter "bas" represents the base index in the "data" array where the
            /// short value will be stored.
            /// @param v The parameter "v" is an integer value that represents the value to be stored in
            /// the data array.
            void putShort(int bas, int v)
            {
                //data[bas] = (byte)(v >>> 8);
                //data[bas] = (byte)UnsignedRightShift(v, 8);
                data[bas] = (byte)rightMove(v, 8);
                data[bas + 1] = (byte)v;
            }

            /// The function "putFloat" takes an integer and a float as input and converts the float
            /// into its binary representation, storing it in a byte array.
            /// 
            /// @param bas The parameter "bas" represents the base index in the "data" array where the
            /// float value will be stored.
            /// @param v The parameter "v" is a float value that needs to be converted and stored in the
            /// "data" array.
            void putFloat(int bas, float v)
            {
                int tmp = BitConverter.SingleToInt32Bits(v);//Float.floatToIntBits(v);
                data[bas] = (byte)(tmp >> 24);
                data[bas + 1] = (byte)(tmp >> 16);
                data[bas + 2] = (byte)(tmp >> 8);
                data[bas + 3] = (byte)tmp;
            }

            /// The function "putInt" takes two integer parameters and stores the bytes of the second
            /// integer in a byte array starting at the specified index in big-endian order.
            /// 
            /// @param bas The parameter "bas" represents the base index in the "data" array where the
            /// integer value will be stored.
            /// @param i The parameter "i" is an integer value that needs to be stored in the "data"
            /// array.
            void putInt(int bas, int i)
            {
                data[bas] = (byte)(i >> 24);
                data[bas + 1] = (byte)(i >> 16);
                data[bas + 2] = (byte)(i >> 8);
                data[bas + 3] = (byte)i;
            }
        }

    }
}
