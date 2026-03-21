using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AForge;
using BruTile;
using Gtk;
using OpenSlideGTK;

namespace BioGTK
{
    public class SlideRenderer
    {
        private readonly SlideGLArea _glArea;
        private OpenSlideBase _openSlideBase;
        private SlideBase _slideBase;
        private bool _useOpenSlide;
        private BioLib.TileCache _tileCache;
        private OpenSlideGTK.TileCache _openTileCache;
        private HashSet<TileIndex> _uploadedTiles = new();
        // Stores the TileInfo (including world-space Extent) for every tile in the GPU cache
        // so the render list can be rebuilt from all cached tiles, not just those fetched this frame.
        private Dictionary<TileIndex, TileInfo> _uploadedTileInfos = new();
        private int _currentLevel = -1;
        private readonly SemaphoreSlim _renderSemaphore = new SemaphoreSlim(1, 1);
        public bool LastRenderSkipped { get; private set; }
        // Latest args saved when a render is skipped so the re-queued render uses current state.
        private volatile bool _pendingRequeue = false;

        public SlideRenderer(SlideGLArea glArea)
        {
            _glArea = glArea;
        }

        public void SetSource(OpenSlideBase source)
        {
            _openSlideBase = source;
            _slideBase = null;
            _useOpenSlide = true;
            _openTileCache = new OpenSlideGTK.TileCache(source, 200);
            ClearCache();
        }

        public void SetSource(SlideBase source)
        {
            _slideBase = source;
            _openSlideBase = null;
            _useOpenSlide = false;
            _tileCache = new BioLib.TileCache(source, 200);
            ClearCache();
        }

        public void ClearCache()
        {
            _glArea.ClearTextureCache();
            _uploadedTiles.Clear();
            _uploadedTileInfos.Clear();
            _currentLevel = -1;
        }

