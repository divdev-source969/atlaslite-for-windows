using System;

namespace AtlasLite
{
    internal static class Nav
    {
        public const string NewTabUrl = "https://atlas.local/newtab.html";
        public const string SettingsUrl = "https://atlas.local/settings.html";

        /// <summary>Turns what the user typed into a URL: address, atlas:// page or web search. Null = ignore.</summary>
        public static string Normalize(string raw, string searchUrl)
        {
            string t = (raw ?? "").Trim();
            if(t.Length == 0) return null;
            string lower = t.ToLowerInvariant();

            if(lower.StartsWith("atlas://", StringComparison.Ordinal))
            {
                string name = lower.Substring(8).Trim('/');
                return name == "settings" ? SettingsUrl : NewTabUrl;
            }
            if(lower.StartsWith("javascript:", StringComparison.Ordinal) || lower.StartsWith("vbscript:", StringComparison.Ordinal))
            {
                return null;
            }
            if(lower.StartsWith("http://", StringComparison.Ordinal) || lower.StartsWith("https://", StringComparison.Ordinal)
               || lower.StartsWith("file://", StringComparison.Ordinal) || lower.StartsWith("edge://", StringComparison.Ordinal)
               || lower.StartsWith("about:", StringComparison.Ordinal))
            {
                return t;
            }

            if(t.IndexOf(' ') < 0)
            {
                Uri u;
                if(Uri.TryCreate("https://" + t, UriKind.Absolute, out u))
                {
                    string host = u.Host;
                    if(host == "localhost" || (host.IndexOf('.') > 0 && !host.EndsWith(".", StringComparison.Ordinal)))
                    {
                        return u.AbsoluteUri;
                    }
                }
            }
            return searchUrl + Uri.EscapeDataString(t);
        }

        /// <summary>What to show in the address box for a URL.</summary>
        public static string Display(string url)
        {
            if(string.IsNullOrEmpty(url)) return "";
            Uri u;
            if(Uri.TryCreate(url, UriKind.Absolute, out u) && string.Equals(u.Host, PageHost.Host, StringComparison.OrdinalIgnoreCase))
            {
                string f = System.IO.Path.GetFileNameWithoutExtension(u.AbsolutePath);
                return "atlas://" + (string.IsNullOrEmpty(f) ? "newtab" : f);
            }
            return url;
        }

        /// <summary>Only http(s) links may be saved as new-tab shortcuts.</summary>
        public static string CleanShortcutUrl(string raw)
        {
            string t = (raw ?? "").Trim();
            if(t.Length == 0) return null;
            if(!t.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !t.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                t = "https://" + t;
            }
            Uri u;
            if(!Uri.TryCreate(t, UriKind.Absolute, out u)) return null;
            if(u.Scheme != "http" && u.Scheme != "https") return null;
            if(u.Host.IndexOf('.') < 0 && u.Host != "localhost") return null;
            return u.AbsoluteUri;
        }
    }
}
