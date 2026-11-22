using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AForge;
using Gtk;

namespace BioGTK.ML
{
    internal static class PixelClassifierMenu
    {
        public static void AddTo(MenuItem run, Window parent)
        {
            var menu = (Menu)run.Submenu ?? new Menu();
            run.Submenu = menu;
            var train = new MenuItem("Train Pixel Classifier…");
            var apply = new MenuItem("Apply Pixel Classifier…");
            train.Activated += (_, _) => Open(parent, train: true);
            apply.Activated += (_, _) => Open(parent, train: false);
            menu.Append(train);
            menu.Append(apply);
            menu.ShowAll();
        }

        private static void Open(Window parent, bool train)
        {
            try
            {
                var view = App.viewer;
                var image = ImageView.SelectedImage;
                if (image == null || image.isPyramidal || image.Buffers.Count == 0)
                    throw new ArgumentException("Open a non-pyramidal image and select the plane to use.");
                if (!(image.PhysicalSizeX > 0 && image.PhysicalSizeY > 0) ||
                    !double.IsFinite(image.PhysicalSizeX) || !double.IsFinite(image.PhysicalSizeY))
                    throw new ArgumentException("The image needs positive pixel sizes to position the annotations.");
                var coord = image.Coordinate;
                var plane = new PixelPlane(ImageView.SelectedBuffer);
                if (train)
                {
                    var regions = ChooseRegions(parent, image, coord, plane);
                    if (regions == null)
                        return;
                    Run(parent, "Training pixel classifier…", token => PixelClassifier.Train(plane, regions, token), model =>
                    {
                        var path = ChooseModel(parent, save: true);
                        if (path != null)
                            model.Save(path);
                    });
                }
                else
                {
                    var path = ChooseModel(parent, save: false);
                    if (path == null)
                        return;
                    Run(parent, "Classifying pixels…", token => PixelClassifier.Load(path).Predict(plane, token), mask =>
                    {
                        if (Array.IndexOf(mask, (byte)255) < 0)
                        {
                            Message(parent, MessageType.Info, "The classifier found no foreground pixels on this plane.");
                            return;
                        }
                        var roi = ROI.CreateMask(coord, mask, plane.Width, plane.Height,
                            new PointD(image.StageSizeX, image.StageSizeY), image.PhysicalSizeX, image.PhysicalSizeY);
                        roi.BoundingBox = new RectangleD(roi.X, roi.Y, roi.W, roi.H);
                        roi.serie = image.series;
                        roi.roiName = Path.GetFileNameWithoutExtension(path);
                        roi.Text = roi.roiName;
                        roi.fillColor = roi.strokeColor = AForge.Color.FromArgb(50, 210, 115);
                        image.Annotations.Add(roi);
                        view.ShowMasks = true;
                        view.UpdateView();
                        App.roiManager?.UpdateAnnotationList();
                    });
                }
            }
            catch (Exception error)
            {
                Message(parent, MessageType.Error, error.Message);
            }
        }

