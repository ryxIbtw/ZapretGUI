// Диагностика (перенесена из service.bat → Run Diagnostics) и окно с результатами.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZapretGui
{
    // Строка результата диагностики.
    class DiagRow : PaintedControl
    {
        readonly DiagItem item;
        bool hoverFix;
        public event EventHandler FixClicked;

        public DiagRow(DiagItem it)
        {
            item = it;
            BackColor = Theme.Surface;
        }

        Rectangle FixRect
        {
            get
            {
                if (item.Fix == null) return Rectangle.Empty;
                int w = TextRenderer.MeasureText(item.FixText, Theme.Small).Width + Theme.P(16);
                return new Rectangle(Width - Theme.P(12) - w, (Height - Theme.P(28)) / 2, w, Theme.P(28));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = FixRect.Contains(e.Location);
            if (h != hoverFix) { hoverFix = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverFix = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (FixRect.Contains(e.Location) && FixClicked != null) FixClicked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);

            Color c = item.Level == 0 ? Theme.Accent : item.Level == 1 ? Theme.Warn : Theme.Danger;
            float d = 18 * S, cx = 14 * S + d / 2, cy = Height / 2f;
            using (var b = new SolidBrush(Color.FromArgb(Theme.IsLight ? 40 : 50, c))) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            using (var pen = new Pen(c, 1.8f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float r = 4 * S;
                if (item.Level == 0) g.DrawLines(pen, new[] { new PointF(cx - r, cy), new PointF(cx - r / 3, cy + r * 0.7f), new PointF(cx + r, cy - r * 0.6f) });
                else if (item.Level == 1) { g.DrawLine(pen, cx, cy - r, cx, cy + r * 0.2f); g.DrawLine(pen, cx, cy + r, cx, cy + r + 0.1f); }
                else { g.DrawLine(pen, cx - r * 0.7f, cy - r * 0.7f, cx + r * 0.7f, cy + r * 0.7f); g.DrawLine(pen, cx + r * 0.7f, cy - r * 0.7f, cx - r * 0.7f, cy + r * 0.7f); }
            }

            var fr = FixRect;
            int textRight = (fr.IsEmpty ? Width - Theme.P(12) : fr.X - Theme.P(8));
            int x = Theme.P(44);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            if (item.Detail == "")
                TextRenderer.DrawText(g, item.Title, Theme.Body, new Rectangle(x, 0, textRight - x, Height), Theme.Text, Theme.Surface, flags | TextFormatFlags.VerticalCenter);
            else
            {
                TextRenderer.DrawText(g, item.Title, Theme.Body, new Rectangle(x, Theme.P(8), textRight - x, Theme.P(20)), Theme.Text, Theme.Surface, flags);
                TextRenderer.DrawText(g, item.Detail, Theme.Caption, new Rectangle(x, Theme.P(28), textRight - x, Theme.P(16)), Theme.Muted, Theme.Surface, flags);
            }

            if (!fr.IsEmpty)
            {
                Color bg = hoverFix ? Theme.Accent : Theme.AccentDeep;
                Theme.FillRound(g, bg, fr, fr.Height / 2f);
                TextRenderer.DrawText(g, item.FixText, Theme.Small, fr, hoverFix ? (Theme.IsLight ? Color.White : Theme.Bg) : Theme.Accent, bg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    class DiagForm : Form
    {
        readonly Zapret z;
        readonly Panel list;
        readonly Label summary;
        readonly FlatLink rerun, discord;
        bool running;

        public DiagForm(Zapret zapret, Icon icon)
        {
            z = zapret;
            Func<float, int> P = Theme.P;
            Text = L.T("Диагностика", "Diagnostics");
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size(P(440), P(600));

            var title = new Label { Text = L.T("Диагностика", "Diagnostics"), Font = Theme.Title, AutoSize = true, Location = new Point(P(20), P(16)), BackColor = Theme.Bg };
            summary = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Bounds = new Rectangle(P(20), P(46), P(400), P(20)), BackColor = Theme.Bg };
            list = new Panel { Bounds = new Rectangle(P(20), P(74), P(400), P(472)), AutoScroll = true, BackColor = Theme.Bg };
            rerun = new FlatLink(L.T("Проверить снова", "Run again")) { Location = new Point(P(20), P(562)) };
            rerun.Click += delegate { Start(); };
            discord = new FlatLink(L.T("Очистить кэш Discord", "Clear Discord cache"));
            discord.Click += delegate { ClearDiscord(); };
            Controls.AddRange(new Control[] { title, summary, list, rerun, discord });
            discord.Location = new Point(ClientSize.Width - P(20) - discord.PreferredSize.Width, P(562));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            MainForm.ApplyTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Start(); }

        public void Start()
        {
            if (running) return;
            running = true;
            summary.ForeColor = Theme.Muted;
            summary.Text = L.T("Проверяю…", "Checking…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DiagItem> items;
                try { items = Diag.Run(z); }
                catch (Exception ex) { items = new List<DiagItem> { new DiagItem { Level = 2, Title = ex.Message } }; }
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate { Fill(items); running = false; });
            });
        }

        void Fill(List<DiagItem> items)
        {
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
            list.Controls.Clear();
            int y = 0, w = list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Theme.P(2);
            foreach (var it in items)
            {
                var row = new DiagRow(it) { Bounds = new Rectangle(0, y, w, Theme.P(it.Detail == "" ? 40 : 52)) };
                var item = it;
                row.FixClicked += delegate
                {
                    try { item.Fix(); } catch (Exception ex) { summary.ForeColor = Theme.Danger; summary.Text = ex.Message; return; }
                    Start();
                };
                list.Controls.Add(row);
                y += row.Height + Theme.P(4);
            }
            list.ResumeLayout();
            int bad = items.Count(i => i.Level == 2), warn = items.Count(i => i.Level == 1);
            if (bad + warn == 0) { summary.ForeColor = Theme.Accent; summary.Text = L.T("Всё в порядке", "Everything looks good"); }
            else
            {
                summary.ForeColor = bad > 0 ? Theme.Danger : Theme.Warn;
                summary.Text = L.T("Проблем: " + bad + ", предупреждений: " + warn, "Problems: " + bad + ", warnings: " + warn);
            }
        }

        void ClearDiscord()
        {
            if (Ask.Show(this, L.T("Discord будет закрыт, его кэш удалён. Продолжить?", "Discord will be closed and its cache removed. Continue?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int n = Diag.ClearDiscordCache();
            summary.ForeColor = n > 0 ? Theme.Accent : Theme.Warn;
            summary.Text = n > 0 ? L.T("Кэш Discord очищен", "Discord cache cleared") : L.T("Discord не найден", "Discord not found");
        }
    }
}
