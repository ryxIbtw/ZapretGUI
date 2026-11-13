// Цвета, темы, масштаб, шрифты и значок программы — общие для всех версий интерфейса.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;

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

        static Theme() { MakeFonts(1f); Apply(false); }

        // Оттенок темы: "" — обычная, "amoled" — чёрная (поверх тёмной), "warm" — тёплая (поверх светлой).
        public static string Flavor = "";

        // Режимы из настроек: system, dark, light, amoled, warm.
        public static readonly string[] Modes = { "system", "dark", "light", "amoled", "warm" };

        public static void ApplyMode(string mode)
        {
            Flavor = mode == "amoled" ? "amoled" : mode == "warm" ? "warm" : "";
            Apply(ResolveLight(mode));
        }

        public static void Apply(bool light)
        {
            IsLight = light;
            if (light)
            {
                Bg = C(0xF3F4F6); Surface = C(0xFFFFFF); SurfaceHover = C(0xEEF1F5); Line = C(0xE4E6EA);
                Knob = C(0xDCDFE4); Text = C(0x17181C); Muted = C(0x6B6F78); Dim = C(0xA3A7AF);
                Accent = C(0x2563EB); AccentDeep = C(0xE6EEFE); Warn = C(0xD97706); Danger = C(0xDC2626);
                KnobDotOff = Color.White; KnobDotOn = Color.White; HoverTint = Color.Black;
                if (Flavor == "warm")
                {
                    // кремовый фон и тёплые серые вместо холодных
                    Bg = C(0xF4EFE6); Surface = C(0xFFFBF4); SurfaceHover = C(0xF1EADD); Line = C(0xE5DCCB);
                    Knob = C(0xDDD3C1); Text = C(0x2A2520); Muted = C(0x7A7064); Dim = C(0xB0A696);
                }
            }
            else
            {
                Bg = C(0x111214); Surface = C(0x1B1C20); SurfaceHover = C(0x25262B); Line = C(0x2C2D33);
                Knob = C(0x34353C); Text = C(0xECECEE); Muted = C(0x8B8C94); Dim = C(0x5C5D65);
                Accent = C(0x34D399); AccentDeep = C(0x10291F); Warn = C(0xFBBF24); Danger = C(0xF87171);
                KnobDotOff = C(0xB9BABF); KnobDotOn = Color.White; HoverTint = Color.White;
                if (Flavor == "amoled")
                {
                    // полностью чёрный фон, карточки чуть светлее
                    Bg = C(0x000000); Surface = C(0x101012); SurfaceHover = C(0x1A1A1D); Line = C(0x222226);
                    Knob = C(0x2C2C31); Dim = C(0x55565C);
                }
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
            if (mode == "light" || mode == "warm") return true;
            if (mode == "dark" || mode == "amoled") return false;
            return SystemPrefersLight();
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

        public static float S = 1f;      // итоговый масштаб: DPI × масштаб интерфейса
        public static float Dpi = 1f;    // масштаб DPI экрана
        public static float UiScale = 1f; // масштаб интерфейса из настроек (0.75 … 1.25)
        public static readonly int[] Scales = { 75, 90, 100, 110, 125 };

        // Меняет масштаб интерфейса: размеры элементов и шрифты.
        public static void SetScale(float dpi, float ui)
        {
            Dpi = dpi; UiScale = ui; S = dpi * ui;
            MakeFonts(ui);
        }

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

        public static Font Title, Status, Body, BodyStrong, Small, Caption, Badge, Mono, MonoSmall;

        // Шрифты пересоздаются при смене масштаба (старые не освобождаем — ими ещё могут пользоваться открытые окна).
        static void MakeFonts(float k)
        {
            Title = new Font(Family("Segoe UI Variable Display Semib", "Segoe UI Semibold"), 14f * k);
            Status = new Font(Family("Segoe UI Variable Display Semib", "Segoe UI Semibold"), 15f * k);
            Body = new Font(Family("Segoe UI Variable Text", "Segoe UI"), 10f * k);
            BodyStrong = new Font(Family("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 11f * k);
            Small = new Font(Family("Segoe UI Variable Text", "Segoe UI"), 9f * k);
            Caption = new Font(Family("Segoe UI Variable Small", "Segoe UI"), 8.5f * k);
            Badge = new Font(Family("Segoe UI Variable Text Semibold", "Segoe UI Semibold"), 8.5f * k);
            Mono = new Font("Consolas", 9f * k);
            MonoSmall = new Font("Consolas", 8.5f * k);
        }

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

        // state: 0 — выключено (серый), 1 — включено (акцент), 2 — идёт подбор/операция (акцент + жёлтая точка),
        // 3 — включено, но YouTube или Discord не открываются (акцент + красная точка)
        public static Icon MakeIcon(int size, int state)
        {
            Color top = state == 0 ? C(0x6B6E76) : Accent;
            Color bottom = state == 0 ? C(0x45474E) : Darker(Accent, 0.72f);
            Color dot = state == 2 ? C(0xFBBF24) : state == 3 ? C(0xEF4444) : Color.Empty;
            using (var bmp = Logo(size, top, bottom, dot)) return Icon.FromHandle(bmp.GetHicon());
        }
    }
}
