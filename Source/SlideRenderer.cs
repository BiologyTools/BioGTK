using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AForge;
using BruTile;
using Gtk;
using OpenSlideGTK;
using ZarrNET.Core.Nodes;
using OmeZarrReader = ZarrNET.Core.OmeZarrReader;

namespace BioGTK
{
    public class SlideRenderer
    {
        private readonly SlideGLArea _glArea;
        private readonly int _gtkThreadId = Environment.CurrentManagedThreadId;
        private OpenSlideBase _openSlideBase;
        private SlideBase _slideBase;
        private bool _useOpenSlide;
        private BioLib.TileCache _tileCache;
        private OpenSlideGTK.TileCache _openTileCache;
        private HashSet<TileIndex> _uploadedTiles = new();
        private readonly object _uploadedTileLock = new object();
        private int _currentLevel = -1;
        private readonly SemaphoreSlim _renderSemaphore = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _fetchSemaphore = new SemaphoreSlim(6, 6);
        private static readonly TimeSpan RemoteTileFetchTimeout = TimeSpan.FromSeconds(120);
        private const int MaxMissingTileRetries = 8;
        // Bumped whenever image state changes in a way that invalidates the
        // current render result, such as threshold or channel-range updates.
        private volatile int _renderStateVersion = 0;
        // Changes on pan/zoom. Unlike _renderStateVersion, this does not make
        // downloaded tile pixels stale and therefore must not cancel fetches.
        private volatile int _viewportVersion = 0;
        public bool LastRenderSkipped { get; private set; }
        // Latest args saved when a render is skipped so the re-queued render uses current state.
        private volatile bool _pendingRequeue = false;
        // Tracks tiles already being fetched so repeated render passes do not
        // launch duplicate remote requests for the same tile.
        private readonly HashSet<TileIndex> _pendingTileFetches = new();
        private readonly Dictionary<TileIndex, int> _tileRetryCounts = new();
        private readonly object _pendingTileFetchLock = new object();
        private readonly object _activeTileFetchLock = new object();
        private CancellationTokenSource _activeTileFetchCts;
        private int _displayRangeProbeStarted = 0;
        private int _displayRangeSeedLevel = -1;
        private readonly SemaphoreSlim _directZarrInitSemaphore = new SemaphoreSlim(1, 1);
        private OmeZarrReader _directZarrReader;
        private List<ResolutionLevelNode> _directZarrLevels;
        private string _directZarrSource;

        public SlideRenderer(SlideGLArea glArea)
        {
            _glArea = glArea;
        }

        private Task RunOnGtkThreadAsync(System.Action action)
        {
            if (Environment.CurrentManagedThreadId == _gtkThreadId)
            {
                action();
                return Task.CompletedTask;
            }

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
            ResetDirectZarrReader();
            _glArea.DiscardCompleteFrame();
            _glArea.TilesToRender.Clear();
            _openSlideBase = source;
            _slideBase = null;
            _useOpenSlide = true;
            _openTileCache = new OpenSlideGTK.TileCache(source, 200);
            ClearCache();
        }

        public void SetSource(SlideBase source)
        {
            ResetDirectZarrReader();
            _glArea.DiscardCompleteFrame();
            _glArea.TilesToRender.Clear();
            _slideBase = source;
            _openSlideBase = null;
            _useOpenSlide = false;
            _tileCache = new BioLib.TileCache(source, 200);
            ClearCache();
        }

        private void ResetDirectZarrReader()
        {
            _directZarrReader = null;
            _directZarrLevels = null;
            _directZarrSource = null;
        }

        private void CancelActiveTileFetches()
        {
            CancellationTokenSource active;
            lock (_activeTileFetchLock)
            {
                active = _activeTileFetchCts;
                _activeTileFetchCts = null;
            }

            if (active == null)
                return;

            try
            {
                active.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
            active.Dispose();
        }

        private async Task<List<ResolutionLevelNode>> GetDirectZarrLevelsAsync(BioImage image, CancellationToken ct)
        {
            string source = image?.SourceFile;
            if (string.IsNullOrWhiteSpace(source))
                return null;

            if (source.StartsWith("s3://", StringComparison.OrdinalIgnoreCase))
            {
                source = "https://uk1s3.embassy.ebi.ac.uk/" +
                    source.Substring("s3://".Length).TrimStart('/');
            }

            if (_directZarrLevels != null &&
                string.Equals(_directZarrSource, source, StringComparison.Ordinal))
            {
                return _directZarrLevels;
            }

            await _directZarrInitSemaphore.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (_directZarrLevels == null ||
                    !string.Equals(_directZarrSource, source, StringComparison.Ordinal))
                {
                    _directZarrReader = await OmeZarrReader.OpenAsync(source, ct).ConfigureAwait(false);
                    var multiscale = await _directZarrReader.AsMultiscaleImageAsync(ct).ConfigureAwait(false);
                    _directZarrLevels = (await multiscale.OpenAllResolutionLevelsAsync().ConfigureAwait(false)).ToList();
                    _directZarrSource = source;
                }

                return _directZarrLevels;
            }
            finally
            {
                _directZarrInitSemaphore.Release();
            }
        }

