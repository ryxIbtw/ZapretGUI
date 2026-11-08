// Общие детали для дополнительных окон: оформление окна, поле ввода в цветах темы, тёмные полосы прокрутки.
using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZapretGui
{
    static class Dialogs
    {
        // Окно-диалог в цветах темы: фиксированный размер, без кнопок свернуть/развернуть.
        public static void Setup(Form f, string title, Icon icon, int w, int h)
        {
            f.Text = title;
            f.Icon = icon;
            f.FormBorderStyle = FormBorderStyle.FixedSingle;
            f.MaximizeBox = false;
            f.MinimizeBox = false;
            f.ShowInTaskbar = false;
            f.StartPosition = FormStartPosition.CenterParent;
            f.AutoScaleMode = AutoScaleMode.None;
            f.BackColor = Theme.Bg;
            f.ForeColor = Theme.Text;
            f.Font = Theme.Body;
            f.ClientSize = new Size(w, h);
            f.KeyPreview = true;
            f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) f.Close(); };
            f.HandleCreated += delegate { MainForm.ApplyTitleBar(f.Handle); };
        }

        // Полосы прокрутки в тёмной теме тоже тёмные (Windows 10 1809+).
        public static void DarkScroll(Control c)
        {
            EventHandler apply = delegate { try { if (!Theme.IsLight) SetWindowTheme(c.Handle, "DarkMode_Explorer", null); } catch { } };
            if (c.IsHandleCreated) apply(c, EventArgs.Empty);
            c.HandleCreated += apply;
        }

        // Серый текст-подсказка в пустом поле ввода.
        public static void Cue(TextBox tb, string text)
        {
            EventHandler apply = delegate { SendMessage(tb.Handle, 0x1501, (IntPtr)1, text); };
            if (tb.IsHandleCreated) apply(tb, EventArgs.Empty);
            tb.HandleCreated += apply;
        }

        public static void Copy(string text)
        {
            try { Clipboard.SetText(text); } catch { }
        }

        [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
        static extern int SetWindowTheme(IntPtr hwnd, string app, string idList);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, string l);
    }

    // Вопросы и предупреждения в стиле программы вместо стандартных серых окон Windows.
    // Те же аргументы, что у MessageBox.Show, и тот же результат.
    static class Ask
    {
        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons, MessageBoxIcon icon)
        {
            Func<float, int> P = Theme.P;
            using (var f = new Form())
            {
                Dialogs.Setup(f, title, null, P(420), P(200));
                f.ShowIcon = false;
                if (owner == null) f.StartPosition = FormStartPosition.CenterScreen;

                int textX = P(76), textW = P(420) - textX - P(24);
                var mark = new MarkIcon(icon) { Bounds = new Rectangle(P(24), P(24), P(36), P(36)) };
                Size ts = TextRenderer.MeasureText(text, Theme.Body, new Size(textW, 0), TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                var lbl = new Label { Text = text, Font = Theme.Body, ForeColor = Theme.Text, BackColor = Theme.Bg, AutoSize = false,
                    Bounds = new Rectangle(textX, P(26), textW, ts.Height + P(6)), UseMnemonic = false };
                f.Controls.AddRange(new Control[] { mark, lbl });

                // кнопки справа налево: главная (акцентная) — крайняя правая
                var defs = new System.Collections.Generic.List<Tuple<string, DialogResult>>();
                switch (buttons)
                {
                    case MessageBoxButtons.YesNo:
                        defs.Add(Tuple.Create(L.T("Да", "Yes"), DialogResult.Yes)); defs.Add(Tuple.Create(L.T("Нет", "No"), DialogResult.No)); break;
                    case MessageBoxButtons.YesNoCancel:
                        defs.Add(Tuple.Create(L.T("Да", "Yes"), DialogResult.Yes)); defs.Add(Tuple.Create(L.T("Нет", "No"), DialogResult.No));
                        defs.Add(Tuple.Create(L.T("Отмена", "Cancel"), DialogResult.Cancel)); break;
                    case MessageBoxButtons.OKCancel:
                        defs.Add(Tuple.Create("OK", DialogResult.OK)); defs.Add(Tuple.Create(L.T("Отмена", "Cancel"), DialogResult.Cancel)); break;
                    default:
                        defs.Add(Tuple.Create("OK", DialogResult.OK)); break;
                }
                int by = Math.Max(lbl.Bottom, mark.Bottom) + P(22), bh = P(36), x = P(420) - P(24);
                for (int i = 0; i < defs.Count; i++)
                {
                    var d = defs[i];
                    int bw = Math.Max(P(96), TextRenderer.MeasureText(d.Item1, Theme.BodyStrong).Width + P(36));
                    x -= bw;
                    var b = new AccentButton(d.Item1) { Secondary = i > 0, Bounds = new Rectangle(x, by, bw, bh) };
                    DialogResult res = d.Item2;
                    b.Click += delegate { f.DialogResult = res; };
                    f.Controls.Add(b);
                    x -= P(10);
                }
                f.ClientSize = new Size(P(420), by + bh + P(20));
                DialogResult primary = defs[0].Item2;
                f.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.Handled = true; f.DialogResult = primary; } };

                DialogResult r = owner != null ? f.ShowDialog(owner) : f.ShowDialog();
                // Esc / крестик: «Нет» для вопроса да/нет, «Отмена» для остальных
                if (r == DialogResult.Cancel && buttons == MessageBoxButtons.YesNo) return DialogResult.No;
                if (r == DialogResult.Cancel && buttons == MessageBoxButtons.OK) return DialogResult.OK;
                return r;
            }
        }

        public static DialogResult Show(IWin32Window owner, string text, string title, MessageBoxButtons buttons)
        {
            return Show(owner, text, title, buttons, MessageBoxIcon.None);
        }

        // Значок вопроса/предупреждения: цветной кружок с символом.
        class MarkIcon : PaintedControl
        {
            readonly MessageBoxIcon kind;
            public MarkIcon(MessageBoxIcon k) { kind = k; }
            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Theme.Bg);
                Color c = kind == MessageBoxIcon.Warning ? Theme.Warn : kind == MessageBoxIcon.Error ? Theme.Danger : Theme.Accent;
                var r = new RectangleF(0, 0, Width - 1, Height - 1);
                using (var b = new SolidBrush(Color.FromArgb(Theme.IsLight ? 34 : 46, c))) g.FillEllipse(b, r);
                string s = kind == MessageBoxIcon.Warning ? "!" : kind == MessageBoxIcon.Error ? "×" : kind == MessageBoxIcon.Question ? "?" : "i";
                using (var font = new Font(Theme.Title.FontFamily, Theme.Title.Size * 1.05f, FontStyle.Bold))
                    TextRenderer.DrawText(g, s, font, ClientRectangle, c, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    // Поле ввода со скруглённой подложкой (обычный TextBox без рамки внутри).
    class TextField : Control
    {
        public readonly TextBox Box;
        bool focused;

        public TextField(bool multiline)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Box = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                Multiline = multiline,
            };
            if (multiline) { Box.ScrollBars = ScrollBars.Vertical; Box.WordWrap = false; Dialogs.DarkScroll(Box); }
            Box.GotFocus += delegate { focused = true; Invalidate(); };
            Box.LostFocus += delegate { focused = false; Invalidate(); };
            Controls.Add(Box);
            Cursor = Cursors.IBeam;
        }

        protected override void OnClick(EventArgs e) { Box.Focus(); base.OnClick(e); }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            int px = Theme.P(12);
            if (Box.Multiline) Box.Bounds = new Rectangle(px, Theme.P(8), Width - px - Theme.P(4), Height - Theme.P(14));
            else Box.Bounds = new Rectangle(px, (Height - Box.PreferredHeight) / 2, Width - px * 2, Box.PreferredHeight);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg);
            var r = new RectangleF(0.5f, 0.5f, Width - 2, Height - 2);
            Theme.FillRound(g, Theme.Surface, r, 10 * Theme.S);
            using (var path = Round(r, 10 * Theme.S))
            using (var pen = new Pen(focused ? Theme.Accent : Theme.Line, focused ? 1.5f * Theme.S : 1f))
                g.DrawPath(pen, path);
        }

        static GraphicsPath Round(RectangleF r, float rad)
        {
            var p = new GraphicsPath();
            float d = rad * 2;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}
