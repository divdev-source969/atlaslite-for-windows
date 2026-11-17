using System;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace AtlasLite
{
    /// <summary>One browser tab = one WebView2 controller hosted directly inside the content panel.</summary>
    internal sealed class BrowserTab
    {
        // Hides common ad containers. Deliberately conservative so normal pages are not broken.
        const string CosmeticJs =
            "(function(){var css='ins.adsbygoogle,[id^=\"google_ads_\"],[id^=\"div-gpt-ad\"],[class~=\"adsbygoogle\"],"
            + "iframe[src*=\"doubleclick.net\"],iframe[src*=\"googlesyndication.com\"],iframe[id^=\"google_ads_iframe\"],"
            + "div[id^=\"taboola-\"],div[id^=\"outbrain\"],.ad-banner,.ad-container,.advert,.advertisement,.sponsored-ad"
            + "{display:none!important}';"
            + "function add(){if(!document.documentElement)return false;var s=document.createElement('style');"
            + "s.textContent=css;document.documentElement.appendChild(s);return true;}"
            + "if(!add()){var o=new MutationObserver(function(){if(add())o.disconnect();});o.observe(document,{childList:true});}})();";

        readonly Control host;
        readonly CoreWebView2Environment env;
        readonly AppSettings settings;
        readonly AdBlocker blocker;

        CoreWebView2Controller controller;
        CoreWebView2 core;
        string pendingUrl;
        string cosmeticId;
        bool syncing;
        bool closed;
        Rectangle bounds;

        public string Title = "New tab";
        public string Url = "";
        public bool Loading;
        public int BlockedCount;
        public bool Visible;
        public bool Suspended;
        public DateTime LastActive = DateTime.UtcNow;

        public event Action Changed;                       // title / url / loading / counters changed
        public event Action<string> NewTabRequested;       // a page wants a new window
        public event Action<BrowserTab, string> MessageReceived; // string message from our own pages
        public event Func<Keys, bool> KeyPressed;          // return true if handled
        public event Action<bool> FullscreenChanged;

        public BrowserTab(Control host, CoreWebView2Environment env, AppSettings settings, AdBlocker blocker)
        {
            this.host = host;
            this.env = env;
            this.settings = settings;
            this.blocker = blocker;
        }

        public bool Ready { get { return core != null; } }
        public bool CanGoBack { get { return core != null && core.CanGoBack; } }
        public bool CanGoForward { get { return core != null && core.CanGoForward; } }

        public async Task InitAsync(string initialUrl)
        {
            pendingUrl = initialUrl;
            controller = await env.CreateCoreWebView2ControllerAsync(host.Handle);
            if(closed)
            {
                try { controller.Close(); } catch { }
                return;
            }
            core = controller.CoreWebView2;
            controller.Bounds = bounds.IsEmpty ? host.ClientRectangle : bounds;
            controller.IsVisible = Visible;
            try { controller.DefaultBackgroundColor = Color.FromArgb(255, Theme.P.Window); } catch { }

            core.Settings.IsStatusBarEnabled = true;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.IsZoomControlEnabled = true;

            // Our own pages (new tab, settings) and the user's wallpaper are served from local folders.
            string pages = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Pages");
            core.SetVirtualHostNameToFolderMapping(PageHost.Host, pages, CoreWebView2HostResourceAccessKind.DenyCors);
            try { Directory.CreateDirectory(AppSettings.WallpaperDir); } catch { }
            core.SetVirtualHostNameToFolderMapping(PageHost.DataHost, AppSettings.WallpaperDir, CoreWebView2HostResourceAccessKind.DenyCors);

            core.SourceChanged += delegate(object s, CoreWebView2SourceChangedEventArgs a) { var c = s as CoreWebView2; if(c == null) return; Url = c.Source ?? ""; Raise(); };
            core.DocumentTitleChanged += delegate(object s, object a)
            {
                var c = s as CoreWebView2;
                if(c == null) return;
                string t = c.DocumentTitle;
                Title = string.IsNullOrWhiteSpace(t) ? "New tab" : t;
                Raise();
            };
            core.NavigationStarting += delegate { Loading = true; BlockedCount = 0; Raise(); };
            core.NavigationCompleted += delegate { Loading = false; Raise(); };
            core.NewWindowRequested += OnNewWindow;
            core.WebResourceRequested += OnResource;
            core.WebMessageReceived += OnMessage;
            core.ProcessFailed += OnProcessFailed;
            core.ContainsFullScreenElementChanged += delegate(object s, object a)
            {
                var c = s as CoreWebView2;
                var h = FullscreenChanged;
                if(c != null && h != null) h(c.ContainsFullScreenElement);
            };
            controller.AcceleratorKeyPressed += OnAccelerator;

            core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            ApplySettings();

            if(!string.IsNullOrEmpty(pendingUrl))
            {
                string u = pendingUrl;
                pendingUrl = null;
                core.Navigate(u);
            }
            if(Visible) controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic);
        }

        void Raise()
        {
            var h = Changed;
            if(h != null) h();
        }

        void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true;
            var h = NewTabRequested;
            if(h != null && !string.IsNullOrEmpty(e.Uri)) h(e.Uri);
        }

        void OnResource(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            if(!settings.AdBlock) return;
            if(e.ResourceContext == CoreWebView2WebResourceContext.Document) return; // never block the page itself
            string uri = e.Request.Uri;
            if(uri.StartsWith("https://atlas", StringComparison.Ordinal)) return;
            if(blocker.ShouldBlock(uri))
            {
                e.Response = env.CreateWebResourceResponse(null, 403, "Blocked by AtlasLite", "Content-Type: text/plain");
                BlockedCount++;
                settings.BlockedTotal++;
                Raise();
            }
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            // Only our own bundled pages may talk to the browser.
            Uri src;
            if(!Uri.TryCreate(e.Source, UriKind.Absolute, out src)) return;
            if(src.Scheme != "https" || !string.Equals(src.Host, PageHost.Host, StringComparison.OrdinalIgnoreCase)) return;
            string msg;
            try { msg = e.TryGetWebMessageAsString(); }
            catch { return; }
            if(msg == null) return;
            var h = MessageReceived;
            if(h != null) h(this, msg);
        }

        void OnProcessFailed(object sender, CoreWebView2ProcessFailedEventArgs e)
        {
            // Page process crashed: reload it instead of leaving a dead tab.
            if(e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessExited
               || e.ProcessFailedKind == CoreWebView2ProcessFailedKind.RenderProcessUnresponsive)
            {
                try { core.Reload(); } catch { }
            }
        }

        void OnAccelerator(object sender, CoreWebView2AcceleratorKeyPressedEventArgs e)
        {
            if(e.KeyEventKind != CoreWebView2KeyEventKind.KeyDown && e.KeyEventKind != CoreWebView2KeyEventKind.SystemKeyDown) return;
            var h = KeyPressed;
            if(h == null) return;
            Keys keys = (Keys) e.VirtualKey | Control.ModifierKeys;
            if(h(keys)) e.Handled = true;
        }

        // ---------- settings ----------

        public void ApplySettings()
        {
            if(core == null) return;
            try
            {
                core.Profile.PreferredColorScheme = settings.Dark ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Light;
            }
            catch { }
            try { controller.DefaultBackgroundColor = Color.FromArgb(255, Theme.P.Window); } catch { }
            SyncCosmetic();
        }

        async void SyncCosmetic()
        {
            if(syncing || core == null) return;
            syncing = true;
            try
            {
                while(!closed)
                {
                    bool want = settings.AdBlock;
                    if(want == (cosmeticId != null)) break;
                    if(want) cosmeticId = await core.AddScriptToExecuteOnDocumentCreatedAsync(CosmeticJs);
                    else
                    {
                        core.RemoveScriptToExecuteOnDocumentCreated(cosmeticId);
                        cosmeticId = null;
                    }
                }
            }
            catch { }
            finally { syncing = false; }
        }

        public bool InElementFullscreen { get { return core != null && core.ContainsFullScreenElement; } }

        public void NotifyMoved()
        {
            try { if(controller != null) controller.NotifyParentWindowPositionChanged(); } catch { }
        }

        public bool IsOurPage
        {
            get
            {
                Uri u;
                return Uri.TryCreate(Url, UriKind.Absolute, out u) && string.Equals(u.Host, PageHost.Host, StringComparison.OrdinalIgnoreCase);
            }
        }

        /// <summary>Sends the current settings (JSON text) to one of our pages.</summary>
        public void PostText(string text)
        {
            if(core == null) return;
            try { core.PostWebMessageAsString(text); } catch { }
        }

        public void PushSettingsIfOurPage()
        {
            if(IsOurPage) PostText(settings.ToJson());
        }

        // ---------- navigation ----------

        public void Navigate(string url)
        {
            if(core == null) { pendingUrl = url; return; }
            try { core.Navigate(url); } catch { }
        }

        public void GoBack() { if(core != null && core.CanGoBack) core.GoBack(); }
        public void GoForward() { if(core != null && core.CanGoForward) core.GoForward(); }
        public void Reload() { if(core != null) core.Reload(); }
        public void Stop() { if(core != null) core.Stop(); }

        public async void ClearBrowsingData()
        {
            if(core == null) return;
            try { await core.Profile.ClearBrowsingDataAsync(); } catch { }
        }

        // ---------- visibility / memory ----------

        public void SetBounds(Rectangle r)
        {
            bounds = r;
            if(controller != null) controller.Bounds = r;
        }

        public void Activate()
        {
            Visible = true;
            LastActive = DateTime.UtcNow;
            if(controller == null) return;
            try
            {
                controller.IsVisible = true;
                if(Suspended)
                {
                    core.Resume();
                    Suspended = false;
                }
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Normal;
            }
            catch { }
            ApplySettings();
            PushSettingsIfOurPage();
            try { controller.MoveFocus(CoreWebView2MoveFocusReason.Programmatic); } catch { }
        }

        public void Deactivate()
        {
            Visible = false;
            LastActive = DateTime.UtcNow;
            if(controller == null) return;
            try
            {
                controller.IsVisible = false;
                core.MemoryUsageTargetLevel = CoreWebView2MemoryUsageTargetLevel.Low;
            }
            catch { }
        }

        /// <summary>Freezes a background tab so it uses almost no CPU/RAM. It wakes up when selected.</summary>
        public async void SuspendIfIdle()
        {
            if(core == null || Suspended || Visible || closed) return;
            try
            {
                bool ok = await core.TrySuspendAsync();
                if(ok && !Visible) Suspended = true;
                else if(ok && Visible) { core.Resume(); }
            }
            catch { }
        }

        public void Close()
        {
            closed = true;
            try
            {
                if(controller != null) controller.Close();
            }
            catch { }
            controller = null;
            core = null;
        }
    }
}
