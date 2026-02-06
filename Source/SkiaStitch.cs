using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AForge;
using BioLib;

namespace BioGTK
{
    /// <summary>
    /// SkiaSharp-based tile stitching pipeline for compositing large pyramidal images
    /// </summary>
    public class SkiaStitchingPipeline : IDisposable
    {
        #region Fields and Properties

        private readonly ISlideSource _slideSource;
        private readonly OpenSlideGTK.ISlideSource _openSlideSource;
        private readonly bool _isOpenSlide;

        // Tile cache with thread-safe access
        private ConcurrentDictionary<string, CachedTile> _tileCache;
        private int _maxCacheSize;
        private object _cacheLock = new object();

        // Rendering state
        private SKSurface _compositeSurface;
        private SKCanvas _compositeCanvas;
        private int _currentSurfaceWidth;
        private int _currentSurfaceHeight;

        // Async operation management
        private CancellationTokenSource _cancellationTokenSource;
        private SemaphoreSlim _renderSemaphore;
        private ConcurrentDictionary<string, Task<SKImage>> _pendingTileFetches;

        // Configuration
        public int TileSize { get; set; } = 256;
        public int PrefetchRadius { get; set; } = 1;
        public SKSamplingOptions Sampling { get; set; } = SKSamplingOptions.Default;
        public bool EnablePrefetch { get; set; } = true;

        #endregion

        #region Nested Types

        /// <summary>
        /// Represents a cached tile with metadata
        /// </summary>
        private class CachedTile
        {
            public SKImage Image { get; set; }
            public DateTime LastAccessed { get; set; }
            public long SizeBytes { get; set; }
            public string Key { get; set; }

            public CachedTile(SKImage image, string key)
            {
                Image = image;
                Key = key;
                LastAccessed = DateTime.UtcNow;
                SizeBytes = CalculateSize(image);
            }

            private long CalculateSize(SKImage image)
            {
                if (image == null) return 0;
                return image.Width * image.Height * 4; // Assume RGBA
            }
        }

        /// <summary>
        /// Tile request information
        /// </summary>
        public class TileRequest
        {
            public int TileX { get; set; }
            public int TileY { get; set; }
            public int Level { get; set; }
            public ZCT Coordinate { get; set; }
            public RectangleD Bounds { get; set; }

            public string GetCacheKey()
            {
                return $"{TileX}_{TileY}_{Level}_{Coordinate.Z}_{Coordinate.C}_{Coordinate.T}";
            }
        }

        #endregion

        #region Constructor and Initialization

        /// <summary>
        /// Initialize pipeline for OpenSlide source
        /// </summary>
        public SkiaStitchingPipeline(OpenSlideGTK.ISlideSource openSlideSource, int maxCacheSizeMB = 512)
        {
            _openSlideSource = openSlideSource ?? throw new ArgumentNullException(nameof(openSlideSource));
            _isOpenSlide = true;

            InitializeCommon(maxCacheSizeMB);
        }

        /// <summary>
        /// Initialize pipeline for BioLib slide source
        /// </summary>
        public SkiaStitchingPipeline(ISlideSource slideSource, int maxCacheSizeMB = 512)
        {
            _slideSource = slideSource ?? throw new ArgumentNullException(nameof(slideSource));
            _isOpenSlide = false;

            InitializeCommon(maxCacheSizeMB);
        }

        private void InitializeCommon(int maxCacheSizeMB)
        {
            _maxCacheSize = maxCacheSizeMB * 1024 * 1024;
            _tileCache = new ConcurrentDictionary<string, CachedTile>();
            _pendingTileFetches = new ConcurrentDictionary<string, Task<SKImage>>();
            _renderSemaphore = new SemaphoreSlim(1, 1);
            _cancellationTokenSource = new CancellationTokenSource();
        }

        #endregion

        #region Public Stitching Methods

