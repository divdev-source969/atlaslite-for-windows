using System;
using System.Collections.Generic;
using System.IO;

namespace AtlasLite
{
    /// <summary>
    /// Small ad / tracker blocker. No dependencies, no regular expressions (keeps RAM and CPU low).
    /// Rule formats (one per line): domain, hosts-file line, ||domain^, ||domain/path, substring, *wildcards*, @@allow.
    /// Lines starting with # or ! are comments.
    /// </summary>
    public sealed class AdBlocker
    {
        readonly HashSet<string> blockDomains = new HashSet<string>(StringComparer.Ordinal);
        readonly HashSet<string> allowDomains = new HashSet<string>(StringComparer.Ordinal);
        readonly List<string> blockPatterns = new List<string>();
        readonly List<string> allowPatterns = new List<string>();

        /// <summary>Replaces all rules with the rules of another blocker (used after the user edits the list).</summary>
        public void ReplaceWith(AdBlocker other)
        {
            blockDomains.Clear(); allowDomains.Clear(); blockPatterns.Clear(); allowPatterns.Clear();
            foreach(var d in other.blockDomains) blockDomains.Add(d);
            foreach(var d in other.allowDomains) allowDomains.Add(d);
            blockPatterns.AddRange(other.blockPatterns);
            allowPatterns.AddRange(other.allowPatterns);
        }

        public int RuleCount { get { return blockDomains.Count + allowDomains.Count + blockPatterns.Count + allowPatterns.Count; } }

        public void LoadFile(string path)
        {
            try
            {
                if(File.Exists(path)) AddLines(File.ReadAllLines(path));
            }
            catch { /* a missing or locked list is not fatal */ }
        }

        public void AddLines(IEnumerable<string> lines)
        {
            foreach(var line in lines) AddRule(line);
        }

        public void AddRule(string raw)
        {
            if(raw == null) return;
            string line = raw.Trim().ToLowerInvariant();
            if(line.Length == 0 || line[0] == '#' || line[0] == '!' || line[0] == '[') return;

            bool allow = false;
            if(line.StartsWith("@@", StringComparison.Ordinal))
            {
                allow = true;
                line = line.Substring(2).Trim();
            }

            // Options such as $third-party are ignored: the rule applies everywhere.
            int dollar = line.IndexOf('$');
            if(dollar >= 0) line = line.Substring(0, dollar);

            // hosts-file line: "0.0.0.0 ads.example.com"
            string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if(parts.Length == 2 && (parts[0] == "0.0.0.0" || parts[0] == "127.0.0.1"))
            {
                if(parts[1] != "localhost" && parts[1].IndexOf('.') > 0) Add(allow, parts[1], true);
                return;
            }
            if(parts.Length != 1) return;
            line = parts[0];
            if(line.Length == 0) return;

            if(line.StartsWith("||", StringComparison.Ordinal))
            {
                string rest = line.Substring(2);
                int caret = rest.IndexOf('^');
                if(caret >= 0) rest = rest.Substring(0, caret);
                if(rest.Length == 0) return;
                if(rest.IndexOf('/') < 0 && rest.IndexOf('*') < 0)
                {
                    Add(allow, rest, true);
                }
                else
                {
                    Add(allow, rest, false); // domain + path: matched against host+path
                }
                return;
            }

            if(line.IndexOf('/') < 0 && line.IndexOf('*') < 0 && line.IndexOf('.') > 0 && !line.StartsWith(".", StringComparison.Ordinal))
            {
                Add(allow, line.TrimEnd('^'), true);
                return;
            }

            line = line.Trim('|', '^');
            if(line.Length >= 3) Add(allow, line, false);
        }

        void Add(bool allow, string value, bool isDomain)
        {
            if(isDomain) (allow ? allowDomains : blockDomains).Add(value);
            else (allow ? allowPatterns : blockPatterns).Add(value);
        }

        /// <summary>True if this request URL should be stopped.</summary>
        public bool ShouldBlock(string url)
        {
            if(string.IsNullOrEmpty(url)) return false;
            string lower = url.ToLowerInvariant();
            string host = HostOf(lower);
            if(host.Length == 0) return false;
            string hostPath = HostAndPath(lower);

            if(DomainMatches(allowDomains, host) || PatternMatches(allowPatterns, hostPath, lower)) return false;
            return DomainMatches(blockDomains, host) || PatternMatches(blockPatterns, hostPath, lower);
        }

        static bool DomainMatches(HashSet<string> set, string host)
        {
            if(set.Count == 0) return false;
            string h = host;
            while(true)
            {
                if(set.Contains(h)) return true;
                int dot = h.IndexOf('.');
                if(dot < 0 || dot == h.Length - 1) return false;
                h = h.Substring(dot + 1);
            }
        }

        static bool PatternMatches(List<string> patterns, string hostPath, string url)
        {
            for(int i = 0; i < patterns.Count; i++)
            {
                string p = patterns[i];
                if(p.IndexOf('*') < 0)
                {
                    if(url.IndexOf(p, StringComparison.Ordinal) >= 0) return true;
                }
                else if(WildcardContains(url, p))
                {
                    return true;
                }
            }
            return false;
        }

        // "ads*banner" matches when the pieces appear in order.
        static bool WildcardContains(string text, string pattern)
        {
            int pos = 0;
            foreach(string piece in pattern.Split('*'))
            {
                if(piece.Length == 0) continue;
                int idx = text.IndexOf(piece, pos, StringComparison.Ordinal);
                if(idx < 0) return false;
                pos = idx + piece.Length;
            }
            return true;
        }

        public static string HostOf(string url)
        {
            string s = url;
            int scheme = s.IndexOf("://", StringComparison.Ordinal);
            if(scheme >= 0) s = s.Substring(scheme + 3);
            else if(s.StartsWith("//", StringComparison.Ordinal)) s = s.Substring(2);
            else return "";
            int end = s.IndexOfAny(new[] { '/', '?', '#' });
            if(end >= 0) s = s.Substring(0, end);
            int at = s.LastIndexOf('@');
            if(at >= 0) s = s.Substring(at + 1);
            int colon = s.LastIndexOf(':');
            if(colon >= 0 && s.IndexOf(']') < colon) s = s.Substring(0, colon);
            return s.Trim('.');
        }

        static string HostAndPath(string url)
        {
            int scheme = url.IndexOf("://", StringComparison.Ordinal);
            if(scheme >= 0) return url.Substring(scheme + 3);
            if(url.StartsWith("//", StringComparison.Ordinal)) return url.Substring(2);
            return url;
        }
    }
}
