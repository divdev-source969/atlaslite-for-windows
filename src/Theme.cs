using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace AtlasLite
{
    internal sealed class Palette
    {
        public Color Window, Bar, Tab, TabHover, TabActive, Text, TextDim, Field, FieldFocus, Border, Hover;
    }

    internal static class Theme
    {
        static readonly Palette DarkP = new Palette
        {
            Window = Color.FromArgb(24, 25, 31),
            Bar = Color.FromArgb(33, 35, 43),
            Tab = Color.FromArgb(24, 25, 31),
            TabHover = Color.FromArgb(40, 42, 52),
            TabActive = Color.FromArgb(33, 35, 43),
            Text = Color.FromArgb(235, 237, 245),
            TextDim = Color.FromArgb(150, 154, 170),
            Field = Color.FromArgb(22, 23, 29),
            FieldFocus = Color.FromArgb(18, 19, 25),
            Border = Color.FromArgb(58, 61, 74),
            Hover = Color.FromArgb(52, 55, 68),
        };

        static readonly Palette LightP = new Palette
        {
            Window = Color.FromArgb(222, 225, 234),
            Bar = Color.FromArgb(247, 248, 252),
            Tab = Color.FromArgb(222, 225, 234),
            TabHover = Color.FromArgb(235, 237, 244),
            TabActive = Color.FromArgb(247, 248, 252),
            Text = Color.FromArgb(28, 30, 38),
            TextDim = Color.FromArgb(104, 108, 124),
            Field = Color.FromArgb(232, 234, 242),
            FieldFocus = Color.FromArgb(255, 255, 255),
            Border = Color.FromArgb(200, 204, 216),
            Hover = Color.FromArgb(220, 223, 233),
        };

        public static bool Dark = true;
        public static Color Accent = Color.FromArgb(0x7c, 0x9c, 0xff);

        public static Palette P { get { return Dark ? DarkP : LightP; } }

        public static void SetAccent(string hex)
        {
            if(!AppSettings.IsHexColor(hex)) return;
            int r = int.Parse(hex.Substring(1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int g = int.Parse(hex.Substring(3, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            int b = int.Parse(hex.Substring(5, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            Accent = Color.FromArgb(r, g, b);
        }
    }

    internal static class Ui
    {
        static float scale;

        public static float Scale
        {
            get
            {
                if(scale <= 0f)
                {
                    try
                    {
                        using(var g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
                    }
                    catch { scale = 1f; }
                    if(scale < 1f) scale = 1f;
                }
                return scale;
            }
        }

        public static int S(int v) { return (int) Math.Round(v * Scale); }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if(d > r.Width) d = r.Width;
            if(d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static GraphicsPath TopRoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = Math.Max(1, radius * 2);
            if(d > r.Width) d = r.Width;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddLine(r.Right, r.Bottom, r.X, r.Bottom);
            p.CloseFigure();
            return p;
        }
    }
}
