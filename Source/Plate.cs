using AForge;
using Bio;
using Cairo;
using Gdk;
using Gtk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Key = Gdk.Key;

namespace BioGTK
{
    public class PlateTool : Gtk.Window
    {
        private const int OuterMargin = 18;
        private const int HeaderMargin = 34;
        private const int WellGap = 10;
        private const int WellSize = 112;
        private const int WellInnerPad = 8;
        private const int SampleGap = 4;
        private const int SampleMinSize = 16;

        private readonly Builder _builder;
        private readonly Dictionary<(int row, int col), BioImage.WellPlate.Well> _wellsByCell = new();
        private readonly List<BioImage.WellPlate.Well> _wells = new();
        private BioImage.WellPlate.Well _selectedWell;
        private BioImage.WellPlate.Well.Sample _selectedSample;
        private int _minRow;
        private int _minCol;
        private int _maxRow;
        private int _maxCol;
        private int _contentWidth;
        private int _contentHeight;

#pragma warning disable 649
        [Builder.Object]
        private Gtk.DrawingArea pictureBox;
#pragma warning restore 649

        public static PlateTool Create()
        {
            if (ImageView.SelectedImage?.Plate == null || ImageView.SelectedImage.Type != BioImage.ImageType.well)
            {
                var dialog = new MessageDialog(
                    null,
                    DialogFlags.Modal,
                    MessageType.Info,
                    ButtonsType.Ok,
                    "The current image does not contain a well plate.");
                dialog.Run();
                dialog.Destroy();
                return null;
            }

            string gladePath = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(Environment.ProcessPath) ?? string.Empty, "Glade", "Plate.glade");
            Builder builder = new Builder(new FileStream(gladePath, FileMode.Open, FileAccess.Read, FileShare.Read));
            PlateTool v = new PlateTool(builder, builder.GetObject("plate").Handle);
            v.ShowAll();
            return v;
        }

        protected PlateTool(Builder builder, IntPtr handle) : base(handle)
        {
            _builder = builder;
            builder.Autoconnect(this);

            Title = BuildTitle();
            SetDefaultSize(1200, 900);

            pictureBox.CanFocus = true;
            pictureBox.Drawn += PictureBox_Drawn;
            pictureBox.ButtonPressEvent += PictureBox_ButtonPressEvent;
            pictureBox.KeyPressEvent += PictureBox_KeyPressEvent;
            pictureBox.ScrollEvent += PictureBox_ScrollEvent;
            pictureBox.AddEvents((int)(EventMask.ButtonPressMask
                | EventMask.ButtonReleaseMask
                | EventMask.KeyPressMask
                | EventMask.PointerMotionMask
                | EventMask.ScrollMask));

            App.ApplyStyles(this);
            RebuildPlateLayout();
        }

        private string BuildTitle()
        {
            var plate = ImageView.SelectedImage?.Plate;
            if (plate == null)
                return "Plate";

            if (!string.IsNullOrWhiteSpace(plate.Name))
                return $"Plate - {plate.Name}";

            if (!string.IsNullOrWhiteSpace(plate.ID))
                return $"Plate - {plate.ID}";

            return "Plate";
        }

        private void RebuildPlateLayout()
        {
            _wells.Clear();
            _wellsByCell.Clear();

            var plate = ImageView.SelectedImage?.Plate;
            if (plate == null || plate.Wells == null || plate.Wells.Count == 0)
            {
                _minRow = _minCol = 0;
                _maxRow = _maxCol = 0;
                _contentWidth = _contentHeight = 0;
                pictureBox.SetSizeRequest(400, 300);
                pictureBox.QueueDraw();
                return;
            }

            _wells.AddRange(plate.Wells);

            _minRow = _wells.Min(w => w.Row);
            _maxRow = _wells.Max(w => w.Row);
            _minCol = _wells.Min(w => w.Column);
            _maxCol = _wells.Max(w => w.Column);

            int cols = Math.Max(1, _maxCol - _minCol + 1);
            int rows = Math.Max(1, _maxRow - _minRow + 1);

            _contentWidth = OuterMargin + HeaderMargin + (cols * WellSize) + ((cols - 1) * WellGap) + OuterMargin;
            _contentHeight = OuterMargin + HeaderMargin + (rows * WellSize) + ((rows - 1) * WellGap) + OuterMargin;

            foreach (var well in _wells)
                _wellsByCell[(well.Row - _minRow, well.Column - _minCol)] = well;

            pictureBox.SetSizeRequest(_contentWidth, _contentHeight);
            SyncSelectionFromImage();
            Title = BuildTitle();
            pictureBox.QueueDraw();
        }

