#if BIOGTK_MAC_BUNDLE
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace BioGTKApp;

internal static class GtkBundleStartup
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        string executable = Environment.ProcessPath
            ?? throw new InvalidOperationException("Could not locate the BioGTK launcher.");
        string contents = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable)!, ".."));
        string gtk = Path.Combine(contents, "Resources", "Gtk");
        string share = Path.Combine(gtk, "share");
        string libraries = Path.Combine(contents, "Frameworks", "Gtk");

        SetNativeEnvironment("GTK_DATA_PREFIX", gtk);
        SetNativeEnvironment("GTK_EXE_PREFIX", gtk);
        SetNativeEnvironment("XDG_DATA_DIRS", share);
        SetNativeEnvironment("GSETTINGS_SCHEMA_DIR", Path.Combine(share, "glib-2.0", "schemas"));
        SetNativeEnvironment("GIO_MODULE_DIR", Path.Combine(gtk, "gio-modules"));

        DirectoryInfo caches = Directory.CreateTempSubdirectory("biogtk-gtk-");
        AppDomain.CurrentDomain.ProcessExit += (_, _) =>
        {
            try
            {
                caches.Delete(recursive: true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        };

        // GTK caches contain quoted absolute paths. Expand them after Finder
        // locates the installed copy of the app.
        string escapedLibraries = libraries.Replace("\\", "\\\\").Replace("\"", "\\\"");
        WriteCache("loaders.cache", "GDK_PIXBUF_MODULE_FILE");
        WriteCache("immodules.cache", "GTK_IM_MODULE_FILE");

        void WriteCache(string name, string variable)
        {
            string template = Path.Combine(gtk, name);
            string cache = Path.Combine(caches.FullName, name);
            File.WriteAllText(cache, File.ReadAllText(template).Replace("@GTK_LIB_DIR@", escapedLibraries));
            SetNativeEnvironment(variable, cache);
        }
    }

    private static void SetNativeEnvironment(string name, string value)
    {
        if (SetEnv(name, value, 1) != 0)
            throw new IOException($"Could not set {name} (errno {Marshal.GetLastPInvokeError()}).");
    }

    // GTK and GLib read the native process environment during Application.Init().
    [DllImport("/usr/lib/libSystem.B.dylib", EntryPoint = "setenv", SetLastError = true)]
    private static extern int SetEnv(string name, string value, int overwrite);
}
#endif