        /// <summary>
        /// Stitch tiles for a given viewport asynchronously
        /// </summary>
        public async Task<SKImage> StitchViewportAsync(
            PointD origin,
            int viewportWidth,
            int viewportHeight,
            double resolution,
            ZCT coordinate,
            CancellationToken cancellationToken = default)
        {
            // Calculate viewport parameters
            int level = CalculateLevelFromResolution(resolution);
            var tileRequests = CalculateTileRequests(origin, viewportWidth, viewportHeight, level, coordinate);

            // Prefetch surrounding tiles if enabled
            if (EnablePrefetch)
            {
                _ = PrefetchSurroundingTilesAsync(origin, viewportWidth, viewportHeight, level, coordinate, cancellationToken);
            }

            // Create or resize composite surface
            EnsureCompositeSurface(viewportWidth, viewportHeight);

            await _renderSemaphore.WaitAsync(cancellationToken);
            try
            {
                // Clear canvas
                _compositeCanvas.Clear(SKColors.Transparent);

                // Fetch and composite all tiles
                await CompositeTilesAsync(tileRequests, origin, resolution, cancellationToken);

                // Create snapshot
                return _compositeSurface.Snapshot();
            }
            finally
            {
                _renderSemaphore.Release();
            }
        }

        /// <summary>
        /// Stitch a specific region synchronously (for smaller areas)
        /// </summary>
        public SKImage StitchRegion(
            RectangleD region,
            int level,
            ZCT coordinate)
        {
            var tileRequests = CalculateTileRequestsForRegion(region, level, coordinate);

            int outputWidth = (int)Math.Ceiling(region.W);
            int outputHeight = (int)Math.Ceiling(region.H);

            using (var surface = SKSurface.Create(new SKImageInfo(outputWidth, outputHeight)))
            {
                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Transparent);

                foreach (var request in tileRequests)
                {
                    var tileImage = FetchTileSync(request);
                    if (tileImage != null)
                    {
                        DrawTileToCanvas(canvas, tileImage, request, new AForge.PointD(region.X,region.Y), 1.0);
                    }
                }

                return surface.Snapshot();
            }
        }

        #endregion

        #region Tile Fetching and Caching

        /// <summary>
        /// Fetch tile asynchronously with caching
        /// </summary>
        private async Task<SKImage> FetchTileAsync(TileRequest request, CancellationToken cancellationToken)
        {
            string cacheKey = request.GetCacheKey();

            // Check cache first
            if (_tileCache.TryGetValue(cacheKey, out var cachedTile))
            {
                cachedTile.LastAccessed = DateTime.UtcNow;
                return cachedTile.Image;
            }

            // Check if already fetching
            if (_pendingTileFetches.TryGetValue(cacheKey, out var existingTask))
            {
                return await existingTask;
            }

            // Create new fetch task
            var fetchTask = Task.Run(async () =>
            {
                try
                {
                    byte[] tileData = await FetchTileDataAsync(request, cancellationToken);
                    if (tileData == null || tileData.Length == 0)
                        return null;

                    SKImage skImage = ConvertTileDataToSKImage(tileData, request);

                    // Add to cache
                    if (skImage != null)
                    {
                        AddToCache(cacheKey, skImage);
                    }

                    return skImage;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error fetching tile {cacheKey}: {ex.Message}");
                    return null;
                }
            }, cancellationToken);

            _pendingTileFetches.TryAdd(cacheKey, fetchTask);

            try
            {
                return await fetchTask;
            }
            finally
            {
                _pendingTileFetches.TryRemove(cacheKey, out _);
            }
        }