        public async Task<bool> PopulateZarrStatisticsAsync(
            BioImage image,
            ZCT coordinate,
            CancellationToken ct = default)
        {
            if (image?.IsZarrSource != true || image.Channels == null || image.Channels.Count == 0)
                return false;

            List<ResolutionLevelNode> levels = await GetDirectZarrLevelsAsync(image, ct).ConfigureAwait(false);
            if (levels == null || levels.Count == 0 || image.Resolutions == null || image.Resolutions.Count == 0)
                return false;

            // Use a compact but representative pyramid level. This avoids reading
            // the full-resolution plane merely to draw a small histogram.
            int usableLevels = Math.Min(levels.Count, image.Resolutions.Count);
            int sampleLevel = 0;
            long bestDistance = long.MaxValue;
            const int targetDimension = 1024;
            for (int level = 0; level < usableLevels; level++)
            {
                int maxDimension = Math.Max(image.Resolutions[level].SizeX, image.Resolutions[level].SizeY);
                long distance = Math.Abs((long)maxDimension - targetDimension);
                if (maxDimension > 0 && distance < bestDistance)
                {
                    bestDistance = distance;
                    sampleLevel = level;
                }
            }

            int width = image.Resolutions[sampleLevel].SizeX;
            int height = image.Resolutions[sampleLevel].SizeY;
            if (width <= 0 || height <= 0)
                return false;

            ResolutionLevelNode levelNode = levels[sampleLevel];
            bool packedPlanarRgb = image.Resolutions[0].PixelFormat == PixelFormat.Format48bppRgb &&
                                   image.Channels.Count == 1;
            int sourceChannelCount = packedPlanarRgb
                ? Math.Max(3, image.RGBChannelCount)
                : image.Channels.Count;
            var imageStatistics = new Statistics(image.bitsPerPixel > 8);
            bool gotAnyStatistics = false;

            for (int channelIndex = 0; channelIndex < image.Channels.Count; channelIndex++)
            {
                int samplesForChannel = packedPlanarRgb ? sourceChannelCount : 1;
                var channelStatistics = new Statistics[samplesForChannel];
                for (int sample = 0; sample < samplesForChannel; sample++)
                {
                    ct.ThrowIfCancellationRequested();
                    int sourceChannel = packedPlanarRgb ? sample : channelIndex;
                    var plane = await levelNode.ReadTileAsync(
                        0, 0, width, height,
                        z: coordinate.Z,
                        c: sourceChannel,
                        t: coordinate.T,
                        ct: ct).ConfigureAwait(false);
                    byte[] data = plane?.Data;
                    if (data == null || data.Length == 0)
                        continue;

                    int bytesPerSample = Math.Max(1, data.Length / Math.Max(1, width * height));
                    int bitsPerSample = bytesPerSample >= 2 ? 16 : 8;
                    PixelFormat statisticsFormat = bitsPerSample > 8
                        ? PixelFormat.Format16bppGrayScale
                        : PixelFormat.Format8bppIndexed;
                    int stride = width * (bitsPerSample / 8);
                    Statistics[] rawStatistics = Statistics.FromBytes(
                        data, width, height, 1, bitsPerSample, stride, statisticsFormat);
                    if (rawStatistics == null || rawStatistics.Length == 0 || rawStatistics[0] == null)
                        continue;

                    var aggregate = new Statistics(bitsPerSample > 8);
                    aggregate.AddStatistics(rawStatistics[0]);
                    aggregate.MeanHistogram();
                    channelStatistics[sample] = aggregate;
                    imageStatistics.AddStatistics(rawStatistics[0]);
                    gotAnyStatistics = true;
                }

                if (channelStatistics.All(statistics => statistics != null))
                    image.Channels[channelIndex].stats = channelStatistics;
            }

            if (!gotAnyStatistics)
                return false;

            imageStatistics.MeanHistogram();
            image.Statistics = imageStatistics;
            return true;
        }

        public async Task<double?> SampleZarrPixelAsync(
            BioImage image,
            ZCT coordinate,
            int pixelX,
            int pixelY,
            CancellationToken ct = default)
        {
            if (image?.IsZarrSource != true || pixelX < 0 || pixelY < 0 ||
                pixelX >= image.SizeX || pixelY >= image.SizeY)
                return null;

            List<ResolutionLevelNode> levels = await GetDirectZarrLevelsAsync(image, ct).ConfigureAwait(false);
            if (levels == null || levels.Count == 0)
                return null;

            var plane = await levels[0].ReadTileAsync(
                pixelX, pixelY, 1, 1,
                z: coordinate.Z,
                c: coordinate.C,
                t: coordinate.T,
                ct: ct).ConfigureAwait(false);
            byte[] data = plane?.Data;
            if (data == null || data.Length == 0)
                return null;

            PixelFormat format = image.Resolutions.Count > 0
                ? image.Resolutions[0].PixelFormat
                : PixelFormat.Format8bppIndexed;
            return format switch
            {
                PixelFormat.Format16bppGrayScale or PixelFormat.Format48bppRgb
                    when data.Length >= 2 => BitConverter.ToUInt16(data, 0),
                PixelFormat.Short when data.Length >= 2 => BitConverter.ToInt16(data, 0),
                PixelFormat.Float when data.Length >= 4 => BitConverter.ToSingle(data, 0),
                PixelFormat.UInt when data.Length >= 4 => BitConverter.ToUInt32(data, 0),
                _ => data[0]
            };
        }

