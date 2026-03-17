using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AForge;
using BruTile;
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
        private int _currentLevel = -1;

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
            var schema = _useOpenSlide ? _openSlideBase.Schema : _slideBase.Schema;

            int level = TileUtil.GetLevel(schema.Resolutions, resolution);
            var levelRes = schema.Resolutions[level];
            double levelUnitsPerPixel = levelRes.UnitsPerPixel;

            if (level != _currentLevel)
            {
                _glArea.ReleaseLevelTextures(_currentLevel);
                _currentLevel = level;
            }

            // Calculate world extent for the viewport.
            // pyramidalOrigin is in level-pixel coordinates. Multiply by
            // levelUnitsPerPixel (not the raw resolution scalar) to get world units.
            // Using the raw resolution diverges from levelUnitsPerPixel between zoom
            // levels, causing GetTileInfos to cover only a fraction of the viewport.
            double minX =  pyramidalOrigin.X * levelUnitsPerPixel;
            double minY = -pyramidalOrigin.Y * levelUnitsPerPixel;
            double width  = viewportWidth  * levelUnitsPerPixel;
            double height = viewportHeight * levelUnitsPerPixel;
            var worldExtent = new Extent(minX, minY - height, minX + width, minY);

            // Get tiles that intersect the viewport
            var tileInfos = schema.GetTileInfos(worldExtent, level).ToList();

            if (tileInfos.Count == 0)
            {
                var pixelExtent = OpenSlideGTK.ExtentEx.WorldToPixelInvertedY(worldExtent, levelUnitsPerPixel);
                tileInfos = schema.GetTileInfos(pixelExtent, level).ToList();
            }

            if(_openSlideBase != null)
                await _openSlideBase.FetchTilesAsync(tileInfos.ToList(), level, coordinate);
            else
                await _slideBase.FetchTilesAsync(tileInfos.ToList(), level, coordinate, pyramidalOrigin, new Size(viewportWidth,viewportHeight));
            
            var renderInfos = new List<TileRenderInfo>();
            foreach (var tileInfo in tileInfos)
            {
                if (!_glArea.HasTileTexture(tileInfo.Index))
                {
                    byte[] tileData;
                    if (_useOpenSlide)
                        tileData = await _openSlideBase.GetTileAsync(tileInfo);
                    else
                        tileData = await _slideBase.GetTileAsync(tileInfo,coordinate);
                    if (tileData != null)
                    {
                        // Nominal tile dimensions derived from the world-space extent.
                        int tW = (int)Math.Round(tileInfo.Extent.Width  / levelUnitsPerPixel);
                        int tH = (int)Math.Round(tileInfo.Extent.Height / levelUnitsPerPixel);
                        tW = Math.Max(1, tW);
                        tH = Math.Max(1, tH);

                        // Safety: if the buffer is still undersized (should not
                        // happen now that ISlideSource uses extent-derived dimensions),
                        // clamp tH to avoid reading past the buffer end in GL.
                        int actualPixels = tileData.Length / 4;
                        if (actualPixels > 0 && actualPixels < tW * tH)
                        {
                            if (actualPixels < tW) tW = actualPixels;
                            tH = Math.Max(1, actualPixels / tW);
                        }

                        _glArea.UploadTileTexture(tileInfo.Index, tileData, tW, tH);
                        _uploadedTiles.Add(tileInfo.Index);
                    }
                }

                if (_glArea.HasTileTexture(tileInfo.Index))
                {
                    var renderInfo = CalculateScreenPosition(
                        tileInfo,
                        pyramidalOrigin,
                        resolution,
                        level,
                        schema);
                    renderInfos.Add(renderInfo);
                }
            }

            _glArea.SetTilesToRender(renderInfos);
            _glArea.RequestRedraw();
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
            // Convert tile world extent to level-pixel coordinates using
            // levelUnitsPerPixel so positions are consistent with pyramidalOrigin.
            double levelUnitsPerPixel = schema.Resolutions[level].UnitsPerPixel;
            var pixelExtent = OpenSlideGTK.ExtentEx.WorldToPixelInvertedY(tile.Extent, levelUnitsPerPixel);

            float screenX = (float)(pixelExtent.MinX - pyramidalOrigin.X);
            float screenY = (float)(pixelExtent.MinY - pyramidalOrigin.Y);

            float screenWidth  = (float)pixelExtent.Width;
            float screenHeight = (float)pixelExtent.Height;

            return new TileRenderInfo(tile.Index, screenX, screenY, screenWidth, screenHeight);
        }

        public int CurrentLevel => _currentLevel;
        public int CachedTextureCount => _uploadedTiles.Count;
    }
}