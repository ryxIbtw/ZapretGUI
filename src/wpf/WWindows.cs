// Дополнительные окна на WPF: мини-панель у трея, результаты подбора, диагностика, журнал, редактор списков, «О программе».
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using WF = System.Windows.Forms;

namespace ZapretGui
{
    // ================= Мини-панель у трея =================
    class FlyoutW : Window
    {
        readonly MainWindow owner;
        readonly PowerHero power;
        readonly TextBlock status, sub;
        readonly ServiceCardW yt, dc;
        readonly StrategyPickerW picker;
        bool menuOpen, closing;

        public FlyoutW(MainWindow o)
        {
            owner = o;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            Topmost = true;
            ShowInTaskbar = false;
            SizeToContent = SizeToContent.WidthAndHeight;
            Background = W.Bg;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            FontFamily = W.TextFont;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -10000; Top = -10000;

            var sp = new StackPanel { Width = 300, Margin = new Thickness(0, 0, 0, 14) };
            var head = new Grid { Margin = new Thickness(16, 14, 16, 0) };
            var brand = new StackPanel { Orientation = Orientation.Horizontal };
            brand.Children.Add(new Image { Source = MainWindow.LogoImage(20), Width = 20, Height = 20 });
            var t = W.T("Zapret", W.BodyStrong, W.Text, true); t.Margin = new Thickness(8, 0, 0, 0); t.VerticalAlignment = VerticalAlignment.Center;
            brand.Children.Add(t);
            head.Children.Add(brand);
            var open = new FlatLink(L.T("Открыть", "Open")) { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            open.Click += delegate { Close(); owner.ShowFromTray(); };
            head.Children.Add(open);
            sp.Children.Add(head);

            power = new PowerHero(104, true) { Margin = new Thickness(0, 8, 0, 0) };
            power.Click += delegate { owner.TogglePower(); };
            sp.Children.Add(power);
            status = W.T("", W.Status, W.Text, true); status.HorizontalAlignment = HorizontalAlignment.Center; status.Margin = new Thickness(0, 2, 0, 0);
            sub = W.T("", W.Small, W.Muted); sub.HorizontalAlignment = HorizontalAlignment.Center; sub.Margin = new Thickness(14, 1, 14, 0);
            sp.Children.Add(status); sp.Children.Add(sub);

            var cards = new Grid { Margin = new Thickness(14, 12, 14, 0) };
            cards.ColumnDefinitions.Add(new ColumnDefinition()); cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) }); cards.ColumnDefinitions.Add(new ColumnDefinition());
            yt = new ServiceCardW("YouTube", "play"); dc = new ServiceCardW("Discord", "chat");
            Grid.SetColumn(dc, 2);
            yt.Click += delegate { owner.CheckNet(); }; dc.Click += delegate { owner.CheckNet(); };
            cards.Children.Add(yt); cards.Children.Add(dc);
            sp.Children.Add(cards);

            picker = new StrategyPickerW { Margin = new Thickness(14, 10, 14, 0) };
            picker.Prev += delegate { owner.StepStrategy(-1); };
            picker.Next += delegate { owner.StepStrategy(1); };
            picker.Open += delegate
            {
                var m = owner.ShowStrategyMenu(picker, true);
                if (m != null) { menuOpen = true; m.Closed += delegate { menuOpen = false; if (!closing) Activate(); }; }
            };
            sp.Children.Add(picker);

            Content = new Border { BorderBrush = W.Line, BorderThickness = new Thickness(1), Child = sp,
                LayoutTransform = new ScaleTransform(Theme.UiScale, Theme.UiScale) };

