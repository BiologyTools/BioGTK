using System;
using System.IO;

namespace BioGTK
{
    public static class AppLog
    {
        public static string FilePath => Path.Combine(AppContext.BaseDirectory, "biolog.txt");

        public static void Clear()
        {
            try
            {
                File.WriteAllText(FilePath, string.Empty);
            }
            catch
            {
            }
        }

        public static void Append(string message)
        {
            try
            {
                File.AppendAllText(FilePath, message + Environment.NewLine);
            }
            catch
            {
            }
        }
    }
}
