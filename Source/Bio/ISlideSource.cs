using BruTile;
using BruTile.Cache;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using BioGTK;

namespace Bio
{

    public abstract class SlideSourceBase : ISlideSource, IDisposable
    {
        #region Static
        public static bool UseRealResolution { get; set; } = true;

        private static IDictionary<string, Func<string, bool, ISlideSource>> keyValuePairs = new Dictionary<string, Func<string, bool, ISlideSource>>();

        /// <summary>
        /// resister decode for Specific format
        /// </summary>
        /// <param name="extensionUpper">dot and extension upper</param>
        /// <param name="factory">file path,enable cache,decoder</param>
        public static void Resister(string extensionUpper, Func<string, bool, ISlideSource> factory)
        {
            keyValuePairs.Add(extensionUpper, factory);
        }

        public static ISlideSource Create(BioImage source, bool enableCache = true)
        {
            var ext = Path.GetExtension(source.file).ToUpper();
            try
            {
                if (keyValuePairs.TryGetValue(ext, out var factory) && factory != null)
                    return factory.Invoke(source.file, enableCache);

                if (!string.IsNullOrEmpty(SlideBase.DetectVendor(source.file)))
                    return new SlideBase(source, enableCache);
            }
            catch (Exception e) 
            { 
                Console.WriteLine(e.Message); 
            }
            return null;
        }
        #endregion

        public abstract byte[] GetTile(TileInfo tileInfo);

        public abstract Task<byte[]> GetTileAsync(TileInfo tileInfo);

        public double MinUnitsPerPixel { get; protected set; }

        public Dictionary<TileIndex, byte[]> _bgraCache = new Dictionary<TileIndex, byte[]>();
        public static byte[] LastSlice;
        public static Extent destExtent;
        public static Extent sourceExtent;
        public static double curUnitsPerPixel = 1;
        public static bool UseVips = true;
        public virtual byte[] GetSlice(SliceInfo sliceInfo)
        {
            var curLevel = OpenSlideGTK.TileUtil.GetLevel(Schema.Resolutions, sliceInfo.Resolution, (OpenSlideGTK.SampleMode)sliceInfo.Parame.SampleMode);
            var curUnitsPerPixel = Schema.Resolutions[curLevel].UnitsPerPixel;
            var tileInfos = Schema.GetTileInfos(sliceInfo.Extent, curLevel);
            List<Tuple<Extent, byte[]>> tiles = new List<Tuple<Extent, byte[]>>();
            foreach (TileInfo t in tileInfos)
            {
                byte[] cache = null;
                if (_bgraCache.ContainsKey(t.Index))
                {
                    cache = _bgraCache[t.Index];
                }
                if (cache == null)
                {
                    cache = GetTile(t);
                    _bgraCache.Add(t.Index, cache);
                }
                tiles.Add(Tuple.Create(t.Extent.WorldToPixelInvertedY(curUnitsPerPixel), cache));
            }
            var srcPixelExtent = sliceInfo.Extent.WorldToPixelInvertedY(curUnitsPerPixel);
            var dstPixelExtent = sliceInfo.Extent.WorldToPixelInvertedY(sliceInfo.Resolution);
            var dstPixelHeight = sliceInfo.Parame.DstPixelHeight > 0 ? sliceInfo.Parame.DstPixelHeight : dstPixelExtent.Height;
            var dstPixelWidth = sliceInfo.Parame.DstPixelWidth > 0 ? sliceInfo.Parame.DstPixelWidth : dstPixelExtent.Width;
            destExtent = new Extent(0, 0, dstPixelWidth, dstPixelHeight);
            sourceExtent = srcPixelExtent;
            if (UseVips)
            {
                try
                {
                    NetVips.Image im = OpenSlideGTK.ImageUtil.JoinVips(tiles, srcPixelExtent, new Extent(0, 0, dstPixelWidth, dstPixelHeight));
                    LastSlice = im.WriteToMemory();
                    return LastSlice;
                }
                catch (Exception e)
                {
                    UseVips = false;
                    Console.WriteLine("Failed to use LibVips please install Libvips for your platform.");
                    Console.WriteLine(e.Message);
                }
            }
            try
            {
                Image<Rgb24> im = OpenSlideGTK.ImageUtil.Join(tiles, srcPixelExtent, new Extent(0, 0, dstPixelWidth, dstPixelHeight));
                LastSlice = GetRgb24Bytes(im);
                im.Dispose();
            }
            catch (Exception er)
            {
                Console.WriteLine(er.Message);
                return null;
            }
            return LastSlice;
        }
        public byte[] GetRgb24Bytes(Image<Rgb24> image)
        {
            int width = image.Width;
            int height = image.Height;
            byte[] rgbBytes = new byte[width * height * 3]; // 3 bytes per pixel (RGB)

            int byteIndex = 0;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Rgb24 pixel = image[x, y];
                    rgbBytes[byteIndex++] = pixel.R;
                    rgbBytes[byteIndex++] = pixel.G;
                    rgbBytes[byteIndex++] = pixel.B;
                }
            }

