// Мини-панель у значка в трее (как быстрые настройки Windows 11) и кнопка с акцентной заливкой.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZapretGui
{
    // Кнопка с заливкой цветом акцента.
    class AccentButton : PaintedControl
    {
        bool hover, down;
        public AccentButton(string text) { Text = text; Cursor = Cursors.Hand; }
        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            Color c = Theme.Accent;
            if (hover) c = Theme.Lerp(c, Theme.IsLight ? Color.Black : Color.White, 0.08f);
            if (down) c = Theme.Lerp(c, Color.Black, 0.12f);
            Theme.FillRound(g, c, new RectangleF(0, 0, Width - 1, Height - 1), Height / 2f);
            TextRenderer.DrawText(g, Text, Theme.BodyStrong, ClientRectangle, Theme.IsLight ? Color.White : Theme.Bg, c,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }
    }

    class TrayFlyout : Form
    {
        public readonly PowerButton Power;
        public readonly Label Status, Sub;
        public readonly StatusDots Dots;
        public readonly StrategyPicker Picker;
        public DateTime ClosedAt = DateTime.MinValue;
        public bool MenuOpen;   // открыто меню стратегий — не закрываться при потере фокуса

        public TrayFlyout(MainForm owner, Icon icon)
        {
            Func<float, int> P = Theme.P;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            StartPosition = FormStartPosition.Manual;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
            Icon = icon;
            int W = P(300);
            ClientSize = new Size(W, P(336));
            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) Close(); };

            var logo = new PictureBox { Image = Theme.Logo(P(20), Theme.Accent, Theme.Lerp(Theme.Accent, Color.Black, 0.28f), Color.Empty),
                SizeMode = PictureBoxSizeMode.AutoSize, Location = new Point(P(16), P(14)), BackColor = Theme.Bg };
            var title = new Label { Text = "Zapret", Font = Theme.BodyStrong, AutoSize = true, Location = new Point(P(42), P(14)), BackColor = Theme.Bg };
            var open = new FlatLink(L.T("Открыть", "Open")) { BackColor = Theme.Bg };
            open.Click += delegate { Close(); owner.ShowFromTray(); };
            Controls.AddRange(new Control[] { logo, title, open });
            open.Location = new Point(W - P(16) - open.PreferredSize.Width, P(17));

            Power = new PowerButton { Bounds = new Rectangle((W - P(132)) / 2, P(40), P(132), P(132)) };
            Power.Click += delegate { owner.TogglePower(); };
            Status = new Label { Font = Theme.Status, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false, Bounds = new Rectangle(0, P(172), W, P(30)), BackColor = Theme.Bg };
            Sub = new Label { Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false,
                Bounds = new Rectangle(P(12), P(202), W - P(24), P(18)), AutoEllipsis = true, BackColor = Theme.Bg };
            Dots = new StatusDots { Bounds = new Rectangle(P(30), P(222), W - P(60), P(22)) };
            Dots.Click += delegate { owner.CheckNet(); };
            Picker = new StrategyPicker { Bounds = new Rectangle(P(14), P(256), W - P(28), P(66)) };
            Picker.Prev += delegate { owner.StepStrategy(-1); };
            Picker.Next += delegate { owner.StepStrategy(1); };
            Picker.Open += delegate { owner.ShowStrategyMenu(Picker); };
            Controls.AddRange(new Control[] { Power, Status, Sub, Dots, Picker });
        }

        // Ставит панель над значком в трее (или у края экрана, если панель задач сбоку/сверху).
        public void PlaceNearCursor()
        {
            Point c = Cursor.Position;
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int m = Theme.P(12);
            int x = Math.Max(wa.Left + m, Math.Min(c.X - Width / 2, wa.Right - Width - m));
            int y = c.Y > wa.Top + wa.Height / 2 ? wa.Bottom - Height - m : wa.Top + m;
            if (c.X < wa.Left || c.X > wa.Right) { x = c.X < wa.Left ? wa.Left + m : wa.Right - Width - m; y = Math.Max(wa.Top + m, Math.Min(c.Y - Height / 2, wa.Bottom - Height - m)); }
            Location = new Point(x, y);
        }

        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ClassStyle |= 0x20000; /* CS_DROPSHADOW */ return cp; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int round = 2; DwmSetWindowAttribute(Handle, 33, ref round, 4); } catch { } // скруглённые углы Windows 11
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var pen = new Pen(Theme.Line)) e.Graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // Клик мимо панели — закрыть.
        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            BeginInvoke((MethodInvoker)delegate { if (!IsDisposed && !MenuOpen && !ContainsFocus && Form.ActiveForm != this) Close(); });
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            ClosedAt = DateTime.Now;
            base.OnFormClosed(e);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
