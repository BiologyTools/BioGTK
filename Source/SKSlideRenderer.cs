using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using AForge;
using BioLib;
using SkiaSharp.Views.Gtk;

namespace BioGTK
{
    /// <summary>
    /// Manages slide rendering by coordinating between the SkiaStitchingPipeline and SlideGLArea
    /// Handles async updates, caching, and progressive rendering for large pyramidal images
    /// </summary>
    public class SKSlideRenderer : IDisposable
    {
        #region Fields and Properties
        private SkiaStitchingPipeline _stitchingPipeline;

        // Current rendering state
        private SKImage _currentRenderedImage;
        private PointD _lastOrigin;
        private double _lastResolution;
        private ZCT _lastCoordinate;
        private int _lastWidth;
        private int _lastHeight;

        // Source tracking
        private OpenSlideGTK.OpenSlideBase _openSlideSource;
        private SlideBase _slideSource;
        private bool _isOpenSlide;
        private bool _hasSource;

        public int MaxCacheSizeMB { get; set; } = 512;
        public int TileSize { get; set; } = 256;
        public SKSamplingOptions Sampling { get; set; } = SKSamplingOptions.Default;
        public bool EnablePrefetch { get; set; } = true;

        // State tracking
        public bool IsRendering { get; private set; }
        public bool HasValidImage => _currentRenderedImage != null;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialize renderer for a specific GLArea widget
        /// </summary>
        public SKSlideRenderer(SKDrawingArea sk)
        {
            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            _lastOrigin = new PointD(0, 0);
            _lastResolution = 1.0;
            _lastCoordinate = new ZCT(0, 0, 0);
            _lastWidth = 600;
            _lastHeight = 400;
        }

        #endregion

        #region Source Management

        /// <summary>
        /// Set OpenSlide source and initialize pipeline
        /// </summary>
        public void SetSource(OpenSlideGTK.OpenSlideBase openSlideSource)
        {
            if (openSlideSource == null)
                throw new ArgumentNullException(nameof(openSlideSource));

            ClearCurrentSource();

            _openSlideSource = openSlideSource;
            _isOpenSlide = true;
            _hasSource = true;

            InitializeStitchingPipeline();
        }

        /// <summary>
        /// Set BioLib slide source and initialize pipeline
        /// </summary>
        public void SetSource(SlideBase slideSource)
        {
            if (slideSource == null)
                throw new ArgumentNullException(nameof(slideSource));

            ClearCurrentSource();

            _slideSource = slideSource;
            _isOpenSlide = false;
            _hasSource = true;

            InitializeStitchingPipeline();
        }

        /// <summary>
        /// Initialize or reinitialize the stitching pipeline with current settings
        /// </summary>
        private void InitializeStitchingPipeline()
        {
            _stitchingPipeline?.Dispose();

            if (_isOpenSlide)
            {
                _stitchingPipeline = new SkiaStitchingPipeline(_openSlideSource, MaxCacheSizeMB);
            }
            else
            {
                _stitchingPipeline = new SkiaStitchingPipeline(_slideSource, MaxCacheSizeMB);
            }

            // Apply configuration
            _stitchingPipeline.TileSize = TileSize;
            //_stitchingPipeline.FilterQuality = FilterQuality;
            _stitchingPipeline.EnablePrefetch = EnablePrefetch;
        }

        /// <summary>
        /// Clear current source and dispose resources
        /// </summary>
        private void ClearCurrentSource()
        {
            _currentRenderedImage?.Dispose();
            _currentRenderedImage = null;

            _stitchingPipeline?.Dispose();
            _stitchingPipeline = null;

            _hasSource = false;
        }

        #endregion

        #region Async View Update