        private void SyncSelectionFromImage()
        {
            _selectedWell = null;
            _selectedSample = null;

            int activeIndex = ImageView.SelectedImage?.WellIndex ?? -1;
            if (activeIndex < 0)
                return;

            foreach (var well in _wells)
            {
                foreach (var sample in well.Samples)
                {
                    if (sample.Index == activeIndex)
                    {
                        _selectedWell = well;
                        _selectedSample = sample;
                        return;
                    }
                }
            }
        }

        private void PictureBox_ButtonPressEvent(object o, ButtonPressEventArgs args)
        {
            if (args.Event.Button != 1 || ImageView.SelectedImage?.Plate == null)
                return;

            pictureBox.GrabFocus();

            if (TryHitTest((int)args.Event.X, (int)args.Event.Y, out var well, out var sample))
            {
                SelectSample(well, sample);
                pictureBox.QueueDraw();
            }
        }

        private void PictureBox_ScrollEvent(object o, ScrollEventArgs args)
        {
            if (ImageView.SelectedImage?.Plate == null || _selectedWell == null)
                return;

            if (args.Event.Direction == ScrollDirection.Up)
                CycleSelectedSample(-1);
            else if (args.Event.Direction == ScrollDirection.Down)
                CycleSelectedSample(1);

            pictureBox.QueueDraw();
        }

        private void PictureBox_KeyPressEvent(object o, KeyPressEventArgs args)
        {
            if (ImageView.SelectedImage?.Plate == null || _wells.Count == 0)
                return;

            switch (args.Event.Key)
            {
                case Key.Left:
                    NavigateWell(0, -1);
                    break;
                case Key.Right:
                    NavigateWell(0, 1);
                    break;
                case Key.Up:
                    NavigateWell(-1, 0);
                    break;
                case Key.Down:
                    NavigateWell(1, 0);
                    break;
                case Key.Page_Up:
                    CycleSelectedSample(-1);
                    break;
                case Key.Page_Down:
                    CycleSelectedSample(1);
                    break;
                case Key.Home:
                    SelectFirstWell();
                    break;
                case Key.End:
                    SelectLastWell();
                    break;
                default:
                    return;
            }

            pictureBox.QueueDraw();
        }

        private void NavigateWell(int rowDelta, int colDelta)
        {
            if (_selectedWell == null)
            {
                SelectFirstWell();
                return;
            }

            int targetRow = _selectedWell.Row + rowDelta;
            int targetCol = _selectedWell.Column + colDelta;
            if (_wellsByCell.TryGetValue((targetRow - _minRow, targetCol - _minCol), out var well))
            {
                SelectFirstSample(well);
                return;
            }

            // If the exact next cell is empty, fall back to a linear nearest-well move.
            var ordered = _wells
                .OrderBy(w => Math.Abs(w.Row - targetRow) + Math.Abs(w.Column - targetCol))
                .ThenBy(w => w.Row)
                .ThenBy(w => w.Column)
                .FirstOrDefault();
            if (ordered != null)
                SelectFirstSample(ordered);
        }

        private void CycleSelectedSample(int direction)
        {
            if (_selectedWell == null || _selectedWell.Samples == null || _selectedWell.Samples.Count == 0)
                return;

            int current = _selectedWell.Samples.FindIndex(s => s.Index == ImageView.SelectedImage.WellIndex);
            if (current < 0)
                current = 0;

            int next = (current + direction) % _selectedWell.Samples.Count;
            if (next < 0)
                next += _selectedWell.Samples.Count;

            SelectSample(_selectedWell, _selectedWell.Samples[next]);
        }

        private void SelectFirstWell()
        {
            var well = _wells.OrderBy(w => w.Row).ThenBy(w => w.Column).FirstOrDefault();
            if (well != null)
                SelectFirstSample(well);
        }

        private void SelectLastWell()
        {
            var well = _wells.OrderByDescending(w => w.Row).ThenByDescending(w => w.Column).FirstOrDefault();
            if (well != null)
                SelectFirstSample(well);
        }

        private void SelectFirstSample(BioImage.WellPlate.Well well)
        {
            if (well == null)
                return;

            var sample = well.Samples?.FirstOrDefault();
            if (sample != null)
                SelectSample(well, sample);
        }

        private void SelectSample(BioImage.WellPlate.Well well, BioImage.WellPlate.Well.Sample sample)
        {
            if (well == null || sample == null || ImageView.SelectedImage == null)
                return;

            _selectedWell = well;
            _selectedSample = sample;

            if (ImageView.SelectedImage.WellIndex != sample.Index)
            {
                ImageView.SelectedImage.WellIndex = sample.Index;
                App.viewer?.ReinitializeWellField();
            }

            pictureBox.QueueDraw();
        }