        private static List<PixelRegion> ChooseRegions(Window parent, BioImage image, ZCT coord, PixelPlane plane)
        {
            var rectangles = image.Annotations.Where(roi => roi?.type == ROI.Type.Rectangle && roi.coord == coord).ToList();
            if (rectangles.Count < 2)
                throw new ArgumentException("Draw rectangles inside representative foreground and background areas on the current plane, then choose Train Pixel Classifier again.");

            var dialog = new Dialog("Train Pixel Classifier", parent, DialogFlags.Modal,
                "Cancel", ResponseType.Cancel, "Train", ResponseType.Ok);
            try
            {
                dialog.ContentArea.BorderWidth = 12;
                dialog.ContentArea.Spacing = 8;
                dialog.ContentArea.PackStart(new Label($"Assign rectangles on {image.Filename} (Z {coord.Z}, C {coord.C}, T {coord.T})."), false, false, 0);
                var grid = new Grid { RowSpacing = 4, ColumnSpacing = 12 };
                var choices = new List<(PixelRegion Region, ComboBoxText Class)>();
                foreach (var roi in rectangles)
                {
                    var bounds = roi.BoundingBox;
                    var start = image.ToImageSpace(new PointD(bounds.X, bounds.Y));
                    var end = image.ToImageSpace(new PointD(bounds.X + bounds.W, bounds.Y + bounds.H));
                    // A pixel belongs to the rectangle when its center lies inside the half-open bounds.
                    int Edge(double value, int limit) => (int)Math.Clamp(Math.Ceiling(value - 0.5), 0, limit);
                    var region = new PixelRegion(Edge(start.X, plane.Width), Edge(start.Y, plane.Height),
                        Edge(end.X, plane.Width), Edge(end.Y, plane.Height), false);
                    if (region.Right <= region.Left || region.Bottom <= region.Top)
                        continue;
                    string name = string.IsNullOrWhiteSpace(roi.Text) ? $"Rectangle {choices.Count + 1}" : roi.Text;
                    var location = new Label($"{name}   ({region.Left}, {region.Top})   {region.Right - region.Left} × {region.Bottom - region.Top} px") { Xalign = 0 };
                    var choice = new ComboBoxText();
                    choice.AppendText("Ignore");
                    choice.AppendText("Foreground");
                    choice.AppendText("Background");
                    choice.Active = name.Trim().ToLowerInvariant() switch { "foreground" => 1, "background" => 2, _ => 0 };
                    grid.Attach(location, 0, choices.Count, 1, 1);
                    grid.Attach(choice, 1, choices.Count, 1, 1);
                    choices.Add((region, choice));
                }
                if (choices.Count < 2)
                    throw new ArgumentException("At least two rectangles must contain pixels on the current plane.");
                void UpdateTrainButton() => dialog.SetResponseSensitive(ResponseType.Ok,
                    choices.Any(c => c.Class.Active == 1) && choices.Any(c => c.Class.Active == 2));
                foreach (var choice in choices)
                    choice.Class.Changed += (_, _) => UpdateTrainButton();
                UpdateTrainButton();
                var scroll = new ScrolledWindow();
                scroll.SetSizeRequest(440, Math.Min(280, choices.Count * 42 + 10));
                scroll.Add(grid);
                dialog.ContentArea.PackStart(scroll, true, true, 0);
                dialog.ShowAll();
                if (dialog.Run() != (int)ResponseType.Ok)
                    return null;
                return choices.Where(c => c.Class.Active != 0)
                    .Select(c => c.Region with { Foreground = c.Class.Active == 1 }).ToList();
            }
            finally
            {
                dialog.Destroy();
            }
        }

        private static string ChooseModel(Window parent, bool save)
        {
            var dialog = new FileChooserDialog(save ? "Save Pixel Classifier" : "Apply Pixel Classifier",
                parent, save ? FileChooserAction.Save : FileChooserAction.Open,
                "Cancel", ResponseType.Cancel, save ? "Save" : "Apply", ResponseType.Accept);
            try
            {
                var filter = new FileFilter { Name = "ML.NET models (*.zip)" };
                filter.AddPattern("*.zip");
                dialog.AddFilter(filter);
                var all = new FileFilter { Name = "All files" };
                all.AddPattern("*");
                dialog.AddFilter(all);
                if (save)
                {
                    dialog.CurrentName = "pixel-classifier.zip";
                    dialog.DoOverwriteConfirmation = true;
                }
                return dialog.Run() == (int)ResponseType.Accept ? dialog.Filename : null;
            }
            finally
            {
                dialog.Destroy();
            }
        }

        private static void Run<T>(Window parent, string title, Func<CancellationToken, T> work, Action<T> done)
        {
            var cancellation = new CancellationTokenSource();
            var progress = new MessageDialog(parent, DialogFlags.Modal, MessageType.Info, ButtonsType.Cancel, "{0}", title);
            progress.Response += (_, _) =>
            {
                cancellation.Cancel();
                progress.SetResponseSensitive(ResponseType.Cancel, false);
            };
            progress.DeleteEvent += (_, args) => { args.RetVal = true; cancellation.Cancel(); };
            progress.ShowAll();
            var task = Task.Run(() => work(cancellation.Token));
            _ = task.ContinueWith(completed => Application.Invoke(delegate
            {
                progress.Destroy();
                try
                {
                    var result = completed.GetAwaiter().GetResult();
                    if (!cancellation.IsCancellationRequested)
                        done(result);
                }
                catch (Exception error)
                {
                    if (!cancellation.IsCancellationRequested)
                        Message(parent, MessageType.Error, error.Message);
                }
                finally
                {
                    cancellation.Dispose();
                }
            }), TaskScheduler.Default);
        }

        private static void Message(Window parent, MessageType type, string text)
        {
            var dialog = new MessageDialog(parent, DialogFlags.Modal, type, ButtonsType.Ok, "{0}", text);
            try { dialog.Run(); }
            finally { dialog.Destroy(); }
        }
    }
}
