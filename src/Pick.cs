// Подбор стратегии: результат проверки одной стратегии и окно с таблицей результатов.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ZapretGui
{
    class PickResult
    {
        public string Name;
        public bool Failed;              // стратегия не запустилась
        public int YtOk, DcOk, Rounds, Samples;
        public long TotalMs;
        public double SpeedKBps;

        public const int PlannedRounds = 3;
        public int YtMax { get { return PlannedRounds * NetCheck.YouTube.Length; } }
        public int DcMax { get { return PlannedRounds * NetCheck.Discord.Length; } }
        // Доля успешных проверок; раунды, пропущенные после пустого первого, считаются проваленными.
        public double Ratio { get { return Failed ? -1 : (YtOk + DcOk) / (double)(PlannedRounds * NetCheck.Max); } }
        public double AvgMs { get { return Samples > 0 ? TotalMs / (double)Samples : double.MaxValue; } }
        public bool Works { get { return !Failed && YtOk + DcOk > 0; } }

        // Сначала стабильность (доля успешных проверок), затем скорость, затем время ответа.
        public static List<PickResult> Rank(IEnumerable<PickResult> list)
        {
            return list.OrderByDescending(r => Math.Round(r.Ratio, 3))
                       .ThenByDescending(r => r.SpeedKBps)
                       .ThenBy(r => r.AvgMs)
                       .ToList();
        }

        public static string FormatSpeed(double kbps)
        {
            if (kbps <= 0) return "—";
            if (kbps >= 1024) return (kbps / 1024).ToString("0.0") + L.T(" МБ/с", " MB/s");
            return ((int)kbps) + L.T(" КБ/с", " KB/s");
        }
    }

    // Строка таблицы результатов.
    class PickRow : PaintedControl
    {
        public readonly PickResult R;
        readonly int rank;
        bool hover;
        public bool Selected;
        public event EventHandler Picked, Applied;

        public PickRow(PickResult r, int rank)
        {
            R = r; this.rank = rank;
            BackColor = Theme.Bg;
            Cursor = r.Works ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e) { if (R.Works && Picked != null) Picked(this, EventArgs.Empty); }
        protected override void OnMouseDoubleClick(MouseEventArgs e) { if (R.Works && Applied != null) Applied(this, EventArgs.Empty); }

        // Колонки: №, стратегия, YouTube, Discord, отклик, скорость.
        public static readonly float[] Cols = { 12, 40, 252, 322, 392, 456 };

        static Color RatioColor(int ok, int max)
        {
            if (ok >= max) return Theme.Accent;
            if (ok > 0) return Theme.Warn;
            return Theme.Danger;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg);
            Color bg = Selected ? Theme.AccentDeep : (hover && R.Works ? Theme.SurfaceHover : Theme.Surface);
            Theme.FillRound(g, bg, new RectangleF(0, 0, Width - 1, Height - 1), 10 * S);
            if (Selected)
                using (var pen = new Pen(Color.FromArgb(110, Theme.Accent), 1f))
                using (var path = Theme.Round(new RectangleF(0.5f, 0.5f, Width - 2, Height - 2), 10 * S)) g.DrawPath(pen, path);

            var f = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            Func<int, int, Rectangle> col = (i, w) => new Rectangle(Theme.P(Cols[i]), 0, Theme.P(w), Height);
            TextRenderer.DrawText(g, rank.ToString(), Theme.Small, col(0, 24), Theme.Dim, bg, f);
            TextRenderer.DrawText(g, R.Name, R.Works && rank == 1 ? Theme.BodyStrong : Theme.Body, col(1, 206), R.Works ? Theme.Text : Theme.Dim, bg, f);
            if (R.Failed)
            {
                TextRenderer.DrawText(g, L.T("не запустилась", "failed to start"), Theme.Small, col(2, 200), Theme.Dim, bg, f);
                return;
            }
            TextRenderer.DrawText(g, R.YtOk + "/" + R.YtMax, Theme.Body, col(2, 60), RatioColor(R.YtOk, R.YtMax), bg, f);
            TextRenderer.DrawText(g, R.DcOk + "/" + R.DcMax, Theme.Body, col(3, 60), RatioColor(R.DcOk, R.DcMax), bg, f);
            TextRenderer.DrawText(g, R.Samples > 0 ? ((int)R.AvgMs) + L.T(" мс", " ms") : "—", Theme.Small, col(4, 60), Theme.Muted, bg, f);
            TextRenderer.DrawText(g, PickResult.FormatSpeed(R.SpeedKBps), Theme.Small, col(5, 76), Theme.Muted, bg, f);
        }
    }

    class PickForm : Form
    {
        public string Chosen { get; private set; }
        readonly List<PickRow> rows = new List<PickRow>();
        readonly Button apply;
        readonly Label summary;

        public PickForm(List<PickResult> ranked, Icon icon, bool partial)
        {
            Func<float, int> P = Theme.P;
            Text = L.T("Результаты подбора", "Strategy test results");
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false; MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg; ForeColor = Theme.Text; Font = Theme.Body;
            ClientSize = new Size(P(580), P(620));

            var title = new Label { Text = Text, Font = Theme.Title, AutoSize = true, Location = new Point(P(20), P(16)), BackColor = Theme.Bg };
            summary = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Bounds = new Rectangle(P(20), P(46), P(540), P(36)), BackColor = Theme.Bg };
            summary.Text = (partial ? L.T("Подбор прерван, показаны проверенные стратегии. ", "Testing was stopped; showing tested strategies. ") : "") +
                L.T("Лучшая по замерам выделена. Можно выбрать другую и нажать «Применить» (или дважды щёлкнуть).",
                    "The best by measurements is selected. Pick another and click Apply (or double-click).");

            // заголовки колонок
            var head = new Panel { Bounds = new Rectangle(P(20), P(88), P(540), P(20)), BackColor = Theme.Bg };
            string[] names = { "#", L.T("Стратегия", "Strategy"), "YouTube", "Discord", L.T("Отклик", "Ping"), L.T("Скорость", "Speed") };
            for (int i = 0; i < names.Length; i++)
                head.Controls.Add(new Label { Text = names[i], Font = Theme.Caption, ForeColor = Theme.Dim, BackColor = Theme.Bg, AutoSize = true, Location = new Point(P(PickRow.Cols[i]), P(2)) });

            var list = new Panel { Bounds = new Rectangle(P(20), P(110), P(540), P(442)), AutoScroll = true, BackColor = Theme.Bg };
            int y = 0, w = list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - P(2);
            for (int i = 0; i < ranked.Count; i++)
            {
                var row = new PickRow(ranked[i], i + 1) { Bounds = new Rectangle(0, y, w, P(38)) };
                row.Picked += delegate { Choose(row); };
                row.Applied += delegate { Choose(row); DialogResult = DialogResult.OK; Close(); };
                rows.Add(row);
                list.Controls.Add(row);
                y += P(42);
            }

            apply = new Button { Text = L.T("Применить", "Apply"), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent,
                ForeColor = Theme.IsLight ? Color.White : Theme.Bg, Bounds = new Rectangle(P(440), P(570), P(120), P(34)) };
            apply.FlatAppearance.BorderSize = 0;
            var cancel = new Button { Text = L.T("Отмена", "Cancel"), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface,
                ForeColor = Theme.Text, Bounds = new Rectangle(P(310), P(570), P(120), P(34)) };
            cancel.FlatAppearance.BorderSize = 0;
            var note = new Label { Text = L.T("«Отмена» вернёт то, что работало до подбора", "Cancel restores what was running before"), Font = Theme.Caption,
                ForeColor = Theme.Dim, BackColor = Theme.Bg, AutoSize = false, Bounds = new Rectangle(P(20), P(570), P(280), P(34)), TextAlign = ContentAlignment.MiddleLeft };

            Controls.AddRange(new Control[] { title, summary, head, list, note, cancel, apply });
            AcceptButton = apply; CancelButton = cancel;

            var first = rows.FirstOrDefault(r => r.R.Works);
            if (first != null) Choose(first); else apply.Enabled = false;
        }

        void Choose(PickRow row)
        {
            foreach (var r in rows) { r.Selected = r == row; r.Invalidate(); }
            Chosen = row.R.Name;
            apply.Enabled = true;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            MainForm.ApplyTitleBar(Handle);
        }
    }
}
