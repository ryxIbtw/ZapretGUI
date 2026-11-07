// Минималистичные элементы интерфейса с собственной отрисовкой (тёмная тема).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZapretGui
{
    static class Theme
    {
        public static bool IsLight;
        public static Color Bg, Surface, SurfaceHover, Line, Knob, Text, Muted, Dim, Accent, AccentDeep, Warn, Danger;
        public static Color KnobDotOff, KnobDotOn, HoverTint;

        // Цвет акцента: auto — зелёный в тёмной теме и синий в светлой (как было раньше).
        public static string AccentName = "auto";
        public static readonly string[] Accents = { "auto", "green", "blue", "purple", "orange", "pink" };
        // тёмная: акцент, фон под акцентом; светлая: акцент, фон под акцентом
        static readonly System.Collections.Generic.Dictionary<string, int[]> Palette = new System.Collections.Generic.Dictionary<string, int[]>
        {
            { "green",  new[] { 0x34D399, 0x10291F, 0x059669, 0xD9F7EA } },
            { "blue",   new[] { 0x60A5FA, 0x0F1E36, 0x2563EB, 0xE6EEFE } },
            { "purple", new[] { 0xA78BFA, 0x1C1736, 0x7C3AED, 0xEFEAFE } },
            { "orange", new[] { 0xFB923C, 0x2D1A0B, 0xEA580C, 0xFFEEDD } },
            { "pink",   new[] { 0xF472B6, 0x2F1426, 0xDB2777, 0xFDE8F3 } },
        };

        public static string AccentLabel(string name)
        {
            switch (name)
            {
                case "green": return L.T("Зелёный", "Green");
                case "blue": return L.T("Синий", "Blue");
                case "purple": return L.T("Фиолетовый", "Purple");
                case "orange": return L.T("Оранжевый", "Orange");
                case "pink": return L.T("Розовый", "Pink");
                default: return L.T("Авто", "Auto");
            }
        }

        // Цвет акцента для образца в меню (с учётом текущей темы).
        public static Color AccentSample(string name)
        {
            if (name == "auto") name = IsLight ? "blue" : "green";
            if (name == "blue" && IsLight) return C(0x2563EB);
            int[] p = Palette[name];
            return C(IsLight ? p[2] : p[0]);
        }

        static Theme() { Apply(false); }

        public static void Apply(bool light)
        {
            IsLight = light;
            if (light)
            {
                Bg = C(0xF3F4F6); Surface = C(0xFFFFFF); SurfaceHover = C(0xEEF1F5); Line = C(0xE4E6EA);
                Knob = C(0xDCDFE4); Text = C(0x17181C); Muted = C(0x6B6F78); Dim = C(0xA3A7AF);
                Accent = C(0x2563EB); AccentDeep = C(0xE6EEFE); Warn = C(0xD97706); Danger = C(0xDC2626);
                KnobDotOff = Color.White; KnobDotOn = Color.White; HoverTint = Color.Black;
            }
            else
            {
                Bg = C(0x111214); Surface = C(0x1B1C20); SurfaceHover = C(0x25262B); Line = C(0x2C2D33);
                Knob = C(0x34353C); Text = C(0xECECEE); Muted = C(0x8B8C94); Dim = C(0x5C5D65);
                Accent = C(0x34D399); AccentDeep = C(0x10291F); Warn = C(0xFBBF24); Danger = C(0xF87171);
                KnobDotOff = C(0xB9BABF); KnobDotOn = Color.White; HoverTint = Color.White;
            }
            int[] pal;
            if (AccentName != "auto" && Palette.TryGetValue(AccentName, out pal))
            {
                Accent = C(light ? pal[2] : pal[0]);
                AccentDeep = C(light ? pal[3] : pal[1]);
            }
        }

        // Тема из настроек: "light", "dark" или "system" (как в Windows).
        public static bool ResolveLight(string mode)
        {
            return mode == "light" || (mode != "dark" && SystemPrefersLight());
        }

        public static bool SystemPrefersLight()
        {
            try
            {
                using (var k = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    object v = k == null ? null : k.GetValue("AppsUseLightTheme");
                    return v is int && (int)v == 1;
                }
            }
            catch { return false; }
        }

        public static float S = 1f; // масштаб DPI

        // Шрифты: на Windows 11 — Segoe UI Variable, на Windows 10 — обычный Segoe UI.
        static System.Collections.Generic.HashSet<string> families;
        static string Family(string win11, string fallback)
        {
            if (families == null)
            {
                families = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try { using (var fc = new System.Drawing.Text.InstalledFontCollection()) foreach (var f in fc.Families) families.Add(f.Name); } catch { }
            }
            return families.Contains(win11) ? win11 : fallback;
        }

        public static readonly Font Title = new Font(Family("Segoe UI Variable Display Semib", "Segoe UI Semibold"), 14f);
        public static readonly Font Status = new Font(Family("Segoe UI Variable Display Semib", "Segoe UI Semibold"), 15f);
        public static readonly Font Body = new Font(Family("Segoe UI Variable Text", "Segoe UI"), 10f);
        public static readonly Font BodyStrong = new Font(Family("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 11f);
        public static readonly Font Small = new Font(Family("Segoe UI Variable Text", "Segoe UI"), 9f);
        public static readonly Font Caption = new Font(Family("Segoe UI Variable Small", "Segoe UI"), 8.5f);
        public static readonly Font Badge = new Font(Family("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 8.5f);

        static Color C(int rgb) { return Color.FromArgb(255, (rgb >> 16) & 255, (rgb >> 8) & 255, rgb & 255); }

        public static int P(float v) { return (int)Math.Round(v * S); }

        public static Color Lerp(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.A + (b.A - a.A) * t), (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t), (int)(a.B + (b.B - a.B) * t));
        }

        public static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = Math.Min(rad * 2, Math.Min(r.Width, r.Height));
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void FillRound(Graphics g, Color c, RectangleF r, float rad)
        {
            using (var p = Round(r, rad)) using (var b = new SolidBrush(c)) g.FillPath(b, p);
        }

        // Значок: скруглённый квадрат с градиентом и белым символом питания (как макет в Figma),
        // dot — точка состояния в правом нижнем углу (Color.Empty — без точки).
        public static Bitmap Logo(int size, Color top, Color bottom, Color dot)
        {
            var bmp = new Bitmap(size, size);
            using (var g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                float pad = size <= 16 ? 0.5f : size * 0.04f;
                var r = new RectangleF(pad, pad, size - pad * 2, size - pad * 2);
                using (var path = Round(r, r.Width * 0.28f))
                using (var b = new LinearGradientBrush(new PointF(0, r.Top), new PointF(0, r.Bottom), top, bottom))
                    g.FillPath(b, path);
                float w = Math.Max(1.5f, size * 0.095f);
                using (var pen = new Pen(Color.White, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                {
                    float c = size / 2f, rr = size * 0.22f;
                    g.DrawArc(pen, c - rr, c - rr + size * 0.035f, rr * 2, rr * 2, -55, 290);
                    g.DrawLine(pen, c, size * 0.24f, c, size * 0.47f);
                }
                if (dot != Color.Empty)
                {
                    float d = size * 0.38f, x = size - d - size * 0.01f, y = size - d - size * 0.01f;
                    using (var ring = new SolidBrush(Color.FromArgb(255, 20, 20, 22))) g.FillEllipse(ring, x - size * 0.04f, y - size * 0.04f, d + size * 0.08f, d + size * 0.08f);
                    using (var b = new SolidBrush(dot)) g.FillEllipse(b, x, y, d, d);
                }
            }
            return bmp;
        }

        static Color Darker(Color c, float k) { return Color.FromArgb(c.A, (int)(c.R * k), (int)(c.G * k), (int)(c.B * k)); }

        // state: 0 — выключено (серый), 1 — включено (акцент), 2 — идёт подбор/операция (акцент + жёлтая точка)
        public static Icon MakeIcon(int size, int state)
        {
            Color top = state == 0 ? C(0x6B6E76) : Accent;
            Color bottom = state == 0 ? C(0x45474E) : Darker(Accent, 0.72f);
            Color dot = state == 2 ? C(0xFBBF24) : Color.Empty;
            using (var bmp = Logo(size, top, bottom, dot)) return Icon.FromHandle(bmp.GetHicon());
        }
    }

    abstract class PaintedControl : Control
    {
        protected PaintedControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
        }
    }

    // Заголовок: «zapret» + плашка «by Flowseal» (автор zapret-discord-youtube),
    // под ним такая же плашка «GUI by ryxIbtw» (автор этого интерфейса).
    class BrandTitle : PaintedControl
    {
        public const string ZapretAuthor = "Flowseal";
        public const string Author = "ryxIbtw";
        static Font BadgeFont { get { return Theme.Badge; } }
        const TextFormatFlags Flags = TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.Top;
        const float PillH = 20, PillPad = 10, Gap = 10, RowGap = 5;

        static float PillWidth(Graphics g, string prefix, string nick)
        {
            return TextRenderer.MeasureText(g, prefix, BadgeFont, Size.Empty, Flags).Width +
                   TextRenderer.MeasureText(g, nick, BadgeFont, Size.Empty, Flags).Width + PillPad * 2 * Theme.S;
        }

        public BrandTitle()
        {
            using (var g = Graphics.FromHwnd(IntPtr.Zero))
            {
                Size s = TextRenderer.MeasureText(g, "zapret", Theme.Title, Size.Empty, Flags);
                float row1 = s.Width + Gap * Theme.S + PillWidth(g, "by ", ZapretAuthor);
                float row2 = PillWidth(g, "GUI by ", Author);
                Size = new Size((int)Math.Ceiling(Math.Max(row1, row2)) + Theme.P(12),
                                s.Height + Theme.P(RowGap) + Theme.P(PillH) + Theme.P(2));
            }
        }

        // Плашка: «prefix» приглушённо + «nick» акцентным цветом.
        static void DrawPill(Graphics g, float x, float y, string prefix, string nick)
        {
            float S = Theme.S, ph = PillH * S, px = PillPad * S;
            Size pre = TextRenderer.MeasureText(g, prefix, BadgeFont, Size.Empty, Flags);
            Size nk = TextRenderer.MeasureText(g, nick, BadgeFont, Size.Empty, Flags);
            var pill = new RectangleF(x, y, pre.Width + nk.Width + px * 2, ph);
            Theme.FillRound(g, Theme.AccentDeep, pill, ph / 2f);
            using (var pen = new Pen(Color.FromArgb(70, Theme.Accent), 1f)) using (var path = Theme.Round(pill, ph / 2f)) g.DrawPath(pen, path);
            int yText = (int)(pill.Y + (ph - nk.Height) / 2f);
            TextRenderer.DrawText(g, prefix, BadgeFont, new Point((int)(x + px), yText), Theme.Muted, Theme.AccentDeep, Flags);
            TextRenderer.DrawText(g, nick, BadgeFont, new Point((int)(x + px) + pre.Width, yText), Theme.Accent, Theme.AccentDeep, Flags);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Size s = TextRenderer.MeasureText(g, "zapret", Theme.Title, Size.Empty, Flags);
            TextRenderer.DrawText(g, "zapret", Theme.Title, new Point(0, 0), Theme.Text, Theme.Bg, Flags);

            DrawPill(g, s.Width + Gap * S, (s.Height - PillH * S) / 2f + 1 * S, "by ", ZapretAuthor);
            DrawPill(g, 1, s.Height + RowGap * S, "GUI by ", Author);
        }
    }

    // Круглая кнопка в шапке: шестерёнка (настройки) или стрелка назад.
    class GearButton : PaintedControl
    {
        bool hover;
        public bool Back;
        public GearButton() { Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            if (hover) using (var b = new SolidBrush(Theme.SurfaceHover)) g.FillEllipse(b, 0, 0, Width - 1, Height - 1);
            Color c = hover ? Theme.Text : Theme.Muted;
            float cx = Width / 2f, cy = Height / 2f;
            if (Back)
            {
                using (var pen = new Pen(c, 1.8f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    float d = 5 * S;
                    g.DrawLines(pen, new[] { new PointF(cx + d * 0.4f, cy - d), new PointF(cx - d * 0.6f, cy), new PointF(cx + d * 0.4f, cy + d) });
                }
                return;
            }
            using (var b = new SolidBrush(c))
            {
                // зубцы
                for (int i = 0; i < 8; i++)
                {
                    var st = g.Save();
                    g.TranslateTransform(cx, cy);
                    g.RotateTransform(i * 45);
                    g.FillRectangle(b, -1.6f * S, -9f * S, 3.2f * S, 4f * S);
                    g.Restore(st);
                }
                g.FillEllipse(b, cx - 6.5f * S, cy - 6.5f * S, 13 * S, 13 * S);
            }
            using (var b = new SolidBrush(hover ? Theme.SurfaceHover : Theme.Bg))
                g.FillEllipse(b, cx - 2.8f * S, cy - 2.8f * S, 5.6f * S, 5.6f * S);
        }
    }

    // Индикаторы доступности: «YouTube ●  Discord ●».
    class StatusDots : PaintedControl
    {
        // -1 — неизвестно, иначе доля доступных адресов 0..1
        public float YouTube = -1, Discord = -1;
        public bool Checking;
        bool hover;
        public Func<string> TooltipText;   // текст подсказки при наведении (подробности последней проверки)
        static DarkToolTip tip;

        public StatusDots() { Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true; Invalidate();
            if (TooltipText != null)
            {
                if (tip == null) tip = new DarkToolTip();
                tip.SetToolTip(this, TooltipText());
            }
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        static Color ColorOf(float v)
        {
            if (v < 0) return Theme.Dim;
            if (v >= 0.99f) return Theme.Accent;
            if (v > 0.01f) return Theme.Warn;
            return Theme.Danger;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.Left | TextFormatFlags.VerticalCenter;
            Color text = hover ? Theme.Text : Theme.Muted;
            string a = "YouTube", b = "Discord";
            int wa = TextRenderer.MeasureText(g, a, Theme.Small, Size.Empty, flags).Width;
            int wb = TextRenderer.MeasureText(g, b, Theme.Small, Size.Empty, flags).Width;
            float dot = 8 * S, gap = 6 * S, sep = 20 * S;
            float total = wa + gap + dot + sep + wb + gap + dot;
            float x = (Width - total) / 2f;
            Action<string, int, float> item = (label, w, v) =>
            {
                TextRenderer.DrawText(g, label, Theme.Small, new Rectangle((int)x, 0, w + 2, Height), text, Theme.Bg, flags);
                x += w + gap;
                Color c = Checking ? Theme.Dim : ColorOf(v);
                if (!Checking && v >= 0)
                    using (var gb = new SolidBrush(Color.FromArgb(45, c))) g.FillEllipse(gb, x - 3 * S, Height / 2f - dot / 2 - 3 * S, dot + 6 * S, dot + 6 * S);
                using (var br = new SolidBrush(c)) g.FillEllipse(br, x, Height / 2f - dot / 2, dot, dot);
                x += dot + sep;
            };
            item(a, wa, YouTube);
            item(b, wb, Discord);
        }
    }

    // Всплывающая подсказка в цветах программы.
    class DarkToolTip : ToolTip
    {
        public DarkToolTip()
        {
            OwnerDraw = true;
            InitialDelay = 350; AutoPopDelay = 15000; ReshowDelay = 100;
            Popup += (s, e) =>
            {
                Size sz = TextRenderer.MeasureText(GetToolTip(e.AssociatedControl), Theme.Small);
                e.ToolTipSize = new Size(sz.Width + Theme.P(20), sz.Height + Theme.P(14));
            };
            Draw += (s, e) =>
            {
                e.Graphics.Clear(Theme.Surface);
                using (var pen = new Pen(Theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
                TextRenderer.DrawText(e.Graphics, e.ToolTipText, Theme.Small, new Rectangle(Theme.P(10), Theme.P(7), e.Bounds.Width, e.Bounds.Height),
                    Theme.Text, Theme.Surface, TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.NoPadding);
            };
        }
    }

    // Кнопка смены темы: луна в тёмной теме, солнце в светлой.
    class ThemeToggle : PaintedControl
    {
        bool hover;
        public ThemeToggle() { Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            var r = new RectangleF(0, 0, Width - 1, Height - 1);
            if (hover) using (var b = new SolidBrush(Theme.SurfaceHover)) g.FillEllipse(b, r);
            Color c = hover ? Theme.Text : Theme.Muted;
            float cx = Width / 2f, cy = Height / 2f;

            if (Theme.IsLight)
            {
                float rr = 4.2f * S;
                using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
                using (var pen = new Pen(c, 1.6f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    for (int i = 0; i < 8; i++)
                    {
                        double ang = i * Math.PI / 4;
                        float c1 = (float)Math.Cos(ang), s1 = (float)Math.Sin(ang);
                        g.DrawLine(pen, cx + c1 * 7 * S, cy + s1 * 7 * S, cx + c1 * 9.5f * S, cy + s1 * 9.5f * S);
                    }
            }
            else
            {
                float rr = 7f * S;
                using (var b = new SolidBrush(c)) g.FillEllipse(b, cx - rr, cy - rr, rr * 2, rr * 2);
                using (var b = new SolidBrush(hover ? Theme.SurfaceHover : Theme.Bg))
                    g.FillEllipse(b, cx - rr + 5f * S, cy - rr - 3f * S, rr * 2, rr * 2);
            }
        }
    }

    // Большая круглая кнопка включения.
    class PowerButton : PaintedControl
    {
        bool on, busy, hover, down;
        float t, spin, phase;
        readonly Timer tm = new Timer { Interval = 15 };
        public bool Breathe = true;   // мягкое «дыхание» свечения, пока обход включён

        public PowerButton()
        {
            Cursor = Cursors.Hand;
            tm.Tick += delegate
            {
                float target = on ? 1f : 0f;
                t += (target - t) * 0.12f;
                bool settled = Math.Abs(target - t) < 0.005f;
                if (settled) t = target;
                var form = FindForm();
                bool shown = Visible && form != null && form.Visible && form.WindowState != FormWindowState.Minimized;
                if (busy) { spin = (spin + 7) % 360; tm.Interval = 15; Invalidate(); return; }
                if (!settled) { tm.Interval = 15; Invalidate(); return; }
                if (on && Breathe)
                {
                    // окно скрыто — не тратим процессор, просто изредка проверяем
                    if (!shown) { tm.Interval = 500; return; }
                    tm.Interval = 33;
                    phase = (phase + 0.045f) % (float)(Math.PI * 2);
                    Invalidate();
                    return;
                }
                tm.Stop();
                Invalidate();
            };
        }

        public bool On { get { return on; } set { if (on != value) { on = value; tm.Start(); } else if (on && Breathe && !tm.Enabled) tm.Start(); } }
        public bool Busy { get { return busy; } set { if (busy != value) { busy = value; tm.Start(); Invalidate(); } } }
        public void Settle() { t = on ? 1f : 0f; Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float S = Theme.S;
            float size = Math.Min(Width, Height) - 28 * S;
            if (down) size -= 4 * S;
            var r = new RectangleF((Width - size) / 2f, (Height - size) / 2f, size, size);

            // свечение (при «дыхании» плавно меняются яркость и радиус)
            if (t > 0.01f)
            {
                float k = Breathe && !busy ? (float)(0.5 + 0.5 * Math.Sin(phase)) : 0.5f;
                float spread = 1.9f + 0.9f * k, bright = 0.75f + 0.45f * k;
                for (int i = 6; i >= 1; i--)
                {
                    var gr = RectangleF.Inflate(r, i * spread * S, i * spread * S);
                    using (var b = new SolidBrush(Color.FromArgb(Math.Min(255, (int)(14 * t * bright * (7 - i) / 6f)), Theme.Accent)))
                        g.FillEllipse(b, gr);
                }
            }

            Color fill = Theme.Lerp(Theme.Surface, Theme.AccentDeep, t);
            if (hover && !busy) fill = Theme.Lerp(fill, Theme.HoverTint, 0.035f);
            using (var b = new SolidBrush(fill)) g.FillEllipse(b, r);
            using (var pen = new Pen(Theme.Lerp(Theme.Line, Theme.Accent, t * 0.9f), 2f * S)) g.DrawEllipse(pen, r);

            // символ питания
            Color glyph = Theme.Lerp(hover ? Theme.Text : Theme.Muted, Theme.Accent, t);
            float cx = r.X + r.Width / 2f, cy = r.Y + r.Height / 2f, ir = r.Width * 0.17f;
            using (var pen = new Pen(glyph, 4.5f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, cx - ir, cy - ir + 3 * S, ir * 2, ir * 2, -55, 290);
                g.DrawLine(pen, cx, cy - ir - 4 * S, cx, cy - ir * 0.15f);
            }

            if (busy)
            {
                var sr = RectangleF.Inflate(r, -6 * S, -6 * S);
                using (var pen = new Pen(Theme.Accent, 3f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, sr, spin, 80);
            }
        }
    }

    // Карточка выбора стратегии: имя + стрелки ‹ ›.
    class StrategyPicker : PaintedControl
    {
        public string Value = "";
        public event EventHandler Prev, Next, Open;
        int hot = -1; // 0 — карточка, 1 — назад, 2 — вперёд

        public StrategyPicker() { Cursor = Cursors.Hand; }

        RectangleF NextR { get { float s = 34 * Theme.S; return new RectangleF(Width - 14 * Theme.S - s, (Height - s) / 2f, s, s); } }
        RectangleF PrevR { get { var n = NextR; return new RectangleF(n.X - 6 * Theme.S - n.Width, n.Y, n.Width, n.Height); } }

        int Hit(Point p)
        {
            if (!ClientRectangle.Contains(p)) return -1;
            if (NextR.Contains(p)) return 2;
            if (PrevR.Contains(p)) return 1;
            return 0;
        }

        public void SetValue(string v) { if (v != Value) { Value = v; Invalidate(); } }

        protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hot) { hot = h; Invalidate(); } base.OnMouseMove(e); }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            int h = Hit(e.Location);
            EventHandler ev = h == 1 ? Prev : h == 2 ? Next : Open;
            if (ev != null) ev(this, EventArgs.Empty);
        }
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            EventHandler ev = e.Delta > 0 ? Prev : Next;
            if (ev != null) ev(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color card = hot == 0 ? Theme.SurfaceHover : Theme.Surface;
            Theme.FillRound(g, card, new RectangleF(0, 0, Width - 1, Height - 1), 14 * S);

            int textW = (int)(PrevR.X - 24 * S);
            TextRenderer.DrawText(g, L.T("Стратегия", "Strategy"), Theme.Caption, new Rectangle(Theme.P(16), Theme.P(11), textW, Theme.P(18)),
                Theme.Muted, card, TextFormatFlags.Left | TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, Value == "" ? L.T("нет .bat файлов", "no .bat files") : Value, Theme.BodyStrong,
                new Rectangle(Theme.P(16), Theme.P(29), textW, Theme.P(26)), Theme.Text, card,
                TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);

            DrawArrow(g, PrevR, hot == 1, true);
            DrawArrow(g, NextR, hot == 2, false);
        }

        void DrawArrow(Graphics g, RectangleF r, bool isHot, bool left)
        {
            float S = Theme.S;
            using (var b = new SolidBrush(isHot ? Theme.Line : Theme.Knob)) g.FillEllipse(b, r);
            float cx = r.X + r.Width / 2f + (left ? -1 : 1) * S, cy = r.Y + r.Height / 2f, d = 4.5f * S;
            using (var pen = new Pen(Theme.Text, 1.8f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                if (left) g.DrawLines(pen, new[] { new PointF(cx + d / 2, cy - d), new PointF(cx - d / 2, cy), new PointF(cx + d / 2, cy + d) });
                else g.DrawLines(pen, new[] { new PointF(cx - d / 2, cy - d), new PointF(cx + d / 2, cy), new PointF(cx - d / 2, cy + d) });
            }
        }
    }

    // Панель страницы с двойной буферизацией — чтобы плавный сдвиг страниц не мерцал.
    class BufferedPanel : Panel
    {
        public BufferedPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
        }
    }

    // Подложка-карточка для строк настроек.
    class Card : PaintedControl
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.FillRound(e.Graphics, Theme.Surface, new RectangleF(0, 0, Width - 1, Height - 1), 14 * Theme.S);
        }
    }

    // Строка настройки: переключатель или значение с выпадающим списком.
    class SettingRow : PaintedControl
    {
        public string Title;
        public string Hint = "";
        public bool IsSwitch;
        public bool Nav;   // стрелка › вместо ⌄ (переход/действие)
        public bool Divider;
        string value = "";
        bool isChecked, hover;
        float a, h;   // a — положение переключателя, h — плавная подсветка при наведении
        readonly Timer tm = new Timer { Interval = 15 };

        public SettingRow(string title, bool isSwitch)
        {
            Title = title; IsSwitch = isSwitch;
            BackColor = Theme.Surface;
            Cursor = Cursors.Hand;
            tm.Tick += delegate
            {
                float ta = isChecked ? 1f : 0f, th = hover && Enabled ? 1f : 0f;
                a += (ta - a) * 0.25f;
                h += (th - h) * 0.3f;
                bool doneA = Math.Abs(ta - a) < 0.01f, doneH = Math.Abs(th - h) < 0.02f;
                if (doneA) a = ta;
                if (doneH) h = th;
                if (doneA && doneH) tm.Stop();
                Invalidate();
            };
        }

        public bool Checked
        {
            get { return isChecked; }
            set { if (isChecked != value) { isChecked = value; if (IsHandleCreated && Visible) tm.Start(); else { a = value ? 1 : 0; Invalidate(); } } }
        }

        public string Value { get { return value; } set { if (this.value != value) { this.value = value; Invalidate(); } } }

        protected override void OnMouseEnter(EventArgs e) { hover = true; tm.Start(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; tm.Start(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);
            Color bg = Theme.Surface;
            if (h > 0.02f)
            {
                bg = Theme.Lerp(Theme.Surface, Theme.SurfaceHover, h);
                Theme.FillRound(g, bg, new RectangleF(0, 0, Width - 1, Height - 1), 10 * S);
            }
            if (Divider && h < 0.5f)
            {
                using (var pen = new Pen(Theme.Line, 1)) g.DrawLine(pen, 14 * S, 0, Width - 14 * S, 0);
            }

            Color titleColor = Enabled ? Theme.Text : Theme.Dim;
            if (Hint == "")
                TextRenderer.DrawText(g, Title, Theme.Body, new Rectangle(Theme.P(14), 0, Width, Height), titleColor, bg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            else
            {
                TextRenderer.DrawText(g, Title, Theme.Body, new Rectangle(Theme.P(14), Theme.P(7), Width, Theme.P(20)), titleColor, bg,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, Hint, Theme.Caption, new Rectangle(Theme.P(14), Theme.P(27), Width - Theme.P(90), Theme.P(16)), Theme.Muted, bg,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            if (IsSwitch)
            {
                float sw = 38 * S, sh = 22 * S;
                var r = new RectangleF(Width - 14 * S - sw, (Height - sh) / 2f, sw, sh);
                Theme.FillRound(g, Theme.Lerp(Theme.Knob, Theme.Accent, a), r, sh / 2f);
                float d = sh - 6 * S;
                float x = r.X + 3 * S + a * (sw - 6 * S - d);
                using (var b = new SolidBrush(Theme.Lerp(Theme.KnobDotOff, Theme.KnobDotOn, a)))
                    g.FillEllipse(b, x, r.Y + 3 * S, d, d);
            }
            else
            {
                float cx = Width - 20 * S, cy = Height / 2f, d = 4 * S;
                using (var pen = new Pen(Theme.Muted, 1.6f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    if (Nav) g.DrawLines(pen, new[] { new PointF(cx - d / 2, cy - d), new PointF(cx + d / 2, cy), new PointF(cx - d / 2, cy + d) });
                    else g.DrawLines(pen, new[] { new PointF(cx - d, cy - d / 2), new PointF(cx, cy + d / 2), new PointF(cx + d, cy - d / 2) });
                }
                var tr = new Rectangle(0, 0, (int)(cx - 12 * S), Height);
                TextRenderer.DrawText(g, value, Theme.Body, tr, Theme.Muted, bg,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // Текстовая ссылка в подвале.
    class FlatLink : Label
    {
        public FlatLink(string text)
        {
            Text = text;
            AutoSize = true;
            Font = Theme.Small;
            ForeColor = Theme.Muted;
            BackColor = Theme.Bg;
            Cursor = Cursors.Hand;
            Padding = new Padding(0);
        }
        protected override void OnMouseEnter(EventArgs e) { ForeColor = Theme.Text; base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { ForeColor = Theme.Muted; base.OnMouseLeave(e); }
    }

    // Тёмное оформление меню.
    class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.SurfaceHover; } }
        public override Color MenuItemBorder { get { return Theme.SurfaceHover; } }
        public override Color MenuBorder { get { return Theme.Line; } }
        public override Color ToolStripDropDownBackground { get { return Theme.Surface; } }
        public override Color ImageMarginGradientBegin { get { return Theme.Surface; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.Surface; } }
        public override Color ImageMarginGradientEnd { get { return Theme.Surface; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.SurfaceHover; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.SurfaceHover; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.Surface; } }
    }

    class DarkRenderer : ToolStripProfessionalRenderer
    {
        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = !e.Item.Enabled ? Theme.Dim : ("current".Equals(e.Item.Tag) ? Theme.Accent : Theme.Text);
            base.OnRenderItemText(e);
        }
        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.ArrowColor = Theme.Muted;
            base.OnRenderArrow(e);
        }
    }

    static class Menus
    {
        public static ContextMenuStrip Create()
        {
            var m = new ContextMenuStrip
            {
                Renderer = new DarkRenderer(),
                ShowImageMargin = false,
                Font = Theme.Body,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Padding = new Padding(2, 4, 2, 4)
            };
            m.Closed += delegate { m.BeginInvoke((MethodInvoker)m.Dispose); };
            return m;
        }

        public static ToolStripMenuItem Add(ContextMenuStrip m, string text, bool current, EventHandler click)
        {
            var it = new ToolStripMenuItem((current ? "✓  " : "     ") + text) { Tag = current ? "current" : null, Padding = new Padding(4, 3, 12, 3) };
            if (click != null) it.Click += click;
            m.Items.Add(it);
            return it;
        }
    }
}