        /// <summary>
        /// Update the rendered view asynchronously - main entry point for rendering
        /// </summary>
        public async Task UpdateViewAsync(
            PointD origin,
            double resolution,
            ZCT coordinate,
            int width = 600,
            int height = 400)
        {
            if (!_hasSource || _stitchingPipeline == null)
                return;
            /*
            // Check if update is needed
            if (!forceUpdate && !NeedsUpdate(origin, width, height, resolution, coordinate))
                return;
            */
            // Cancel any in-progress render
            //CancelCurrentRender();

            // Acquire update lock
            //await _updateSemaphore.WaitAsync();

            try
            {
                IsRendering = true;
                /*
                // Create new cancellation token for this render
                _renderCancellation = new CancellationTokenSource();
                var cancellationToken = _renderCancellation.Token;
                */
                // Store current parameters
                _lastOrigin = origin;
                _lastResolution = resolution;
                _lastCoordinate = coordinate;
                _lastWidth = width;
                _lastHeight = height;
                /*
                // Progressive rendering: render low-res preview first if enabled
                if (_useProgressiveRendering && ShouldRenderPreview(resolution))
                {
                    await RenderLowResPreviewAsync(origin, width, height, resolution, coordinate);
                }
                */
                // Render full quality
                await RenderFullQualityAsync(origin, resolution, coordinate, width, height);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during view update: {ex.Message}");
            }
            finally
            {
                IsRendering = false;
            }
        }

        /// <summary>
        /// Render low-resolution preview for immediate feedback
        /// </summary>
        private async Task RenderLowResPreviewAsync(
            PointD origin,
            int width,
            int height,
            double resolution,
            ZCT coordinate)
        {
            try
            {
                // Use a lower resolution for preview (e.g., 4x lower)
                double previewResolution = resolution * 4.0;
                int previewWidth = width / 2;
                int previewHeight = height / 2;

                // Create preview with stitching pipeline
                var previewImage = await _stitchingPipeline.StitchViewportAsync(
                    origin,
                    previewResolution,
                    coordinate,
                    previewWidth,
                    previewHeight
                );
                /*
                if (previewImage != null)
                {
                    _lowResPreview?.Dispose();
                    _lowResPreview = previewImage;

                    // Request immediate redraw with preview
                    //RequestRedraw();
                }
                */
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Preview render error: {ex.Message}");
            }
        }

