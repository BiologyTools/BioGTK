using System;

namespace BioGTK
{
    internal static class ZarrSource
    {
        internal static string Resolve(string source)
        {
            if (!Uri.TryCreate(source, UriKind.Absolute, out Uri uri))
                return source;

            if (uri.Scheme.Equals("s3", StringComparison.OrdinalIgnoreCase) &&
                uri.Host.Equals("idr", StringComparison.OrdinalIgnoreCase))
            {
                return "https://livingobjects.ebi.ac.uk/idr" + uri.PathAndQuery + uri.Fragment;
            }

            if ((uri.Scheme == "https" || uri.Scheme == "http") &&
                uri.Host.Equals("uk1s3.embassy.ebi.ac.uk", StringComparison.OrdinalIgnoreCase) &&
                uri.AbsolutePath.StartsWith("/idr/", StringComparison.Ordinal))
            {
                var resolved = new UriBuilder(uri) { Scheme = "https", Host = "livingobjects.ebi.ac.uk", Port = -1 };
                return resolved.Uri.AbsoluteUri;
            }

            return source;
        }
    }
}
