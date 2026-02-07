using AForge;
using BioLib;
using BruTile;
using CSScripting;
using OpenSlideGTK;
using org.apache.xmlgraphics.image.codec.tiff;
using SkiaSharp;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Runtime.InteropServices;
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
                canvas.Clear(SKColors.Gray);

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
            ZCT coordinate, double resolution, int pxwidth, int pxheight)
        {
            var tileRequests = CalculateTileRequests(
                new PointD(region.X,region.Y),pxwidth, pxheight, level, coordinate, resolution);

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
                        DrawTileToCanvas(canvas,request, new AForge.PointD(region.X,region.Y), resolution,  tileImage);
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
        private async Task<SKImage> FetchTileAsync(TileRequest request, ZCT coordinate)
        {
            string key = request.GetCacheKey();

            // 1. Check cache again to be sure
            if (_tileCache.TryGetValue(key, out var cached)) return cached.Image;

            // 2. Check if already being fetched
            if (_pendingTileFetches.TryGetValue(key, out var pendingTask)) return await pendingTask;

            // 3. Create the fetch task
            var fetchTask = Task.Run(async () =>
            {
                try
                {
                    byte[] data;
                    if (_isOpenSlide)
                    {
                        TileInfo tf = new TileInfo { Index = request.Index, Extent = request.Extent };
                        // OpenSlide usually takes Level, X-index, Y-index
                        data = _openSlideSource.GetTile(tf);
                    }
                    else
                    {
                        // SlideBase usually takes the Index object and ZCT
                        TileInfo tf = new TileInfo { Index = request.Index, Extent = request.Extent };
                        // OpenSlide usually takes Level, X-index, Y-index
                        data = _slideSource.GetTile(tf,request.Coordinate);
                    }

                    if (data == null || data.Length == 0) return null;

                    // Convert to Skia Image (using the shear-fix logic from before)
                    var levelResolution = CalculateResolutionFromLevel(request.Index.Level);
                    var image = ConvertTileDataToSKImage(data, request);

                    if (image != null)
                    {
                        var cachedTile = new CachedTile(image, DateTime.Now.ToString());
                        _tileCache.AddOrUpdate(key, cachedTile, (k, old) => cachedTile);
                        return image;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Fetch error for tile {key}: {ex.Message}");
                }
                finally
                {
                    _pendingTileFetches.TryRemove(key, out _);
                }
                return null;
            });

            _pendingTileFetches[key] = fetchTask;
            return await fetchTask;
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
                    TileInformation tf = new TileInformation(request.Index, request.Extent, request.Coordinate);
                    byte[] tileData = await _slideSource.GetTileAsync(tf, request.Coordinate);

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
        /// Composite all tiles onto the canvas using a unified coordinate system
        /// </summary>
        private async Task CompositeTilesAsync(
            SKCanvas canvas,
            List<TileRequest> tileRequests,
            PointD origin,
            double resolution)
        {
            if (tileRequests.Count == 0) return;

            // Fetch all tiles in parallel
            var tileTasks = tileRequests.Select(async req =>
            {
                var image = await FetchTileAsync(req, req.Coordinate);
                return new { Request = req, Image = image };
            }).ToArray();

            var results = await Task.WhenAll(tileTasks);

            using (var paint = new SKPaint { IsAntialias = false })
            {
                foreach (var result in results)
                {
                    if (result.Image == null) continue;
                    DrawTileToCanvas(canvas, result.Request, origin, resolution, result.Image, paint);
                }
            }
        }
        /// <summary>
        /// Unified drawing method to handle BruTile Y-up to Skia Y-down conversion
        /// </summary>
        private void DrawTileToCanvas(
            SKCanvas canvas,
            TileRequest request,
            PointD viewportOrigin,
            double resolution,
            SKImage tileImage,
            SKPaint paint = null)
        {
            if (tileImage == null) return;

            // X: (Tile Left - Viewport Left) / Resolution
            float destX = (float)((request.Extent.MinX - viewportOrigin.X) / resolution);

            // Y: (Viewport Top - Tile Top) / Resolution 
            // In BruTile, MaxY is the Top of the tile
            float destY = (float)((viewportOrigin.Y - request.Extent.MaxY) / resolution);

            // Calculate dimensions based on the Extent to ensure precision
            float destW = (float)(request.Extent.Width / resolution);
            float destH = (float)(request.Extent.Height / resolution);

            var destRect = new SKRect(destX, destY, destX + destW, destY + destH);

            // Use Sampling if provided in the class, otherwise use the paint object
            if (paint != null)
                canvas.DrawImage(tileImage, destRect, paint);
            else
                canvas.DrawImage(tileImage, destRect, Sampling);
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
            // origin.X/Y is the Top-Left of the viewport in World Space.
            // In BruTile (OSM/TMS style), Y increases UPWARDS.

            double minX = origin.X;
            double maxX = origin.X + (viewportWidth * resolution);

            // Because Y increases UP, the Top of the screen (origin.Y) 
            // is the MAXIMUM Y value.
            double maxY = origin.Y;

            // The Bottom of the screen is the MINIMUM Y value.
            double minY = origin.Y - (viewportHeight * resolution);

            // Create the extent (MinX, MinY, MaxX, MaxY)
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
        // SkiaStitch.cs - Update this method
        private SKImage ConvertTileDataToSKImage(byte[] tileData, TileRequest request)
        {
            try
            {
                // 1. Try standard decoding (JPEG/PNG/etc)
                using (var data = SKData.CreateCopy(tileData))
                {
                    var image = SKImage.FromEncodedData(data);
                    if (image != null) return image;
                }

                // 2. Fallback for Raw Pixels (The "Shear" Fix)
                // Use the Schema's defined TileWidth/Height, NOT the calculated resolution width
                int width = _isOpenSlide ? _openSlideSource.Schema.GetTileWidth(request.Index.Level)
                                         : _slideSource.Schema.GetTileWidth(request.Index.Level);
                int height = _isOpenSlide ? _openSlideSource.Schema.GetTileHeight(request.Index.Level)
                                          : _slideSource.Schema.GetTileHeight(request.Index.Level);

                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                // If decode fails, create from raw RGBA data
                // CRITICAL: Don't dispose SKData until image is disposed
                var imageInfo = new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
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