        /// <summary>
        /// Render full quality image
        /// </summary>
        private async Task RenderFullQualityAsync(
            PointD origin,
            double resolution,
            ZCT coordinate,
            int width = 600,
            int height = 400)
        {
            try
            {
                // Use stitching pipeline to create full quality composite
                var stitchedImage = await _stitchingPipeline.StitchViewportAsync(
                    origin,
                    resolution,
                    coordinate,
                    width,
                    height
                );

                if (stitchedImage != null)
                {
                    _currentRenderedImage?.Dispose();
                    _currentRenderedImage = stitchedImage;

                    // Request redraw with new image
                    //RequestRedraw();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Full quality render error: {ex.Message}");
            }
        }

        #endregion

        #region Synchronous Rendering Methods

        /// <summary>
        /// Render a specific region synchronously (for exports, small areas)
        /// </summary>
        public SKImage RenderRegion(
            RectangleD region,
            int level,
            ZCT coordinate, double resolution)
        {
            if (!_hasSource || _stitchingPipeline == null)
                return null;

            return _stitchingPipeline.StitchRegion(region, level, coordinate, resolution);
        }

        /// <summary>
        /// Render current viewport synchronously (blocking)
        /// </summary>
        public SKImage RenderCurrentViewSync()
        {
            if (!_hasSource || _stitchingPipeline == null)
                return null;

            return _stitchingPipeline.StitchViewportAsync(
                _lastOrigin,
                _lastResolution,
                _lastCoordinate,
                _lastWidth,
                _lastHeight
            ).Result;
        }

        #endregion

        #region Image Access for Rendering

        /// <summary>
        /// Get the current image for rendering - called by GLArea during draw
        /// Returns the best available image (full quality or preview)
        /// </summary>
        public SKImage GetCurrentImage()
        {
            // Return full quality if available, otherwise preview
            return _currentRenderedImage;
        }

        /// <summary>
        /// Draw the current image to a canvas with proper scaling
        /// Called during the Skia render callback from SlideGLArea
        /// </summary>
        public void DrawToCanvas(SKCanvas canvas, int canvasWidth, int canvasHeight)
        {
            if (canvas == null)
                return;

            var image = GetCurrentImage();
            if (image == null)
                return;
            try
            {
                using (var paint = new SKPaint
                {
                    IsAntialias = true,
                    BlendMode = SKBlendMode.SrcOver
                })
                {
                    // Draw image to fill canvas
                    var destRect = new SKRect(0, 0, canvasWidth, canvasHeight);
                    canvas.DrawImage(image, destRect, paint);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error drawing to canvas: {ex.Message}");
            }
        }

        #endregion

        #region Update Management

        /// <summary>
        /// Check if an update is needed based on parameter changes
        /// </summary>
        private bool NeedsUpdate(
            PointD origin,
            int width,
            int height,
            double resolution,
            ZCT coordinate)
        {
            const double EPSILON = 0.0001;

            return Math.Abs(origin.X - _lastOrigin.X) > EPSILON ||
                   Math.Abs(origin.Y - _lastOrigin.Y) > EPSILON ||
                   Math.Abs(resolution - _lastResolution) > EPSILON ||
                   width != _lastWidth ||
                   height != _lastHeight ||
                   coordinate.Z != _lastCoordinate.Z ||
                   coordinate.C != _lastCoordinate.C ||
                   coordinate.T != _lastCoordinate.T;
        }

        /// <summary>
        /// Determine if preview rendering should be used
        /// </summary>
        private bool ShouldRenderPreview(double resolution)
        {
            // Use preview for high-resolution views where full quality takes time
            return resolution < 1.0; // Zoomed in significantly
        }

        /// <summary>
        /// Request GLArea redraw on GTK main thread
        /// </summary>
        private void RequestRedraw()
        {
            Gtk.Application.Invoke((sender, args) =>
            {
                App.viewer.RequestDeferredRender();
            });
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Clear the tile cache
        /// </summary>
        public void ClearCache()
        {
            _stitchingPipeline?.ClearCache();
        }

        /// <summary>
        /// Get cache statistics for monitoring
        /// </summary>
        public CacheStatistics GetCacheStatistics()
        {
            // Could be extended to expose actual stats from pipeline
            return new CacheStatistics
            {
                IsEnabled = _stitchingPipeline != null,
                MaxSizeMB = MaxCacheSizeMB,
                TileSize = TileSize
            };
        }

        public class CacheStatistics
        {
            public bool IsEnabled { get; set; }
            public int MaxSizeMB { get; set; }
            public int TileSize { get; set; }
        }

        #endregion

        #region Configuration Updates

        /// <summary>
        /// Update renderer configuration and reinitialize if needed
        /// </summary>
        public void UpdateConfiguration(
            int? maxCacheSizeMB = null,
            int? tileSize = null,
            SKSamplingOptions? sampling = null,
            bool? enablePrefetch = null,
            bool? enableProgressiveRendering = null)
        {
            bool needsReinit = false;

            if (maxCacheSizeMB.HasValue && maxCacheSizeMB.Value != MaxCacheSizeMB)
            {
                MaxCacheSizeMB = maxCacheSizeMB.Value;
                needsReinit = true;
            }

            if (tileSize.HasValue && tileSize.Value != TileSize)
            {
                TileSize = tileSize.Value;
                needsReinit = true;
            }

            // Reinitialize pipeline if structural changes were made
            if (needsReinit && _hasSource)
            {
                InitializeStitchingPipeline();
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            _currentRenderedImage?.Dispose();       
            _stitchingPipeline?.Dispose();
            _currentRenderedImage = null;
            _stitchingPipeline = null;
        }

        #endregion
    }
}