        public async Task UpdateViewAsync(
            PointD pyramidalOrigin,
            int viewportWidth,
            int viewportHeight,
            double resolution,
            ZCT coordinate)
        {
            if (_openSlideBase == null && _slideBase == null)
                return;
            if (viewportWidth <= 1 && viewportHeight <= 1)
                return;

            // Prevent concurrent renders racing to update the GL tile set.
            if (!_renderSemaphore.Wait(0))
            {
                LastRenderSkipped = true;
                _pendingRequeue = true;
                return;
            }
            LastRenderSkipped = false;

            try
            {
                var schema = _useOpenSlide ? _openSlideBase.Schema : _slideBase.Schema;

                int level = TileUtil.GetLevel(schema.Resolutions, resolution);
                var levelRes = schema.Resolutions[level];
                double levelUnitsPerPixel = levelRes.UnitsPerPixel;

                // pyramidalOrigin is in world (micron) units, matching the schema's world coordinate space.
                double tileWorldW = levelRes.TileWidth  * levelUnitsPerPixel;
                double tileWorldH = levelRes.TileHeight * levelUnitsPerPixel;

                // Exact viewport extent (used for the render list)
                double minX =  pyramidalOrigin.X;
                double minY = -pyramidalOrigin.Y;
                double width  = viewportWidth  * resolution;
                double height = viewportHeight * resolution;
                var viewportExtent = new Extent(minX, minY - height, minX + width, minY);

                // Padded extent — 1 tile on every side — used for prefetch so surrounding
                // tiles are already in the cache when the user pans into them.
                const int PAD = 2;
                var fetchExtent = new Extent(
                    viewportExtent.MinX - PAD * tileWorldW,
                    viewportExtent.MinY - PAD * tileWorldH,
                    viewportExtent.MaxX + PAD * tileWorldW,
                    viewportExtent.MaxY + PAD * tileWorldH);

                var fetchTileInfos = schema.GetTileInfos(fetchExtent, level).ToList();
                if (fetchTileInfos.Count == 0)
                {
                    var pixelExtent = OpenSlideGTK.ExtentEx.WorldToPixelInvertedY(fetchExtent, levelUnitsPerPixel);
                    fetchTileInfos = schema.GetTileInfos(pixelExtent, level).ToList();
                }

                // ---------------------------------------------------------------
                // Phase 1: Fetch tile data for the padded extent (includes surroundings).
                // ---------------------------------------------------------------
                if (_openSlideBase != null)
                    await _openSlideBase.FetchTilesAsync(fetchTileInfos, level, coordinate);
                else
                    await _slideBase.FetchTilesAsync(fetchTileInfos, level, coordinate, pyramidalOrigin, new Size(viewportWidth, viewportHeight));

                var pendingUploads = new List<(TileIndex index, byte[] data, int tW, int tH)>();
                foreach (var tileInfo in fetchTileInfos)
                {
                    if (!_glArea.HasTileTexture(tileInfo.Index))
                    {
                        byte[] tileData;
                        if (_useOpenSlide)
                            tileData = await _openSlideBase.GetTileAsync(tileInfo);
                        else
                            tileData = await _slideBase.GetTileAsync(tileInfo, coordinate);
                        if (tileData != null)
                        {
                            int tW = (int)Math.Round(tileInfo.Extent.Width  / levelUnitsPerPixel);
                            int tH = (int)Math.Round(tileInfo.Extent.Height / levelUnitsPerPixel);
                            tW = Math.Max(1, tW);
                            tH = Math.Max(1, tH);

                            int actualPixels = tileData.Length / 4;
                            if (actualPixels > 0 && actualPixels < tW * tH)
                            {
                                if (actualPixels < tW) tW = actualPixels;
                                tH = Math.Max(1, actualPixels / tW);
                            }

                            pendingUploads.Add((tileInfo.Index, tileData, tW, tH));
                        }
                    }
                }

                // ---------------------------------------------------------------
                // Phase 2: Upload textures and set render list on the GTK main
                // thread — GL calls require the main thread context.
                // ---------------------------------------------------------------
                var capturedFetchTileInfos   = fetchTileInfos;
                var capturedUploads          = pendingUploads;
                var capturedOrigin           = pyramidalOrigin;
                var capturedResolution       = resolution;
                var capturedLevel            = level;
                var capturedSchema           = schema;
                var capturedLevel0UPP        = schema.Resolutions.ContainsKey(0)
                                                ? schema.Resolutions[0].UnitsPerPixel : 1.0;

                Gtk.Application.Invoke((s, a) =>
                {
                    try
                    {
                        // On level change, evict stale textures and their infos.
                        if (capturedLevel != _currentLevel)
                        {
                            _glArea.ReleaseLevelTextures(_currentLevel);
                            var staleKeys = _uploadedTileInfos.Keys
                                .Where(k => k.Level != capturedLevel).ToList();
                            foreach (var k in staleKeys)
                            {
                                _uploadedTiles.Remove(k);
                                _uploadedTileInfos.Remove(k);
                            }
                            _currentLevel = capturedLevel;
                        }

                        // Upload new tiles and record their TileInfo for future render passes.
                        foreach (var (idx, data, tW, tH) in capturedUploads)
                        {
                            if (!_glArea.HasTileTexture(idx))
                            {
                                _glArea.UploadTileTexture(idx, data, tW, tH);
                                _uploadedTiles.Add(idx);
                            }
                        }
                        foreach (var tileInfo in capturedFetchTileInfos)
                        {
                            if (_glArea.HasTileTexture(tileInfo.Index))
                                _uploadedTileInfos[tileInfo.Index] = tileInfo;
                        }

                        // Build render list from ALL tiles currently in the GPU cache.
                        // This means tiles cached from previous positions are still drawn
                        // while the user pans, eliminating dark edges.
                        // Viewport dimensions are needed for screen-space culling.
                        int vpW = _glArea.AllocatedWidth;
                        int vpH = _glArea.AllocatedHeight;

                        // Schema extent is in pixels; tile extents are in world (micron) units.
                        // Convert schema extent to world units using level-0 UnitsPerPixel.
                        double l0upp = capturedSchema.Resolutions.ContainsKey(0)
                            ? capturedSchema.Resolutions[0].UnitsPerPixel : 1.0;
                        double wMaxX =  capturedSchema.Extent.MaxX * l0upp;
                        double wMinX =  capturedSchema.Extent.MinX * l0upp;
                        double wMaxY =  capturedSchema.Extent.MaxY * l0upp; // 0
                        double wMinY =  capturedSchema.Extent.MinY * l0upp; // negative

                        var renderInfos = new List<TileRenderInfo>();
                        foreach (var kv in _uploadedTileInfos)
                        {
                            if (!_glArea.HasTileTexture(kv.Key))
                                continue;

                            // Cull tiles entirely outside the image in world units.
                            var te = kv.Value.Extent;
                            if (te.MaxX <= wMinX || te.MinX >= wMaxX ||
                                te.MaxY <= wMinY || te.MinY >= wMaxY)
                                continue;

                            var ri = CalculateScreenPosition(
                                kv.Value,
                                capturedOrigin,
                                capturedResolution,
                                capturedLevel,
                                capturedSchema);

                            // Screen-space cull: skip tiles fully outside the visible viewport.
                            if (ri.ScreenX + ri.ScreenWidth  <= 0 || ri.ScreenX >= vpW ||
                                ri.ScreenY + ri.ScreenHeight <= 0 || ri.ScreenY >= vpH)
                                continue;

                            renderInfos.Add(ri);
                        }

                        // Compute the image boundary in screen pixels and pass to the GL area
                        // for scissor clipping. This definitively prevents any tile data
                        // from being rendered outside the image boundary regardless of tile
                        // extent rounding or coordinate space mismatches.
                        double imgWorldMaxX = capturedSchema.Extent.MaxX * capturedLevel0UPP;
                        double imgWorldMaxY = capturedSchema.Extent.MaxY * capturedLevel0UPP; // 0
                        double imgWorldMinX = capturedSchema.Extent.MinX * capturedLevel0UPP;
                        double imgWorldMinY = capturedSchema.Extent.MinY * capturedLevel0UPP;

                        float imgScreenX = (float)(imgWorldMinX / capturedResolution - capturedOrigin.X / capturedResolution);
                        float imgScreenY = (float)(-imgWorldMaxY / capturedResolution - capturedOrigin.Y / capturedResolution);
                        float imgScreenW = (float)((imgWorldMaxX - imgWorldMinX) / capturedResolution);
                        // imgWorldMinY is negative so height = |imgWorldMinY| / res
                        float imgScreenH = (float)(Math.Abs(imgWorldMinY - imgWorldMaxY) / capturedResolution);

                        _glArea.ImageScreenX = imgScreenX;
                        _glArea.ImageScreenY = imgScreenY;
                        _glArea.ImageScreenW = imgScreenW;
                        _glArea.ImageScreenH = imgScreenH;

                        _glArea.SetTilesToRender(renderInfos);
                        _glArea.RequestRedraw();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"SlideRenderer GL upload error: {ex.Message}");
                    }
                    finally
                    {
                        _renderSemaphore.Release();
                        // If any render was skipped while we were busy, fire one more
                        // pass now with the latest viewer state.
                        if (_pendingRequeue)
                        {
                            _pendingRequeue = false;
                            App.viewer?.RequestDeferredRender();
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SlideRenderer.UpdateViewAsync error: {ex.Message}");
                _renderSemaphore.Release();
            }
        }

        private async Task<byte[]> FetchTileAsync(TileInfo tileInfo, int level, ZCT coordinate)
        {
            try
            {
                if (_useOpenSlide)
                {
                    var bt = await _openTileCache.GetTile(new OpenSlideGTK.Info(coordinate, tileInfo.Index, tileInfo.Extent, level));
                    return bt;
                }
                else
                {
                    return await _tileCache.GetTile(new BioLib.TileInformation(tileInfo.Index, tileInfo.Extent, coordinate));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching tile {tileInfo.Index}: {ex.Message}");
                return null;
            }
        }

        private TileRenderInfo CalculateScreenPosition(
            TileInfo tile,
            PointD pyramidalOrigin,
            double viewResolution,
            int level,
            ITileSchema schema)
        {
            // tile.Extent and pyramidalOrigin are both in world units (microns).
            // viewResolution converts world units → screen pixels.

            double r = schema.Resolutions[level].UnitsPerPixel;

            // Image boundary in the same world units as tile extents.
            // Schema.Extent is in pixels; multiply by level-0 UPP to get world units.
            double level0UPP = schema.Resolutions.ContainsKey(0)
                ? schema.Resolutions[0].UnitsPerPixel : r;
            double imgMaxX =  schema.Extent.MaxX * level0UPP;
            double imgMinX =  schema.Extent.MinX * level0UPP;
            double imgMaxY =  schema.Extent.MaxY * level0UPP; // 0
            double imgMinY =  schema.Extent.MinY * level0UPP; // negative

            // Clamp tile world extent to image boundary.
            double clampedMinX = Math.Max(tile.Extent.MinX, imgMinX);
            double clampedMaxX = Math.Min(tile.Extent.MaxX, imgMaxX);
            double clampedMinY = Math.Max(tile.Extent.MinY, imgMinY);
            double clampedMaxY = Math.Min(tile.Extent.MaxY, imgMaxY);

            if (clampedMaxX <= clampedMinX || clampedMaxY <= clampedMinY)
                return new TileRenderInfo(tile.Index, -9999, -9999, 0, 0);

            // UV sub-region within the texture for the clamped portion.
            double tw = tile.Extent.Width;
            double th = tile.Extent.Height;
            float u0 = (float)((clampedMinX - tile.Extent.MinX) / tw);
            float u1 = (float)((clampedMaxX - tile.Extent.MinX) / tw);
            float v0 = (float)((tile.Extent.MaxY - clampedMaxY) / th);
            float v1 = (float)((tile.Extent.MaxY - clampedMinY) / th);

            // Screen position using viewResolution (continuous zoom).
            double originScreenX = pyramidalOrigin.X / viewResolution;
            double originScreenY = pyramidalOrigin.Y / viewResolution;

            float screenX      = (float)(clampedMinX /  viewResolution - originScreenX);
            float screenY      = (float)(-clampedMaxY / viewResolution - originScreenY);
            float screenWidth  = (float)((clampedMaxX - clampedMinX) / viewResolution);
            float screenHeight = (float)((clampedMaxY - clampedMinY) / viewResolution);

            return new TileRenderInfo(tile.Index, screenX, screenY, screenWidth, screenHeight, u0, v0, u1, v1);
        }

        public int CurrentLevel => _currentLevel;
        public int CachedTextureCount => _uploadedTiles.Count;
    }
}