        public void ClearCache(bool resetLevel = true, bool cancelActiveFetches = true)
        {
            // Clearing the visible tile cache must also invalidate any in-flight
            // fetches. Otherwise an old Z/C/T request can finish after the
            // coordinate changes and repopulate the GPU cache with stale tiles.
            _glArea.TileFrameVersion = Interlocked.Increment(ref _renderStateVersion);
            if (cancelActiveFetches)
                CancelActiveTileFetches();

            _glArea.ClearTextureCache();
            lock (_uploadedTileLock)
                _uploadedTiles.Clear();
            if (resetLevel)
                _currentLevel = -1;
            _tileCache?.Clear();
            _openTileCache?.cache.cacheMap.Clear();

            _slideBase?.cache?.Clear();
            _openSlideBase?.cache?.cache.cacheMap.Clear();
            _displayRangeSeedLevel = -1;

            lock (_pendingTileFetchLock)
            {
                _pendingTileFetches.Clear();
                _tileRetryCounts.Clear();
            }
        }

        internal void InvalidateCoordinate(bool channelChanged)
        {
            int seedLevel = _displayRangeSeedLevel;
            ClearCache(resetLevel: false);
            // A channel keeps its display window while navigating Z or T.
            if (!channelChanged)
                _displayRangeSeedLevel = seedLevel;
        }

        /// <summary>
        /// Marks all in-flight tile work as stale. Call this before changing
        /// threshold/range state so old tiles cannot overwrite the current view.
        /// </summary>
        public void InvalidateRenderState(bool cancelActiveFetches = true)
        {
            Interlocked.Increment(ref _renderStateVersion);
            if (cancelActiveFetches)
                CancelActiveTileFetches();
            lock (_pendingTileFetchLock)
            {
                _pendingTileFetches.Clear();
                _tileRetryCounts.Clear();
            }
        }

