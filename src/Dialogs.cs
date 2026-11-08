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
