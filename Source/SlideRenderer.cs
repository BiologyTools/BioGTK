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
                        var renderInfos = new List<TileRenderInfo>();
                        foreach (var kv in _uploadedTileInfos)
                        {
                            if (_glArea.HasTileTexture(kv.Key))
                            {
                                renderInfos.Add(CalculateScreenPosition(
                                    kv.Value,
                                    capturedOrigin,
                                    capturedResolution,
                                    capturedLevel,
                                    capturedSchema));
                            }
                        }

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
            // Both tile.Extent and pyramidalOrigin are in world (micron) units.
            // Convert both to level-N screen pixels by dividing by levelUnitsPerPixel.
            double levelUnitsPerPixel = schema.Resolutions[level].UnitsPerPixel;

            // Tile top-left in level-N pixels (Y axis is inverted: world MaxY → screen MinY)
            double tilePixelX = tile.Extent.MinX / levelUnitsPerPixel;
            double tilePixelY = -tile.Extent.MaxY / levelUnitsPerPixel;

            // Origin in level-N pixels
            double originPixelX = pyramidalOrigin.X / levelUnitsPerPixel;
            double originPixelY = pyramidalOrigin.Y / levelUnitsPerPixel;

            float screenX = (float)(tilePixelX - originPixelX);
            float screenY = (float)(tilePixelY - originPixelY);

            float screenWidth  = (float)(tile.Extent.Width  / levelUnitsPerPixel);
            float screenHeight = (float)(tile.Extent.Height / levelUnitsPerPixel);

            return new TileRenderInfo(tile.Index, screenX, screenY, screenWidth, screenHeight);
        }

        public int CurrentLevel => _currentLevel;
        public int CachedTextureCount => _uploadedTiles.Count;
    }
}