            Deactivated += delegate { Dispatcher.BeginInvoke((Action)delegate { if (!menuOpen && !IsActive && !closing) Close(); }); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) Close(); };
            Closing += delegate { closing = true; };
            SourceInitialized += delegate
            {
                IntPtr h = new WindowInteropHelper(this).Handle;
                try { int round = 2; DwmSetWindowAttribute(h, 33, ref round, 4); } catch { }   // скруглённые углы Windows 11
                try { SetClassLong(h, -26, GetClassLong(h, -26) | 0x20000); } catch { }        // тень (CS_DROPSHADOW)
            };
        }

        public void Sync(bool running, bool busy, double progress, string st, Brush stBrush, string subText,
                         float netYt, float netDc, bool checking, string ytMeta, string dcMeta, string strategy)
        {
            power.SetOn(running, IsVisible);
            power.SetBusy(busy);
            power.SetProgress(progress);
            status.Text = st; status.Foreground = stBrush; sub.Text = subText;
            yt.Set(netYt, checking, ytMeta); dc.Set(netDc, checking, dcMeta);
            picker.SetValue(strategy);
        }

        // Ставит панель над значком в трее (или у края экрана, если панель задач сбоку/сверху).
        public void ShowNearCursor()
        {
            Show();
            UpdateLayout();
            var src = PresentationSource.FromVisual(this);
            Matrix toDip = src != null ? src.CompositionTarget.TransformFromDevice : Matrix.Identity;
            var c = WF.Cursor.Position;
            var wa = WF.Screen.FromPoint(c).WorkingArea;
            Point cur = toDip.Transform(new Point(c.X, c.Y));
            Point tl = toDip.Transform(new Point(wa.Left, wa.Top)), br = toDip.Transform(new Point(wa.Right, wa.Bottom));
            double m = 12, w = ActualWidth, h = ActualHeight;
            double x = Math.Max(tl.X + m, Math.Min(cur.X - w / 2, br.X - w - m));
            double y = cur.Y > (tl.Y + br.Y) / 2 ? br.Y - h - m : tl.Y + m;
            if (cur.X < tl.X || cur.X > br.X) { x = cur.X < tl.X ? tl.X + m : br.X - w - m; y = Math.Max(tl.Y + m, Math.Min(cur.Y - h / 2, br.Y - h - m)); }
            Left = x; Top = y;
            Activate();
        }

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
        [DllImport("user32.dll", EntryPoint = "GetClassLong")] static extern int GetClassLong(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetClassLong")] static extern int SetClassLong(IntPtr h, int i, int v);
    }

    // ================= Результаты подбора =================
    class PickW : WinBase
    {
        public string Chosen { get; private set; }
        const int HistoryDots = 5;
        readonly List<Tuple<Border, PickResult>> rows = new List<Tuple<Border, PickResult>>();
        readonly Pill apply;

        static Grid Cols()
        {
            var g = new Grid();
            foreach (double w in new[] { 28.0, 212, 66, 66, 66, 92 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            return g;
        }

        static TextBlock Cell(Grid g, int col, string text, double size, Brush b, bool strong = false)
        {
            var t = W.T(text, size, b, strong); t.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(t, col); g.Children.Add(t); return t;
        }

        public PickW(Window owner, List<PickResult> ranked, bool partial, List<PickHistory.Run> history, string title)
            : base(title ?? L.T("Результаты подбора", "Strategy test results"), owner)
        {
            history = history ?? new List<PickHistory.Run>();
            var sp = new StackPanel { Width = 640, Margin = new Thickness(20, 16, 20, 18) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            string hist = history.Count == 0
                ? L.T(" История появится после следующего подбора.", " History will appear after the next test.")
                : L.T(" «История» — прошлые подборы (до " + HistoryDots + "): зелёный — всё работало, жёлтый — частично, красный — нет.",
                      " “History” shows past tests (up to " + HistoryDots + "): green — all worked, yellow — partly, red — not.");
            sp.Children.Add(new TextBlock
            {
                Text = (partial ? L.T("Подбор прерван, показаны проверенные стратегии. ", "Testing was stopped; showing tested strategies. ") : "") +
                       L.T("Лучшая по замерам выделена, можно выбрать другую и нажать «Применить».", "The best by measurements is selected; pick another and click Apply.") + hist,
                TextWrapping = TextWrapping.Wrap, Foreground = W.Muted, FontSize = W.Small, Margin = new Thickness(0, 4, 0, 10),
            });
            var head = Cols(); head.Margin = new Thickness(12, 0, 12, 4);
            string[] names = { "#", L.T("Стратегия", "Strategy"), "YouTube", "Discord", L.T("Отклик", "Ping"), L.T("Скорость", "Speed"), L.T("История", "History") };
            for (int i = 0; i < names.Length; i++) Cell(head, i, names[i], W.Caption, W.Dim);
            sp.Children.Add(head);

            var list = new StackPanel();
            for (int i = 0; i < ranked.Count; i++) list.Children.Add(Row(ranked[i], i + 1, PickHistory.LevelsFor(ranked[i].Name, history, HistoryDots)));
            sp.Children.Add(new ScrollViewer { Content = list, Height = 430, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

            var bar = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            bar.Children.Add(new TextBlock { Text = title == null ? L.T("«Отмена» вернёт то, что работало до подбора", "Cancel restores what was running before") : "",
                Foreground = W.Dim, FontSize = W.Caption, VerticalAlignment = VerticalAlignment.Center });
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Pill(L.T("Отмена", "Cancel"), true) { MinWidth = 110 };
            apply = new Pill(L.T("Применить", "Apply")) { MinWidth = 120, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Click += delegate { Chosen = null; Close(); };
            apply.Click += delegate { if (Chosen != null) { applied = true; Close(); } };
            btns.Children.Add(cancel); btns.Children.Add(apply);
            bar.Children.Add(btns);
            sp.Children.Add(bar);
            Root.Child = sp;

            var first = rows.FirstOrDefault(r => r.Item2.Works);
            if (first != null) Choose(first.Item1, first.Item2); else apply.IsEnabled = false;
            KeyDown += (s, e) => { if (e.Key == Key.Enter && Chosen != null) { applied = true; Close(); } };
            Closing += delegate { if (!applied) Chosen = null; };
        }
        bool applied;

        Border Row(PickResult r, int rank, List<int> past)
        {
            var g = Cols();
            Func<int, int, Brush> ratio = (ok, max) => ok >= max ? W.Accent : ok > 0 ? W.Warn : W.Danger;
            Cell(g, 0, rank.ToString(), W.Small, W.Dim);
            Cell(g, 1, r.Name, r.Works && rank == 1 ? W.BodyStrong : W.Body, r.Works ? W.Text : W.Dim, r.Works && rank == 1);
            if (r.Failed) { var f = Cell(g, 2, L.T("не запустилась", "failed to start"), W.Small, W.Dim); Grid.SetColumnSpan(f, 4); }
            else
            {
                Cell(g, 2, r.YtOk + "/" + r.YtMax, W.Body, ratio(r.YtOk, r.YtMax));
                Cell(g, 3, r.DcOk + "/" + r.DcMax, W.Body, ratio(r.DcOk, r.DcMax));
                Cell(g, 4, r.Samples > 0 ? ((int)r.AvgMs) + L.T(" мс", " ms") : "—", W.Small, W.Muted);
                Cell(g, 5, PickResult.FormatSpeed(r.SpeedKBps), W.Small, W.Muted);
            }
            var dots = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            if (past.Count == 0) dots.Children.Add(W.T("—", W.Small, W.Dim));
            foreach (int lv in past)
                dots.Children.Add(new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 5, 0),
                    Fill = lv == 2 ? W.Accent : lv == 1 ? W.Warn : lv == 0 ? W.Danger : W.Line });
            Grid.SetColumn(dots, 6); g.Children.Add(dots);

            var b = new Border { Background = W.Surface, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 0, 12, 0), Height = 38,
                Margin = new Thickness(0, 0, 0, 4), Child = g, BorderThickness = new Thickness(1), BorderBrush = Brushes.Transparent };
            if (r.Works)
            {
                b.Cursor = Cursors.Hand;
                b.MouseEnter += delegate { if (Chosen != r.Name) b.Background = W.SurfaceHover; };
                b.MouseLeave += delegate { if (Chosen != r.Name) b.Background = W.Surface; };
                b.MouseLeftButtonUp += delegate { Choose(b, r); };
                b.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { Choose(b, r); applied = true; Close(); } };
            }
            rows.Add(Tuple.Create(b, r));
            return b;
        }

        void Choose(Border row, PickResult r)
        {
            foreach (var x in rows)
            {
                bool sel = x.Item1 == row;
                x.Item1.Background = sel ? W.AccentDeep : W.Surface;
                x.Item1.BorderBrush = sel ? (Brush)W.AccentLine : Brushes.Transparent;
            }
            Chosen = r.Name;
            apply.IsEnabled = true;
        }
    }

    // ================= Диагностика =================
    class DiagW : WinBase
    {
        readonly Zapret z;
        readonly StackPanel list = new StackPanel();
        readonly TextBlock summary;
        bool running;

        public DiagW(Window owner, Zapret zapret) : base(L.T("Диагностика", "Diagnostics"), owner)
        {
            z = zapret;
            var sp = new StackPanel { Width = 400, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            summary = W.T("", W.Small, W.Muted); summary.Margin = new Thickness(0, 4, 0, 10);
            sp.Children.Add(summary);
            sp.Children.Add(new ScrollViewer { Content = list, Height = 470, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var bar = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            var rerun = new FlatLink(L.T("Проверить снова", "Run again")) { HorizontalAlignment = HorizontalAlignment.Left };
            rerun.Click += delegate { Start(); };
            var discord = new FlatLink(L.T("Очистить кэш Discord", "Clear Discord cache")) { HorizontalAlignment = HorizontalAlignment.Right };
            discord.Click += delegate { ClearDiscord(); };
            bar.Children.Add(rerun); bar.Children.Add(discord);
            sp.Children.Add(bar);
            Root.Child = sp;
            ContentRendered += delegate { Start(); };
        }

        public void Start()
        {
            if (running) return;
            running = true;
            summary.Foreground = W.Muted;
            summary.Text = L.T("Проверяю…", "Checking…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DiagItem> items;
                try { items = Diag.Run(z); }
                catch (Exception ex) { items = new List<DiagItem> { new DiagItem { Level = 2, Title = ex.Message } }; }
                Dispatcher.BeginInvoke((Action)delegate { Fill(items); running = false; });
            });
        }

        void Fill(List<DiagItem> items)
        {
            list.Children.Clear();
            foreach (var it in items)
            {
                var item = it;
                Brush c = it.Level == 0 ? W.Accent : it.Level == 1 ? W.Warn : W.Danger;
                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition());
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var mark = new Grid { Width = 22, Height = 22, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
                mark.Children.Add(new Ellipse { Fill = c, Opacity = Theme.IsLight ? 0.16 : 0.2 });
                mark.Children.Add(new Ico(it.Level == 0 ? "check" : it.Level == 1 ? "alert" : "x", 13, c) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
                g.Children.Add(mark);
                var tx = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                tx.Children.Add(W.T(it.Title, W.Body, W.Text));
                if (!string.IsNullOrEmpty(it.Detail)) tx.Children.Add(W.T(it.Detail, W.Caption, W.Muted));
                Grid.SetColumn(tx, 1); g.Children.Add(tx);
                if (it.Fix != null)
                {
                    var fix = new Pill(it.FixText, false, 28) { Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
                    fix.Click += delegate
                    {
                        try { item.Fix(); } catch (Exception ex) { summary.Foreground = W.Danger; summary.Text = ex.Message; return; }
                        Start();
                    };
                    Grid.SetColumn(fix, 2); g.Children.Add(fix);
                }
                list.Children.Add(new Border { Background = W.Surface, CornerRadius = new CornerRadius(10), Padding = new Thickness(12, 9, 10, 9), Margin = new Thickness(0, 0, 4, 4), Child = g });
            }
            int bad = items.Count(i => i.Level == 2), warn = items.Count(i => i.Level == 1);
            if (bad + warn == 0) { summary.Foreground = W.Accent; summary.Text = L.T("Всё в порядке", "Everything looks good"); }
            else
            {
                summary.Foreground = bad > 0 ? W.Danger : W.Warn;
                summary.Text = L.T("Проблем: " + bad + ", предупреждений: " + warn, "Problems: " + bad + ", warnings: " + warn);
            }
        }

        void ClearDiscord()
        {
            if (AskW.Show(this, L.T("Discord будет закрыт, его кэш удалён. Продолжить?", "Discord will be closed and its cache removed. Continue?"),
                Title, MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            int n = Diag.ClearDiscordCache();
            summary.Foreground = n > 0 ? W.Accent : W.Warn;
            summary.Text = n > 0 ? L.T("Кэш Discord очищен", "Discord cache cleared") : L.T("Discord не найден", "Discord not found");
        }
    }

    // ================= Журнал событий =================
    class JournalW : WinBase
    {
        readonly TextBox box;
        readonly TextBlock summary;
        readonly Action<string> onAdded;

        public JournalW(Window owner, Func<string> header) : base(L.T("Журнал событий", "Event log"), owner)
        {
            var sp = new StackPanel { Width = 520, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            summary = W.T("", W.Small, W.Muted); summary.Margin = new Thickness(0, 4, 0, 10);
            sp.Children.Add(summary);
            box = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 420, FontFamily = W.MonoFont, FontSize = 12,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            sp.Children.Add(box);
            var bar = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            var open = new FlatLink(L.T("Открыть файл", "Open file")) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            open.Click += delegate
            {
                try
                {
                    if (!File.Exists(Journal.FilePath)) Journal.Add(L.T("Журнал создан", "Log created"));
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Journal.FilePath + "\"") { UseShellExecute = true });
                }
                catch { }
            };
            var copy = new Pill(L.T("Копировать", "Copy")) { MinWidth = 130, HorizontalAlignment = HorizontalAlignment.Right };
            copy.Click += delegate
            {
                try { Clipboard.SetText(header() + "\r\n\r\n" + string.Join("\r\n", Journal.Read())); } catch { }
                summary.Foreground = W.Accent;
                summary.Text = L.T("Скопировано вместе со сведениями о системе — можно вставить в сообщение", "Copied with system info — paste it into a message");
            };
            bar.Children.Add(open); bar.Children.Add(copy);
            sp.Children.Add(bar);
            Root.Child = sp;
            onAdded = line => Dispatcher.BeginInvoke((Action)Fill);
            Journal.Added += onAdded;
            Closed += delegate { Journal.Added -= onAdded; };
            Fill();
        }

        void Fill()
        {
            var lines = Journal.Read();
            box.Text = lines.Count == 0 ? L.T("Пока записей нет.", "No entries yet.") : string.Join("\r\n", lines);
            box.ScrollToEnd();
            summary.Foreground = W.Muted;
            summary.Text = L.T("Записей: " + lines.Count + "  ·  хранятся последние " + Journal.Keep, "Entries: " + lines.Count + "  ·  the last " + Journal.Keep + " are kept");
        }
    }

    // ================= Редактор списков =================
    class ListEditorW : WinBase
    {
        readonly string path;
        readonly bool ipList;
        readonly List<string> keep = new List<string>(), items = new List<string>();
        readonly TextBox input, search;
        readonly ListBox box;
        readonly TextBlock count, error;
        bool dirty;
        public bool Saved { get; private set; }
        public bool OpenInNotepad { get; private set; }

        public ListEditorW(Window owner, string file, string title, string description, bool ip) : base(title, owner)
        {
            path = file; ipList = ip;
            ListFile.Load(path, keep, items);
            var sp = new StackPanel { Width = 400, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(title, W.Title, W.Text, true));
            var d = W.T(description, W.Small, W.Muted); d.Margin = new Thickness(0, 4, 0, 12);
            sp.Children.Add(d);

            var addRow = new Grid();
            addRow.ColumnDefinitions.Add(new ColumnDefinition()); addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input = new TextBox { Tag = ip ? L.T("IP или подсеть, например 1.2.3.0/24", "IP or subnet, e.g. 1.2.3.0/24") : L.T("домен, например example.com", "domain, e.g. example.com") };
            input.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; AddFromInput(); } };
            addRow.Children.Add(input);
            var add = new Pill(L.T("Добавить", "Add"), false, 36) { Margin = new Thickness(8, 0, 0, 0) };
            add.Click += delegate { AddFromInput(); };
            Grid.SetColumn(add, 1); addRow.Children.Add(add);
            sp.Children.Add(addRow);
            error = W.T("", W.Caption, W.Danger); error.Margin = new Thickness(2, 4, 0, 6);
            sp.Children.Add(error);
            search = new TextBox { Tag = L.T("Поиск…", "Search…") };
            search.TextChanged += delegate { Refill(); };
            sp.Children.Add(search);

            box = new ListBox { SelectionMode = SelectionMode.Extended, Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = W.Body };
            box.KeyDown += (s, e) => { if (e.Key == Key.Delete) RemoveSelected(); };
            box.SelectionChanged += delegate { UpdateCount(); };
            ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
            sp.Children.Add(new Border { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(6), Margin = new Thickness(0, 10, 0, 0), Height = 300, Child = box });

            var row2 = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            var remove = new FlatLink(L.T("Удалить выбранные", "Remove selected")) { HorizontalAlignment = HorizontalAlignment.Left };
            remove.Click += delegate { RemoveSelected(); };
            count = W.T("", W.Small, W.Dim); count.HorizontalAlignment = HorizontalAlignment.Right;
            row2.Children.Add(remove); row2.Children.Add(count);
            sp.Children.Add(row2);

            var row3 = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            var notepad = new FlatLink(L.T("Открыть в Блокноте", "Open in Notepad")) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            notepad.Click += delegate { OpenInNotepad = true; Close(); };
            var save = new Pill(L.T("Сохранить", "Save")) { MinWidth = 140, HorizontalAlignment = HorizontalAlignment.Right };
            save.Click += delegate { if (WriteFile()) Close(); };
            row3.Children.Add(notepad); row3.Children.Add(save);
            sp.Children.Add(row3);
            Root.Child = sp;
            Refill();
            Loaded += delegate { input.Focus(); };
            Closing += OnClosingAsk;
        }

        bool WriteFile()
        {
            try { ListFile.Write(path, keep, items, ipList); }
            catch (Exception ex) { error.Foreground = W.Danger; error.Text = ex.Message; return false; }
            dirty = false; Saved = true;
            return true;
        }

        void AddFromInput()
        {
            int added, dup; List<string> bad;
            ListFile.AddFrom(input.Text, ipList, items, out added, out dup, out bad);
            if (added > 0) { dirty = true; input.Text = ""; search.Text = ""; }
            Refill();
            if (added > 0) { box.SelectedIndex = box.Items.Count - 1; box.ScrollIntoView(box.SelectedItem); }
            if (bad.Count > 0) { error.Foreground = W.Danger; error.Text = L.T("Не похоже на ", "Doesn't look like ") + (ipList ? "IP" : L.T("домен", "a domain")) + ": " + string.Join(", ", bad); }
            else if (dup > 0) { error.Foreground = W.Warn; error.Text = L.T("Уже есть в списке", "Already in the list"); }
            else error.Text = "";
        }

        void RemoveSelected()
        {
            var sel = box.SelectedItems.Cast<string>().ToList();
            if (sel.Count == 0) return;
            foreach (string s in sel) items.Remove(s);
            dirty = true;
            Refill();
        }

        void Refill()
        {
            string q = search.Text.Trim();
            box.Items.Clear();
            foreach (string s in items) if (q == "" || s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) box.Items.Add(s);
            UpdateCount();
        }

        void UpdateCount()
        {
            string t = L.T("Записей: ", "Entries: ") + items.Count;
            if (box.Items.Count != items.Count) t += L.T(", найдено: ", ", found: ") + box.Items.Count;
            if (box.SelectedItems.Count > 1) t += L.T(", выбрано: ", ", selected: ") + box.SelectedItems.Count;
            if (dirty) t += L.T("  ·  не сохранено", "  ·  unsaved");
            count.Text = t;
        }

        void OnClosingAsk(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!dirty || Saved) return;
            var r = AskW.Show(this, L.T("Сохранить изменения в списке?", "Save changes to the list?"), Title, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (r == MessageBoxResult.Cancel) { e.Cancel = true; OpenInNotepad = false; return; }
            if (r == MessageBoxResult.Yes && !WriteFile()) { e.Cancel = true; OpenInNotepad = false; }
        }
    }

    // ================= О программе =================
    class AboutW : WinBase
    {
        public const string RepoUrl = "https://github.com/ryxIbtw/ZapretGUI";
        public const string ZapretUrl = "https://github.com/Flowseal/zapret-discord-youtube";
        public const string LicenseUrl = "https://github.com/ryxIbtw/ZapretGUI/blob/main/LICENSE";

        public AboutW(Window owner, string zapretVersion) : base(L.T("О программе", "About"), owner)
        {
            var sp = new StackPanel { Width = 340, Margin = new Thickness(20, 22, 20, 16) };
            sp.Children.Add(new Image { Source = MainWindow.LogoImage(64), Width = 64, Height = 64 });
            var name = W.T("Zapret GUI", W.Status, W.Text, true); name.HorizontalAlignment = HorizontalAlignment.Center; name.Margin = new Thickness(0, 10, 0, 0);
            sp.Children.Add(name);
            var ver = W.T(L.T("Версия ", "Version ") + SelfUpdate.Current.ToString(3) + "  ·  GUI by " + BrandHeader.Author, W.Small, W.Muted);
            ver.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(ver);

            var card = new CardBox { Margin = new Thickness(0, 16, 0, 0) };
            var repo = new SettingRowW(L.T("Репозиторий ZapretGUI", "ZapretGUI repository"), false, null, "github.com/ryxIbtw/ZapretGUI", true);
            var zap = new SettingRowW("zapret-discord-youtube", false, null, "by " + BrandHeader.ZapretAuthor + (zapretVersion != "" ? L.T("  ·  установлена v", "  ·  installed v") + zapretVersion : ""), true);
            var lic = new SettingRowW(L.T("Лицензия", "License"), false, null, "MIT  ·  © 2026 " + BrandHeader.Author, true);
            repo.Click += delegate { Open(RepoUrl); }; zap.Click += delegate { Open(ZapretUrl); }; lic.Click += delegate { Open(LicenseUrl); };
            card.Add(repo); card.Add(zap); card.Add(lic);
            sp.Children.Add(card);
            sp.Children.Add(new TextBlock { Text = L.T("Неофициальный интерфейс. Сам zapret-discord-youtube принадлежит его авторам.", "Unofficial GUI. zapret-discord-youtube itself belongs to its authors."),
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = W.Dim, FontSize = W.Caption, Margin = new Thickness(10, 10, 10, 0) });

            string hash = SelfUpdate.FileSha256(WF.Application.ExecutablePath) ?? "—";
            var hrow = new Grid { Margin = new Thickness(2, 14, 2, 4) };
            hrow.Children.Add(W.T(L.T("SHA256 этого файла", "SHA256 of this file"), W.Caption, W.Muted));
            var copy = new FlatLink(L.T("Копировать", "Copy")) { HorizontalAlignment = HorizontalAlignment.Right };
            copy.Click += delegate { try { Clipboard.SetText(hash); } catch { } copy.Text = L.T("Скопировано", "Copied"); };
            hrow.Children.Add(copy);
            sp.Children.Add(hrow);
            sp.Children.Add(new Border { Background = W.Surface, CornerRadius = new CornerRadius(12), Padding = new Thickness(10, 8, 10, 8),
                Child = new TextBlock { Text = hash.Length == 64 ? hash.Substring(0, 32) + "\n" + hash.Substring(32) : hash, FontFamily = W.MonoFont, FontSize = 11.5,
                    Foreground = W.Text, TextAlignment = TextAlignment.Center } });
            var close = new FlatLink(L.T("Закрыть", "Close")) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 14, 0, 0) };
            close.Click += delegate { Close(); };
            sp.Children.Add(close);
            Root.Child = sp;
        }

        static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
    }
}