        /// <summary>
        /// Fetch tile synchronously (for immediate needs)
        /// </summary>
        private SKImage FetchTileSync(TileRequest request)
        {
            string cacheKey = request.GetCacheKey();

            // Check cache
            if (_tileCache.TryGetValue(cacheKey, out var cachedTile))
            {
                cachedTile.LastAccessed = DateTime.UtcNow;
                return cachedTile.Image;
            }

            // Fetch directly
            try
            {
                byte[] tileData = FetchTileDataSync(request);
                if (tileData == null || tileData.Length == 0)
                    return null;

                SKImage skImage = ConvertTileDataToSKImage(tileData, request);

                if (skImage != null)
                {
                    AddToCache(cacheKey, skImage);
                }

                return skImage;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching tile sync {cacheKey}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetch raw tile data based on source type
        /// </summary>
        private async Task<byte[]> FetchTileDataAsync(TileRequest request, CancellationToken cancellationToken)
        {
            if (_isOpenSlide)
            {
                var sliceInfo = new OpenSlideGTK.SliceInfo(
                    request.Bounds.X,
                    request.Bounds.Y,
                    request.Bounds.W,
                    request.Bounds.H,
                    CalculateResolutionFromLevel(request.Level)
                );

                return _openSlideSource.GetSlice(sliceInfo, request.Coordinate, request.Level);
            }
            else
            {
                var sliceInfo = new SliceInfo(
                    request.Bounds.X,
                    request.Bounds.Y,
                    request.Bounds.W,
                    request.Bounds.H,
                    CalculateResolutionFromLevel(request.Level),
                    request.Coordinate
                );

                var origin = new PointD(request.Bounds.X, request.Bounds.Y);
                var size = new AForge.Size((int)request.Bounds.W, (int)request.Bounds.H);

                return await _slideSource.GetSlice(sliceInfo, origin, size);
            }
        }

        /// <summary>
        /// Fetch raw tile data synchronously
        /// </summary>
        private byte[] FetchTileDataSync(TileRequest request)
        {
            if (_isOpenSlide)
            {
                var sliceInfo = new OpenSlideGTK.SliceInfo(
                    request.Bounds.X,
                    request.Bounds.Y,
                    request.Bounds.W,
                    request.Bounds.H,
                    CalculateResolutionFromLevel(request.Level)
                );

                return _openSlideSource.GetSlice(sliceInfo, request.Coordinate, request.Level);
            }
            else
            {
                var sliceInfo = new SliceInfo(
                    request.Bounds.X,
                    request.Bounds.Y,
                    request.Bounds.W,
                    request.Bounds.H,
                    CalculateResolutionFromLevel(request.Level),
                    request.Coordinate
                );

                var origin = new PointD(request.Bounds.X, request.Bounds.Y);
                var size = new AForge.Size((int)request.Bounds.W, (int)request.Bounds.H);

                return _slideSource.GetSlice(sliceInfo, origin, size).Result;
            }
        }

        #endregion

        #region Tile Compositing

        /// <summary>
        /// Composite all tiles onto the canvas
        /// </summary>
        private async Task CompositeTilesAsync(
            List<TileRequest> tileRequests,
            PointD viewportOrigin,
            double resolution,
            CancellationToken cancellationToken)
        {
            var fetchTasks = new List<Task<(TileRequest request, SKImage image)>>();

            foreach (var request in tileRequests)
            {
                var task = Task.Run(async () =>
                {
                    var image = await FetchTileAsync(request, cancellationToken);
                    return (request, image);
                }, cancellationToken);

                fetchTasks.Add(task);
            }

            var results = await Task.WhenAll(fetchTasks);

            using (var paint = CreateCompositePaint())
            {
                foreach (var (request, image) in results)
                {
                    if (image != null && !cancellationToken.IsCancellationRequested)
                    {
                        DrawTileToCanvas(_compositeCanvas, image, request, viewportOrigin, resolution);
                    }
                }
            }
        }

        /// <summary>
        /// Draw individual tile to canvas with proper positioning
        /// </summary>
        private void DrawTileToCanvas(
            SKCanvas canvas,
            SKImage tileImage,
            TileRequest request,
            PointD viewportOrigin,
            double resolution)
        {
            // Calculate destination rectangle in canvas coordinates
            float destX = (float)((request.Bounds.X - viewportOrigin.X) / resolution);
            float destY = (float)((request.Bounds.Y - viewportOrigin.Y) / resolution);
            float destW = (float)(request.Bounds.W / resolution);
            float destH = (float)(request.Bounds.H / resolution);

            var destRect = new SKRect(destX, destY, destX + destW, destY + destH);

            using (var paint = CreateTilePaint())
            {
                canvas.DrawImage(tileImage, destRect, paint);
            }
        }

        /// <summary>
        /// Create paint for compositing operations
        /// </summary>
        private SKPaint CreateCompositePaint()
        {
            return new SKPaint
            {
                IsAntialias = true,
                BlendMode = SKBlendMode.SrcOver
            };
        }

        /// <summary>
        /// Create paint for tile drawing
        /// </summary>
        private SKPaint CreateTilePaint()
        {
            return new SKPaint
            {
                IsAntialias = false // Tiles should align perfectly
            };
        }

        #endregion

        #region Tile Calculation and Management

        /// <summary>
        /// Calculate which tiles are needed for the viewport
        /// </summary>
        private List<TileRequest> CalculateTileRequests(
            PointD origin,
            int viewportWidth,
            int viewportHeight,
            int level,
            ZCT coordinate)
        {
            var requests = new List<TileRequest>();

            double resolution = CalculateResolutionFromLevel(level);

            // Calculate tile grid bounds
            int startTileX = (int)Math.Floor(origin.X / TileSize);
            int startTileY = (int)Math.Floor(origin.Y / TileSize);
            int endTileX = (int)Math.Ceiling((origin.X + viewportWidth * resolution) / TileSize);
            int endTileY = (int)Math.Ceiling((origin.Y + viewportHeight * resolution) / TileSize);

            // Create requests for each tile
            for (int tileY = startTileY; tileY <= endTileY; tileY++)
            {
                for (int tileX = startTileX; tileX <= endTileX; tileX++)
                {
                    var bounds = new RectangleD(
                        tileX * TileSize,
                        tileY * TileSize,
                        TileSize,
                        TileSize
                    );

                    requests.Add(new TileRequest
                    {
                        TileX = tileX,
                        TileY = tileY,
                        Level = level,
                        Coordinate = coordinate,
                        Bounds = bounds
                    });
                }
            }

            return requests;
        }

        /// <summary>
        /// Calculate tiles for a specific region
        /// </summary>
        private List<TileRequest> CalculateTileRequestsForRegion(
            RectangleD region,
            int level,
            ZCT coordinate)
        {
            var requests = new List<TileRequest>();

            int startTileX = (int)Math.Floor(region.X / TileSize);
            int startTileY = (int)Math.Floor(region.Y / TileSize);
            int endTileX = (int)Math.Ceiling((region.X + region.W) / TileSize);
            int endTileY = (int)Math.Ceiling((region.Y + region.H) / TileSize);

            for (int tileY = startTileY; tileY <= endTileY; tileY++)
            {
                for (int tileX = startTileX; tileX <= endTileX; tileX++)
                {
                    var bounds = new RectangleD(
                        tileX * TileSize,
                        tileY * TileSize,
                        TileSize,
                        TileSize
                    );

                    requests.Add(new TileRequest
                    {
                        TileX = tileX,
                        TileY = tileY,
                        Level = level,
                        Coordinate = coordinate,
                        Bounds = bounds
                    });
                }
            }

            return requests;
        }

        #endregion

        #region Prefetching

        /// <summary>
        /// Prefetch tiles surrounding the viewport
        /// </summary>
        private async Task PrefetchSurroundingTilesAsync(
            PointD origin,
            int viewportWidth,
            int viewportHeight,
            int level,
            ZCT coordinate,
            CancellationToken cancellationToken)
        {
            double resolution = CalculateResolutionFromLevel(level);

            // Expand viewport by prefetch radius
            double expandedX = origin.X - (PrefetchRadius * TileSize);
            double expandedY = origin.Y - (PrefetchRadius * TileSize);
            int expandedWidth = viewportWidth + (2 * PrefetchRadius * TileSize);
            int expandedHeight = viewportHeight + (2 * PrefetchRadius * TileSize);

            var prefetchRequests = CalculateTileRequests(
                new PointD(expandedX, expandedY),
                expandedWidth,
                expandedHeight,
                level,
                coordinate
            );

            // Fetch in background without blocking
            _ = Task.Run(async () =>
            {
                foreach (var request in prefetchRequests)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    // Only prefetch if not already in cache
                    string cacheKey = request.GetCacheKey();
                    if (!_tileCache.ContainsKey(cacheKey))
                    {
                        try
                        {
                            await FetchTileAsync(request, cancellationToken);
                        }
                        catch
                        {
                            // Silently ignore prefetch errors
                        }
                    }
                }
            }, cancellationToken);
        }

        #endregion

        #region Cache Management

        /// <summary>
        /// Add tile to cache with LRU eviction
        /// </summary>
        private void AddToCache(string key, SKImage image)
        {
            var cachedTile = new CachedTile(image, key);

            lock (_cacheLock)
            {
                _tileCache[key] = cachedTile;

                // Check if we need to evict
                long currentCacheSize = CalculateTotalCacheSize();

                if (currentCacheSize > _maxCacheSize)
                {
                    EvictLeastRecentlyUsedTiles(currentCacheSize - _maxCacheSize);
                }
            }
        }

        /// <summary>
        /// Evict tiles to free up specified amount of memory
        /// </summary>
        private void EvictLeastRecentlyUsedTiles(long bytesToFree)
        {
            var sortedTiles = new List<CachedTile>(_tileCache.Values);
            sortedTiles.Sort((a, b) => a.LastAccessed.CompareTo(b.LastAccessed));

            long freedBytes = 0;
            foreach (var tile in sortedTiles)
            {
                if (freedBytes >= bytesToFree)
                    break;

                if (_tileCache.TryRemove(tile.Key, out var removed))
                {
                    freedBytes += removed.SizeBytes;
                    removed.Image?.Dispose();
                }
            }
        }

        /// <summary>
        /// Calculate total cache size
        /// </summary>
        private long CalculateTotalCacheSize()
        {
            long total = 0;
            foreach (var tile in _tileCache.Values)
            {
                total += tile.SizeBytes;
            }
            return total;
        }

        /// <summary>
        /// Clear entire cache
        /// </summary>
        public void ClearCache()
        {
            lock (_cacheLock)
            {
                foreach (var tile in _tileCache.Values)
                {
                    tile.Image?.Dispose();
                }
                _tileCache.Clear();
            }
        }

        #endregion

        #region Utility Methods

        /// <summary>
        /// Ensure composite surface exists and is correct size
        /// </summary>
        private void EnsureCompositeSurface(int width, int height)
        {
            if (_compositeSurface == null ||
                _currentSurfaceWidth != width ||
                _currentSurfaceHeight != height)
            {
                _compositeSurface?.Dispose();

                var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
                _compositeSurface = SKSurface.Create(imageInfo);
                _compositeCanvas = _compositeSurface.Canvas;

                _currentSurfaceWidth = width;
                _currentSurfaceHeight = height;
            }
        }

        /// <summary>
        /// Convert raw tile data to SKImage
        /// </summary>
        private SKImage ConvertTileDataToSKImage(byte[] tileData, TileRequest request)
        {
            try
            {
                // Assume RGBA format from source
                int width = (int)request.Bounds.W;
                int height = (int)request.Bounds.H;

                var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);

                using (var data = SKData.CreateCopy(tileData))
                using (var skBitmap = SKBitmap.Decode(data))
                {
                    if (skBitmap != null)
                    {
                        return SKImage.FromBitmap(skBitmap);
                    }
                }

                // If decode fails, try creating from raw RGBA data
                using (var data = SKData.CreateCopy(tileData))
                {
                    var pixmap = new SKPixmap(imageInfo, data.Data, imageInfo.RowBytes);
                    return SKImage.FromPixels(pixmap);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error converting tile data: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Calculate pyramid level from resolution
        /// </summary>
        private int CalculateLevelFromResolution(double resolution)
        {
            if (_isOpenSlide)
            {
                return OpenSlideGTK.TileUtil.GetLevel(_openSlideSource.Schema.Resolutions, resolution);
            }
            else
            {
                return OpenSlideGTK.TileUtil.GetLevel(_slideSource.Schema.Resolutions, resolution);
            }
        }

        /// <summary>
        /// Calculate resolution from pyramid level
        /// </summary>
        private double CalculateResolutionFromLevel(int level)
        {
            if (_isOpenSlide)
            {
                return _openSlideSource.Schema.Resolutions[level].UnitsPerPixel;
            }
            else
            {
                return _slideSource.Schema.Resolutions[level].UnitsPerPixel;
            }
        }

        #endregion

        #region Disposal

        public void Dispose()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();

            ClearCache();

            _compositeSurface?.Dispose();
            _renderSemaphore?.Dispose();

            _pendingTileFetches.Clear();
        }

        #endregion
    }
}