            return rgbBytes;
        }

        public ITileSchema Schema { get; protected set; }

        public string Name { get; protected set; }

        public Attribution Attribution { get; protected set; }

        public IReadOnlyDictionary<string, object> ExternInfo { get; protected set; }

        public string Source { get; protected set; }

        public abstract IReadOnlyDictionary<string, byte[]> GetExternImages();

        #region IDisposable
        private bool disposedValue;

        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    //_bgraCache.Dispose();
                }
                disposedValue = true;
            }
        }

        ~SlideSourceBase()
        {
            Dispose(disposing: false);
        }

        public void Dispose()
        {
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion
    }

    /// <summary>
    /// 
    /// </summary>
    public interface ISlideSource : ITileSource, ISliceProvider, ISlideExternInfo
    {

    }

    /// <summary>
    /// </summary>
    public interface ISlideExternInfo
    {
        /// <summary>
        /// File path.
        /// </summary>
        string Source { get; }

        /// <summary>
        /// Extern info.
        /// </summary>
        IReadOnlyDictionary<string, object> ExternInfo { get; }

        /// <summary>
        /// Extern image.
        /// </summary>
        /// <returns></returns>
        IReadOnlyDictionary<string, byte[]> GetExternImages();
    }

    /// <summary>
    /// </summary>
    public interface ISliceProvider
    {
        /// <summary>
        /// um/pixel
        /// </summary>
        double MinUnitsPerPixel { get; }

        /// <summary>
        /// Get slice.
        /// </summary>
        /// <param name="sliceInfo">Slice info</param>
        /// <returns></returns>
        byte[] GetSlice(SliceInfo sliceInfo);
    }

    /// <summary>
    /// Slice info.
    /// </summary>
    public class SliceInfo
    {
        public SliceInfo() { }

        /// <summary>
        /// Create a world extent by pixel and resolution.
        /// </summary>
        /// <param name="xPixel">pixel x</param>
        /// <param name="yPixel">pixel y</param>
        /// <param name="widthPixel">pixel width</param>
        /// <param name="heightPixel">pixel height</param>
        /// <param name="unitsPerPixel">um/pixel</param>
        public SliceInfo(double xPixel, double yPixel, double widthPixel, double heightPixel, double unitsPerPixel)
        {
            Extent = new Extent(xPixel, yPixel, xPixel + widthPixel, yPixel + heightPixel).PixelToWorldInvertedY(unitsPerPixel);
            Resolution = unitsPerPixel;
        }

        /// <summary>
        /// um/pixel
        /// </summary>
        public double Resolution
        {
            get;
            set;
        } = 1;

        /// <summary>
        /// World extent.
        /// </summary>
        public Extent Extent
        {
            get;
            set;
        }
        public SliceParame Parame
        {
            get;
            set;
        } = new SliceParame();
    }

    public class SliceParame
    {
        /// <summary>
        /// Scale to width,default 0(no scale)
        /// /// </summary>
        public int DstPixelWidth { get; set; } = 0;

        /// <summary>
        /// Scale to height,default 0(no scale)
        /// </summary>
        public int DstPixelHeight { get; set; } = 0;

        /// <summary>
        /// Sample mode.
        /// </summary>
        public SampleMode SampleMode { get; set; } = SampleMode.Nearest;

        /// <summary>
        /// Image quality.
        /// </summary>
        public int? Quality { get; set; }
    }


    public enum SampleMode
    {
        /// <summary>
        /// Nearest.
        /// </summary>
        Nearest = 0,
        /// <summary>
        /// Nearest up.
        /// </summary>
        NearestUp,
        /// <summary>
        /// Nearest dwon.
        /// </summary>
        NearestDwon,
        /// <summary>
        /// Top.
        /// </summary>
        Top,
        /// <summary>
        /// Bottom.
        /// </summary>
        /// <remarks>
        /// maybe very slow, just for clearer images.
        /// </remarks>
        Bottom,
    }

    /// <summary>
    /// Image type.
    /// </summary>
    public enum ImageType : int
    {
        /// <summary>
        /// </summary>
        Label,

        /// <summary>
        /// </summary>
        Title,

        /// <summary>
        /// </summary>
        Preview,
    }

    public static class ExtentEx
    {
        /// <summary>
        /// Convert OSM world to pixel
        /// </summary>
        /// <param name="extent">world extent</param>
        /// <param name="unitsPerPixel">resolution,um/pixel</param>
        /// <returns></returns>
        public static Extent WorldToPixelInvertedY(this Extent extent, double unitsPerPixel)
        {
            return new Extent(extent.MinX / unitsPerPixel, -extent.MaxY / unitsPerPixel, extent.MaxX / unitsPerPixel, -extent.MinY / unitsPerPixel);
        }


        /// <summary>
        /// Convert pixel to OSM world.
        /// </summary>
        /// <param name="extent">pixel extent</param>
        /// <param name="unitsPerPixel">resolution,um/pixel</param>
        /// <returns></returns>
        public static Extent PixelToWorldInvertedY(this Extent extent, double unitsPerPixel)
        {
            return new Extent(extent.MinX * unitsPerPixel, -extent.MaxY * unitsPerPixel, extent.MaxX * unitsPerPixel, -extent.MinY * unitsPerPixel);
        }

        /// <summary>
        /// Convert double to int.
        /// </summary>
        /// <param name="extent"></param>
        /// <returns></returns>
        public static Extent ToIntegerExtent(this Extent extent)
        {
            return new Extent((int)Math.Round(extent.MinX), (int)Math.Round(extent.MinY), (int)Math.Round(extent.MaxX), (int)Math.Round(extent.MaxY));
        }
    }

    public static class ObjectEx
    {
        /// <summary>
        /// Get fields and properties
        /// </summary>
        /// <param name="obj"></param>
        /// <returns></returns>
        public static Dictionary<string, object> GetFieldsProperties(this object obj)
        {
            Dictionary<string, object> keys = new Dictionary<string, object>();
            foreach (var item in obj.GetType().GetFields())
            {
                keys.Add(item.Name, item.GetValue(obj));
            }
            foreach (var item in obj.GetType().GetProperties())
            {
                try
                {
                    if (item.GetIndexParameters().Any()) continue;
                    keys.Add(item.Name, item.GetValue(obj));
                }
                catch (Exception) { }
            }
            return keys;
        }
    }
}