using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace BioGTK
{
    internal static class TemporaryZarrStorage
    {
        private const string SessionPrefix = "session-";
        private const string FijiPrefix = "fiji-";
        private const string LeaseName = ".lease";
        private static readonly string root = Path.Combine(Path.GetTempPath(), "BioLib-Zarr");
        private static readonly string legacyRoot = Path.Combine(
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory, "Temp");
        private static int initialized;

        internal static DirectoryInfo CreateFijiWorkspace()
        {
            Initialize();
            if (AppContext.GetData("BioLib.TemporaryZarrRoot") is not string session)
                throw new IOException("Temporary image storage is unavailable.");
            return Directory.CreateDirectory(Path.Combine(session, FijiPrefix + Guid.NewGuid().ToString("N")));
        }

        internal static void Initialize()
        {
            if (Interlocked.Exchange(ref initialized, 1) != 0)
                return;

            string session = Path.Combine(root, SessionPrefix + Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(session);
                var lease = File.Open(Path.Combine(session, LeaseName), FileMode.CreateNew,
                    FileAccess.ReadWrite, FileShare.None);
                AppContext.SetData("BioLib.TemporaryZarrRoot", session);
                AppDomain.CurrentDomain.ProcessExit += (_, _) =>
                {
                    lease.Dispose();
                    TryDelete(session);
                };
                _ = Task.Run(() => RemoveAbandonedStores(session));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not initialize temporary Zarr storage: {ex.Message}");
            }
        }

        internal static bool IsTemporarySource(string source)
        {
            if (string.IsNullOrWhiteSpace(source) || !Path.IsPathFullyQualified(source))
                return false;

            string parent;
            try
            {
                parent = Path.GetDirectoryName(Path.GetFullPath(source));
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or IOException)
            {
                return false;
            }

            string name = Path.GetFileName(parent);
            string session = Path.GetDirectoryName(parent);
            if (name?.StartsWith(FijiPrefix, StringComparison.Ordinal) == true &&
                Guid.TryParseExact(name[FijiPrefix.Length..], "N", out _) &&
                IsSessionName(Path.GetFileName(session)) && SamePath(Path.GetDirectoryName(session), root))
                return true;

            if (!IsResultName(Path.GetFileName(source)))
                return false;
            if (SamePath(parent, root) || SamePath(parent, legacyRoot))
                return true;
            if (IsSessionName(Path.GetFileName(parent)) && SamePath(Path.GetDirectoryName(parent), root))
                return true;

            // A saved entry may come from an older app bundle at a different location.
            if (!OperatingSystem.IsMacOS() || Path.GetFileName(parent) != "Temp")
                return false;
            string bundleChild = Path.GetDirectoryName(parent);
            string contents = Path.GetDirectoryName(bundleChild);
            if (Path.GetFileName(bundleChild) == "Dotnet" && Path.GetFileName(contents) == "Resources")
                contents = Path.GetDirectoryName(contents);
            else if (Path.GetFileName(bundleChild) != "MacOS")
                return false;
            return Path.GetFileName(contents) == "Contents" &&
                Path.GetFileName(Path.GetDirectoryName(contents)) == "BioGTK.app";
        }

        private static bool IsResultName(string name) => name != null && name.Length > 32 &&
            (name[32..] is "-level.zarr" or "-pyr.zarr") &&
            Guid.TryParseExact(name[..32], "N", out _);

        private static bool IsSessionName(string name) => name != null &&
            name.StartsWith(SessionPrefix, StringComparison.Ordinal) &&
            Guid.TryParseExact(name[SessionPrefix.Length..], "N", out _);

        private static bool SamePath(string first, string second)
        {
            // macOS can report the same temporary directory through /var or /private/var.
            if (OperatingSystem.IsMacOS())
            {
                if (first?.StartsWith("/private/var/", StringComparison.Ordinal) == true)
                    first = first[8..];
                if (second?.StartsWith("/private/var/", StringComparison.Ordinal) == true)
                    second = second[8..];
            }
            return string.Equals(first, second, OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
        }

        private static void RemoveAbandonedStores(string session)
        {
            try
            {
                foreach (DirectoryInfo directory in new DirectoryInfo(root).EnumerateDirectories())
                {
                    if (!IsSessionName(directory.Name) || SamePath(directory.FullName, session) ||
                        (directory.Attributes & FileAttributes.ReparsePoint) != 0)
                        continue;

                    try
                    {
                        // Session names are unique. An unlocked lease belongs to an exited process.
                        using (File.Open(Path.Combine(directory.FullName, LeaseName), FileMode.Open,
                            FileAccess.ReadWrite, FileShare.None)) { }
                        TryDelete(directory.FullName);
                    }
                    catch (FileNotFoundException)
                    {
                        // Give a starting process time to create its lease.
                        if (directory.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-1))
                            TryDelete(directory.FullName);
                    }
                    catch (IOException)
                    {
                        // The lease is held by another instance, or the session was already removed.
                    }
                }

                if (!AnotherInstanceIsRunning())
                {
                    RemoveLegacyStores(root);
                    RemoveLegacyStores(legacyRoot);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                Console.Error.WriteLine($"Could not clean temporary Zarr storage: {ex.Message}");
            }
        }

        private static bool AnotherInstanceIsRunning()
        {
            using var current = Process.GetCurrentProcess();
            bool found = false;
            foreach (Process process in Process.GetProcessesByName(current.ProcessName))
            {
                using (process)
                    found |= process.Id != current.Id;
            }
            return found;
        }

        private static void RemoveLegacyStores(string directory)
        {
            if (!Directory.Exists(directory))
                return;
            foreach (DirectoryInfo entry in new DirectoryInfo(directory).EnumerateDirectories())
            {
                if (IsResultName(entry.Name) && (entry.Attributes & FileAttributes.ReparsePoint) == 0)
                    TryDelete(entry.FullName);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (DirectoryNotFoundException) { }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine($"Could not remove temporary Zarr storage '{path}': {ex.Message}");
            }
        }
    }
}
