// Окно «Статистика»: когда YouTube и Discord работали, на какой стратегии, и сводка по стратегиям.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace ZapretGui
{
    // Полоса из отрезков (сервис или стратегия) с подсказкой при наведении.
    class StatBar : FrameworkElement
    {
        List<StatSeg> segs = new List<StatSeg>();
        List<DateTime> dividers = new List<DateTime>();
        DateTime from, to;
        bool strategyBand, withDate;
        readonly Dictionary<string, int> strategyIndex = new Dictionary<string, int>();
        readonly ToolTip tip = new ToolTip();

        public StatBar(bool strategy)
        {
            strategyBand = strategy;
            Height = 20;
            tip.PlacementTarget = this;
            tip.Placement = PlacementMode.Relative;
            MouseMove += (s, e) => Hover(e.GetPosition(this).X);
            MouseLeave += delegate { tip.IsOpen = false; };
        }

        public void Set(List<StatSeg> s, DateTime f, DateTime t, List<DateTime> div, bool date)
        {
            segs = s; from = f; to = t; dividers = div; withDate = date;
            strategyIndex.Clear();
            foreach (var x in segs)
                if (x.Kind == 3 && !strategyIndex.ContainsKey(x.Strategy)) strategyIndex[x.Strategy] = strategyIndex.Count;
            InvalidateVisual();
        }

        double X(DateTime t) { return (t - from).TotalSeconds / Math.Max(1, (to - from).TotalSeconds) * ActualWidth; }

        Brush BrushOf(StatSeg s)
        {
            if (s.Kind == 1 || s.Kind == 2) return W.Knob;
            if (s.Kind == 4) return W.Warn;
            if (s.Kind == 5) return W.Danger;
            if (!strategyBand) return W.Accent;
            int i;
            strategyIndex.TryGetValue(s.Strategy, out i);
            return new SolidColorBrush(W.A(W.Accent.Color, i % 2 == 0 ? 0.42 : 0.22));
        }

        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth, h = ActualHeight;
            if (w <= 0) return;
            var rect = new Rect(0, 0, w, h);
            dc.PushClip(new RectangleGeometry(rect, 5, 5));
            dc.DrawRectangle(W.Bg, null, rect);
            foreach (var s in segs)
            {
                if (s.Kind == 0 || (strategyBand && s.Kind != 3)) continue;
                double x0 = X(s.From), x1 = X(s.To);
                if (x1 - x0 < 1) x1 = x0 + 1;
                dc.DrawRectangle(BrushOf(s), null, new Rect(x0, 0, x1 - x0, h));
                if (strategyBand && x1 - x0 > 30)
                {
                    var ft = Text(s.Strategy, W.Text);
                    ft.MaxTextWidth = x1 - x0 - 8; ft.MaxLineCount = 1; ft.Trimming = TextTrimming.CharacterEllipsis;
                    dc.DrawText(ft, new Point(x0 + 5, (h - ft.Height) / 2));
                }
            }
            var pen = new Pen(W.Surface, 1);
            foreach (var d in dividers) { double x = Math.Round(X(d)) + 0.5; dc.DrawLine(pen, new Point(x, 0), new Point(x, h)); }
            dc.Pop();
        }

        internal static FormattedText Text(string s, Brush b)
        {
#pragma warning disable 618
            return new FormattedText(s ?? "", CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(W.SmallFont, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal), W.Caption, b);
#pragma warning restore 618
        }

        void Hover(double x)
        {
            StatSeg hit = null;
            foreach (var s in segs)
            {
                if (s.Kind == 0 || (strategyBand && s.Kind != 3)) continue;
                double x0 = X(s.From), x1 = Math.Max(X(s.To), x0 + 1);
                if (x >= x0 - 1 && x <= x1 + 1) { hit = s; break; }
            }
            if (hit == null) { tip.IsOpen = false; return; }
            string fmt = withDate ? "dd.MM HH:mm" : "HH:mm";
            string when = hit.From.ToString(fmt) + " – " + hit.To.ToString(withDate && hit.To.Date == hit.From.Date ? "HH:mm" : fmt);
            string what;
            if (strategyBand) what = hit.Strategy;
            else
            {
                what = hit.Kind == 1 ? L.T("обход выключен", "bypass off") : hit.Kind == 2 ? L.T("нет интернета", "no internet") :
                       hit.Kind == 3 ? L.T("работает", "working") : hit.Kind == 4 ? L.T("работает частично", "partly working") : L.T("не открывается", "unreachable");
                if (hit.AvgMs >= 0) what += "  ·  " + hit.AvgMs + L.T(" мс", " ms");
            }
            tip.Content = when + "\n" + what;
            tip.HorizontalOffset = x + 12;
            tip.VerticalOffset = ActualHeight + 4;
            if (!tip.IsOpen) tip.IsOpen = true;
        }
    }

    // Подписи времени под полосами.
    class StatAxis : FrameworkElement
    {
        List<Tuple<DateTime, string>> ticks = new List<Tuple<DateTime, string>>();
        DateTime from, to;
        public StatAxis() { Height = 16; }
        public void Set(List<Tuple<DateTime, string>> t, DateTime f, DateTime e) { ticks = t; from = f; to = e; InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            double w = ActualWidth;
            foreach (var t in ticks)
            {
                var ft = StatBar.Text(t.Item2, W.Dim);
                double x = (t.Item1 - from).TotalSeconds / Math.Max(1, (to - from).TotalSeconds) * w - ft.Width / 2;
                x = Math.Max(0, Math.Min(w - ft.Width, x));
                dc.DrawText(ft, new Point(x, 2));
            }
        }
    }

    // Переключатель «Сегодня / Неделя / Месяц».
    class RangeTab : Clickable
    {
        readonly TextBlock tb;
        bool on;
        public RangeTab(string text)
        {
            CornerRadius = new CornerRadius(8);
            Padding = new Thickness(12, 4, 12, 5);
            tb = W.T(text, W.Small, W.Muted);
            Child = tb;
        }
        public bool On { get { return on; } set { on = value; Background = on ? (Brush)W.Knob : Brushes.Transparent; tb.Foreground = on ? W.Text : W.Muted; } }
        protected override void OnHot(bool h) { if (!on) tb.Foreground = h ? W.Text : W.Muted; }
    }

    class StatsW : WinBase
    {
        readonly Func<DateTime, List<StatRec>> load;
        readonly RangeTab[] tabs;
        readonly StatBar barYt = new StatBar(false), barDc = new StatBar(false), barStrat = new StatBar(true);
        readonly StatAxis axis = new StatAxis();
        readonly TextBlock tYt, tDc, tOut, tOn, empty;
        readonly Grid table = new Grid();
        readonly StackPanel tableBox = new StackPanel();
        readonly Action repaint;

        public StatsW(Window owner, Func<DateTime, List<StatRec>> loader) : base(L.T("Статистика", "Statistics"), owner)
        {
            load = loader;
            var sp = new StackPanel { Width = 600, Margin = new Thickness(20, 16, 20, 16) };

            var head = new Grid();
            head.Children.Add(W.T(Title, W.Title, W.Text, true));
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            tabs = new[] { new RangeTab(L.T("Сегодня", "Today")), new RangeTab(L.T("Неделя", "Week")), new RangeTab(L.T("Месяц", "Month")) };
            for (int i = 0; i < tabs.Length; i++)
            {
                int idx = i;
                tabs[i].Click += delegate { Select(idx); };
                tabs[i].Margin = new Thickness(4, 0, 0, 0);
                tabRow.Children.Add(tabs[i]);
            }
            head.Children.Add(tabRow);
            sp.Children.Add(head);
            var sub = W.T(L.T("Проверка раз в минуту, пока программа запущена (в окне или в трее). Наведите на полосу, чтобы увидеть подробности.",
                "Checked every minute while the app is running (window or tray). Hover a bar for details."), W.Small, W.Muted);
            sub.TextWrapping = TextWrapping.Wrap; sub.Margin = new Thickness(0, 4, 0, 12);
            sp.Children.Add(sub);

            // полосы
            var card = new Border { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12, 14, 10) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            var labels = new[] { "YouTube", "Discord", L.T("Стратегия", "Strategy"), "" };
            var cells = new FrameworkElement[] { barYt, barDc, barStrat, axis };
            for (int i = 0; i < 4; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var lb = W.T(labels[i], W.Body, i < 2 ? W.Text : W.Muted);
                lb.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetRow(lb, i); g.Children.Add(lb);
                cells[i].Margin = new Thickness(0, i == 3 ? 4 : 0, 0, i < 2 ? 7 : 0);
                Grid.SetRow(cells[i], i); Grid.SetColumn(cells[i], 1); g.Children.Add(cells[i]);
            }
            card.Child = g;
            sp.Children.Add(card);

            // легенда
            var legend = new WrapPanel { Margin = new Thickness(2, 10, 0, 14) };
            Action<Brush, string> sw = (b, t) =>
            {
                var it = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 16, 0) };
                it.Children.Add(new Border { Width = 10, Height = 10, CornerRadius = new CornerRadius(3), Background = b, VerticalAlignment = VerticalAlignment.Center,
                    BorderBrush = b == W.Bg ? W.Line : null, BorderThickness = new Thickness(b == W.Bg ? 1 : 0) });
                var tb = W.T(t, W.Small, W.Muted); tb.Margin = new Thickness(6, 0, 0, 0);
                it.Children.Add(tb);
                legend.Children.Add(it);
            };
            sw(W.Accent, L.T("работает", "working"));
            sw(W.Warn, L.T("частично", "partly"));
            sw(W.Danger, L.T("не открывается", "unreachable"));
            sw(W.Knob, L.T("обход выключен, нет сети", "bypass off, no internet"));
            sw(W.Bg, L.T("нет данных", "no data"));
            sp.Children.Add(legend);

            // плитки
            var tiles = new UniformGrid { Columns = 4 };
            Func<string, TextBlock> tile = cap =>
            {
                var b = new Border { Background = W.Surface, CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 10), Margin = new Thickness(0, 0, 8, 0) };
                var st = new StackPanel();
                st.Children.Add(W.T(cap, W.Small, W.Muted));
                var v = W.T("—", W.Status, W.Text, true); v.Margin = new Thickness(0, 2, 0, 0);
                st.Children.Add(v);
                b.Child = st;
                tiles.Children.Add(b);
                return v;
            };
            tYt = tile(L.T("YouTube работал", "YouTube up"));
            tDc = tile(L.T("Discord работал", "Discord up"));
            tOut = tile(L.T("Сбоев", "Outages"));
            tOn = tile(L.T("Обход включён", "Bypass on"));
            ((Border)tiles.Children[3]).Margin = new Thickness(0);
            sp.Children.Add(tiles);

            // по стратегиям
            var th = W.T(L.T("По стратегиям", "By strategy"), W.BodyStrong, W.Text, true); th.Margin = new Thickness(0, 16, 0, 6);
            tableBox.Children.Add(th);
            var tcard = new Border { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 8, 14, 8), Child = table };
            tableBox.Children.Add(tcard);
            sp.Children.Add(tableBox);
            empty = W.T("", W.Small, W.Muted);
            empty.TextWrapping = TextWrapping.Wrap; empty.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(empty);

            var foot = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            var open = new FlatLink(L.T("Открыть файл", "Open file")) { HorizontalAlignment = HorizontalAlignment.Left };
            open.Click += delegate
            {
                try { if (File.Exists(Stats.FilePath)) Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Stats.FilePath + "\"") { UseShellExecute = true }); }
                catch { }
            };
            foot.Children.Add(open);
            var keep = W.T(L.T("Хранится " + Stats.KeepDays + " дней", "Kept for " + Stats.KeepDays + " days"), W.Small, W.Dim);
            keep.HorizontalAlignment = HorizontalAlignment.Right;
            foot.Children.Add(keep);
            sp.Children.Add(foot);

            Root.Child = sp;
            repaint = () => { barYt.InvalidateVisual(); barDc.InvalidateVisual(); barStrat.InvalidateVisual(); axis.InvalidateVisual(); };
            W.Changed += repaint;
            Closed += delegate { W.Changed -= repaint; };
            Select(0);
        }

        public void Select(int range)
        {
            for (int i = 0; i < tabs.Length; i++) tabs[i].On = i == range;
            DateTime to = DateTime.Today.AddDays(1);
            DateTime from = range == 0 ? DateTime.Today : DateTime.Today.AddDays(range == 1 ? -6 : -(Stats.KeepDays - 1));
            var recs = load(from);

            var ticks = new List<Tuple<DateTime, string>>();
            var div = new List<DateTime>();
            var culture = new CultureInfo(L.En ? "en-US" : "ru-RU");
            if (range == 0)
                for (int hr = 0; hr <= 24; hr += 3) ticks.Add(Tuple.Create(from.AddHours(hr), hr.ToString("00") + ":00"));
            else
                for (DateTime d = from; d < to; d = d.AddDays(1))
                {
                    if (d > from) div.Add(d);
                    if (range == 1) ticks.Add(Tuple.Create(d.AddHours(12), d.ToString("ddd d", culture)));
                    else if ((to - d).Days % 5 == 1) ticks.Add(Tuple.Create(d.AddHours(12), d.ToString("dd.MM")));
                }
            bool date = range > 0;
            barYt.Set(Stats.Segments(recs, 0, from, to), from, to, div, date);
            barDc.Set(Stats.Segments(recs, 1, from, to), from, to, div, date);
            barStrat.Set(Stats.Segments(recs, 2, from, to), from, to, div, date);
            axis.Set(ticks, from, to);

            double yt = Stats.UpPct(recs, 0, from, to), dc = Stats.UpPct(recs, 1, from, to);
            SetPct(tYt, yt); SetPct(tDc, dc);
            int outs = Stats.Outages(recs, 0, from, to) + Stats.Outages(recs, 1, from, to);
            tOut.Text = yt < 0 && dc < 0 ? "—" : outs.ToString();
            tOut.Foreground = outs > 0 ? W.Warn : W.Text;
            var by = Stats.ByStrategy(recs, from, to);
            double onSec = 0; foreach (var s in by) onSec += s.OnSec;
            tOn.Text = onSec > 0 ? Dur(onSec) : "—";
            FillTable(by);
            tableBox.Visibility = by.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            empty.Text = recs.Count == 0
                ? L.T("За этот период данных нет. Статистика собирается, пока программа запущена — в окне или в трее.",
                      "No data for this period. Statistics are collected while the app is running — in the window or in the tray.")
                : by.Count == 0 ? L.T("Обход за этот период не включался.", "The bypass was not on during this period.") : "";
            empty.Visibility = empty.Text == "" ? Visibility.Collapsed : Visibility.Visible;
        }

        static void SetPct(TextBlock t, double v)
        {
            t.Text = v < 0 ? "—" : Pct(v);
            t.Foreground = PctBrush(v);
        }

        static string Pct(double v) { return v < 0 ? "—" : (v > 99 && v < 100 ? "99" : Math.Round(v).ToString()) + "%"; }
        static Brush PctBrush(double v) { return v < 0 ? W.Muted : v >= 95 ? W.Accent : v >= 70 ? W.Warn : W.Danger; }

        static string Dur(double sec)
        {
            int m = (int)Math.Round(sec / 60);
            if (m < 1) return L.T("<1 мин", "<1 min");
            if (m < 60) return m + L.T(" мин", " min");
            int h = m / 60; m %= 60;
            if (h < 24) return h + L.T(" ч", " h") + (m > 0 ? " " + m + L.T(" мин", " min") : "");
            return h / 24 + L.T(" д ", " d ") + h % 24 + L.T(" ч", " h");
        }

        void FillTable(List<StatStrategy> by)
        {
            table.Children.Clear(); table.RowDefinitions.Clear(); table.ColumnDefinitions.Clear();
            table.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (int wd in new[] { 96, 76, 76, 70 }) table.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(wd) });
            Action<int, int, string, Brush, bool> cell = (r, c, text, b, right) =>
            {
                var t = W.T(text, r == 0 ? W.Small : W.Body, b);
                t.Margin = new Thickness(0, r == 0 ? 2 : 6, 0, r == 0 ? 4 : 6);
                if (right) t.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetRow(t, r); Grid.SetColumn(t, c); table.Children.Add(t);
            };
            table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            string[] hd = { L.T("Стратегия", "Strategy"), L.T("Время", "Time"), "YouTube", "Discord", L.T("Пинг", "Ping") };
            for (int c = 0; c < hd.Length; c++) cell(0, c, hd[c], W.Muted, c > 0);
            int n = Math.Min(by.Count, 6);
            for (int i = 0; i < n; i++)
            {
                var s = by[i];
                int r = i + 1;
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var line = new Border { BorderBrush = W.Line, BorderThickness = new Thickness(0, 1, 0, 0), VerticalAlignment = VerticalAlignment.Top };
                Grid.SetRow(line, r); Grid.SetColumnSpan(line, 5); table.Children.Add(line);
                cell(r, 0, s.Name, W.Text, false);
                cell(r, 1, Dur(s.OnSec), W.Muted, true);
                cell(r, 2, Pct(s.YtPct), PctBrush(s.YtPct), true);
                cell(r, 3, Pct(s.DcPct), PctBrush(s.DcPct), true);
                cell(r, 4, s.AvgMs >= 0 ? s.AvgMs + L.T(" мс", " ms") : "—", W.Muted, true);
            }
            if (by.Count > n)
            {
                table.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cell(n + 1, 0, L.T("и ещё ", "and ") + (by.Count - n) + L.T(" — реже", " more, used less"), W.Dim, false);
            }
        }
    }
}
