using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace BioGTK
{
    internal static class FijiApp
    {
        internal static string Executable { get; private set; } = string.Empty;
        internal static bool IsFijiInstallation => Executable.Contains("Fiji", StringComparison.OrdinalIgnoreCase);

        internal static bool Configure(string setting)
        {
            string executable = string.Empty;
            if (!string.IsNullOrWhiteSpace(setting))
            {
                try
                {
                    executable = ResolveExecutable(setting);
                }
                catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or
                    XmlException or System.ComponentModel.Win32Exception)
                {
                    Console.Error.WriteLine($"Could not use ImageJPath '{setting}'. Using bundled ImageJ. {ex.Message}");
                }
            }

            Executable = executable;
            Fiji.ImageJPath = executable;
            ImageJ.ImageJPath = executable;
            return executable.Length > 0;
        }

        internal static string ResolveExecutable(string setting)
        {
            string path = setting.Trim().Trim('"');
            if (path.StartsWith("~/", StringComparison.Ordinal))
                path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), path[2..]);
            if (!Path.IsPathFullyQualified(path))
                throw new ArgumentException("Use an absolute path to the Fiji or ImageJ launcher.");

            path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            if (OperatingSystem.IsMacOS() && path.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                string contents = Path.Combine(path, "Contents");
                string executable = ReadBundleExecutable(Path.Combine(contents, "Info.plist"));
                if (string.IsNullOrWhiteSpace(executable) || executable != Path.GetFileName(executable) ||
                    executable is "." or "..")
                    throw new InvalidDataException("The app bundle has an invalid CFBundleExecutable.");
                path = Path.Combine(contents, "MacOS", executable);
            }

            if (!File.Exists(path))
                throw new FileNotFoundException("Fiji/ImageJ executable was not found.", path);
            if (OperatingSystem.IsWindows() &&
                !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Use the native Fiji or ImageJ .exe launcher on Windows.");
            if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
            {
                const UnixFileMode execute = UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute;
                if ((File.GetUnixFileMode(path) & execute) == 0)
                    throw new IOException($"Fiji/ImageJ launcher is not executable: {path}");
            }
            return path;
        }

        private static string ReadBundleExecutable(string plist)
        {
            using var stream = File.OpenRead(plist);
            Span<byte> header = stackalloc byte[8];
            if (stream.Read(header) == header.Length && header.SequenceEqual("bplist00"u8))
            {
                var start = new ProcessStartInfo("/usr/bin/plutil")
                {
                    UseShellExecute = false,
                    RedirectStandardOutput = true
                };
                foreach (string argument in new[] { "-extract", "CFBundleExecutable", "raw", "-o", "-", plist })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start) ?? throw new IOException("Could not read the Fiji app bundle.");
                string executable = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (process.ExitCode != 0)
                    throw new InvalidDataException("The app bundle has no readable CFBundleExecutable.");
                return executable;
            }

            stream.Position = 0;
            using var reader = XmlReader.Create(stream, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            var executableKey = XDocument.Load(reader).Root?.Element("dict")?.Elements("key")
                .FirstOrDefault(key => key.Value == "CFBundleExecutable");
            var value = executableKey?.ElementsAfterSelf().FirstOrDefault();
            return value?.Name == "string" ? value.Value : string.Empty;
        }
    }
}
