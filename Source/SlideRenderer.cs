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
        private static readonly bool DiagnosticLogging = false;
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
        private readonly object _pendingLevelSwitchLock = new object();
        private List<TileRenderInfo> _pendingLevelRenderInfos;
        private int _pendingLevel = -1;
        private int _pendingPreviousLevel = -1;
        private readonly SemaphoreSlim _renderSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _fetchSemaphore = new SemaphoreSlim(6, 6);
        private static readonly TimeSpan RemoteTileFetchTimeout = TimeSpan.FromSeconds(60);
        // Bumped whenever image state changes in a way that invalidates the
        // current render result, such as threshold or channel-range updates.
        private volatile int _renderStateVersion = 0;
        public bool LastRenderSkipped { get; private set; }
        // Latest args saved when a render is skipped so the re-queued render uses current state.
        private volatile bool _pendingRequeue = false;
        // Tracks tiles already being fetched so repeated render passes do not
        // launch duplicate remote requests for the same tile.
        private readonly HashSet<TileIndex> _pendingTileFetches = new();
        private readonly object _pendingTileFetchLock = new object();
        private int _redrawQueued = 0;
        private int _displayRangeProbeStarted = 0;
        private int _displayRangeSeedLevel = -1;

        public SlideRenderer(SlideGLArea glArea)
        {
            _glArea = glArea;
        }

        private static Task RunOnGtkThreadAsync(System.Action action)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            Gtk.Application.Invoke((s, a) =>
            {
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            return tcs.Task;
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
            // Clearing the visible tile cache must also invalidate any in-flight
            // fetches. Otherwise an old Z/C/T request can finish after the
            // coordinate changes and repopulate the GPU cache with stale tiles.
            Interlocked.Increment(ref _renderStateVersion);

            _glArea.ClearTextureCache();
            _uploadedTiles.Clear();
            _uploadedTileInfos.Clear();
            _currentLevel = -1;
            _tileCache?.Clear();
            _openTileCache?.Clear();
            _slideBase?.cache?.Clear();
            _openSlideBase?.cache?.Clear();
            _displayRangeSeedLevel = -1;

            lock (_pendingLevelSwitchLock)
            {
                _pendingLevelRenderInfos = null;
                _pendingLevel = -1;
                _pendingPreviousLevel = -1;
            }

            lock (_pendingTileFetchLock)
            {
                _pendingTileFetches.Clear();
            }
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
            int renderVersion = _renderStateVersion;
            using var tileFetchCts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

            var schema = _useOpenSlide ? _openSlideBase.Schema : _slideBase.Schema;

            int level = TileUtil.GetLevel(schema.Resolutions, resolution);
            var levelRes = schema.Resolutions[level];
            double levelUnitsPerPixel = levelRes.UnitsPerPixel;

            _ = EnsureGlobalDisplayRangeAsync(coordinate, renderVersion, level);

            LogDiag($"[UpdateViewAsync] type={(_useOpenSlide ? "openslide" : "slidebase")} level={level} res={resolution:F3} upp={levelUnitsPerPixel:F3} viewport={viewportWidth}x{viewportHeight} origin=({pyramidalOrigin.X:F2},{pyramidalOrigin.Y:F2}) schemaExtent=({schema.Extent.MinX:F0},{schema.Extent.MinY:F0},{schema.Extent.MaxX:F0},{schema.Extent.MaxY:F0})");

            bool levelChanged = level != _currentLevel;

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
            var pendingFetches = new List<(TileInfo Tile, Task<byte[]> FetchTask)>();
            foreach (var tileInfo in tileInfos)
            {
                if (renderVersion != _renderStateVersion)
                    return;

                var fetchTileInfo = tileInfo;
                var renderTileInfo = tileInfo;

                var renderInfo = CalculateScreenPosition(
                    renderTileInfo,
                    pyramidalOrigin,
                    resolution,
                    level,
                    schema);
                renderInfos.Add(renderInfo);

                if (!_glArea.HasTileTexture(fetchTileInfo.Index))
                {
                    bool alreadyPending;
                    lock (_pendingTileFetchLock)
                    {
                        alreadyPending = !_pendingTileFetches.Add(fetchTileInfo.Index);
                    }

                    if (!alreadyPending)
                        pendingFetches.Add((fetchTileInfo, FetchTileAsync(fetchTileInfo, level, coordinate, tileFetchCts.Token)));
                }
            }

            if (pendingFetches.Count > 0)
            {
                foreach (var pending in pendingFetches)
                    _ = ProcessPendingFetchAsync(pending.Tile, pending.FetchTask, tileFetchCts.Token, renderVersion, pyramidalOrigin, resolution, level, schema);
            }

            LogDiag($"[UpdateViewAsync] renderInfos={renderInfos.Count} cachedTextures={_glArea.CachedTextureCount}");

            if (renderVersion != _renderStateVersion)
                return;

            bool hasRenderableTexture = renderInfos.Any(tile => _glArea.HasTileTexture(tile.Index));
            if (levelChanged)
            {
                if (hasRenderableTexture)
                {
                    bool canSwapNow = renderInfos.All(tile => _glArea.HasTileTexture(tile.Index));
                    if (canSwapNow)
                    {
                        _glArea.PreservePreviousFrameWhileIncomplete = false;
                        _glArea.ReleaseLevelTextures(_currentLevel);
                        _currentLevel = level;
                        lock (_pendingLevelSwitchLock)
                        {
                            _pendingLevelRenderInfos = null;
                            _pendingLevel = -1;
                            _pendingPreviousLevel = -1;
                        }
                    }
                    else
                    {
                        _glArea.PreservePreviousFrameWhileIncomplete = true;
                        lock (_pendingLevelSwitchLock)
                        {
                            _pendingLevelRenderInfos = new List<TileRenderInfo>(renderInfos);
                            _pendingLevel = level;
                            _pendingPreviousLevel = _currentLevel;
                        }
                        return;
                    }
                }
                else
                {
                    _glArea.PreservePreviousFrameWhileIncomplete = true;
                    lock (_pendingLevelSwitchLock)
                    {
                        _pendingLevelRenderInfos = new List<TileRenderInfo>(renderInfos);
                        _pendingLevel = level;
                        _pendingPreviousLevel = _currentLevel;
                    }
                    return;
                }
            }

            if (renderVersion != _renderStateVersion)
                return;

            await RunOnGtkThreadAsync(() =>
            {
                _glArea.SetTilesToRender(renderInfos);
                _glArea.RequestRedraw();
            });
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

        private async Task ProcessPendingFetchAsync(
            TileInfo tile,
            Task<byte[]> fetchTask,
            CancellationToken ct,
            int renderVersion,
            PointD pyramidalOrigin,
            double resolution,
            int level,
            ITileSchema schema)
        {
            try
            {
                byte[] tileData = await fetchTask.ConfigureAwait(false);
                if (ct.IsCancellationRequested || renderVersion != _renderStateVersion)
                    return;

                if (tileData == null)
                {
                    LogDiag($"[UpdateViewAsync] fetch returned null tile={tile.Index} extent=({tile.Extent.MinX:F0},{tile.Extent.MinY:F0},{tile.Extent.MaxX:F0},{tile.Extent.MaxY:F0})");
                    return;
                }

                int tW = schema.Resolutions[level].TileWidth;
                int tH = schema.Resolutions[level].TileHeight;

                await RunOnGtkThreadAsync(() =>
                    _glArea.UploadTileTexture(tile.Index, tileData, tW, tH));
                _uploadedTiles.Add(tile.Index);
                _uploadedTileInfos[tile.Index] = tile;
                LogDiag($"[UpdateViewAsync] uploaded tile={tile.Index} bytes={tileData.Length} texSize={tW}x{tH} extent=({tile.Extent.MinX:F0},{tile.Extent.MinY:F0},{tile.Extent.MaxX:F0},{tile.Extent.MaxY:F0})");

                List<TileRenderInfo> pendingLevelRenderInfos = null;
                int pendingPreviousLevel = -1;
                bool applyPendingLevel = false;
                lock (_pendingLevelSwitchLock)
                {
                    if (_pendingLevelRenderInfos != null &&
                        level == _pendingLevel &&
                        _pendingLevelRenderInfos.All(info => _glArea.HasTileTexture(info.Index)))
                    {
                        applyPendingLevel = true;
                        pendingLevelRenderInfos = _pendingLevelRenderInfos;
                        pendingPreviousLevel = _pendingPreviousLevel;
                        _pendingLevelRenderInfos = null;
                        _pendingLevel = -1;
                        _pendingPreviousLevel = -1;
                        _currentLevel = level;
                    }
                }

                if (applyPendingLevel)
                {
                    await RunOnGtkThreadAsync(() =>
                    {
                        if (pendingPreviousLevel >= 0)
                            _glArea.ReleaseLevelTextures(pendingPreviousLevel);
                        _glArea.PreservePreviousFrameWhileIncomplete = false;
                        _glArea.SetTilesToRender(pendingLevelRenderInfos);
                        _glArea.RequestRedraw();
                    });
                }

                if (renderVersion == _renderStateVersion &&
                    Interlocked.Exchange(ref _redrawQueued, 1) == 0)
                {
                    await RunOnGtkThreadAsync(() =>
                    {
                        try
                        {
                            _glArea.RequestRedraw();
                        }
                        finally
                        {
                            Interlocked.Exchange(ref _redrawQueued, 0);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                LogDiag($"[UpdateViewAsync] pending fetch failed tile={tile.Index} err={ex.Message}");
            }
            finally
            {
                lock (_pendingTileFetchLock)
                {
                    _pendingTileFetches.Remove(tile.Index);
                }
            }
        }

        private async Task EnsureGlobalDisplayRangeAsync(ZCT coordinate, int renderVersion, int renderLevel)
        {
            if (_useOpenSlide || _slideBase?.Image?.BioImage == null)
                return;

            var bioImage = _slideBase.Image.BioImage;
            int seedLevel = 0;

            if (bioImage.ZarrDisplayMax > bioImage.ZarrDisplayMin &&
                _displayRangeSeedLevel == seedLevel)
                return;
            if (Interlocked.Exchange(ref _displayRangeProbeStarted, 1) != 0)
                return;
            if (bioImage.Resolutions == null || bioImage.Resolutions.Count == 0)
            {
                Interlocked.Exchange(ref _displayRangeProbeStarted, 0);
                return;
            }

            var pixelFormat = bioImage.Resolutions[0].PixelFormat;
            if (pixelFormat != PixelFormat.Format16bppGrayScale &&
                pixelFormat != PixelFormat.Format48bppRgb)
            {
                Interlocked.Exchange(ref _displayRangeProbeStarted, 0);
                return;
            }

            int width = bioImage.Resolutions[seedLevel].SizeX;
            int height = bioImage.Resolutions[seedLevel].SizeY;
            if (width <= 0 || height <= 0)
            {
                Interlocked.Exchange(ref _displayRangeProbeStarted, 0);
                return;
            }

            int frameIndex = bioImage.GetFrameIndex(coordinate.Z, coordinate.C, coordinate.T);

            // Probe a small grid of tiles from level 0 so the display range is
            // stable across zoom levels and does not depend on the current view.
            int sampleW = Math.Min(width, 128);
            int sampleH = Math.Min(height, 128);
            int[] xs = new[]
            {
                0,
                Math.Max(0, (width - sampleW) / 2),
                Math.Max(0, width - sampleW)
            };
            int[] ys = new[]
            {
                0,
                Math.Max(0, (height - sampleH) / 2),
                Math.Max(0, height - sampleH)
            };

            ushort bestMax = 0;
            bool gotRange = false;
            foreach (int sampleY in ys)
            {
                foreach (int sampleX in xs)
                {
                    var seedBitmap = await bioImage.GetTile(frameIndex, seedLevel, sampleX, sampleY, sampleW, sampleH, null, true);
                    if (TryComputeDisplayRangeFromUsedBits(seedBitmap, out ushort displayMin, out ushort displayMax))
                    {
                        if (displayMax > bestMax)
                            bestMax = displayMax;
                        gotRange = true;
                    }
                    seedBitmap?.Dispose();
                }
            }

            if (gotRange && bestMax > 0)
            {
                bool rangeChanged = bioImage.ZarrDisplayMin != 0 ||
                                    bioImage.ZarrDisplayMax != bestMax;
                bioImage.ZarrDisplayMin = 0;
                bioImage.ZarrDisplayMax = bestMax;
                _displayRangeSeedLevel = seedLevel;

                if (rangeChanged)
                {
                    await RunOnGtkThreadAsync(() =>
                    {
                        ClearCache();
                        _displayRangeSeedLevel = seedLevel;
                        _glArea.RequestRedraw();
                    });
                }
                else if (renderVersion == _renderStateVersion)
                {
                    await RunOnGtkThreadAsync(() => _glArea.RequestRedraw());
                }
            }
            Interlocked.Exchange(ref _displayRangeProbeStarted, 0);
        }

        private static ushort SnapUsedBitCeiling(ushort value)
        {
            if (value == 0)
                return 0;

            uint ceiling = 1;
            while (ceiling - 1 < value && ceiling < (1u << 16))
                ceiling <<= 1;

            return (ushort)Math.Min(ushort.MaxValue, ceiling - 1);
        }

        private static bool TryComputeDisplayRangeFromUsedBits(Bitmap bitmap, out ushort displayMin, out ushort displayMax)
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

            // Determine the effective bit depth from the highest used sample
            // value rather than percentiles. A 16-bit container often stores
            // 10/12/14-bit microscopy data and should be normalized to that
            // simple max-bit ceiling (for example 4095 for 12-bit data).
            ushort rawMax = 0;

            for (int valueIndex = 0; valueIndex < valueCount; valueIndex++)
            {
                int offset = valueIndex * 2;
                ushort value = littleEndian
                    ? (ushort)(bytes[offset] | (bytes[offset + 1] << 8))
                    : (ushort)((bytes[offset] << 8) | bytes[offset + 1]);
                if (value > rawMax) rawMax = value;
            }

            if (rawMax == 0)
                return false;

            displayMin = 0;
            displayMax = SnapUsedBitCeiling(rawMax);
            return true;
        }

        private async Task<byte[]> FetchTileAsync(TileInfo tileInfo, int level, ZCT coordinate, CancellationToken ct)
        {
            await _fetchSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var fetchCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                var fetchTask = _useOpenSlide
                    ? Task.Run(() => _openTileCache.GetTile(new OpenSlideGTK.Info(coordinate, tileInfo.Index, tileInfo.Extent, level)))
                    : _tileCache.GetTile(new BioLib.TileInformation(tileInfo.Index, tileInfo.Extent, coordinate), fetchCts.Token);

                var timeoutTask = Task.Delay(RemoteTileFetchTimeout, ct);
                var completed = await Task.WhenAny(fetchTask, timeoutTask).ConfigureAwait(false);
                if (completed != fetchTask)
                {
                    LogDiag($"[FetchTileAsync] timeout tile={tileInfo.Index} level={level} extent=({tileInfo.Extent.MinX:F0},{tileInfo.Extent.MinY:F0},{tileInfo.Extent.MaxX:F0},{tileInfo.Extent.MaxY:F0})");
                    fetchCts.Cancel();
                    return null;
                }

                if (_useOpenSlide)
                {
                    var bt = await fetchTask.ConfigureAwait(false);
                    return bt;
                }
                else
                {
                    return await fetchTask.ConfigureAwait(false);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching tile {tileInfo.Index}: {ex.Message}");
                return null;
            }
            finally
            {
                _fetchSemaphore.Release();
            }
        }

        private static void LogDiag(string message)
        {
            if (!DiagnosticLogging)
                return;

            AppLog.Append(message);
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
