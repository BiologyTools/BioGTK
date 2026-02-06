using Gtk;
using SkiaSharp;
using SkiaSharp.Views.Desktop;
using SkiaSharp.Views.Gtk;
using System;

namespace BioGTK
{
    /// <summary>
    /// GTK widget that provides SkiaSharp rendering surface for slide content
    /// Manages the rendering pipeline and exposes Skia canvas for custom drawing
    /// </summary>
    public class SlideArea : SKDrawingArea
    {
        #region Events

        /// <summary>
        /// Event fired during Skia rendering phase
        /// Allows custom drawing on top of the slide content (annotations, overlays, etc.)
        /// </summary>
        public event Action<SKCanvas, int, int> OnSkiaRender;

        #endregion

        #region Fields

        private SKSlideRenderer _renderer;
        private bool _isInitialized;

        #endregion

        #region Constructor

        public SlideArea() : base()
        {
            // Configure widget
            this.Expand = true;
            this.CanFocus = true;

            // Subscribe to paint event
            this.PaintSurface += OnPaintSurface;

            _isInitialized = true;
        }

        #endregion

        #region Renderer Integration

        /// <summary>
        /// Set the renderer that will provide content for this area
        /// </summary>
        public void SetRenderer(SKSlideRenderer renderer)
        {
            _renderer = renderer;
        }

        #endregion

        #region Rendering

        /// <summary>
        /// Main paint handler - renders slide content and custom overlays
        /// </summary>
        private void OnPaintSurface(object sender, SKPaintSurfaceEventArgs e)
        {
            if (!_isInitialized)
                return;

            var canvas = e.Surface.Canvas;
            var width = e.Info.Width;
            var height = e.Info.Height;

            // Clear canvas
            canvas.Clear(SKColors.Black);

            try
            {
                // Render slide content from renderer
                if (_renderer != null)
                {
                    _renderer.DrawToCanvas(canvas, width, height);
                }

                // Allow custom drawing on top (annotations, etc.)
                OnSkiaRender?.Invoke(canvas, width, height);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Paint surface error: {ex.Message}");
            }
        }

        /// <summary>
        /// Request redraw of the widget
        /// </summary>
        public void RequestRedraw()
        {
            this.QueueDraw();
        }

        #endregion

        #region Cleanup

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.PaintSurface -= OnPaintSurface;
            }

            base.Dispose(disposing);
        }

        #endregion
    }
}