        private bool TryHitTest(int x, int y, out BioImage.WellPlate.Well well, out BioImage.WellPlate.Well.Sample sample)
        {
            well = null;
            sample = null;

            if (_wells.Count == 0)
                return false;

            int localX = x - OuterMargin - HeaderMargin;
            int localY = y - OuterMargin - HeaderMargin;
            if (localX < 0 || localY < 0)
                return false;

            int cellPitch = WellSize + WellGap;
            int col = localX / cellPitch;
            int row = localY / cellPitch;
            int cellX = localX % cellPitch;
            int cellY = localY % cellPitch;

            if (col < 0 || row < 0)
                return false;

            if (!_wellsByCell.TryGetValue((row, col), out well))
                return false;

            // Ignore clicks on the gap between cells.
            if (cellX >= WellSize || cellY >= WellSize)
                return false;

            int sampleCount = well.Samples?.Count ?? 0;
            if (sampleCount == 0)
                return true;

            var sampleRects = GetSampleRects(sampleCount, new RectangleD(
                OuterMargin + HeaderMargin + (col * cellPitch),
                OuterMargin + HeaderMargin + (row * cellPitch),
                WellSize, WellSize));

            for (int i = 0; i < sampleRects.Count && i < sampleCount; i++)
            {
                var rect = sampleRects[i];
                if (x >= rect.X && x < rect.X + rect.W &&
                    y >= rect.Y && y < rect.Y + rect.H)
                {
                    sample = well.Samples[i];
                    return true;
                }
            }

            sample = well.Samples[0];
            return true;
        }

        private void PictureBox_Drawn(object o, DrawnArgs args)
        {
            var cr = args.Cr;
            DrawBackground(cr);

            if (ImageView.SelectedImage?.Plate == null || _wells.Count == 0)
            {
                DrawEmptyState(cr);
                return;
            }

            DrawHeaders(cr);

            foreach (var well in _wells)
                DrawWell(cr, well);
        }

        private void DrawBackground(Context cr)
        {
            cr.SetSourceRGB(0.09, 0.09, 0.10);
            cr.Paint();
        }

