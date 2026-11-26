using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;

internal static class StartupHook
{
    public static void Initialize()
    {
        var gtk = Assembly.Load("GtkSharp").GetType("Gtk.Application", true)!;
        gtk.GetMethod("Init", Type.EmptyTypes)!.Invoke(null, null);
        var biogtk = Assembly.Load("BioGTK");
        var nodeType = biogtk.GetType("BioGTK.NodeView", true)!;
        var node = nodeType.GetMethod("Create")!.Invoke(null, new object[] { Array.Empty<string>() })!;
        nodeType.GetMethod("Show", Type.EmptyTypes)!.Invoke(node, null);
        var invoke = gtk.GetMethod("Invoke", new[] { typeof(EventHandler) })!;
        invoke.Invoke(null, new object[] { new EventHandler(async (_, _) => await OpenImage(biogtk)) });
        gtk.GetMethod("Run", Type.EmptyTypes)!.Invoke(null, null);
    }

    private static async Task OpenImage(Assembly biogtk)
    {
        string path = Environment.GetEnvironmentVariable("BIOGTK_OPEN_PROBE_PATH")
            ?? throw new InvalidOperationException("Missing input image path.");
        string expected = Environment.GetEnvironmentVariable("BIOGTK_OPEN_PROBE_EXPECTED") ?? "";
        try
        {
            var app = biogtk.GetType("BioGTK.App", true)!;
            var tabs = app.GetField("tabsView", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!
                .GetValue(null)!;
            var method = tabs.GetType().GetMethod("OpenPyramidRootsAsTabsAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var task = (Task<bool>)method.Invoke(tabs, new object[] { path, false })!;
            if (!await task)
                throw new IOException($"No image opened for '{path}'.");

            var view = biogtk.GetType("BioGTK.ImageView", true)!;
            var image = view.GetProperty("SelectedImage", BindingFlags.Static | BindingFlags.Public)!
                .GetValue(null) ?? throw new InvalidOperationException("The image was not selected in its tab.");
            var type = image.GetType();
            int depth = (int)type.GetProperty("SizeZ")!.GetValue(image)!;
            bool pyramid = (bool)type.GetProperty("isPyramidal")!.GetValue(image)!;
            if (expected == "stack" && (depth != 2 || pyramid))
                throw new InvalidOperationException($"Expected a 2-plane stack, got Z={depth}, pyramid={pyramid}.");
            if (expected == "pyramid" && !pyramid)
                throw new InvalidOperationException("The whole-slide image opened without its pyramid.");

            Console.WriteLine($"Opened {Path.GetFileName(path)}: Z={depth}, pyramid={pyramid}.");
            Environment.Exit(0);
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            for (Exception? cause = error; cause != null; cause = cause.InnerException)
                cause.GetType().GetMethod("printStackTrace", Type.EmptyTypes)?.Invoke(cause, null);
            Environment.Exit(1);
        }
    }
}
