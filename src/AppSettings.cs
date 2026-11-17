using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace AtlasLite
{
    internal sealed class Shortcut
    {
        public string Name;
        public string Url;
    }

    /// <summary>User settings, stored as plain text in %APPDATA%\AtlasLite (no extra libraries).</summary>
    internal sealed class AppSettings
    {
        public bool Dark = true;
        public bool AdBlock = true;
        public bool RamSaver = true;
        public bool ForceDarkPages = false;      // needs restart (browser flag)
        public string Search = "duckduckgo";
        public string Wallpaper = "aurora";      // preset id, or "custom"
        public int WallpaperDim = 25;            // 0-80 %
        public int WallpaperBlur = 0;            // 0-20 px
        public string Accent = "#7c9cff";
        public long BlockedTotal = 0;
        public List<Shortcut> Shortcuts = new List<Shortcut>();

        public static readonly string[][] Engines =
        {
            new[] { "duckduckgo", "DuckDuckGo", "https://duckduckgo.com/?q=" },
            new[] { "google", "Google", "https://www.google.com/search?q=" },
            new[] { "bing", "Bing", "https://www.bing.com/search?q=" },
            new[] { "brave", "Brave Search", "https://search.brave.com/search?q=" },
            new[] { "startpage", "Startpage", "https://www.startpage.com/do/search?q=" },
        };

        public static string DataDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AtlasLite");
            }
        }

        public static string WallpaperDir { get { return Path.Combine(DataDir, "wallpaper"); } }
        public static string WebViewDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AtlasLite", "WebView2Data");
            }
        }

        string SettingsFile { get { return Path.Combine(DataDir, "settings.ini"); } }
        string ShortcutsFile { get { return Path.Combine(DataDir, "shortcuts.txt"); } }

        public string SearchUrl
        {
            get
            {
                foreach(var e in Engines)
                {
                    if(e[0] == Search) return e[2];
                }
                return Engines[0][2];
            }
        }

        public string CustomWallpaperFile()
        {
            try
            {
                if(!Directory.Exists(WallpaperDir)) return null;
                var files = Directory.GetFiles(WallpaperDir, "wallpaper.*");
                return files.Length > 0 ? files[0] : null;
            }
            catch { return null; }
        }

        public static AppSettings Load()
        {
            var s = new AppSettings();
            try
            {
                string f = s.SettingsFile;
                if(File.Exists(f))
                {
                    foreach(var raw in File.ReadAllLines(f, Encoding.UTF8))
                    {
                        int eq = raw.IndexOf('=');
                        if(eq <= 0) continue;
                        s.Apply(raw.Substring(0, eq).Trim(), raw.Substring(eq + 1).Trim());
                    }
                }
            }
            catch { /* corrupt file: keep defaults */ }

            try
            {
                if(File.Exists(s.ShortcutsFile))
                {
                    foreach(var raw in File.ReadAllLines(s.ShortcutsFile, Encoding.UTF8))
                    {
                        var parts = raw.Split('\t');
                        if(parts.Length == 2 && parts[0].Length > 0 && parts[1].Length > 0)
                        {
                            s.Shortcuts.Add(new Shortcut { Name = parts[0], Url = parts[1] });
                        }
                    }
                }
                else
                {
                    s.Shortcuts.Add(new Shortcut { Name = "DuckDuckGo", Url = "https://duckduckgo.com" });
                    s.Shortcuts.Add(new Shortcut { Name = "Wikipedia", Url = "https://www.wikipedia.org" });
                    s.Shortcuts.Add(new Shortcut { Name = "YouTube", Url = "https://www.youtube.com" });
                    s.Shortcuts.Add(new Shortcut { Name = "GitHub", Url = "https://github.com" });
                    s.Shortcuts.Add(new Shortcut { Name = "Reddit", Url = "https://www.reddit.com" });
                }
            }
            catch { }
            return s;
        }

        /// <summary>Applies one setting from text. Returns false if the key or value is not valid.</summary>
        public bool Apply(string key, string value)
        {
            switch(key)
            {
                case "dark": return ParseBool(value, ref Dark);
                case "adblock": return ParseBool(value, ref AdBlock);
                case "ramSaver": return ParseBool(value, ref RamSaver);
                case "forceDark": return ParseBool(value, ref ForceDarkPages);
                case "search":
                    foreach(var e in Engines)
                    {
                        if(e[0] == value) { Search = value; return true; }
                    }
                    return false;
                case "wallpaper":
                    if(value.Length > 0 && value.Length <= 24 && IsSimpleId(value)) { Wallpaper = value; return true; }
                    return false;
                case "dim": return ParseInt(value, 0, 80, ref WallpaperDim);
                case "blur": return ParseInt(value, 0, 20, ref WallpaperBlur);
                case "accent":
                    if(IsHexColor(value)) { Accent = value.ToLowerInvariant(); return true; }
                    return false;
                case "blockedTotal":
                    long n;
                    if(long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out n)) { BlockedTotal = n; return true; }
                    return false;
            }
            return false;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(DataDir);
                var sb = new StringBuilder();
                sb.AppendLine("dark=" + (Dark ? "1" : "0"));
                sb.AppendLine("adblock=" + (AdBlock ? "1" : "0"));
                sb.AppendLine("ramSaver=" + (RamSaver ? "1" : "0"));
                sb.AppendLine("forceDark=" + (ForceDarkPages ? "1" : "0"));
                sb.AppendLine("search=" + Search);
                sb.AppendLine("wallpaper=" + Wallpaper);
                sb.AppendLine("dim=" + WallpaperDim.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("blur=" + WallpaperBlur.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("accent=" + Accent);
                sb.AppendLine("blockedTotal=" + BlockedTotal.ToString(CultureInfo.InvariantCulture));
                File.WriteAllText(SettingsFile, sb.ToString(), new UTF8Encoding(false));

                var sc = new StringBuilder();
                foreach(var s in Shortcuts)
                {
                    sc.AppendLine(Clean(s.Name) + "\t" + Clean(s.Url));
                }
                File.WriteAllText(ShortcutsFile, sc.ToString(), new UTF8Encoding(false));
            }
            catch { /* read-only profile etc.: settings just will not persist */ }
        }

        static string Clean(string s)
        {
            return (s ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }

        static bool ParseBool(string v, ref bool target)
        {
            if(v == "1" || v == "true") { target = true; return true; }
            if(v == "0" || v == "false") { target = false; return true; }
            return false;
        }

        static bool ParseInt(string v, int min, int max, ref int target)
        {
            int n;
            if(!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out n)) return false;
            if(n < min) n = min;
            if(n > max) n = max;
            target = n;
            return true;
        }

        static bool IsSimpleId(string v)
        {
            foreach(char c in v)
            {
                if(!(char.IsLetterOrDigit(c) || c == '-' || c == '_')) return false;
            }
            return true;
        }

        public static bool IsHexColor(string v)
        {
            if(v == null || v.Length != 7 || v[0] != '#') return false;
            for(int i = 1; i < 7; i++)
            {
                char c = v[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if(!ok) return false;
            }
            return true;
        }

        // ---------- JSON for the settings / new-tab pages ----------

        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append('{');
            sb.Append("\"dark\":").Append(Dark ? "true" : "false");
            sb.Append(",\"adblock\":").Append(AdBlock ? "true" : "false");
            sb.Append(",\"ramSaver\":").Append(RamSaver ? "true" : "false");
            sb.Append(",\"forceDark\":").Append(ForceDarkPages ? "true" : "false");
            sb.Append(",\"search\":").Append(Json.Str(Search));
            sb.Append(",\"wallpaper\":").Append(Json.Str(Wallpaper));
            sb.Append(",\"dim\":").Append(WallpaperDim.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"blur\":").Append(WallpaperBlur.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"accent\":").Append(Json.Str(Accent));
            sb.Append(",\"blocked\":").Append(BlockedTotal.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"version\":").Append(Json.Str("1.0.0"));

            string custom = CustomWallpaperFile();
            if(custom != null)
            {
                long stamp = 0;
                try { stamp = File.GetLastWriteTimeUtc(custom).Ticks; } catch { }
                sb.Append(",\"customWallpaper\":").Append(Json.Str("https://" + PageHost.DataHost + "/" + Path.GetFileName(custom) + "?v=" + stamp.ToString(CultureInfo.InvariantCulture)));
            }
            else
            {
                sb.Append(",\"customWallpaper\":null");
            }

            sb.Append(",\"engines\":[");
            for(int i = 0; i < Engines.Length; i++)
            {
                if(i > 0) sb.Append(',');
                sb.Append("{\"id\":").Append(Json.Str(Engines[i][0])).Append(",\"name\":").Append(Json.Str(Engines[i][1])).Append('}');
            }
            sb.Append(']');

            sb.Append(",\"shortcuts\":[");
            for(int i = 0; i < Shortcuts.Count; i++)
            {
                if(i > 0) sb.Append(',');
                sb.Append("{\"name\":").Append(Json.Str(Shortcuts[i].Name)).Append(",\"url\":").Append(Json.Str(Shortcuts[i].Url)).Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }
    }

    internal static class Json
    {
        public static string Str(string s)
        {
            if(s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach(char c in s)
            {
                switch(c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    case '<': sb.Append("\\u003c"); break; // keeps "</script>" harmless
                    default:
                        if(c < 0x20) sb.Append("\\u").Append(((int) c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }
    }

    internal static class PageHost
    {
        public const string Host = "atlas.local";          // our pages (new tab, settings)
        public const string DataHost = "atlas-data.local"; // user wallpaper only
    }
}