        private void DrawEmptyState(Context cr)
        {
            cr.SetSourceRGB(0.85, 0.85, 0.88);
            cr.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Normal);
            cr.SetFontSize(16);
            DrawText(cr, "No well plate metadata is available.", OuterMargin, OuterMargin + 30);
        }

        private void DrawHeaders(Context cr)
        {
            cr.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            cr.SetFontSize(13);
            cr.SetSourceRGB(0.85, 0.85, 0.90);

            int cellPitch = WellSize + WellGap;
            int cols = Math.Max(1, _maxCol - _minCol + 1);
            int rows = Math.Max(1, _maxRow - _minRow + 1);

            for (int c = 0; c < cols; c++)
            {
                string label = (c + 1).ToString();
                double x = OuterMargin + HeaderMargin + (c * cellPitch) + (WellSize / 2.0);
                DrawCenteredText(cr, label, x, OuterMargin + 14);
            }

            for (int r = 0; r < rows; r++)
            {
                string label = ToPlateRowLabel(r);
                double y = OuterMargin + HeaderMargin + (r * cellPitch) + (WellSize / 2.0);
                DrawCenteredText(cr, label, OuterMargin + 14, y);
            }
        }

        private void DrawWell(Context cr, BioImage.WellPlate.Well well)
        {
            int gridRow = well.Row - _minRow;
            int gridCol = well.Column - _minCol;
            int cellPitch = WellSize + WellGap;
            double x = OuterMargin + HeaderMargin + (gridCol * cellPitch);
            double y = OuterMargin + HeaderMargin + (gridRow * cellPitch);

            var fill = ToCairoColor(well.Color);
            cr.SetSourceRGBA(fill.R * 0.35, fill.G * 0.35, fill.B * 0.35, 0.75);
            cr.Rectangle(x, y, WellSize, WellSize);
            cr.FillPreserve();

            bool isSelectedWell = ReferenceEquals(well, _selectedWell);
            double borderWidth = isSelectedWell ? 3.5 : 1.25;
            if (isSelectedWell)
            {
                double outlineLeft = x - 5.0;
                double outlineTop = y - 2.0;
                double outlineWidth = WellSize + 10.0;
                double outlineHeight = WellSize + 6.0;
                cr.Rectangle(outlineLeft, outlineTop, outlineWidth, outlineHeight);
                cr.LineWidth = borderWidth;
                cr.SetSourceRGB(1.0, 0.45, 0.15);
                cr.Stroke();
            }

            cr.LineWidth = borderWidth;
            cr.SetSourceRGB(0.18, 0.18, 0.18);
            cr.Rectangle(x, y, WellSize, WellSize);
            cr.Stroke();

            DrawWellLabel(cr, well, x, y);
            DrawSampleSlots(cr, well, x, y);
        }

        private void DrawWellLabel(Context cr, BioImage.WellPlate.Well well, double x, double y)
        {
            string label = string.IsNullOrWhiteSpace(well.ID)
                ? $"{ToPlateRowLabel(well.Row - _minRow)}{(well.Column - _minCol) + 1}"
                : well.ID;

            cr.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
            cr.SetFontSize(11);
            cr.SetSourceRGB(0.95, 0.95, 0.95);
            DrawText(cr, label, x + 6, y + 14);
        }

        private void DrawSampleSlots(Context cr, BioImage.WellPlate.Well well, double x, double y)
        {
            int sampleCount = well.Samples?.Count ?? 0;
            if (sampleCount <= 0)
                return;

            var rects = GetSampleRects(sampleCount, new RectangleD(x, y, WellSize, WellSize));
            int activeIndex = ImageView.SelectedImage?.WellIndex ?? -1;

            for (int i = 0; i < sampleCount && i < rects.Count; i++)
            {
                var sample = well.Samples[i];
                var rect = rects[i];
                bool isSelectedSample = sample.Index == activeIndex;

                if (isSelectedSample)
                    cr.SetSourceRGB(0.98, 0.90, 0.15);
                else
                    cr.SetSourceRGB(0.82, 0.82, 0.84);

                cr.Rectangle(rect.X, rect.Y, rect.W, rect.H);
                cr.FillPreserve();

                cr.LineWidth = isSelectedSample ? 2.0 : 1.0;
                cr.SetSourceRGB(isSelectedSample ? 0.35 : 0.22, isSelectedSample ? 0.18 : 0.22, isSelectedSample ? 0.04 : 0.22);
                cr.Stroke();

                if (rect.W >= 22 && rect.H >= 18)
                {
                    string text = !string.IsNullOrWhiteSpace(sample.ID) ? sample.ID : sample.Index.ToString();
                    cr.SelectFontFace("Sans", FontSlant.Normal, FontWeight.Bold);
                    cr.SetFontSize(Math.Max(8, Math.Min(11, rect.H * 0.35)));
                    cr.SetSourceRGB(0.10, 0.10, 0.10);
                    DrawCenteredText(cr, text, rect.X + rect.W / 2.0, rect.Y + rect.H / 2.0 + 1);
                }
            }
        }

        private static List<RectangleD> GetSampleRects(int sampleCount, RectangleD wellRect)
        {
            var rects = new List<RectangleD>();
            if (sampleCount <= 0)
                return rects;

            int columns = Math.Max(1, (int)Math.Ceiling(Math.Sqrt(sampleCount)));
            int rows = Math.Max(1, (int)Math.Ceiling(sampleCount / (double)columns));
            double innerX = wellRect.X + WellInnerPad;
            double innerY = wellRect.Y + 30;
            double innerW = wellRect.W - (WellInnerPad * 2);
            double innerH = wellRect.H - 38 - WellInnerPad;
            double slotW = Math.Max(SampleMinSize, (innerW - ((columns - 1) * SampleGap)) / columns);
            double slotH = Math.Max(SampleMinSize, (innerH - ((rows - 1) * SampleGap)) / rows);

            double slot = Math.Min(slotW, slotH);
            double totalW = (columns * slot) + ((columns - 1) * SampleGap);
            double totalH = (rows * slot) + ((rows - 1) * SampleGap);
            double startX = wellRect.X + (wellRect.W - totalW) / 2.0;
            double startY = innerY + (innerH - totalH) / 2.0;

            for (int i = 0; i < sampleCount; i++)
            {
                int r = i / columns;
                int c = i % columns;
                rects.Add(new RectangleD(
                    startX + c * (slot + SampleGap),
                    startY + r * (slot + SampleGap),
                    slot,
                    slot));
            }

            return rects;
        }

        private static void DrawText(Context cr, string text, double x, double y)
        {
            cr.MoveTo(x, y);
            cr.ShowText(text ?? string.Empty);
        }

        private static void DrawCenteredText(Context cr, string text, double centerX, double centerY)
        {
            if (string.IsNullOrEmpty(text))
                return;

            var ext = cr.TextExtents(text);
            cr.MoveTo(centerX - (ext.Width / 2.0) - ext.XBearing,
                      centerY - (ext.Height / 2.0) - ext.YBearing);
            cr.ShowText(text);
        }

        private static string ToPlateRowLabel(int rowIndex)
        {
            if (rowIndex < 0)
                rowIndex = 0;

            // Convert 0-based row index to spreadsheet-style letters.
            string label = string.Empty;
            int value = rowIndex;
            do
            {
                label = (char)('A' + (value % 26)) + label;
                value = (value / 26) - 1;
            }
            while (value >= 0);

            return label;
        }

        private static Cairo.Color ToCairoColor(System.Drawing.Color color)
        {
            if (color.A == 0)
                return new Cairo.Color(0.20, 0.20, 0.20, 1.0);

            return new Cairo.Color(color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);
        }
    }
}
