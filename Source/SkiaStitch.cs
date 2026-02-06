using AForge;
using BioLib;
using BruTile;
using CSScripting;
using OpenSlideGTK;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BioGTK
{
    /// <summary>
    /// SkiaSharp-based tile stitching pipeline for compositing large pyramidal images
    /// </summary>
    public class SkiaStitchingPipeline : IDisposable
    {
        #region Fields and Properties

        private readonly SlideBase _slideSource;
        private readonly OpenSlideGTK.OpenSlideBase _openSlideSource;
        private readonly bool _isOpenSlide;

        // Tile cache with thread-safe access
        private ConcurrentDictionary<string, CachedTile> _tileCache;
        private int _maxCacheSize;
        private object _cacheLock = new object();

        // Rendering state
        private SKSurface _compositeSurface;
        private SKCanvas _compositeCanvas;
        private int _currentSurfaceWidth = 600;
        private int _currentSurfaceHeight = 400;

        // Async operation management
        private ConcurrentDictionary<string, Task<SKImage>> _pendingTileFetches;

        // Configuration
        public int TileSize { get; set; } = 256;
        public int PrefetchRadius { get; set; } = 1;
        public SKSamplingOptions Sampling { get; set; } = SKSamplingOptions.Default;
        public bool EnablePrefetch { get; set; } = false;

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
            public int Level
            {
                get => Index.Level;
                set => Index = new TileIndex(Index.Col, Index.Row, value);
            }

            // FIXED: Col is X, Row is Y
            public int TileX
            {
                get => Index.Col * 256;  // Col = X coordinate
                set
                {
                    int col = value / 256;
                    Index = new TileIndex(col, Index.Row, Level);
                }
            }

            public int TileY
            {
                get => Index.Row * 256;  // Row = Y coordinate
                set
                {
                    int row = value / 256;
                    Index = new TileIndex(Index.Col, row, Level);
                }
            }

            public ZCT Coordinate { get; set; }
            public Extent Extent { get; set; }
            public TileIndex Index { get; set; }

            public RectangleD Bounds
            {
                get
                {
                    return new RectangleD(
                        Extent.MinX,
                        Extent.MinY,
                        Extent.MaxX - Extent.MinX,
                        Extent.MaxY - Extent.MinY
                    );
                }
            }

            public TileInfo TileInfo
            {
                get => new TileInfo { Index = Index, Extent = Extent };
                set
                {
                    Index = value.Index;
                    Extent = value.Extent;
                }
            }

            public TileRequest(TileInfo tf, ZCT coord)
            {
                Index = tf.Index;
                Extent = tf.Extent;
                Coordinate = coord;
            }

            public string GetCacheKey()
            {
                // Use Col/Row directly to avoid confusion
                return $"{Index.Col}_{Index.Row}_{Level}_{Coordinate.Z}_{Coordinate.C}_{Coordinate.T}";
            }
        }
        #endregion

        #region Constructor and Initialization

        /// <summary>
        /// Initialize pipeline for OpenSlide source
        /// </summary>
        public SkiaStitchingPipeline(OpenSlideGTK.OpenSlideBase openSlideSource, int maxCacheSizeMB = 512)
        {
            _openSlideSource = openSlideSource ?? throw new ArgumentNullException(nameof(openSlideSource));
            _isOpenSlide = true;

            InitializeCommon(maxCacheSizeMB);
        }

        /// <summary>
        /// Initialize pipeline for BioLib slide source
        /// </summary>
        public SkiaStitchingPipeline(SlideBase slideSource, int maxCacheSizeMB = 512)
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
        }

        #endregion

        #region Public Stitching Methods
        private readonly SemaphoreSlim _renderSemaphore = new SemaphoreSlim(1, 1);
        /// <summary>
        /// Stitch tiles for a given viewport asynchronously
        /// </summary>
        public async Task<SKImage> StitchViewportAsync(
            PointD origin,
            double resolution,
            ZCT coordinate,
            int viewportWidth = 600,
            int viewportHeight = 400)
        {
            // Calculate viewport parameters
            int level = CalculateLevelFromResolution(resolution);
            var tileRequests = CalculateTileRequests(origin, viewportWidth, viewportHeight, level, coordinate, resolution);
            if (viewportWidth <= 1 || viewportHeight <= 1)
            {
                viewportWidth = 600;
                viewportHeight = 400;
            }
            // Create a NEW surface for this render - no shared state, fully parallel-safe
            using (var surface = SKSurface.Create(new SKImageInfo(
                viewportWidth,
                viewportHeight,
                SKColorType.Rgba8888,
                SKAlphaType.Premul)))
            {
                if (surface == null)
                {
                    Console.WriteLine("Failed to create SKSurface");
                    return null;
                }

                var canvas = surface.Canvas;
                canvas.Clear(SKColors.Red);

                // Fetch and composite all tiles
                await CompositeTilesAsync(canvas, tileRequests, origin, resolution);

                // Create and return snapshot
                return surface.Snapshot();
            }  // Surface automatically disposed here
        }

        /// <summary>
        /// Stitch a specific region synchronously (for smaller areas)
        /// </summary>
        public SKImage StitchRegion(
            RectangleD region,
            int level,
            ZCT coordinate, double resolution)
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
                        DrawTileToCanvas(canvas, tileImage, request, new AForge.PointD(region.X,region.Y), resolution);
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
        private async Task<SKImage> FetchTileAsync(TileRequest request)
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
                    byte[] tileData = await FetchTileDataAsync(request);
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
            });

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
        private async Task<byte[]> FetchTileDataAsync(TileRequest request)
        {
            Console.WriteLine($"\n--- Fetching tile data for Level {request.Level}, Index[{request.Index.Col},{request.Index.Row}] ---");
            Console.WriteLine($"  Extent: ({request.Extent.MinX:F0}, {request.Extent.MinY:F0}) to ({request.Extent.MaxX:F0}, {request.Extent.MaxY:F0})");

            try
            {
                if (_isOpenSlide)
                {
                    // Use BruTile's GetTile method directly
                    byte[] tileData = _openSlideSource.GetTile(request.TileInfo);

                    if (tileData == null || tileData.Length == 0)
                    {
                        Console.WriteLine($"  ERROR: OpenSlide returned null/empty tile data");
                        return null;
                    }

                    Console.WriteLine($"  OpenSlide returned {tileData.Length} bytes");
                    return tileData;
                }
                else
                {
                    // For BioLib SlideBase
                    var sliceInfo = new BioLib.SliceInfo(
                        request.Extent.MinX,
                        request.Extent.MinY,
                        request.Bounds.W,
                        request.Bounds.H,
                        CalculateResolutionFromLevel(request.Level),
                        request.Coordinate
                    );

                    var origin = new PointD(request.Extent.MinX, request.Extent.MinY);
                    var size = new AForge.Size((int)request.Bounds.W, (int)request.Bounds.H);

                    byte[] tileData = await _slideSource.GetSlice(sliceInfo, origin, size);

                    if (tileData == null || tileData.Length == 0)
                    {
                        Console.WriteLine($"  ERROR: SlideBase returned null/empty tile data");
                        return null;
                    }

                    Console.WriteLine($"  SlideBase returned {tileData.Length} bytes");
                    return tileData;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  EXCEPTION in FetchTileDataAsync: {ex.Message}");
                Console.WriteLine($"  {ex.StackTrace}");
                return null;
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
                var sliceInfo = new BioLib.SliceInfo(
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
    SKCanvas canvas,
    List<TileRequest> tileRequests,
    PointD origin,
    double resolution)
        {
            Console.WriteLine($"\n=== CompositeTilesAsync ===");
            Console.WriteLine($"Compositing {tileRequests.Count} tiles");
            Console.WriteLine($"Origin (pixels): ({origin.X:F2}, {origin.Y:F2}), Resolution: {resolution:F4}");

            if (tileRequests.Count == 0)
            {
                Console.WriteLine("⚠ WARNING: No tile requests to composite!");
                return;
            }

            int drawnCount = 0;
            int failedCount = 0;

            // Fetch all tiles in parallel
            var tileTasks = tileRequests.Select(async req =>
            {
                var image = await FetchTileAsync(req);
                return new { Request = req, Image = image };
            }).ToArray();

            var results = await Task.WhenAll(tileTasks);

            using (var paint = new SKPaint())
            {
                paint.IsAntialias = false;

                foreach (var result in results)
                {
                    if (result.Image == null)
                    {
                        failedCount++;
                        continue;
                    }

                    var request = result.Request;

                    // Convert tile world space extent to pixel coordinates
                    // Tile extents are in world space (inverted Y from schema)
                    float tilePixelMinX = (float)(request.Extent.MinX / resolution);
                    float tilePixelMaxY = (float)(request.Extent.MaxY / resolution); // Top in inverted Y
                    float tilePixelWidth = (float)((request.Extent.MaxX - request.Extent.MinX) / resolution);
                    float tilePixelHeight = (float)((request.Extent.MaxY - request.Extent.MinY) / resolution);

                    // Position on canvas relative to origin (in pixel space)
                    float destX = tilePixelMinX - (float)origin.X;
                    float destY = tilePixelMaxY - (float)origin.Y; // MaxY is top in inverted Y

                    var destRect = new SKRect(destX, destY, destX + tilePixelWidth, destY + tilePixelHeight);

                    Console.WriteLine($"    Tile [{request.Index.Col},{request.Index.Row}]:");
                    Console.WriteLine($"      World Extent: ({request.Extent.MinX:F0}, {request.Extent.MinY:F0}) to ({request.Extent.MaxX:F0}, {request.Extent.MaxY:F0})");
                    Console.WriteLine($"      Pixel Pos: ({tilePixelMinX:F1}, {tilePixelMaxY:F1})");
                    Console.WriteLine($"      Canvas Rect: ({destX:F1}, {destY:F1}) size {tilePixelWidth:F1}x{tilePixelHeight:F1}");

                    canvas.DrawImage(result.Image, destRect, paint);
                    drawnCount++;
                }
            }

            Console.WriteLine($"\n✓ Composited {drawnCount} tiles, {failedCount} failed");
        }
        /// <summary>
        /// Draw individual tile to canvas with proper positioning
        /// </summary>
        private void DrawTileToCanvas(SKCanvas canvas, SKImage Image, TileRequest request, PointD viewportOrigin, double resolution)
        {
            // Screen X: (Tile Left - Viewport Left) / resolution
            // Standard left-to-right logic.
            float destX = (float)((request.Extent.MinX - viewportOrigin.X) / resolution);

            // Screen Y: (Viewport Top World - Tile Top World) / resolution
            // In BruTile, MaxY is the Top edge of the tile.
            // viewportOrigin.Y is the Top edge of the visible screen.
            float destY = (float)((viewportOrigin.Y - request.Extent.MaxY) / resolution);

            // Convert world dimensions to pixel dimensions
            float destW = (float)(request.Extent.Width / resolution);
            float destH = (float)(request.Extent.Height / resolution);

            var destRect = new SKRect(destX, destY, destX + destW, destY + destH);

            if (Image != null)
            {
                // Use Sampling options for smooth zooming
                canvas.DrawImage(Image, destRect, Sampling);
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
        private List<TileRequest> CalculateTileRequests(PointD origin, int viewportWidth, int viewportHeight, int level, ZCT coordinate, double resolution)
        {
            // In BruTile (OSM), Y increases UPWARDS.
            // origin.Y is the TOP-LEFT of your view in world space.

            double minX = origin.X;
            double maxX = origin.X + (viewportWidth * resolution);

            // MaxY is the Top of the viewport
            double maxY = origin.Y;
            // MinY is the Bottom of the viewport (Origin minus the physical height in world units)
            double minY = origin.Y - (viewportHeight * resolution);

            var viewportExtent = new Extent(minX, minY, maxX, maxY);

            IEnumerable<TileInfo> tileInfos;
            if (_isOpenSlide)
            {
                tileInfos = _openSlideSource.Schema.GetTileInfos(viewportExtent, level);
            }
            else
            {
                tileInfos = _slideSource.Schema.GetTileInfos(viewportExtent, level);
            }

            var requests = new List<TileRequest>();
            foreach (var info in tileInfos)
            {
                requests.Add(new TileRequest(info, coordinate));
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
                    Extent bounds = new Extent(
                        tileX * TileSize,
                        tileY * TileSize,
                        TileSize,
                        TileSize
                    );
                    TileInfo tf = new TileInfo
                    {
                        Index = new TileIndex(tileX, tileY, level),
                        Extent = bounds
                    };
                    requests.Add(new TileRequest(tf, coordinate));
                }
            }
            return requests;
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

                // Try decoding as compressed image first
                using (var data = SKData.CreateCopy(tileData))
                using (var skBitmap = SKBitmap.Decode(data))
                {
                    if (skBitmap != null)
                    {
                        return SKImage.FromBitmap(skBitmap);
                    }
                }

                // If decode fails, create from raw RGBA data
                // CRITICAL: Don't dispose SKData until image is disposed
                var rawData = SKData.CreateCopy(tileData);
                var pixmap = new SKPixmap(imageInfo, rawData.Data, imageInfo.RowBytes);

                // Use release delegate to properly manage lifetime
                return SKImage.FromPixels(pixmap, (address, context) =>
                {
                    rawData?.Dispose();
                }, null);
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
            ClearCache();
            _pendingTileFetches.Clear();;
        }
        #endregion
    }
}