        /// <summary>
        /// Marks screen-space layout work as stale without cancelling tile I/O.
        /// Tile pixels are independent of the current pan/zoom and remain useful
        /// cache entries for the next viewport render.
        /// </summary>
        public void InvalidateViewport()
        {
            Interlocked.Increment(ref _viewportVersion);
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
                int viewportVersion = _viewportVersion;
                CancellationToken tileFetchToken;
                lock (_activeTileFetchLock)
                {
                    // This token represents render invalidation only. A timeout on a
                    // shared token would cancel every request in the batch after 60
                    // seconds, including tiles that were started much later. Each
                    // individual tile gets its own timeout in FetchTileAsync.
                    if (_activeTileFetchCts == null || _activeTileFetchCts.IsCancellationRequested)
                        _activeTileFetchCts = new CancellationTokenSource();
                    tileFetchToken = _activeTileFetchCts.Token;
                }

            var schema = _useOpenSlide ? _openSlideBase.Schema : _slideBase.Schema;

            int level = TileUtil.GetLevel(schema.Resolutions, resolution);
            var levelRes = schema.Resolutions[level];
            double levelUnitsPerPixel = levelRes.UnitsPerPixel;

            double originX = pyramidalOrigin.X;
            double originY = pyramidalOrigin.Y;
            double minX = originX;
            double width = viewportWidth * resolution;
            double height = viewportHeight * resolution;
            // BioLib pyramids use OSM-style inverted Y coordinates. Keep the
            // renderer in the same convention rather than trying to infer a
            // positive-down variant from the schema bounds.
            var viewportExtent = new Extent(minX, -originY - height, minX + width, -originY);

            // Keep the GL scissor aligned with the actual image boundary so
            // edge tiles don't leak padded black pixels outside the image.
            double imageMinX = schema.Extent.MinX;
            double imageMaxX = schema.Extent.MaxX;
            double imageMinY = schema.Extent.MinY;
            double imageMaxY = schema.Extent.MaxY;
            // Prefetch one tile beyond each viewport edge so partially visible
            // edge tiles are available without overfetching an arbitrary 2x area.
            double tileMarginX = levelRes.TileWidth * levelUnitsPerPixel;
            double tileMarginY = levelRes.TileHeight * levelUnitsPerPixel;
            var fetchExtent = new Extent(
                viewportExtent.MinX - tileMarginX,
                viewportExtent.MinY - tileMarginY,
                viewportExtent.MaxX + tileMarginX,
                viewportExtent.MaxY + tileMarginY);

            List<TileInfo> tileInfos;
            List<TileInfo> fallbackInfos;
            if (_useOpenSlide)
            {
                // OpenSlide schemas use their historical pixel-space lookup path.
                var lookupExtent = BioLib.ExtentEx.WorldToPixelInvertedY(fetchExtent, levelUnitsPerPixel);
                tileInfos = schema.GetTileInfos(lookupExtent, level).ToList();
                fallbackInfos = schema.GetTileInfos(fetchExtent, level).ToList();

                if (fallbackInfos.Count > 0)
                {
                    var seen = new HashSet<TileIndex>(tileInfos.Select(t => t.Index));
                    foreach (var info in fallbackInfos)
                    {
                        if (seen.Add(info.Index))
                            tileInfos.Add(info);
                    }
                }
            }
            else
            {
                // BioLib/Zarr SlideBase schemas already use the same inverted-Y
                // world convention as BioLib's slice path. Keep lookups in that
                // space so translated datasets stay aligned with the tile schema.
                tileInfos = GetTileInfosIncludingBoundaries(schema, fetchExtent, level);
                fallbackInfos = new List<TileInfo>();

                if (tileInfos.Count == 0)
                {
                    var pixelToWorldExtent = BioLib.ExtentEx.PixelToWorldInvertedY(fetchExtent, levelUnitsPerPixel);
                    fallbackInfos = schema.GetTileInfos(pixelToWorldExtent, level).ToList();

                    if (fallbackInfos.Count == 0)
                    {
                        var worldToPixelExtent = BioLib.ExtentEx.WorldToPixelInvertedY(fetchExtent, levelUnitsPerPixel);
                        fallbackInfos = schema.GetTileInfos(worldToPixelExtent, level).ToList();
                    }

                    if (fallbackInfos.Count > 0)
                        tileInfos = fallbackInfos;
                }
            }

            var renderInfos = new List<TileRenderInfo>();
            var visibleTiles = new List<TileInfo>();
            var prefetchTiles = new List<TileInfo>();
            foreach (var tileInfo in tileInfos)
            {
                var renderInfo = CalculateScreenPosition(
                    tileInfo,
                    pyramidalOrigin,
                    resolution,
                    level,
                    schema);
                if (IsVisible(renderInfo, viewportWidth, viewportHeight))
                {
                    renderInfos.Add(renderInfo);
                    visibleTiles.Add(tileInfo);
                }
                else
                    prefetchTiles.Add(tileInfo);
            }

            await RunOnGtkThreadAsync(() =>
            {
                if (renderVersion != _renderStateVersion || viewportVersion != _viewportVersion)
                    return;

                _glArea.ImageScreenX = (float)((imageMinX - pyramidalOrigin.X) / resolution);
                _glArea.ImageScreenY = (float)((-imageMaxY - pyramidalOrigin.Y) / resolution);
                _glArea.ImageScreenW = (float)((imageMaxX - imageMinX) / resolution);
                _glArea.ImageScreenH = (float)((imageMaxY - imageMinY) / resolution);
                _glArea.TileFrameVersion = renderVersion;
                _glArea.SetProtectedTileIndices(renderInfos.Select(info => info.Index));

                _currentLevel = level;
                _glArea.SetTilesToRender(renderInfos);
                _glArea.RequestRedraw();

                // Metadata is available immediately. Sampling runs separately
                // so it cannot hold up requests for the visible tiles.
                _ = EnsureGlobalDisplayRangeAsync(coordinate, renderVersion);

                // Offscreen prefetch must never hold up the visible frame.
                foreach (var tile in visibleTiles.Concat(prefetchTiles))
                {
                    if (_glArea.HasTileTexture(tile.Index))
                        continue;

                    lock (_pendingTileFetchLock)
                    {
                        if (!_pendingTileFetches.Add(tile.Index))
                            continue;
                    }
                    _ = ProcessPendingFetchAsync(tile, coordinate, tileFetchToken, renderVersion, level, schema);
                }
            }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Cancellation is normal when a source closes or display state is
                // replaced. Never let it fault the fire-and-forget UI render task.
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

        private static List<TileInfo> GetTileInfosIncludingBoundaries(
            ITileSchema schema,
            Extent requestedExtent,
            int level)
        {
            var tiles = schema.GetTileInfos(requestedExtent, level).ToList();
            if (!schema.Resolutions.TryGetValue(level, out var resolution))
                return tiles;

            double tileWorldWidth = resolution.TileWidth * resolution.UnitsPerPixel;
            double tileWorldHeight = resolution.TileHeight * resolution.UnitsPerPixel;
            if (tileWorldWidth <= 0 || tileWorldHeight <= 0)
                return tiles;

            double minX = Math.Max(requestedExtent.MinX, schema.Extent.MinX);
            double maxX = Math.Min(requestedExtent.MaxX, schema.Extent.MaxX);
            double minY = Math.Max(requestedExtent.MinY, schema.Extent.MinY);
            double maxY = Math.Min(requestedExtent.MaxY, schema.Extent.MaxY);
            if (maxX <= minX || maxY <= minY)
                return tiles;

            int maxColumn = Math.Max(0, (int)Math.Ceiling(schema.Extent.Width / tileWorldWidth) - 1);
            int maxRow = Math.Max(0, (int)Math.Ceiling(schema.Extent.Height / tileWorldHeight) - 1);
            double originX = schema.Extent.MinX;
            double originY = schema.Extent.MaxY;
            int firstColumn = Math.Clamp(
                (int)Math.Floor((minX - originX) / tileWorldWidth),
                0,
                maxColumn);
            int lastColumn = Math.Clamp(
                (int)Math.Floor((Math.BitDecrement(maxX) - originX) / tileWorldWidth),
                0,
                maxColumn);
            int firstRow = Math.Clamp(
                (int)Math.Floor((originY - maxY) / tileWorldHeight),
                0,
                maxRow);
            int lastRow = Math.Clamp(
                (int)Math.Floor((originY - Math.BitIncrement(minY)) / tileWorldHeight),
                0,
                maxRow);

            var existing = new HashSet<TileIndex>(tiles.Select(tile => tile.Index));
            for (int row = firstRow; row <= lastRow; row++)
            {
                for (int column = firstColumn; column <= lastColumn; column++)
                {
                    var index = new TileIndex(column, row, level);
                    if (!existing.Add(index))
                        continue;

                    double tileMinX = originX + column * tileWorldWidth;
                    double tileMaxY = originY - row * tileWorldHeight;
                    tiles.Add(new TileInfo
                    {
                        Index = index,
                        Extent = new Extent(
                            tileMinX,
                            tileMaxY - tileWorldHeight,
                            tileMinX + tileWorldWidth,
                            tileMaxY)
                    });
                }
            }

            return tiles;
        }

        private static bool IsVisible(TileRenderInfo tile, int viewportWidth, int viewportHeight) =>
            tile.ScreenWidth > 0 && tile.ScreenHeight > 0 &&
            tile.ScreenX < viewportWidth && tile.ScreenY < viewportHeight &&
            tile.ScreenX + tile.ScreenWidth > 0 && tile.ScreenY + tile.ScreenHeight > 0;

        private async Task ProcessPendingFetchAsync(
            TileInfo tile,
            ZCT coordinate,
            CancellationToken ct,
            int renderVersion,
            int level,
            ITileSchema schema)
        {
            bool tileUploaded = false;
            bool retryMissingTile = false;
            try
            {
                if (ct.IsCancellationRequested || renderVersion != _renderStateVersion)
                    return;

                int tW = schema.Resolutions[level].TileWidth;
                int tH = schema.Resolutions[level].TileHeight;
                byte[] tileData = await FetchTileAsync(tile, level, coordinate, renderVersion, ct).ConfigureAwait(false);
                if (ct.IsCancellationRequested || renderVersion != _renderStateVersion)
                    return;

                if (tileData == null)
                {
                    retryMissingTile = true;
                    return;
                }

                // The fetch layer already returns padded edge tiles. The shader
                // UVs handle world-space clipping, so re-cropping the texture here
                // would double-clip the edge and leave a visible border.
                if (tileData.Length < (long)tW * tH * 4)
                {
                    retryMissingTile = true;
                    return;
                }

                await RunOnGtkThreadAsync(() =>
                {
                    // The coordinate can change after a download completes but
                    // before GTK runs its upload callback.
                    if (ct.IsCancellationRequested || renderVersion != _renderStateVersion)
                        return;

                    _glArea.UploadTileTexture(tile.Index, tileData, tW, tH);
                    if (!_glArea.HasTileTexture(tile.Index))
                    {
                        retryMissingTile = true;
                        return;
                    }
                    lock (_uploadedTileLock)
                        _uploadedTiles.Add(tile.Index);
                    tileUploaded = true;

                    _glArea.RequestRedraw();
                }).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error processing tile {tile.Index}: {ex.Message}");
                retryMissingTile = !ct.IsCancellationRequested &&
                    renderVersion == _renderStateVersion;
            }
            finally
            {
                int retryAttempt = 0;
                lock (_pendingTileFetchLock)
                {
                    if (renderVersion == _renderStateVersion)
                    {
                        _pendingTileFetches.Remove(tile.Index);
                        if (tileUploaded)
                            _tileRetryCounts.Remove(tile.Index);
                        else if (retryMissingTile && !ct.IsCancellationRequested)
                        {
                            _tileRetryCounts.TryGetValue(tile.Index, out int previousAttempts);
                            retryAttempt = previousAttempts + 1;
                            _tileRetryCounts[tile.Index] = retryAttempt;
                        }
                    }
                }

                if (retryAttempt > 0 && retryAttempt <= MaxMissingTileRetries)
                    _ = RequestMissingTileRetryAsync(renderVersion, retryAttempt);
            }
        }

        private async Task RequestMissingTileRetryAsync(int renderVersion, int retryAttempt)
        {
            try
            {
                int exponentialStep = Math.Min(retryAttempt - 1, 4);
                int delayMilliseconds = Math.Min(4000, 250 * (1 << exponentialStep));
                await Task.Delay(delayMilliseconds).ConfigureAwait(false);
                if (renderVersion != _renderStateVersion)
                    return;

                await RunOnGtkThreadAsync(() => App.viewer?.RequestDeferredRender())
                    .ConfigureAwait(false);
            }
            catch (Exception)
            {
                // The viewer may be closing while a delayed retry is pending.
            }
        }

        private async Task EnsureGlobalDisplayRangeAsync(ZCT coordinate, int renderVersion)
        {
            if (_useOpenSlide || _slideBase?.Image?.BioImage == null)
                return;

            var bioImage = _slideBase.Image.BioImage;
            int seedLevel = 0;

            if (bioImage.ZarrDisplayMax > bioImage.ZarrDisplayMin &&
                _displayRangeSeedLevel == seedLevel)
                return;
            if (bioImage.Resolutions == null || bioImage.Resolutions.Count == 0 ||
                bioImage.Resolutions[0].PixelFormat != PixelFormat.Format16bppGrayScale)
                return;

            // Use the declared contrast when available. On a sharded Zarr, even
            // a small full-resolution sample can require downloading an entire shard.
            if (TryGetZarrDisplayWindow(bioImage, coordinate.C, out ushort min, out int max))
            {
                bioImage.ZarrDisplayMin = min;
                bioImage.ZarrDisplayMax = max;
                _displayRangeSeedLevel = seedLevel;
                return;
            }

            int width = bioImage.Resolutions[seedLevel].SizeX;
            int height = bioImage.Resolutions[seedLevel].SizeY;
            if (width <= 0 || height <= 0 || Interlocked.Exchange(ref _displayRangeProbeStarted, 1) != 0)
                return;

            var viewer = App.viewer;
            ushort displayMax = 0;
            try
            {
                displayMax = await Task.Run(() =>
                    ProbeDisplayRangeAsync(bioImage, coordinate, renderVersion, seedLevel, width, height))
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                AppLog.Append($"Display range sampling failed: {ex.Message}");
            }

            await RunOnGtkThreadAsync(() =>
            {
                Interlocked.Exchange(ref _displayRangeProbeStarted, 0);
                if (renderVersion != _renderStateVersion)
                {
                    // The latest plane may have skipped sampling while this one
                    // was still reading. Let it retry with the gate released.
                    viewer?.RequestDeferredRender();
                    return;
                }
                if (displayMax == 0)
                    return;

                bool rangeChanged = bioImage.ZarrDisplayMin != 0 || bioImage.ZarrDisplayMax != displayMax;
                bioImage.ZarrDisplayMin = 0;
                bioImage.ZarrDisplayMax = displayMax;
                if (rangeChanged)
                {
                    ClearCache(resetLevel: false, cancelActiveFetches: false);
                    viewer?.RequestDeferredRender();
                }
                _displayRangeSeedLevel = seedLevel;
                _glArea.RequestRedraw();
            }).ConfigureAwait(false);
        }

        private static bool TryGetZarrDisplayWindow(BioImage image, int channel, out ushort min, out int max)
        {
            min = 0;
            max = 0;

            // Root attributes describe the root image. A plate or scene's
            // root metadata does not describe the selected child image.
            if (image.multiscale?.Path != string.Empty ||
                image.zarrReader?.RawAttributes is not JsonElement attributes ||
                attributes.ValueKind != JsonValueKind.Object)
                return false;

            if (attributes.TryGetProperty("ome", out var ome) && ome.ValueKind == JsonValueKind.Object)
                attributes = ome;

            if (!attributes.TryGetProperty("omero", out var omero) || omero.ValueKind != JsonValueKind.Object ||
                !omero.TryGetProperty("channels", out var channels) || channels.ValueKind != JsonValueKind.Array ||
                channel < 0 || channel >= channels.GetArrayLength())
                return false;

            var channelMetadata = channels[channel];
            if (channelMetadata.ValueKind != JsonValueKind.Object ||
                !channelMetadata.TryGetProperty("window", out var window) || window.ValueKind != JsonValueKind.Object ||
                !window.TryGetProperty("start", out var start) || start.ValueKind != JsonValueKind.Number ||
                !window.TryGetProperty("end", out var end) || end.ValueKind != JsonValueKind.Number ||
                !start.TryGetDouble(out double lower) || !end.TryGetDouble(out double upper) ||
                !double.IsFinite(lower) || !double.IsFinite(upper) || lower >= upper)
                return false;

            min = (ushort)Math.Clamp(Math.Floor(lower), 0, ushort.MaxValue);
            max = (int)Math.Clamp(Math.Ceiling(upper), 0, ushort.MaxValue);
            return max > min;
        }

        private async Task<ushort> ProbeDisplayRangeAsync(
            BioImage bioImage, ZCT coordinate, int renderVersion, int seedLevel, int width, int height)
        {
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
            foreach (int sampleY in ys)
            {
                foreach (int sampleX in xs)
                {
                    if (renderVersion != _renderStateVersion)
                        return 0;

                    using var seedBitmap = await bioImage.GetTile(frameIndex, seedLevel, sampleX, sampleY, sampleW, sampleH, coordinate, true)
                        .ConfigureAwait(false);
                    if (TryComputeDisplayRangeFromUsedBits(seedBitmap, out _, out ushort displayMax))
                        bestMax = Math.Max(bestMax, displayMax);
                }
            }

            return bestMax;
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

        private async Task<byte[]> FetchTileAsync(
            TileInfo tileInfo, int level, ZCT coordinate, int renderVersion, CancellationToken ct)
        {
            bool semaphoreEntered = false;
            try
            {
                await _fetchSemaphore.WaitAsync(ct).ConfigureAwait(false);
                semaphoreEntered = true;

                // Drain active reads, but skip obsolete work still waiting for a slot.
                if (renderVersion != _renderStateVersion)
                    return null;

                // HttpZarrStore owns remote timeout and retry policy. Supplying a
                // second CancelAfter token here aborts its transport stream and
                // turns an ordinary slow tile into a TaskCanceledException.
                if (ShouldComposeRgbTile())
                    return await FetchRgbCompositeTileAsync(tileInfo, level, coordinate, ct).ConfigureAwait(false);

                return await FetchSingleTileAsync(tileInfo, level, coordinate, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                // Routine render invalidation (view closed, channel/threshold
                // changed, or cache cleared). The caller treats null as stale.
                return null;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error fetching tile {tileInfo.Index}: {ex.Message}");
                return null;
            }
            finally
            {
                if (semaphoreEntered)
                    _fetchSemaphore.Release();
            }
        }

        private bool ShouldComposeRgbTile() =>
            !_useOpenSlide && App.viewer?.Mode == ImageView.ViewMode.RGBImage &&
            CanComposeRgbTile(_slideBase?.Image?.BioImage);

        internal static bool CanComposeRgbTile(BioImage bioImage)
        {
            if (bioImage == null || bioImage.Channels == null)
                return false;
            if (bioImage.Resolutions == null || bioImage.Resolutions.Count == 0)
                return false;

            var pixelFormat = bioImage.Resolutions[0].PixelFormat;
            // OME-Zarr represents 16-bit RGB as three independent C planes even
            // though BioImage exposes it as one logical Format48bppRgb channel.
            // Compose those planes here; BioLib's ordinary packed-RGB path is for
            // interleaved data and otherwise returns only one of the three planes.
            if (bioImage.IsZarrSource && pixelFormat == PixelFormat.Format48bppRgb)
                return true;

            if (bioImage.Channels.Count <= 1)
                return false;

            return pixelFormat != PixelFormat.Format24bppRgb &&
                   pixelFormat != PixelFormat.Format48bppRgb;
        }

        private async Task<byte[]> FetchRgbCompositeTileAsync(TileInfo tileInfo, int level, ZCT coordinate, CancellationToken ct)
        {
            var bioImage = _slideBase?.Image?.BioImage;
            if (bioImage == null)
                return await FetchSingleTileAsync(tileInfo, level, coordinate, ct).ConfigureAwait(false);

            bool planarRgb16 = bioImage.IsZarrSource &&
                bioImage.Resolutions.Count > 0 &&
                bioImage.Resolutions[0].PixelFormat == PixelFormat.Format48bppRgb;
            int channelCount = planarRgb16 ? 3 : Math.Max(1, bioImage.Channels.Count);
            int redIndex = Math.Clamp(bioImage.rgbChannels[0], 0, channelCount - 1);
            int greenIndex = Math.Clamp(bioImage.rgbChannels[1], 0, channelCount - 1);
            int blueIndex = Math.Clamp(bioImage.rgbChannels[2], 0, channelCount - 1);

            Task<byte[]> redTask = FetchSlideTileDirectAsync(tileInfo, level, new ZCT(coordinate.Z, redIndex, coordinate.T), ct);
            Task<byte[]> greenTask = FetchSlideTileDirectAsync(tileInfo, level, new ZCT(coordinate.Z, greenIndex, coordinate.T), ct);
            Task<byte[]> blueTask = FetchSlideTileDirectAsync(tileInfo, level, new ZCT(coordinate.Z, blueIndex, coordinate.T), ct);

            await Task.WhenAll(redTask, greenTask, blueTask).ConfigureAwait(false);
            return ComposeBgraTiles(redTask.Result, greenTask.Result, blueTask.Result);
        }

        private async Task<byte[]> FetchSlideTileDirectAsync(TileInfo tileInfo, int level, ZCT coordinate, CancellationToken ct)
        {
            if (IsPlanar16BitZarrLevel(level))
                return await FetchGray16ZarrTileAsync(tileInfo, level, coordinate, ct).ConfigureAwait(false);

            // FetchTileAsync already supplies the per-request timeout token.
            // Do not create a second timer that aborts the same HTTP stream.
            return await _slideBase.GetTileAsync(
                new BioLib.TileInformation(tileInfo.Index, tileInfo.Extent, coordinate),
                coordinate,
                ct).ConfigureAwait(false);
        }

        private async Task<byte[]> FetchSingleTileAsync(TileInfo tileInfo, int level, ZCT coordinate, CancellationToken ct)
        {
            if (IsPlanar16BitZarrLevel(level))
                return await FetchGray16ZarrTileAsync(tileInfo, level, coordinate, ct).ConfigureAwait(false);

            var fetchTask = _useOpenSlide
                ? Task.Run(() => _openTileCache.GetTile(new OpenSlideGTK.Info(coordinate, tileInfo.Index, tileInfo.Extent, level)))
                : _tileCache.GetTile(new BioLib.TileInformation(tileInfo.Index, tileInfo.Extent, coordinate), ct);

            if (_useOpenSlide)
            {
                var bt = await fetchTask.ConfigureAwait(false);
                return bt;
            }

            return await fetchTask.ConfigureAwait(false);
        }

        private bool IsPlanar16BitZarrLevel(int level)
        {
            var image = _slideBase?.Image?.BioImage;
            return !_useOpenSlide && image != null && image.IsZarrSource &&
                level >= 0 && level < image.Resolutions.Count &&
                (image.Resolutions[level].PixelFormat == PixelFormat.Format16bppGrayScale ||
                 image.Resolutions[level].PixelFormat == PixelFormat.Format48bppRgb);
        }

        private async Task<byte[]> FetchGray16ZarrTileAsync(TileInfo tileInfo, int level, ZCT coordinate, CancellationToken ct)
        {
            var image = _slideBase.Image.BioImage;
            var tileResolution = _slideBase.Schema.Resolutions[level];
            int tileWidth = tileResolution.TileWidth;
            int tileHeight = tileResolution.TileHeight;
            int x = checked(tileInfo.Index.Col * tileWidth);
            int y = checked(tileInfo.Index.Row * tileHeight);
            var dimensions = _slideBase.Image.GetLevelDimension(level);
            int width = (int)Math.Max(0, Math.Min(tileWidth, dimensions.Width - x));
            int height = (int)Math.Max(0, Math.Min(tileHeight, dimensions.Height - y));
            if (width == 0 || height == 0 || x < 0 || y < 0)
                return new byte[tileWidth * tileHeight * 4];

            // Read packed pixels with their known rectangular dimensions. The
            // SlideImage BGRA conversion guesses a square from the pixel count,
            // which truncates narrow right-edge tiles (e.g. 16x256 becomes 64x64).
            byte[] raw;
            ResolutionLevelNode levelNode = null;
            if (image.Resolutions[level].PixelFormat == PixelFormat.Format48bppRgb)
            {
                // Read the channel plane directly. BioLib 4.3's compatibility
                // APIs either truncate these uint16 samples to one byte or read
                // all three channels repeatedly before returning one plane.
                List<ResolutionLevelNode> levels = await GetDirectZarrLevelsAsync(image, ct).ConfigureAwait(false);
                if (levels == null || levels.Count == 0)
                    return null;

                levelNode = levels[Math.Clamp(level, 0, levels.Count - 1)];
            }
            else if (image.Type != BioImage.ImageType.well && image.levels?.Count > level)
            {
                levelNode = image.levels[level];
            }

            if (levelNode != null)
            {
                var plane = await levelNode.ReadTileAsync(
                    x, y, width, height,
                    z: coordinate.Z,
                    c: coordinate.C,
                    t: coordinate.T,
                    ct: ct).ConfigureAwait(false);
                raw = plane?.Data;
            }
            else
            {
                raw = await image.GetTileBytesRaw(
                    image.GetFrameIndex(coordinate.Z, coordinate.C, coordinate.T),
                    level, x, y, width, height, coordinate)
                    .WaitAsync(RemoteTileFetchTimeout, ct).ConfigureAwait(false);
            }
            if (raw == null || raw.Length == 0)
                return null;

            int min = image.ZarrDisplayMin;
            int max = image.ZarrDisplayMax;
            if (max <= min)
            {
                min = 0;
                max = ushort.MaxValue;
            }
            return Gray16TileConverter.ToBgra(raw, width, height, tileWidth, tileHeight, min, max);
        }

        internal static byte[] ComposeBgraTiles(byte[] redTile, byte[] greenTile, byte[] blueTile)
        {
            byte[] fallback = redTile ?? greenTile ?? blueTile;
            if (fallback == null)
                return null;

            redTile ??= fallback;
            greenTile ??= fallback;
            blueTile ??= fallback;

            int byteCount = Math.Min(redTile.Length, Math.Min(greenTile.Length, blueTile.Length));
            byteCount -= byteCount % 4;
            if (byteCount <= 0)
                return null;

            byte[] composed = new byte[byteCount];
            for (int i = 0; i < byteCount; i += 4)
            {
                byte r = ExtractIntensity(redTile, i);
                byte g = ExtractIntensity(greenTile, i);
                byte b = ExtractIntensity(blueTile, i);
                byte a = (byte)Math.Max(redTile[i + 3], Math.Max(greenTile[i + 3], blueTile[i + 3]));
                if (a == 0 && (r != 0 || g != 0 || b != 0))
                    a = byte.MaxValue;

                composed[i] = b;
                composed[i + 1] = g;
                composed[i + 2] = r;
                composed[i + 3] = a;
            }

            return composed;
        }

        private static byte ExtractIntensity(byte[] tile, int offset)
        {
            return (byte)Math.Max(tile[offset], Math.Max(tile[offset + 1], tile[offset + 2]));
        }

        private TileRenderInfo CalculateScreenPosition(
            TileInfo tile,
            PointD pyramidalOrigin,
            double viewResolution,
            int level,
            ITileSchema schema)
        {
            // viewResolution and pyramidalOrigin are in world units (microns or
            // GetUnitPerPixel units).  Tile extents are already expressed in the
            // same world-space convention used by GetTileInfos/SkiaStitch, so use
            // them directly here instead of re-clipping against the schema extent.
            // That avoids introducing a one-sided padding strip when the dataset
            // carries a translation transform.

            var tileExtent = tile.Extent;
            if (tileExtent.MaxX <= tileExtent.MinX || tileExtent.MaxY <= tileExtent.MinY)
                return new TileRenderInfo(tile.Index, -9999, -9999, 0, 0);

            float u0 = 0f;
            float u1 = 1f;
            float v0 = 0f;
            float v1 = 1f;

            // Screen position: divide world coords by viewResolution to get pixels.
            double originScreenX = pyramidalOrigin.X;
            double originScreenY = pyramidalOrigin.Y;

            float screenX      = (float)((tileExtent.MinX - originScreenX) / viewResolution);
            float screenY      = (float)((-tileExtent.MaxY - originScreenY) / viewResolution);
            float screenWidth  = (float)((tileExtent.MaxX - tileExtent.MinX) / viewResolution);
            float screenHeight = (float)((tileExtent.MaxY - tileExtent.MinY) / viewResolution);

            return new TileRenderInfo(tile.Index, screenX, screenY, screenWidth, screenHeight, u0, v0, u1, v1);
        }

        public int CurrentLevel => _currentLevel;
        public int CachedTextureCount
        {
            get
            {
                lock (_uploadedTileLock)
                    return _uploadedTiles.Count;
            }
        }
    }
}
