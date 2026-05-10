using AForge;
using Bio;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BioGTK
{
    /// <summary>
    /// Post-processes a saved Zarr directory so viewers like Fiji can pick up
    /// sane channel display defaults for RGB datasets.
    /// </summary>
    public static class ZarrMetadataFixup
    {
        public static void AddRgbDisplayMetadata(string zarrDir, BioImage image)
        {
            if (image == null || string.IsNullOrWhiteSpace(zarrDir))
                return;

            var pixelFormat = GetSourcePixelFormat(image);
            bool interleavedRgb = IsInterleavedRgb(pixelFormat);
            int exportChannelCount = interleavedRgb && image.SizeC == 1
                ? 3
                : Math.Max(1, image.SizeC);

            if (exportChannelCount <= 1)
                return;

            var omeroNode = CreateRgbDisplayMetadata(image);

            PatchRootScaleMetadata(Path.Combine(zarrDir, "zarr.json"));
            PatchRootScaleMetadata(Path.Combine(zarrDir, ".zattrs"));
            PatchRootMetadata(Path.Combine(zarrDir, "zarr.json"), omeroNode, true);
            PatchRootMetadata(Path.Combine(zarrDir, ".zattrs"), omeroNode, false);
        }

        public static JsonObject CreateRgbDisplayMetadata(BioImage image)
        {
            var pixelFormat = GetSourcePixelFormat(image);
            bool interleavedRgb = IsInterleavedRgb(pixelFormat);
            int exportChannelCount = interleavedRgb && image.SizeC == 1
                ? 3
                : Math.Max(1, image.SizeC);
            int maxValue = GetDisplayMaxValue(pixelFormat);
            return BuildOmeroNode(exportChannelCount, maxValue, Path.GetFileNameWithoutExtension(image?.Filename));
        }

        private static JsonObject BuildOmeroNode(int channelCount, int maxValue, string? imageName)
        {
            var channels = new JsonArray();
            string[] labels = channelCount == 3
                ? new[] { "Red", "Green", "Blue" }
                : Enumerable.Range(1, channelCount).Select(i => $"Channel {i}").ToArray();
            string[] colors = channelCount == 3
                ? new[] { "FF0000", "00FF00", "0000FF" }
                : Enumerable.Repeat("FFFFFF", channelCount).ToArray();

            for (int i = 0; i < channelCount; i++)
            {
                channels.Add(new JsonObject
                {
                    ["active"] = true,
                    ["coefficient"] = 1,
                    ["color"] = colors[i],
                    ["family"] = "linear",
                    ["inverted"] = false,
                    ["label"] = labels[i],
                    ["window"] = new JsonObject
                    {
                        ["min"] = 0,
                        ["max"] = maxValue,
                        ["start"] = 0,
                        ["end"] = maxValue
                    }
                });
            }

            return new JsonObject
            {
                ["version"] = "0.5",
                ["id"] = 0,
                ["name"] = string.IsNullOrWhiteSpace(imageName) ? "image" : imageName,
                ["channels"] = channels,
                ["rdefs"] = new JsonObject
                {
                    ["defaultZ"] = 0,
                    ["defaultT"] = 0,
                    ["defaultC"] = 0,
                    ["model"] = channelCount > 1 ? "color" : "greyscale"
                }
            };
        }

        private static void PatchRootMetadata(string path, JsonObject omeroNode, bool isV3Root)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var text = File.ReadAllText(path);
                var root = JsonNode.Parse(text) as JsonObject;
                if (root == null)
                    return;

                if (isV3Root)
                {
                    var attributes = root["attributes"] as JsonObject;
                    var ome = attributes?["ome"] as JsonObject;
                    if (ome == null)
                        return;
                    ome["omero"] = omeroNode.DeepClone();
                }
                else
                {
                    root["omero"] = omeroNode.DeepClone();
                }

                File.WriteAllText(
                    path,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Keep the save flow resilient if the metadata patch fails.
            }
        }

        private static void PatchRootScaleMetadata(string path)
        {
            if (!File.Exists(path))
                return;

            try
            {
                var text = File.ReadAllText(path);
                var root = JsonNode.Parse(text) as JsonObject;
                if (root == null)
                    return;

                if (root["attributes"] is JsonObject attributes &&
                    attributes["ome"] is JsonObject ome &&
                    ome["multiscales"] is JsonArray multiscales)
                {
                    foreach (var multiscaleNode in multiscales.OfType<JsonObject>())
                    {
                        NormalizeScaleEntries(multiscaleNode);
                    }
                }
                else if (root["multiscales"] is JsonArray legacyMultiscales)
                {
                    foreach (var multiscaleNode in legacyMultiscales.OfType<JsonObject>())
                    {
                        NormalizeScaleEntries(multiscaleNode);
                    }
                }

                File.WriteAllText(
                    path,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Keep the save flow resilient if the metadata patch fails.
            }
        }

        private static void NormalizeScaleEntries(JsonObject multiscaleNode)
        {
            if (multiscaleNode["datasets"] is not JsonArray datasets)
                return;

            foreach (var datasetNode in datasets.OfType<JsonObject>())
            {
                if (datasetNode["coordinateTransformations"] is not JsonArray transforms)
                    continue;

                foreach (var transformNode in transforms.OfType<JsonObject>())
                {
                    if (!string.Equals(transformNode["type"]?.GetValue<string>(), "scale", StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (transformNode["scale"] is not JsonArray scale)
                        continue;

                    if (scale.Count < 3)
                        continue;

                    for (int i = 0; i < scale.Count; i++)
                    {
                        if (scale[i] is JsonValue value && value.TryGetValue<double>(out double d) && (!double.IsFinite(d) || d <= 0))
                        {
                            scale[i] = 1.0;
                        }
                    }
                }
            }
        }

        private static PixelFormat GetSourcePixelFormat(BioImage image)
        {
            if (image.Resolutions.Count > 0)
                return image.Resolutions[0].PixelFormat;

            if (image.Buffers.Count > 0)
                return image.Buffers[0].PixelFormat;

            return PixelFormat.Format16bppGrayScale;
        }

        private static bool IsInterleavedRgb(PixelFormat format)
        {
            return format == PixelFormat.Format24bppRgb ||
                   format == PixelFormat.Format32bppArgb ||
                   format == PixelFormat.Format32bppPArgb ||
                   format == PixelFormat.Format32bppRgb ||
                   format == PixelFormat.Format48bppRgb ||
                   format == PixelFormat.Format64bppArgb ||
                   format == PixelFormat.Format64bppPArgb;
        }

        private static int GetDisplayMaxValue(PixelFormat format)
        {
            return format switch
            {
                PixelFormat.Format16bppGrayScale => ushort.MaxValue,
                PixelFormat.Format48bppRgb => ushort.MaxValue,
                PixelFormat.Format64bppArgb => ushort.MaxValue,
                PixelFormat.Format64bppPArgb => ushort.MaxValue,
                _ => byte.MaxValue
            };
        }
    }
}
