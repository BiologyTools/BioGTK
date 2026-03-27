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
        private const bool DiagnosticLogging = true;
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
        // Bumped whenever image state changes in a way that invalidates the
        // current render result, such as threshold or channel-range updates.
        private volatile int _renderStateVersion = 0;
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
            _tileCache?.Clear();
            _openTileCache?.Clear();
            _slideBase?.cache?.Clear();
            _openSlideBase?.cache?.Clear();
        }

        /// <summary>
        /// Marks all in-flight tile work as stale. Call this before changing
        /// threshold/range state so old tiles cannot overwrite the current view.
        /// </summary>
        public void InvalidateRenderState()
        {
            Interlocked.Increment(ref _renderStateVersion);
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

            // Only allow one render pass at a time. Without this gate, rapid pan
            // events can pile up overlapping async tile fetches and keep large
            // byte[] buffers alive until every stale render finishes.
            if (!_renderSemaphore.Wait(0))
            {
                LastRenderSkipped = true;
                return;
            }

            LastRenderSkipped = false;

            try
            {
            await EnsureGlobalDisplayRangeAsync(coordinate);

            var schema = _useOpenSlide ? _openSlideBase.Schema : _slideBase.Schema;

            int level = TileUtil.GetLevel(schema.Resolutions, resolution);
            var levelRes = schema.Resolutions[level];
            double levelUnitsPerPixel = levelRes.UnitsPerPixel;

            LogDiag($"[UpdateViewAsync] type={(_useOpenSlide ? "openslide" : "slidebase")} level={level} res={resolution:F3} upp={levelUnitsPerPixel:F3} viewport={viewportWidth}x{viewportHeight} origin=({pyramidalOrigin.X:F2},{pyramidalOrigin.Y:F2}) schemaExtent=({schema.Extent.MinX:F0},{schema.Extent.MinY:F0},{schema.Extent.MaxX:F0},{schema.Extent.MaxY:F0})");

            if (level != _currentLevel)
            {
                _glArea.ReleaseLevelTextures(_currentLevel);
                _currentLevel = level;
            }

            // Keep renderer-side bookkeeping scoped to the current viewport.
            // The GL area already owns the actual texture cache; these collections
            // are only used for the current render pass and should not accumulate
            // every tile ever seen while panning.
            _uploadedTiles.Clear();
            _uploadedTileInfos.Clear();

            double minX = pyramidalOrigin.X;
            double width = viewportWidth * resolution;
            double height = viewportHeight * resolution;
            var viewportExtent = new Extent(minX, -pyramidalOrigin.Y - height, minX + width, -pyramidalOrigin.Y);

            // Prefetch one tile beyond each viewport edge so partially visible
            // edge tiles are available without overfetching an arbitrary 2x area.
            double tileMarginX = levelRes.TileWidth * levelUnitsPerPixel;
            double tileMarginY = levelRes.TileHeight * levelUnitsPerPixel;
            var fetchExtent = new Extent(
                viewportExtent.MinX - tileMarginX,
                viewportExtent.MinY - tileMarginY,
                viewportExtent.MaxX + tileMarginX,
                viewportExtent.MaxY + tileMarginY);

            // BioLib's own tile-query path converts the viewer extent into the
            // schema's pixel-space, inverted-Y coordinates before calling BruTile.
            // Using the raw world extent first can return a non-empty but incorrect
            // subset of tiles for Zarr, so prefer the converted extent and only
            // fall back to the raw world extent if it still yields nothing.
            var lookupExtent = BioLib.ExtentEx.WorldToPixelInvertedY(fetchExtent, levelUnitsPerPixel);
            var tileInfos = schema.GetTileInfos(lookupExtent, level).ToList();
            var fallbackInfos = schema.GetTileInfos(fetchExtent, level).ToList();

            if (fallbackInfos.Count > 0)
            {
                var seen = new HashSet<TileIndex>(tileInfos.Select(t => t.Index));
                foreach (var info in fallbackInfos)
                {
                    if (seen.Add(info.Index))
                        tileInfos.Add(info);
                }
            }

            LogDiag($"[UpdateViewAsync] fetchExtent=({fetchExtent.MinX:F0},{fetchExtent.MinY:F0},{fetchExtent.MaxX:F0},{fetchExtent.MaxY:F0}) lookupExtent=({lookupExtent.MinX:F0},{lookupExtent.MinY:F0},{lookupExtent.MaxX:F0},{lookupExtent.MaxY:F0}) tileInfos={tileInfos.Count} fallbackInfos={fallbackInfos.Count}");

            var renderInfos = new List<TileRenderInfo>();
            foreach (var tileInfo in tileInfos)
            {
                var fetchTileInfo = tileInfo;
                var renderTileInfo = tileInfo;

                if (!_glArea.HasTileTexture(fetchTileInfo.Index))
                {
                    byte[] tileData = await FetchTileAsync(fetchTileInfo, level, coordinate);
                    if (tileData != null)
                    {
                        // Upload using the schema tile size for this level.
                        // The fetch path already pads edge tiles to the full tile
                        // buffer, so deriving dimensions from the extent can shrink
                        // the GL texture and show only the top-left corner.
                        int tW = levelRes.TileWidth;
                        int tH = levelRes.TileHeight;

                        _glArea.UploadTileTexture(fetchTileInfo.Index, tileData, tW, tH);
                        _uploadedTiles.Add(fetchTileInfo.Index);
                        _uploadedTileInfos[fetchTileInfo.Index] = renderTileInfo;
                        LogDiag($"[UpdateViewAsync] uploaded tile={fetchTileInfo.Index} bytes={tileData.Length} texSize={tW}x{tH} extent=({fetchTileInfo.Extent.MinX:F0},{fetchTileInfo.Extent.MinY:F0},{fetchTileInfo.Extent.MaxX:F0},{fetchTileInfo.Extent.MaxY:F0})");
                    }
                    else
                    {
                        LogDiag($"[UpdateViewAsync] fetch returned null tile={fetchTileInfo.Index} extent=({fetchTileInfo.Extent.MinX:F0},{fetchTileInfo.Extent.MinY:F0},{fetchTileInfo.Extent.MaxX:F0},{fetchTileInfo.Extent.MaxY:F0})");
                    }
                }

                if (_glArea.HasTileTexture(fetchTileInfo.Index))
                {
                    var renderInfo = CalculateScreenPosition(
                        renderTileInfo,
                        pyramidalOrigin,
                        resolution,
                        level,
                        schema);
                    renderInfos.Add(renderInfo);
                }
            }

            LogDiag($"[UpdateViewAsync] renderInfos={renderInfos.Count} cachedTextures={_glArea.CachedTextureCount}");
            _glArea.SetTilesToRender(renderInfos);
            _glArea.RequestRedraw();
            }
            finally
            {
                _renderSemaphore.Release();

                if (LastRenderSkipped)
                {
                    LastRenderSkipped = false;
                    Gtk.Application.Invoke((s, a) => App.viewer?.RequestDeferredRender());
                }
            }
        }

        private async Task EnsureGlobalDisplayRangeAsync(ZCT coordinate)
        {
            if (_useOpenSlide || _slideBase?.Image?.BioImage == null)
                return;

            var bioImage = _slideBase.Image.BioImage;
            if (bioImage.ZarrDisplayMax > bioImage.ZarrDisplayMin)
                return;
            if (bioImage.Resolutions == null || bioImage.Resolutions.Count == 0)
                return;

            var pixelFormat = bioImage.Resolutions[0].PixelFormat;
            if (pixelFormat != PixelFormat.Format16bppGrayScale &&
                pixelFormat != PixelFormat.Format48bppRgb)
                return;

            int seedLevel = bioImage.MacroResolution.HasValue
                ? Math.Max(0, bioImage.MacroResolution.Value - 1)
                : bioImage.Resolutions.Count - 1;
            if (seedLevel >= bioImage.Resolutions.Count)
                seedLevel = bioImage.Resolutions.Count - 1;

            int width = bioImage.Resolutions[seedLevel].SizeX;
            int height = bioImage.Resolutions[seedLevel].SizeY;
            if (width <= 0 || height <= 0)
                return;

            int frameIndex = bioImage.GetFrameIndex(coordinate.Z, coordinate.C, coordinate.T);
            var seedBitmap = await bioImage.GetTile(frameIndex, seedLevel, 0, 0, width, height);
            if (TryComputeRobustDisplayRange(seedBitmap, out ushort displayMin, out ushort displayMax))
            {
                bioImage.ZarrDisplayMin = displayMin;
                bioImage.ZarrDisplayMax = SnapMicroscopyCeiling(displayMax);
            }
            seedBitmap?.Dispose();
        }

        private static ushort SnapMicroscopyCeiling(ushort value)
        {
            if (value <= 255)
                return 255;
            if (value <= 4095)
                return 4095;
            if (value <= 16383)
                return 16383;
            return ushort.MaxValue;
        }

        private static bool TryComputeRobustDisplayRange(Bitmap bitmap, out ushort displayMin, out ushort displayMax)
        {
            displayMin = 0;
            displayMax = 0;

            if (bitmap == null || bitmap.Bytes == null || bitmap.Bytes.Length < 2)
                return false;
            if (bitmap.PixelFormat != PixelFormat.Format16bppGrayScale &&
                bitmap.PixelFormat != PixelFormat.Format48bppRgb)
                return false;

            byte[] bytes = bitmap.Bytes;
            bool littleEndian = bitmap.LittleEndian;
            int valueCount = bytes.Length / 2;
            if (valueCount <= 0)
                return false;

            // Sample large preview tiles to keep the seed cheap while still producing
            // one stable global display range for all visible tiles.
            int sampleStep = Math.Max(1, valueCount / 500000);
            int[] histogram = new int[ushort.MaxValue + 1];
            int sampleCount = 0;
            ushort rawMin = ushort.MaxValue;
            ushort rawMax = 0;

            for (int valueIndex = 0; valueIndex < valueCount; valueIndex += sampleStep)
            {
                int offset = valueIndex * 2;
                ushort value = littleEndian
                    ? (ushort)(bytes[offset] | (bytes[offset + 1] << 8))
                    : (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
                histogram[value]++;
                sampleCount++;
                if (value < rawMin) rawMin = value;
                if (value > rawMax) rawMax = value;
            }

            if (sampleCount == 0 || rawMax <= rawMin)
                return false;

            int lowTarget = (int)(sampleCount * 0.005);
            int highTarget = (int)(sampleCount * 0.995);

            int cumulative = 0;
            ushort low = rawMin;
            for (int i = rawMin; i <= rawMax; i++)
            {
                cumulative += histogram[i];
                if (cumulative > lowTarget)
                {
                    low = (ushort)i;
                    break;
                }
            }

            cumulative = 0;
            ushort high = rawMax;
            for (int i = rawMax; i >= rawMin; i--)
            {
                cumulative += histogram[i];
                if (cumulative > (sampleCount - highTarget))
                {
                    high = (ushort)i;
                    break;
                }
            }

            if (high <= low)
            {
                low = rawMin;
                high = rawMax;
                if (high <= low)
                    return false;
            }

            displayMin = low;
            displayMax = high;
            return true;
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

        private static void LogDiag(string message)
        {
            if (!DiagnosticLogging)
                return;

            try
            {
                System.IO.File.AppendAllText(@"C:\Users\Public\biolog.txt", message + Environment.NewLine);
            }
            catch
            {
            }
        }

        private TileRenderInfo CalculateScreenPosition(
            TileInfo tile,
            PointD pyramidalOrigin,
            double viewResolution,
            int level,
            ITileSchema schema)
        {
            // viewResolution and pyramidalOrigin are in world units (microns or
            // GetUnitPerPixel units).  Schema.Extent and tile.Extent are in the
            // schema's native coordinate space, which for SlideBase is raw pixels.
            // Multiply by level-0 UnitsPerPixel to bring everything into the same
            // world-unit space before computing screen positions.

            double r = schema.Resolutions[level].UnitsPerPixel;
            double level0UPP = schema.Resolutions.ContainsKey(0)
                ? schema.Resolutions[0].UnitsPerPixel : r;

            // Image boundary in world units.
            double imgMaxX =  schema.Extent.MaxX * level0UPP;
            double imgMinX =  schema.Extent.MinX * level0UPP;
            double imgMaxY =  schema.Extent.MaxY * level0UPP; // 0
            double imgMinY =  schema.Extent.MinY * level0UPP; // negative

            var tileExtent = tile.Extent;
            double tMinX = tileExtent.MinX;
            double tMaxX = tileExtent.MaxX;
            double tMinY = tileExtent.MinY;
            double tMaxY = tileExtent.MaxY;

            // Render the full tile quad. The tile fetch already limits us to
            // real intersecting tiles, and clipping here can shave off the last
            // visible edge when the viewport sits near a tile boundary.
            if (tMaxX <= tMinX || tMaxY <= tMinY)
                return new TileRenderInfo(tile.Index, -9999, -9999, 0, 0);

            // Screen position: divide world coords by viewResolution to get pixels.
            double originScreenX = pyramidalOrigin.X / viewResolution;
            double originScreenY = pyramidalOrigin.Y / viewResolution;

            float screenX      = (float)(tMinX /  viewResolution - originScreenX);
            float screenY      = (float)(-tMaxY / viewResolution - originScreenY);
            float screenWidth  = (float)((tMaxX - tMinX) / viewResolution);
            float screenHeight = (float)((tMaxY - tMinY) / viewResolution);

            return new TileRenderInfo(tile.Index, screenX, screenY, screenWidth, screenHeight);
        }

        public int CurrentLevel => _currentLevel;
        public int CachedTextureCount => _uploadedTiles.Count;
    }
}
