using System;
using System.IO;

namespace BioGTK
{
    internal static class AppStorage
    {
        private static readonly string executableDirectory =
            Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;

        internal static readonly bool IsPackagedMacApp = OperatingSystem.IsMacOS() &&
            Directory.Exists(Path.Combine(executableDirectory, "..", "Resources", "Gtk"));

        internal static string DirectoryPath => IsPackagedMacApp
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BioGTK")
            : executableDirectory;
    }
}
