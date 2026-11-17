using System;
using AtlasLite;

// Plain console test (no test framework needed): exit code 1 on failure.
internal static class Program
{
    static int failures;

    static void Check(bool ok, string what)
    {
        if(!ok) { Console.WriteLine("FAIL: " + what); failures++; }
    }

    static int Main()
    {
        var b = new AdBlocker();
        b.AddLines(new[]
        {
            "# comment", "! another comment", "",
            "ads.example.com",
            "0.0.0.0 tracker.example.net",
            "127.0.0.1 localhost",
            "||doubleclick.net^",
            "||cdn.site.com/banners/",
            "/adframe/",
            "ad*banner",
            "@@ads.example.com/allowed",
            "@@good.doubleclick.net",
            "||thirdparty.com^$third-party",
        });

        Check(b.ShouldBlock("https://ads.example.com/x.js"), "plain domain");
        Check(b.ShouldBlock("https://sub.ads.example.com/x.js"), "subdomain of blocked domain");
        Check(!b.ShouldBlock("https://notads.example.com/x.js"), "similar-looking domain must not match");
        Check(b.ShouldBlock("https://tracker.example.net/p.gif"), "hosts-file line");
        Check(!b.ShouldBlock("http://localhost:8080/"), "localhost hosts line ignored");
        Check(b.ShouldBlock("https://stats.doubleclick.net/c?x=1"), "||domain^ matches subdomain");
        Check(!b.ShouldBlock("https://good.doubleclick.net/c"), "allow rule beats block rule");
        Check(b.ShouldBlock("https://cdn.site.com/banners/a.png"), "domain+path rule");
        Check(!b.ShouldBlock("https://cdn.site.com/images/a.png"), "domain+path rule: other path ok");
        Check(b.ShouldBlock("https://site.org/adframe/1.html"), "substring rule");
        Check(b.ShouldBlock("https://site.org/ad-x-banner.png"), "wildcard rule");
        Check(!b.ShouldBlock("https://site.org/header.png"), "normal URL not blocked");
        Check(b.ShouldBlock("https://thirdparty.com/a.js"), "option after $ ignored");
        Check(!b.ShouldBlock(""), "empty URL");
        Check(!b.ShouldBlock("not a url"), "garbage URL");
        Check(b.ShouldBlock("//cdn.ads.example.com/a.js"), "protocol-relative URL");
        Check(b.ShouldBlock("https://USER:pw@ADS.EXAMPLE.COM:8443/x"), "userinfo, port and case");
        Check(AdBlocker.HostOf("https://a.b.com:81/p?q#f") == "a.b.com", "HostOf");

        var c = new AdBlocker();
        c.AddRule("only.test");
        b.ReplaceWith(c);
        Check(b.ShouldBlock("https://only.test/"), "ReplaceWith keeps new rule");
        Check(!b.ShouldBlock("https://ads.example.com/"), "ReplaceWith drops old rules");

        Console.WriteLine(failures == 0 ? "adblock tests passed" : "FAILED (" + failures + ")");
        return failures == 0 ? 0 : 1;
    }
}
