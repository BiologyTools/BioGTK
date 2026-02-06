using SkiaSharp;
using System;
using System.Threading;
using System.Threading.Tasks;
using AForge;
using BioLib;

namespace BioGTK
{
    /// <summary>
    /// Manages slide rendering by coordinating between the SkiaStitchingPipeline and SlideGLArea
    /// Handles async updates, caching, and progressive rendering for large pyramidal images
    /// </summary>
    public class SKSlideRenderer : IDisposable
    {
        #region Fields and Properties

        private readonly SlideGLArea _glArea;
        private SkiaStitchingPipeline _stitchingPipeline;

        // Current rendering state
        private SKImage _currentRenderedImage;
        private PointD _lastOrigin;
        private double _lastResolution;
        private ZCT _lastCoordinate;
        private int _lastWidth;
        private int _lastHeight;

        // Async rendering management
        private CancellationTokenSource _renderCancellation;
        private Task _currentRenderTask;
        private readonly object _renderLock = new object();
        private readonly SemaphoreSlim _updateSemaphore;

        // Progressive rendering
        private bool _useProgressiveRendering = true;
        private SKImage _lowResPreview;

        // Source tracking
        private OpenSlideGTK.ISlideSource _openSlideSource;
        private ISlideSource _slideSource;
        private bool _isOpenSlide;
        private bool _hasSource;

        // Performance configuration
        public bool EnableProgressiveRendering
        {
            get => _useProgressiveRendering;
            set => _useProgressiveRendering = value;
        }

        public int MaxCacheSizeMB { get; set; } = 512;
        public int TileSize { get; set; } = 256;
        public SKFilterQuality FilterQuality { get; set; } = SKFilterQuality.Medium;
        public bool EnablePrefetch { get; set; } = true;

        // State tracking
        public bool IsRendering { get; private set; }
        public bool HasValidImage => _currentRenderedImage != null;

        #endregion

        #region Constructor

        /// <summary>
        /// Initialize renderer for a specific GLArea widget
        /// </summary>
        public SKSlideRenderer(SlideGLArea glArea)
        {
            _glArea = glArea ?? throw new ArgumentNullException(nameof(glArea));
            _updateSemaphore = new SemaphoreSlim(1, 1);
            _renderCancellation = new CancellationTokenSource();

            InitializeDefaults();
        }

        private void InitializeDefaults()
        {
            _lastOrigin = new PointD(0, 0);
            _lastResolution = 1.0;
            _lastCoordinate = new ZCT(0, 0, 0);
            _lastWidth = 0;
            _lastHeight = 0;
        }

        #endregion

        #region Source Management

        /// <summary>
        /// Set OpenSlide source and initialize pipeline
        /// </summary>
        public void SetSource(OpenSlideGTK.ISlideSource openSlideSource)
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
        public void SetSource(ISlideSource slideSource)
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
            CancelCurrentRender();

            _currentRenderedImage?.Dispose();
            _currentRenderedImage = null;

            _lowResPreview?.Dispose();
            _lowResPreview = null;

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
            int width,
            int height,
            double resolution,
            ZCT coordinate,
            bool forceUpdate = false)
        {
            if (!_hasSource || _stitchingPipeline == null)
                return;

            // Check if update is needed
            if (!forceUpdate && !NeedsUpdate(origin, width, height, resolution, coordinate))
                return;

            // Cancel any in-progress render
            CancelCurrentRender();

            // Acquire update lock
            await _updateSemaphore.WaitAsync();

            try
            {
                IsRendering = true;

                // Create new cancellation token for this render
                _renderCancellation = new CancellationTokenSource();
                var cancellationToken = _renderCancellation.Token;

                // Store current parameters
                _lastOrigin = origin;
                _lastResolution = resolution;
                _lastCoordinate = coordinate;
                _lastWidth = width;
                _lastHeight = height;

                // Progressive rendering: render low-res preview first if enabled
                if (_useProgressiveRendering && ShouldRenderPreview(resolution))
                {
                    await RenderLowResPreviewAsync(origin, width, height, resolution, coordinate, cancellationToken);
                }

                // Render full quality
                await RenderFullQualityAsync(origin, width, height, resolution, coordinate, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Render was cancelled - this is expected behavior
                Console.WriteLine("Render cancelled");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during view update: {ex.Message}");
            }
            finally
            {
                IsRendering = false;
                _updateSemaphore.Release();
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
            ZCT coordinate,
            CancellationToken cancellationToken)
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
                    previewWidth,
                    previewHeight,
                    previewResolution,
                    coordinate,
                    cancellationToken
                );

                if (previewImage != null && !cancellationToken.IsCancellationRequested)
                {
                    _lowResPreview?.Dispose();
                    _lowResPreview = previewImage;

                    // Request immediate redraw with preview
                    RequestRedraw();
                }
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
            int width,
            int height,
            double resolution,
            ZCT coordinate,
            CancellationToken cancellationToken)
        {
            try
            {
                // Use stitching pipeline to create full quality composite
                var stitchedImage = await _stitchingPipeline.StitchViewportAsync(
                    origin,
                    width,
                    height,
                    resolution,
                    coordinate,
                    cancellationToken
                );

                if (stitchedImage != null && !cancellationToken.IsCancellationRequested)
                {
                    // Swap in new image
                    lock (_renderLock)
                    {
                        _currentRenderedImage?.Dispose();
                        _currentRenderedImage = stitchedImage;

                        // Clear preview once we have full quality
                        _lowResPreview?.Dispose();
                        _lowResPreview = null;
                    }

                    // Request redraw with new image
                    RequestRedraw();
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
            ZCT coordinate)
        {
            if (!_hasSource || _stitchingPipeline == null)
                return null;

            return _stitchingPipeline.StitchRegion(region, level, coordinate);
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
                _lastWidth,
                _lastHeight,
                _lastResolution,
                _lastCoordinate
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
            lock (_renderLock)
            {
                // Return full quality if available, otherwise preview
                return _currentRenderedImage ?? _lowResPreview;
            }
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

            lock (_renderLock)
            {
                try
                {
                    using (var paint = new SKPaint
                    {
                        FilterQuality = FilterQuality,
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
        /// Cancel any currently running render operation
        /// </summary>
        public void CancelCurrentRender()
        {
            try
            {
                _renderCancellation?.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Already disposed, ignore
            }
        }

        /// <summary>
        /// Request GLArea redraw on GTK main thread
        /// </summary>
        private void RequestRedraw()
        {
            Gtk.Application.Invoke((sender, args) =>
            {
                _glArea?.RequestRedraw();
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
            SKFilterQuality? filterQuality = null,
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

            if (enablePrefetch.HasValue)
            {
                EnablePrefetch = enablePrefetch.Value;
                if (_stitchingPipeline != null)
                    _stitchingPipeline.EnablePrefetch = enablePrefetch.Value;
            }

            if (enableProgressiveRendering.HasValue)
            {
                EnableProgressiveRendering = enableProgressiveRendering.Value;
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
            CancelCurrentRender();

            _renderCancellation?.Dispose();
            _updateSemaphore?.Dispose();

            _currentRenderedImage?.Dispose();
            _lowResPreview?.Dispose();

            _stitchingPipeline?.Dispose();

            _currentRenderedImage = null;
            _lowResPreview = null;
            _stitchingPipeline = null;
        }

        #endregion
    }
}