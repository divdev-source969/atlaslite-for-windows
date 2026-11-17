using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace AtlasLite
{
    internal sealed class TabInfo
    {
        public string Title = "New tab";
        public bool Loading;
        public Rectangle Bounds;
        public Rectangle CloseBounds;
    }

    /// <summary>Custom tab strip that also acts as the window's title bar (drag, minimise, maximise, close).</summary>
    internal sealed class TabStrip : Control
    {
        public readonly List<TabInfo> Tabs = new List<TabInfo>();
        public int Selected = -1;

        public event Action<int> TabSelected;
        public event Action<int> TabClosed;
        public event Action NewTabClicked;
        public event Action MinimizeClicked;
        public event Action MaximizeClicked;
        public event Action CloseClicked;

        Rectangle newTabRect, minRect, maxRect, closeRect;
        int hoverTab = -1;      // index of hovered tab
        bool hoverTabClose;     // hovering the x on that tab
        int hoverCaption = 0;   // 1 new tab, 2 min, 3 max, 4 close
        readonly Font font;

        public TabStrip()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            font = new Font("Segoe UI", 9f);
            Height = Ui.S(40);
            Dock = DockStyle.Top;
            TabStop = false;
        }

        protected override void Dispose(bool disposing)
        {
            if(disposing) font.Dispose();
            base.Dispose(disposing);
        }

        bool IsMaximized
        {
            get
            {
                var f = FindForm();
                return f != null && f.WindowState == FormWindowState.Maximized;
            }
        }

        void ComputeLayout()
        {
            int btnW = Ui.S(46);
            closeRect = new Rectangle(Width - btnW, 0, btnW, Ui.S(32));
            maxRect = new Rectangle(Width - btnW * 2, 0, btnW, Ui.S(32));
            minRect = new Rectangle(Width - btnW * 3, 0, btnW, Ui.S(32));

            int left = Ui.S(10);
            int plus = Ui.S(30);
            int dragGap = Ui.S(40);
            int avail = minRect.Left - left - plus - dragGap;
            int n = Math.Max(1, Tabs.Count);
            int tabW = avail / n;
            if(tabW > Ui.S(220)) tabW = Ui.S(220);
            if(tabW < Ui.S(48)) tabW = Ui.S(48);

            int top = Ui.S(7);
            int x = left;
            foreach(var t in Tabs)
            {
                t.Bounds = new Rectangle(x, top, tabW - Ui.S(2), Height - top);
                int cs = Ui.S(18);
                t.CloseBounds = new Rectangle(t.Bounds.Right - cs - Ui.S(8), t.Bounds.Top + (Ui.S(26) - cs) / 2 + Ui.S(1), cs, cs);
                x += tabW;
            }
            newTabRect = new Rectangle(x + Ui.S(2), top + Ui.S(4), Ui.S(26), Ui.S(26));
        }

        protected override void WndProc(ref Message m)
        {
            if(m.Msg == NativeMethods.WM_NCHITTEST)
            {
                var pt = PointToClient(new Point(NativeMethods.GetX(m.LParam), NativeMethods.GetY(m.LParam)));
                int b = Ui.S(5);
                var f = FindForm();
                if(f != null && f.WindowState == FormWindowState.Normal && (pt.Y < b || pt.X < b || pt.X >= Width - b)
                    && !minRect.Contains(pt) && !maxRect.Contains(pt) && !closeRect.Contains(pt))
                {
                    m.Result = (IntPtr) (-1); // let the form resize from the window edge
                    return;
                }
            }
            base.WndProc(ref m);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(p.Window);
            ComputeLayout();

            for(int i = 0; i < Tabs.Count; i++)
            {
                var t = Tabs[i];
                bool active = i == Selected;
                Color fill = active ? p.TabActive : (i == hoverTab ? p.TabHover : p.Tab);
                if(active || i == hoverTab)
                {
                    using(var path = Ui.TopRoundRect(t.Bounds, Ui.S(9)))
                    using(var br = new SolidBrush(fill))
                    {
                        g.FillPath(br, path);
                    }
                }

                var textRect = new Rectangle(t.Bounds.Left + Ui.S(12), t.Bounds.Top, t.Bounds.Width - Ui.S(12) - Ui.S(30), Ui.S(28));
                string title = (t.Loading ? "… " : "") + t.Title;
                TextRenderer.DrawText(g, title, font, textRect, active ? p.Text : p.TextDim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);

                if(Tabs.Count > 1 || active)
                {
                    if(i == hoverTab && hoverTabClose)
                    {
                        using(var br = new SolidBrush(p.Hover)) g.FillEllipse(br, t.CloseBounds);
                    }
                    DrawCross(g, t.CloseBounds, active ? p.Text : p.TextDim, 4.5f);
                }
            }

            // new-tab button
            if(hoverCaption == 1)
            {
                using(var br = new SolidBrush(p.Hover)) g.FillEllipse(br, newTabRect);
            }
            using(var pen = new Pen(p.TextDim, Math.Max(1.4f, Ui.Scale * 1.4f)))
            {
                int cx = newTabRect.Left + newTabRect.Width / 2, cy = newTabRect.Top + newTabRect.Height / 2, r = Ui.S(5);
                g.DrawLine(pen, cx - r, cy, cx + r, cy);
                g.DrawLine(pen, cx, cy - r, cx, cy + r);
            }

            // caption buttons
            DrawCaption(g, minRect, 2, p);
            DrawCaption(g, maxRect, 3, p);
            DrawCaption(g, closeRect, 4, p);
        }

        void DrawCaption(Graphics g, Rectangle r, int id, Palette p)
        {
            if(hoverCaption == id)
            {
                Color c = id == 4 ? Color.FromArgb(232, 17, 35) : p.Hover;
                using(var br = new SolidBrush(c)) g.FillRectangle(br, r);
            }
            Color ink = (id == 4 && hoverCaption == 4) ? Color.White : p.Text;
            using(var pen = new Pen(ink, Math.Max(1f, Ui.Scale)))
            {
                int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2, s = Ui.S(5);
                g.SmoothingMode = SmoothingMode.None;
                if(id == 2)
                {
                    g.DrawLine(pen, cx - s, cy, cx + s, cy);
                }
                else if(id == 3)
                {
                    if(IsMaximized)
                    {
                        g.DrawRectangle(pen, cx - s, cy - s + Ui.S(2), s * 2 - Ui.S(2), s * 2 - Ui.S(2));
                        g.DrawLine(pen, cx - s + Ui.S(2), cy - s + Ui.S(2), cx - s + Ui.S(2), cy - s);
                        g.DrawLine(pen, cx - s + Ui.S(2), cy - s, cx + s, cy - s);
                        g.DrawLine(pen, cx + s, cy - s, cx + s, cy + s - Ui.S(2));
                    }
                    else
                    {
                        g.DrawRectangle(pen, cx - s, cy - s, s * 2, s * 2);
                    }
                }
                else
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.DrawLine(pen, cx - s, cy - s, cx + s, cy + s);
                    g.DrawLine(pen, cx - s, cy + s, cx + s, cy - s);
                }
                g.SmoothingMode = SmoothingMode.AntiAlias;
            }
        }

        static void DrawCross(Graphics g, Rectangle r, Color c, float half)
        {
            float h = half * Ui.Scale * 0.8f;
            float cx = r.Left + r.Width / 2f, cy = r.Top + r.Height / 2f;
            using(var pen = new Pen(c, Math.Max(1.2f, Ui.Scale * 1.2f)))
            {
                g.DrawLine(pen, cx - h, cy - h, cx + h, cy + h);
                g.DrawLine(pen, cx - h, cy + h, cx + h, cy - h);
            }
        }

        // -1 none; otherwise 0..n-1
        int TabAt(Point pt)
        {
            for(int i = 0; i < Tabs.Count; i++)
            {
                if(Tabs[i].Bounds.Contains(pt)) return i;
            }
            return -1;
        }

        int CaptionAt(Point pt)
        {
            if(newTabRect.Contains(pt)) return 1;
            if(minRect.Contains(pt)) return 2;
            if(maxRect.Contains(pt)) return 3;
            if(closeRect.Contains(pt)) return 4;
            return 0;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int t = TabAt(e.Location);
            bool onClose = t >= 0 && Tabs[t].CloseBounds.Contains(e.Location);
            int cap = CaptionAt(e.Location);
            if(t != hoverTab || onClose != hoverTabClose || cap != hoverCaption)
            {
                hoverTab = t;
                hoverTabClose = onClose;
                hoverCaption = cap;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoverTab = -1;
            hoverTabClose = false;
            hoverCaption = 0;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            int t = TabAt(e.Location);
            if(t >= 0)
            {
                if(e.Button == MouseButtons.Middle || (e.Button == MouseButtons.Left && Tabs[t].CloseBounds.Contains(e.Location)))
                {
                    var h = TabClosed;
                    if(h != null) h(t);
                }
                else if(e.Button == MouseButtons.Left)
                {
                    var h = TabSelected;
                    if(h != null) h(t);
                }
                return;
            }
            if(e.Button != MouseButtons.Left) return;

            switch(CaptionAt(e.Location))
            {
                case 1: { var h = NewTabClicked; if(h != null) h(); return; }
                case 2: { var h = MinimizeClicked; if(h != null) h(); return; }
                case 3: { var h = MaximizeClicked; if(h != null) h(); return; }
                case 4: { var h = CloseClicked; if(h != null) h(); return; }
            }

            // empty area = title bar
            if(e.Clicks >= 2)
            {
                var h = MaximizeClicked;
                if(h != null) h();
                return;
            }
            var form = FindForm();
            if(form != null)
            {
                NativeMethods.ReleaseCapture();
                NativeMethods.SendMessage(form.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr) NativeMethods.HTCAPTION, IntPtr.Zero);
            }
        }
    }

    internal enum Glyph { Back, Forward, Reload, Stop, Menu, Shield }

    /// <summary>Flat round toolbar button drawn with vector lines (no icon font needed).</summary>
    internal sealed class IconButton : Control
    {
        public Glyph Glyph;
        public bool Active = true;     // shield: blocker on
        public string Badge = "";      // shield: counter text
        bool hover, down;

        public IconButton(Glyph glyph)
        {
            Glyph = glyph;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            SetStyle(ControlStyles.Selectable, false);
            TabStop = false;
            Cursor = Cursors.Hand;
            Size = new Size(Ui.S(34), Ui.S(34));
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(p.Bar);

            if(Enabled && (hover || down))
            {
                using(var path = Ui.RoundRect(new Rectangle(0, 0, Width - 1, Height - 1), Ui.S(10)))
                using(var br = new SolidBrush(down ? p.Border : p.Hover))
                {
                    g.FillPath(br, path);
                }
            }

            Color ink = !Enabled ? Color.FromArgb(90, p.TextDim) : p.Text;
            float w = Math.Max(1.6f, Ui.Scale * 1.7f);
            float s = Ui.S(7);

            if(Glyph == Glyph.Shield)
            {
                ink = Active ? Theme.Accent : p.TextDim;
                DrawShield(g, ink, w);
                return;
            }

            float cx = Width / 2f, cy = Height / 2f;
            using(var pen = new Pen(ink, w))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                pen.LineJoin = LineJoin.Round;
                switch(Glyph)
                {
                    case Glyph.Back:
                        g.DrawLine(pen, cx + s, cy, cx - s, cy);
                        g.DrawLines(pen, new[] { new PointF(cx - s + s * 0.8f, cy - s * 0.8f), new PointF(cx - s, cy), new PointF(cx - s + s * 0.8f, cy + s * 0.8f) });
                        break;
                    case Glyph.Forward:
                        g.DrawLine(pen, cx - s, cy, cx + s, cy);
                        g.DrawLines(pen, new[] { new PointF(cx + s - s * 0.8f, cy - s * 0.8f), new PointF(cx + s, cy), new PointF(cx + s - s * 0.8f, cy + s * 0.8f) });
                        break;
                    case Glyph.Reload:
                        {
                            var r = new RectangleF(cx - s, cy - s, s * 2, s * 2);
                            g.DrawArc(pen, r, -50, 280);
                            // arrow head at the end of the arc (top right)
                            float ax = cx + (float) Math.Cos(-50 * Math.PI / 180) * s;
                            float ay = cy + (float) Math.Sin(-50 * Math.PI / 180) * s;
                            g.DrawLines(pen, new[] { new PointF(ax + s * 0.15f, ay - s * 0.75f), new PointF(ax, ay), new PointF(ax + s * 0.85f, ay + s * 0.1f) });
                        }
                        break;
                    case Glyph.Stop:
                        g.DrawLine(pen, cx - s * 0.8f, cy - s * 0.8f, cx + s * 0.8f, cy + s * 0.8f);
                        g.DrawLine(pen, cx - s * 0.8f, cy + s * 0.8f, cx + s * 0.8f, cy - s * 0.8f);
                        break;
                    case Glyph.Menu:
                        g.DrawLine(pen, cx - s, cy - s * 0.7f, cx + s, cy - s * 0.7f);
                        g.DrawLine(pen, cx - s, cy, cx + s, cy);
                        g.DrawLine(pen, cx - s, cy + s * 0.7f, cx + s, cy + s * 0.7f);
                        break;
                }
            }
        }

        void DrawShield(Graphics g, Color ink, float w)
        {
            float s = Ui.S(7);
            float cx = Ui.S(17), cy = Height / 2f;
            var pts = new[]
            {
                new PointF(cx, cy - s * 1.15f),
                new PointF(cx + s, cy - s * 0.6f),
                new PointF(cx + s * 0.9f, cy + s * 0.35f),
                new PointF(cx, cy + s * 1.15f),
                new PointF(cx - s * 0.9f, cy + s * 0.35f),
                new PointF(cx - s, cy - s * 0.6f),
            };
            using(var pen = new Pen(ink, w))
            {
                pen.LineJoin = LineJoin.Round;
                g.DrawPolygon(pen, pts);
                if(Active)
                {
                    g.DrawLines(pen, new[] { new PointF(cx - s * 0.4f, cy), new PointF(cx - s * 0.05f, cy + s * 0.4f), new PointF(cx + s * 0.5f, cy - s * 0.35f) });
                }
            }
            if(Badge.Length > 0)
            {
                using(var f = new Font("Segoe UI", 8.5f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, Badge, f, new Rectangle(Ui.S(30), 0, Width - Ui.S(30), Height), ink,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine);
                }
            }
        }
    }

    /// <summary>Rounded "pill" address field wrapping a borderless TextBox.</summary>
    internal sealed class AddressBar : Control
    {
        public readonly TextBox Box;
        public event Action<string> Submitted;
        public event Action Cancelled;
        bool focused;

        public AddressBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                Font = new Font("Segoe UI", 10f),
            };
            Controls.Add(Box);
            Box.GotFocus += delegate
            {
                focused = true;
                ApplyTheme();
                Invalidate();
                BeginInvoke((Action) delegate { Box.SelectAll(); });
            };
            Box.LostFocus += delegate { focused = false; ApplyTheme(); Invalidate(); };
            Box.KeyDown += OnKey;
            Height = Ui.S(34);
            ApplyTheme();
        }

        void OnKey(object sender, KeyEventArgs e)
        {
            if(e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                var h = Submitted;
                if(h != null) h(Box.Text);
            }
            else if(e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                e.Handled = true;
                var h = Cancelled;
                if(h != null) h();
            }
        }

        public void ApplyTheme()
        {
            var p = Theme.P;
            Box.BackColor = focused ? p.FieldFocus : p.Field;
            Box.ForeColor = p.Text;
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            int pad = Ui.S(14);
            Box.Left = pad;
            Box.Width = Math.Max(10, Width - pad * 2);
            Box.Top = Math.Max(0, (Height - Box.Height) / 2);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            var p = Theme.P;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(p.Bar);
            var r = new Rectangle(0, 0, Width - 1, Height - 1);
            using(var path = Ui.RoundRect(r, Height / 2))
            using(var br = new SolidBrush(focused ? p.FieldFocus : p.Field))
            using(var pen = new Pen(focused ? Theme.Accent : p.Border, focused ? 1.6f : 1f))
            {
                g.FillPath(br, path);
                g.DrawPath(pen, path);
            }
        }
    }
}
