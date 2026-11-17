using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace AtlasLite
{
    internal sealed class EdgePanel : Panel
    {
        // Lets the form underneath handle resize at the outer edges (HTTRANSPARENT).
        public bool BottomEdge;

        public EdgePanel(bool bottomEdge)
        {
            BottomEdge = bottomEdge;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint, true);
        }

        protected override void WndProc(ref Message m)
        {
            if(m.Msg == NativeMethods.WM_NCHITTEST)
            {
                var pt = PointToClient(new Point(NativeMethods.GetX(m.LParam), NativeMethods.GetY(m.LParam)));
                int b = Ui.S(5);
                var f = FindForm();
                bool edge = pt.X < b || pt.X >= Width - b || (BottomEdge && pt.Y >= Height - b);
                if(edge && f != null && f.WindowState == FormWindowState.Normal)
                {
                    m.Result = (IntPtr) (-1); // HTTRANSPARENT
                    return;
                }
            }
            base.WndProc(ref m);
        }
    }

    internal sealed class MenuColors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground { get { return Theme.P.Bar; } }
        public override Color MenuBorder { get { return Theme.P.Border; } }
        public override Color MenuItemBorder { get { return Theme.P.Hover; } }
        public override Color MenuItemSelected { get { return Theme.P.Hover; } }
        public override Color ImageMarginGradientBegin { get { return Theme.P.Bar; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.P.Bar; } }
        public override Color ImageMarginGradientEnd { get { return Theme.P.Bar; } }
        public override Color SeparatorDark { get { return Theme.P.Border; } }
        public override Color SeparatorLight { get { return Theme.P.Border; } }
    }

    internal sealed class MainForm : Form
    {
        readonly AppSettings settings;
        readonly AdBlocker blocker = new AdBlocker();
        readonly string startUrl;

        readonly TabStrip strip = new TabStrip();
        readonly EdgePanel toolbar = new EdgePanel(false);
        readonly EdgePanel content = new EdgePanel(true);
        readonly IconButton btnBack = new IconButton(Glyph.Back);
        readonly IconButton btnForward = new IconButton(Glyph.Forward);
        readonly IconButton btnReload = new IconButton(Glyph.Reload);
        readonly IconButton btnShield = new IconButton(Glyph.Shield);
        readonly IconButton btnMenu = new IconButton(Glyph.Menu);
        readonly AddressBar address = new AddressBar();
        readonly ToolTip tips = new ToolTip();
        readonly Timer housekeeping = new Timer();
        readonly List<BrowserTab> tabs = new List<BrowserTab>();

        CoreWebView2Environment env;
        bool roundedByDwm;
        bool fullscreen;
        Rectangle restoreBounds;
        FormWindowState restoreState;
        DateTime lastSave = DateTime.UtcNow;

        public MainForm(string startUrl)
        {
            this.startUrl = startUrl;
            settings = AppSettings.Load();
            Theme.Dark = settings.Dark;
            Theme.SetAccent(settings.Accent);

            Text = "AtlasLite";
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            DoubleBuffered = true;
            KeyPreview = true;
            Size = new Size(Ui.S(1120), Ui.S(720));
            MinimumSize = new Size(Ui.S(520), Ui.S(360));
            Font = new Font("Segoe UI", 9f);

            toolbar.Dock = DockStyle.Top;
            toolbar.Height = Ui.S(46);
            content.Dock = DockStyle.Fill;

            toolbar.Controls.Add(btnBack);
            toolbar.Controls.Add(btnForward);
            toolbar.Controls.Add(btnReload);
            toolbar.Controls.Add(address);
            toolbar.Controls.Add(btnShield);
            toolbar.Controls.Add(btnMenu);
            toolbar.Layout += delegate { LayoutToolbar(); };

            // add in reverse dock order: Fill first, then toolbar, then strip (top-most)
            Controls.Add(content);
            Controls.Add(toolbar);
            Controls.Add(strip);

            tips.SetToolTip(btnBack, "Back (Alt+Left)");
            tips.SetToolTip(btnForward, "Forward (Alt+Right)");
            tips.SetToolTip(btnReload, "Reload (F5)");
            tips.SetToolTip(btnShield, "Ad blocker - click to turn on/off");
            tips.SetToolTip(btnMenu, "Menu");

            btnBack.Click += delegate { var t = Current; if(t != null) t.GoBack(); };
            btnForward.Click += delegate { var t = Current; if(t != null) t.GoForward(); };
            btnReload.Click += delegate
            {
                var t = Current;
                if(t == null) return;
                if(t.Loading) t.Stop(); else t.Reload();
            };
            btnShield.Click += delegate { SetSetting("adblock", settings.AdBlock ? "0" : "1"); };
            btnMenu.Click += delegate { ShowMenu(); };

            address.Submitted += delegate(string text)
            {
                string url = Nav.Normalize(text, settings.SearchUrl);
                var t = Current;
                if(url != null && t != null)
                {
                    t.Navigate(url);
                    content.Focus();
                    t.Activate();
                }
            };
            address.Cancelled += delegate
            {
                var t = Current;
                if(t != null) { address.Box.Text = Nav.Display(t.Url); t.Activate(); }
            };

            strip.TabSelected += delegate(int i) { SelectTab(i); };
            strip.TabClosed += delegate(int i) { CloseTab(i); };
            strip.NewTabClicked += delegate { AddTab(Nav.NewTabUrl); };
            strip.MinimizeClicked += delegate { WindowState = FormWindowState.Minimized; };
            strip.MaximizeClicked += delegate { ToggleMaximize(); };
            strip.CloseClicked += delegate { Close(); };

            content.Resize += delegate { LayoutContent(); };

            housekeeping.Interval = 30000;
            housekeeping.Tick += delegate { Housekeep(); };
            housekeeping.Start();

            LoadBlocklists();
            ApplyChromeTheme();
        }

        BrowserTab Current
        {
            get { return strip.Selected >= 0 && strip.Selected < tabs.Count ? tabs[strip.Selected] : null; }
        }

        // ---------- window frame ----------

        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                // thick frame (snap, taskbar minimise) -- the frame itself is removed in WM_NCCALCSIZE
                cp.Style |= NativeMethods.WS_THICKFRAME | NativeMethods.WS_MINIMIZEBOX | NativeMethods.WS_SYSMENU | 0x00010000;
                return cp;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            int pref = NativeMethods.DWMWCP_ROUND;
            try
            {
                // Windows 11 rounds windows itself. Windows 10 returns an error, so we cut the corners ourselves.
                roundedByDwm = NativeMethods.DwmSetWindowAttribute(Handle, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4) == 0;
            }
            catch { roundedByDwm = false; }
            UpdateRegion();
        }

        void UpdateRegion()
        {
            if(!IsHandleCreated) return;
            if(roundedByDwm) return;
            if(WindowState != FormWindowState.Normal || fullscreen)
            {
                NativeMethods.SetWindowRgn(Handle, IntPtr.Zero, true);
                return;
            }
            int r = Ui.S(16);
            IntPtr rgn = NativeMethods.CreateRoundRectRgn(0, 0, Width + 1, Height + 1, r * 2, r * 2);
            NativeMethods.SetWindowRgn(Handle, rgn, true); // the system owns the region now
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            UpdateRegion();
            Invalidate();
            if(WindowState == FormWindowState.Minimized)
            {
                GC.Collect();
                NativeMethods.SetProcessWorkingSetSize(Process.GetCurrentProcess().Handle, (IntPtr) (-1), (IntPtr) (-1));
            }
        }

        protected override void WndProc(ref Message m)
        {
            switch(m.Msg)
            {
                case 0x0083: // WM_NCCALCSIZE: no non-client area
                    if(m.WParam != IntPtr.Zero)
                    {
                        m.Result = IntPtr.Zero;
                        return;
                    }
                    break;

                case NativeMethods.WM_GETMINMAXINFO:
                    {
                        var scr = Screen.FromHandle(m.HWnd);
                        var wa = scr.WorkingArea;
                        var sb = scr.Bounds;
                        var mmi = (NativeMethods.MINMAXINFO) Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.MINMAXINFO));
                        mmi.ptMaxPosition.X = wa.Left - sb.Left;
                        mmi.ptMaxPosition.Y = wa.Top - sb.Top;
                        mmi.ptMaxSize.X = wa.Width;
                        mmi.ptMaxSize.Y = wa.Height;
                        mmi.ptMaxTrackSize.X = Math.Max(sb.Width, wa.Width);
                        mmi.ptMaxTrackSize.Y = Math.Max(sb.Height, wa.Height);
                        mmi.ptMinTrackSize.X = MinimumSize.Width;
                        mmi.ptMinTrackSize.Y = MinimumSize.Height;
                        Marshal.StructureToPtr(mmi, m.LParam, true);
                        m.Result = IntPtr.Zero;
                        return;
                    }

                case NativeMethods.WM_NCHITTEST:
                    if(WindowState == FormWindowState.Normal && !fullscreen)
                    {
                        var pt = PointToClient(new Point(NativeMethods.GetX(m.LParam), NativeMethods.GetY(m.LParam)));
                        int b = Ui.S(6);
                        bool l = pt.X < b, r = pt.X >= ClientSize.Width - b, t = pt.Y < b, bt = pt.Y >= ClientSize.Height - b;
                        int hit = 0;
                        if(t && l) hit = NativeMethods.HTTOPLEFT;
                        else if(t && r) hit = NativeMethods.HTTOPRIGHT;
                        else if(bt && l) hit = NativeMethods.HTBOTTOMLEFT;
                        else if(bt && r) hit = NativeMethods.HTBOTTOMRIGHT;
                        else if(l) hit = NativeMethods.HTLEFT;
                        else if(r) hit = NativeMethods.HTRIGHT;
                        else if(t) hit = NativeMethods.HTTOP;
                        else if(bt) hit = NativeMethods.HTBOTTOM;
                        if(hit != 0)
                        {
                            m.Result = (IntPtr) hit;
                            return;
                        }
                    }
                    break;
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if(WindowState != FormWindowState.Normal || fullscreen) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using(var path = Ui.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(16)))
            using(var pen = new Pen(Theme.P.Border, 1f))
            {
                g.DrawPath(pen, path);
            }
        }

        protected override void OnMove(EventArgs e)
        {
            base.OnMove(e);
            foreach(var t in tabs) t.NotifyMoved();
        }

        void ToggleMaximize()
        {
            WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized;
            strip.Invalidate();
        }

        void SetFullscreen(bool on)
        {
            if(on == fullscreen) return;
            fullscreen = on;
            if(on)
            {
                restoreState = WindowState;
                restoreBounds = WindowState == FormWindowState.Maximized ? RestoreBounds : Bounds;
                strip.Visible = false;
                toolbar.Visible = false;
                WindowState = FormWindowState.Normal;
                Bounds = Screen.FromControl(this).Bounds;
            }
            else
            {
                strip.Visible = true;
                toolbar.Visible = true;
                Bounds = restoreBounds;
                WindowState = restoreState;
            }
            UpdateRegion();
            LayoutContent();
            Invalidate();
        }

        // ---------- layout ----------

        void LayoutToolbar()
        {
            int pad = Ui.S(10), gap = Ui.S(4);
            int bw = Ui.S(34);
            int y = (toolbar.Height - bw) / 2;
            int x = pad;
            btnBack.SetBounds(x, y, bw, bw); x += bw + gap;
            btnForward.SetBounds(x, y, bw, bw); x += bw + gap;
            btnReload.SetBounds(x, y, bw, bw); x += bw + Ui.S(8);

            int right = toolbar.Width - pad;
            btnMenu.SetBounds(right - bw, y, bw, bw); right -= bw + gap;
            int sw = Ui.S(80);
            btnShield.SetBounds(right - sw, y, sw, bw); right -= sw + Ui.S(8);

            int aw = Math.Max(Ui.S(80), right - x);
            address.SetBounds(x, (toolbar.Height - Ui.S(34)) / 2, aw, Ui.S(34));
        }

        void LayoutContent()
        {
            int b = fullscreen ? 0 : Ui.S(5);
            var r = new Rectangle(b, 0, Math.Max(1, content.Width - b * 2), Math.Max(1, content.Height - b));
            foreach(var t in tabs) t.SetBounds(r);
        }

        void ApplyChromeTheme()
        {
            var p = Theme.P;
            BackColor = p.Window;
            toolbar.BackColor = p.Bar;
            content.BackColor = p.Window;
            address.ApplyTheme();
            foreach(Control c in toolbar.Controls) c.Invalidate();
            strip.Invalidate();
            Invalidate();
            RefreshToolbarState();
        }

        void RefreshToolbarState()
        {
            var t = Current;
            btnBack.Enabled = t != null && t.CanGoBack;
            btnForward.Enabled = t != null && t.CanGoForward;
            btnReload.Glyph = (t != null && t.Loading) ? Glyph.Stop : Glyph.Reload;
            btnReload.Invalidate();
            btnShield.Active = settings.AdBlock;
            btnShield.Badge = t != null ? t.BlockedCount.ToString() : "0";
            btnShield.Invalidate();
            btnBack.Invalidate();
            btnForward.Invalidate();
        }

        // ---------- start-up ----------

        protected override async void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                string args = "--renderer-process-limit=4 --disk-cache-size=67108864";
                if(settings.RamSaver) args += " --enable-low-end-device-mode --process-per-site";
                if(settings.ForceDarkPages) args += " --enable-features=WebContentsForceDark";
                var opts = new CoreWebView2EnvironmentOptions(additionalBrowserArguments: args);
                Directory.CreateDirectory(AppSettings.WebViewDir);
                env = await CoreWebView2Environment.CreateAsync(null, AppSettings.WebViewDir, opts);
            }
            catch(Exception ex)
            {
                MessageBox.Show(
                    "AtlasLite needs the Microsoft Edge WebView2 Runtime.\n\nInstall it from:\nhttps://go.microsoft.com/fwlink/p/?LinkId=2124703\n\n(" + ex.Message + ")",
                    "AtlasLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Close();
                return;
            }

            string first = Nav.NewTabUrl;
            if(!string.IsNullOrEmpty(startUrl))
            {
                string u = Nav.Normalize(startUrl, settings.SearchUrl);
                if(u != null) first = u;
            }
            AddTab(first);
        }

        void LoadBlocklists()
        {
            blocker.LoadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "blocklist.txt"));
            blocker.LoadFile(UserBlocklistPath);
        }

        static string UserBlocklistPath { get { return Path.Combine(AppSettings.DataDir, "blocklist.txt"); } }

        // ---------- tabs ----------

        async void AddTab(string url)
        {
            if(env == null) return;
            var tab = new BrowserTab(content, env, settings, blocker);
            tab.Changed += delegate { OnTabChanged(tab); };
            tab.NewTabRequested += delegate(string u) { AddTab(u); };
            tab.MessageReceived += OnPageMessage;
            tab.KeyPressed += OnShortcut;
            tab.FullscreenChanged += delegate(bool on) { SetFullscreen(on); };
            tab.SetBounds(new Rectangle(0, 0, 10, 10));

            tabs.Add(tab);
            strip.Tabs.Add(new TabInfo());
            SelectTab(tabs.Count - 1);
            LayoutContent();
            try
            {
                await tab.InitAsync(url);
                if(tab == Current) tab.Activate();
            }
            catch(Exception ex)
            {
                int i = tabs.IndexOf(tab);
                if(i >= 0) CloseTab(i);
                MessageBox.Show("Could not open a tab: " + ex.Message, "AtlasLite");
            }
        }

        void SelectTab(int i)
        {
            if(i < 0 || i >= tabs.Count) return;
            var old = Current;
            if(old != null && old != tabs[i]) old.Deactivate();
            strip.Selected = i;
            var t = tabs[i];
            t.Activate();
            address.Box.Text = Nav.Display(t.Url);
            RefreshToolbarState();
            strip.Invalidate();
        }

        void CloseTab(int i)
        {
            if(i < 0 || i >= tabs.Count) return;
            if(tabs.Count == 1) { Close(); return; }
            var t = tabs[i];
            bool wasCurrent = i == strip.Selected;
            tabs.RemoveAt(i);
            strip.Tabs.RemoveAt(i);
            t.Close();
            int next = strip.Selected;
            if(wasCurrent) next = Math.Min(i, tabs.Count - 1);
            else if(i < strip.Selected) next = strip.Selected - 1;
            strip.Selected = -1;
            SelectTab(next);
        }

        void OnTabChanged(BrowserTab tab)
        {
            int i = tabs.IndexOf(tab);
            if(i < 0) return;
            var info = strip.Tabs[i];
            info.Title = tab.IsOurPage && tab.Url.IndexOf("settings", StringComparison.OrdinalIgnoreCase) >= 0 && tab.Title == "New tab" ? "Settings" : tab.Title;
            info.Loading = tab.Loading;
            strip.Invalidate();
            if(tab == Current)
            {
                if(!address.Box.Focused) address.Box.Text = Nav.Display(tab.Url);
                RefreshToolbarState();
            }
        }

        // ---------- shortcuts ----------

        bool OnShortcut(Keys k)
        {
            switch(k)
            {
                case Keys.Control | Keys.T: AddTab(Nav.NewTabUrl); return true;
                case Keys.Control | Keys.W:
                    BeginInvoke((Action) delegate { CloseTab(strip.Selected); }); // never close a tab inside its own key event
                    return true;
                case Keys.Control | Keys.L:
                case Keys.Alt | Keys.D:
                case Keys.F6:
                    BeginInvoke((Action) delegate { address.Box.Focus(); });
                    return true;
                case Keys.F5:
                case Keys.Control | Keys.R:
                    { var t = Current; if(t != null) t.Reload(); return true; }
                case Keys.Alt | Keys.Left:
                    { var t = Current; if(t != null) t.GoBack(); return true; }
                case Keys.Alt | Keys.Right:
                    { var t = Current; if(t != null) t.GoForward(); return true; }
                case Keys.Control | Keys.Tab:
                    if(tabs.Count > 1) SelectTab((strip.Selected + 1) % tabs.Count);
                    return true;
                case Keys.Control | Keys.Shift | Keys.Tab:
                    if(tabs.Count > 1) SelectTab((strip.Selected + tabs.Count - 1) % tabs.Count);
                    return true;
                case Keys.Control | Keys.Oemcomma:
                    AddTab(Nav.SettingsUrl);
                    return true;
                case Keys.F11:
                    SetFullscreen(!fullscreen);
                    return true;
                case Keys.Escape:
                    {
                        var cur = Current;
                        if(fullscreen && cur != null && !cur.InElementFullscreen) { SetFullscreen(false); return true; }
                    }
                    return false;
            }
            return false;
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // Same shortcuts when focus is in the address bar or toolbar.
            if(keyData == (Keys.Control | Keys.T) || keyData == (Keys.Control | Keys.W) || keyData == (Keys.Control | Keys.Tab)
               || keyData == (Keys.Control | Keys.Shift | Keys.Tab) || keyData == Keys.F5 || keyData == Keys.F11
               || keyData == (Keys.Control | Keys.Oemcomma))
            {
                if(OnShortcut(keyData)) return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        // ---------- menu ----------

        void ShowMenu()
        {
            var menu = new ContextMenuStrip();
            menu.Renderer = new ToolStripProfessionalRenderer(new MenuColors());
            menu.BackColor = Theme.P.Bar;
            menu.ForeColor = Theme.P.Text;
            menu.ShowImageMargin = false;
            menu.Font = new Font("Segoe UI", 9.5f);

            Action<string, Action> add = delegate(string text, Action act)
            {
                var item = new ToolStripMenuItem(text);
                item.ForeColor = Theme.P.Text;
                item.BackColor = Theme.P.Bar;
                item.Click += delegate { act(); };
                menu.Items.Add(item);
            };

            add("New tab", delegate { AddTab(Nav.NewTabUrl); });
            add("Settings && wallpaper", delegate { AddTab(Nav.SettingsUrl); });
            menu.Items.Add(new ToolStripSeparator());
            add(settings.Dark ? "Switch to light mode" : "Switch to dark mode", delegate { SetSetting("dark", settings.Dark ? "0" : "1"); });
            add(settings.AdBlock ? "Turn ad blocker off" : "Turn ad blocker on", delegate { SetSetting("adblock", settings.AdBlock ? "0" : "1"); });
            add("Clear browsing data", delegate { ClearData(); });
            menu.Items.Add(new ToolStripSeparator());
            add("Full screen", delegate { SetFullscreen(!fullscreen); });
            add("Exit", delegate { Close(); });

            menu.Show(btnMenu, new Point(btnMenu.Width - menu.GetPreferredSize(Size.Empty).Width, btnMenu.Height + Ui.S(4)));
        }

        // ---------- settings protocol with our pages ----------

        void SetSetting(string key, string value)
        {
            if(!settings.Apply(key, value)) return;
            settings.Save();
            ApplyAllSettings();
        }

        void ApplyAllSettings()
        {
            Theme.Dark = settings.Dark;
            Theme.SetAccent(settings.Accent);
            ApplyChromeTheme();
            foreach(var t in tabs)
            {
                t.ApplySettings();
                t.PushSettingsIfOurPage();
            }
        }

        void OnPageMessage(BrowserTab tab, string msg)
        {
            string[] a = msg.Split(new[] { '|' }, 3);
            switch(a[0])
            {
                case "get":
                    tab.PostText(settings.ToJson());
                    break;
                case "set":
                    if(a.Length == 3) SetSetting(a[1], a[2]);
                    break;
                case "go":
                    if(msg.Length > 3)
                    {
                        string url = Nav.Normalize(msg.Substring(3), settings.SearchUrl);
                        if(url != null) tab.Navigate(url);
                    }
                    break;
                case "pickwall":
                    BeginInvoke((Action) PickWallpaper);
                    break;
                case "clearwall":
                    ClearWallpaper();
                    break;
                case "addshortcut":
                    if(a.Length == 3 && settings.Shortcuts.Count < 24)
                    {
                        string url = Nav.CleanShortcutUrl(a[2]);
                        string name = (a[1] ?? "").Trim();
                        if(url != null)
                        {
                            if(name.Length == 0) name = new Uri(url).Host;
                            if(name.Length > 30) name = name.Substring(0, 30);
                            settings.Shortcuts.Add(new Shortcut { Name = name, Url = url });
                            settings.Save();
                            PushAll();
                        }
                    }
                    break;
                case "delshortcut":
                    {
                        int idx;
                        if(a.Length >= 2 && int.TryParse(a[1], out idx) && idx >= 0 && idx < settings.Shortcuts.Count)
                        {
                            settings.Shortcuts.RemoveAt(idx);
                            settings.Save();
                            PushAll();
                        }
                    }
                    break;
                case "clear":
                    ClearData();
                    break;
                case "resetblocked":
                    settings.BlockedTotal = 0;
                    settings.Save();
                    PushAll();
                    break;
                case "editblocklist":
                    EditBlocklist();
                    break;
                case "reloadblocklist":
                    ReloadBlocklist();
                    break;
            }
        }

        void PushAll()
        {
            foreach(var t in tabs) t.PushSettingsIfOurPage();
        }

        void ClearData()
        {
            foreach(var t in tabs) { t.ClearBrowsingData(); break; } // one profile is shared by all tabs
            MessageBox.Show(this, "Browsing data (history, cookies, cache) was cleared.", "AtlasLite", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void PickWallpaper()
        {
            using(var dlg = new OpenFileDialog())
            {
                dlg.Title = "Choose a wallpaper";
                dlg.Filter = "Images|*.jpg;*.jpeg;*.png;*.webp;*.bmp;*.gif";
                if(dlg.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var info = new FileInfo(dlg.FileName);
                    if(info.Length > 15 * 1024 * 1024)
                    {
                        MessageBox.Show(this, "That image is over 15 MB. Pick a smaller one to keep memory low.", "AtlasLite");
                        return;
                    }
                    Directory.CreateDirectory(AppSettings.WallpaperDir);
                    foreach(var old in Directory.GetFiles(AppSettings.WallpaperDir, "wallpaper.*")) File.Delete(old);
                    string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                    File.Copy(dlg.FileName, Path.Combine(AppSettings.WallpaperDir, "wallpaper" + ext), true);
                    settings.Wallpaper = "custom";
                    settings.Save();
                    PushAll();
                }
                catch(Exception ex)
                {
                    MessageBox.Show(this, "Could not use that image: " + ex.Message, "AtlasLite");
                }
            }
        }

        void ClearWallpaper()
        {
            try
            {
                if(Directory.Exists(AppSettings.WallpaperDir))
                {
                    foreach(var f in Directory.GetFiles(AppSettings.WallpaperDir, "wallpaper.*")) File.Delete(f);
                }
            }
            catch { }
            if(settings.Wallpaper == "custom") settings.Wallpaper = "aurora";
            settings.Save();
            PushAll();
        }

        void EditBlocklist()
        {
            try
            {
                Directory.CreateDirectory(AppSettings.DataDir);
                if(!File.Exists(UserBlocklistPath))
                {
                    File.WriteAllText(UserBlocklistPath,
                        "# Your own ad-block rules, one per line.\r\n# Examples:  ads.example.com   ||tracker.example.net^   /banner/ads/   @@allowed.example.com\r\n");
                }
                Process.Start(new ProcessStartInfo("notepad.exe", "\"" + UserBlocklistPath + "\"") { UseShellExecute = true });
            }
            catch(Exception ex)
            {
                MessageBox.Show(this, ex.Message, "AtlasLite");
            }
        }

        void ReloadBlocklist()
        {
            // The blocker object is shared by all tabs, so we rebuild it in place by loading into a fresh one.
            var fresh = new AdBlocker();
            fresh.LoadFile(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "blocklist.txt"));
            fresh.LoadFile(UserBlocklistPath);
            blocker.ReplaceWith(fresh);
            PushAll();
        }

        // ---------- upkeep ----------

        void Housekeep()
        {
            if((DateTime.UtcNow - lastSave).TotalMinutes >= 2)
            {
                lastSave = DateTime.UtcNow;
                settings.Save(); // keeps the blocked counter
            }
            if(!settings.RamSaver) return;
            var now = DateTime.UtcNow;
            foreach(var t in tabs)
            {
                if(!t.Visible && !t.Suspended && (now - t.LastActive).TotalSeconds > 90) t.SuspendIfIdle();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            housekeeping.Stop();
            settings.Save();
            foreach(var t in tabs) t.Close();
        }
    }
}
