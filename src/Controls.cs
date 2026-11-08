// Минималистичные элементы интерфейса с собственной отрисовкой (тёмная тема).
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace ZapretGui
{
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
            g.Clear(BackColor);
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
                TextRenderer.DrawText(g, label, Theme.Small, new Rectangle((int)x, 0, w + 2, Height), text, BackColor, flags);
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

    // Две плашки на главном экране: YouTube и Discord. Цвет плашки — состояние, под названием — «3/3 · 124 мс».
    class ServiceCards : PaintedControl
    {
        public float YouTube = -1, Discord = -1;   // доля доступных адресов 0..1, -1 — неизвестно
        public bool Checking;
        public string YouTubeMeta = "", DiscordMeta = "";
        public Func<string> TooltipText;
        int hot = -1;
        static DarkToolTip tip;

        public ServiceCards() { Cursor = Cursors.Hand; }

        RectangleF CardRect(int i)
        {
            float gap = 12 * Theme.S, w = (Width - gap) / 2f;
            return new RectangleF(i * (w + gap), 0, w - 1, Height - 1);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int h = CardRect(0).Contains(e.Location) ? 0 : CardRect(1).Contains(e.Location) ? 1 : -1;
            if (h != hot) { hot = h; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseEnter(EventArgs e)
        {
            if (TooltipText != null) { if (tip == null) tip = new DarkToolTip(); tip.SetToolTip(this, TooltipText()); }
            base.OnMouseEnter(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hot = -1; Invalidate(); base.OnMouseLeave(e); }

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
            DrawCard(g, 0, "YouTube", "play", YouTube, YouTubeMeta);
            DrawCard(g, 1, "Discord", "chat", Discord, DiscordMeta);
        }

        void DrawCard(Graphics g, int i, string name, string icon, float v, string meta)
        {
            float S = Theme.S;
            var r = CardRect(i);
            bool known = !Checking && v >= 0;
            Color c = known ? ColorOf(v) : Theme.Muted;
            Color bg = known ? Theme.Lerp(Theme.Surface, c, Theme.IsLight ? 0.07f : 0.09f) : Theme.Surface;
            if (hot == i) bg = Theme.Lerp(bg, Theme.HoverTint, Theme.IsLight ? 0.03f : 0.04f);
            Theme.FillRound(g, bg, r, 14 * S);
            if (known)
                using (var path = Theme.Round(r, 14 * S)) using (var pen = new Pen(Color.FromArgb(Theme.IsLight ? 70 : 55, c), 1f)) g.DrawPath(pen, path);

            float ts = 30 * S;
            var tile = new RectangleF(r.X + 10 * S, r.Y + (r.Height - ts) / 2f, ts, ts);
            Theme.FillRound(g, Color.FromArgb(Theme.IsLight ? 34 : 42, c), tile, 9 * S);
            Icons.Draw(g, icon, RectangleF.Inflate(tile, -7 * S, -7 * S), c);

            int x = (int)(tile.Right + 10 * S), w = (int)(r.Right - x - 8 * S);
            string m = Checking ? L.T("проверка…", "checking…") : v < 0 ? L.T("нажмите, чтобы проверить", "click to check") : meta;
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            int cy = (int)(r.Y + r.Height / 2f);
            TextRenderer.DrawText(g, name, Theme.Body, new Rectangle(x, cy - Theme.P(18), w, Theme.P(19)), Theme.Text, flags);
            TextRenderer.DrawText(g, m, Theme.Caption, new Rectangle(x, cy + Theme.P(1), w, Theme.P(16)), known ? Theme.Muted : Theme.Dim, flags);
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
    // В режиме Hero (главный экран) кнопка рисует ещё мягкое пятно света за собой, расходящееся кольцо
    // в момент включения, кольцо прогресса подбора и подписи Title/Subtitle под кругом.
    class PowerButton : PaintedControl
    {
        bool on, busy, hover, down;
        float t, spin, phase;
        float ripple = -1;            // 0…1 — идёт волна после включения, -1 — нет
        float shownProgress = -1;     // плавно догоняет Progress
        readonly Timer tm = new Timer { Interval = 15 };
        public bool Breathe = true;   // мягкое «дыхание» свечения, пока обход включён
        public bool Hero;
        public float CircleSize;      // диаметр круга в режиме Hero
        public float CircleCenterY;   // центр круга по вертикали в режиме Hero
        public string Title = "", Subtitle = "";
        public Color TitleColor = Color.Empty;
        float progress = -1;

        // Доля проверенных стратегий 0…1 во время подбора; -1 — подбора нет.
        public float Progress
        {
            get { return progress; }
            set { if (progress != value) { progress = value; if (value < 0) shownProgress = -1; tm.Start(); } }
        }

        public void SetText(string title, Color color, string subtitle)
        {
            if (title == Title && color == TitleColor && subtitle == Subtitle) return;
            Title = title; TitleColor = color; Subtitle = subtitle; Invalidate();
        }

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
                bool anim = false;
                if (ripple >= 0) { ripple += 0.022f; if (ripple >= 1) ripple = -1; anim = true; }
                if (progress >= 0)
                {
                    if (shownProgress < 0) shownProgress = 0;
                    shownProgress += (progress - shownProgress) * 0.15f;
                    if (Math.Abs(progress - shownProgress) < 0.002f) shownProgress = progress; else anim = true;
                }
                if (busy) { spin = (spin + 7) % 360; tm.Interval = 15; Invalidate(); return; }
                if (!settled || anim) { tm.Interval = 15; Invalidate(); return; }
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

        public bool On
        {
            get { return on; }
            set
            {
                if (on != value)
                {
                    on = value;
                    // волна только при живом включении, а не при первом показе окна
                    if (value && Hero && IsHandleCreated && Visible && !busy) ripple = 0;
                    tm.Start();
                }
                else if (on && Breathe && !tm.Enabled) tm.Start();
            }
        }
        public bool Busy { get { return busy; } set { if (busy != value) { busy = value; tm.Start(); Invalidate(); } } }
        public void Settle() { t = on ? 1f : 0f; ripple = -1; shownProgress = progress; Invalidate(); }

        // Круг кнопки (кликабельная область).
        RectangleF CircleRect
        {
            get
            {
                float S = Theme.S;
                if (!Hero)
                {
                    float sz = Math.Min(Width, Height) - 28 * S;
                    return new RectangleF((Width - sz) / 2f, (Height - sz) / 2f, sz, sz);
                }
                return new RectangleF((Width - CircleSize) / 2f, CircleCenterY - CircleSize / 2f, CircleSize, CircleSize);
            }
        }

        bool InCircle(Point p)
        {
            var r = CircleRect;
            float dx = p.X - (r.X + r.Width / 2f), dy = p.Y - (r.Y + r.Height / 2f);
            return dx * dx + dy * dy <= r.Width * r.Width / 4f;
        }

        // В режиме Hero нажимается только круг, а не вся область с подписями.
        protected override void OnClick(EventArgs e)
        {
            if (Hero && !InCircle(PointToClient(MousePosition))) return;
            base.OnClick(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = !Hero || InCircle(e.Location);
            if (h != hover) { hover = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseEnter(EventArgs e) { if (!Hero) { hover = true; Invalidate(); } base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (hover) { down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float S = Theme.S;
            var r = CircleRect;
            if (down) r = RectangleF.Inflate(r, -2 * S, -2 * S);
            float size = r.Width;
            float cxc = r.X + r.Width / 2f, cyc = r.Y + r.Height / 2f;

            if (Hero)
            {
                // мягкое пятно цвета акцента за кнопкой: проявляется при включении
                if (t > 0.01f)
                {
                    float k = Breathe && !busy ? (float)(0.5 + 0.5 * Math.Sin(phase)) : 0.5f;
                    float gw = Math.Min(Width * 0.92f, size * 2.6f), gh = Math.Min(cyc * 2f - 2, size * 1.9f);
                    using (var gp = new GraphicsPath())
                    {
                        gp.AddEllipse(cxc - gw / 2, cyc - gh / 2, gw, gh);
                        using (var pb = new PathGradientBrush(gp))
                        {
                            pb.CenterColor = Color.FromArgb((int)((Theme.IsLight ? 34 : 46) * t * (0.8f + 0.35f * k)), Theme.Accent);
                            pb.SurroundColors = new[] { Color.FromArgb(0, Theme.Accent) };
                            pb.FocusScales = new PointF(0.25f, 0.25f);
                            g.FillPath(pb, gp);
                        }
                    }
                }
                // расходящееся кольцо в момент включения
                if (ripple >= 0)
                {
                    float e1 = 1 - (float)Math.Pow(1 - ripple, 3);
                    float rr = size / 2f + e1 * Math.Min(cyc - size / 2f - 1, 46 * S) + 2 * S;
                    int alpha = (int)(170 * (1 - ripple));
                    using (var pen = new Pen(Color.FromArgb(alpha, Theme.Accent), (2.5f - 1.5f * ripple) * S))
                        g.DrawEllipse(pen, cxc - rr, cyc - rr, rr * 2, rr * 2);
                }
                // подписи под кругом
                var flags = TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
                float ty = r.Bottom + 12 * S + (down ? 2 * S : 0);
                Color tc = TitleColor == Color.Empty ? Theme.Text : TitleColor;
                // без заливки фона: текст ложится поверх пятна света, а не прямоугольником
                TextRenderer.DrawText(g, Title, Theme.Status, new Rectangle(0, (int)ty, Width, Theme.P(30)), tc, flags);
                TextRenderer.DrawText(g, Subtitle, Theme.Small, new Rectangle(Theme.P(20), (int)ty + Theme.P(31), Width - Theme.P(40), Theme.P(20)), Theme.Muted, flags);
            }
            else if (t > 0.01f)
            {
                // свечение (при «дыхании» плавно меняются яркость и радиус)
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
            using (var pen = new Pen(glyph, Math.Max(2.5f, r.Width / 30f)) { StartCap = LineCap.Round, EndCap = LineCap.Round })
            {
                g.DrawArc(pen, cx - ir, cy - ir + r.Width * 0.022f, ir * 2, ir * 2, -55, 290);
                g.DrawLine(pen, cx, cy - ir - r.Width * 0.03f, cx, cy - ir * 0.15f);
            }

            if (busy && shownProgress >= 0)
            {
                // кольцо прогресса подбора: дорожка + заполненная часть
                float w = 4f * S;
                var pr = RectangleF.Inflate(r, 7 * S, 7 * S);
                using (var track = new Pen(Theme.Line, w)) g.DrawEllipse(track, pr);
                float sweep = Math.Max(2f, 360f * Math.Min(1f, shownProgress));
                using (var pen = new Pen(Theme.Accent, w) { StartCap = LineCap.Round, EndCap = LineCap.Round })
                    g.DrawArc(pen, pr, -90, sweep);
            }
            else if (busy)
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
        public string Icon;   // имя значка из Icons (null — без значка)
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
            int tx = Theme.P(14);
            if (Icon != null)
            {
                float isz = 18 * S;
                Icons.Draw(g, Icon, new RectangleF(14 * S, (Height - isz) / 2f, isz, isz), Theme.Lerp(Theme.Muted, Theme.Text, h * 0.6f));
                tx = Theme.P(44);
            }
            if (Hint == "")
                TextRenderer.DrawText(g, Title, Theme.Body, new Rectangle(tx, 0, Width, Height), titleColor, bg,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            else
            {
                int top = (Height - Theme.P(36)) / 2;   // две строки (20 + 16) по центру строки любой высоты
                TextRenderer.DrawText(g, Title, Theme.Body, new Rectangle(tx, top, Width, Theme.P(20)), titleColor, bg,
                    TextFormatFlags.Left | TextFormatFlags.NoPadding);
                // значение справа стоит на уровне названия, поэтому подпись может занимать всю ширину до стрелки
                int hintW = IsSwitch ? Width - tx - Theme.P(76) : Width - tx - Theme.P(36);
                TextRenderer.DrawText(g, Hint, Theme.Caption, new Rectangle(tx, top + Theme.P(20), hintW, Theme.P(16)), Theme.Muted, bg,
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
                if (Hint != "" && value != "") cy = (Height - Theme.P(36)) / 2 + Theme.P(10);   // стрелка рядом со значением
                using (var pen = new Pen(Theme.Muted, 1.6f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
                {
                    if (Nav) g.DrawLines(pen, new[] { new PointF(cx - d / 2, cy - d), new PointF(cx + d / 2, cy), new PointF(cx - d / 2, cy + d) });
                    else g.DrawLines(pen, new[] { new PointF(cx - d, cy - d / 2), new PointF(cx, cy + d / 2), new PointF(cx + d, cy - d / 2) });
                }
                // значение справа не заходит на название: начинается не левее конца заголовка + отступ
                int titleW = TextRenderer.MeasureText(g, Title, Theme.Body, Size.Empty, TextFormatFlags.NoPadding).Width;
                int left = tx + titleW + Theme.P(16);
                int right = (int)(cx - 12 * S);
                // со подписью — на строке названия, без подписи — по центру строки
                var vr = Hint == "" ? new Rectangle(left, 0, right - left, Height)
                                    : new Rectangle(left, (Height - Theme.P(36)) / 2, right - left, Theme.P(20));
                if (right - left > Theme.P(10))
                    TextRenderer.DrawText(g, value, Theme.Body, vr, Theme.Muted, bg,
                        TextFormatFlags.Right | (Hint == "" ? TextFormatFlags.VerticalCenter : TextFormatFlags.Top) | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
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
