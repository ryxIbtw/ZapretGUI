// Стратегии: избранные, заметки, быстрый подбор, подбор по расписанию, свои сайты для проверки, страница «Стратегии».
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WF = System.Windows.Forms;

namespace ZapretGui
{
    partial class MainWindow
    {
        // Избранные стратегии (звёздочки): стрелки на панели листают только их, автовосстановление пробует их первыми.
        static List<string> Favorites
        {
            get { return Prefs.Get("Favorites", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
            set { Prefs.Set("Favorites", string.Join("|", value)); }
        }

        static void ToggleFavorite(string name)
        {
            var f = Favorites;
            if (f.Contains(name)) f.Remove(name); else f.Add(name);
            Favorites = f;
        }

        // для окна «Стратегии»
        public static List<string> FavoritesPublic { get { return Favorites; } }
        public static void ToggleFavoritePublic(string name) { ToggleFavorite(name); }
        public string Current { get { return selected; } }
        public void SelectStrategyPublic(string name) { SelectStrategy(name); }
        public Zapret Zap { get { return z; } }
        public string JournalHeaderPublic() { return JournalHeader(); }
        public void CreateShortcutsPublic() { CreateShortcuts(); }

        // Заметка к стратегии, например «лучшая вечером».
        public static string Note(string name) { return Prefs.Get("Note:" + name, ""); }
        public static void SetNote(string name, string text) { Prefs.Set("Note:" + name, (text ?? "").Trim()); }

        // Список, по которому листают стрелки: избранные (если их хотя бы две) или все.
        List<string> StepList()
        {
            var fav = Favorites.Where(strategies.Contains).ToList();
            return fav.Count >= 2 ? strategies.Where(fav.Contains).ToList() : strategies;
        }

        // Быстрый подбор: до 5 стратегий, которые у вас раньше были лучшими (по прошлым подборам и статистике).
        List<string> QuickList()
        {
            var order = new List<string>();
            var runs = PickHistory.Load();
            for (int i = runs.Count - 1; i >= 0 && order.Count < 5; i--)
                foreach (var r in PickResult.Rank(runs[i].Results).Where(r => r.Works).Take(5))
                    if (!order.Contains(r.Name)) order.Add(r.Name);
            try
            {
                foreach (var s in Stats.ByStrategy(Stats.Load(DateTime.Today.AddDays(-Stats.KeepDays)), DateTime.Today.AddDays(-Stats.KeepDays), DateTime.Now)
                                       .Where(x => x.OnSec > 600).OrderByDescending(x => Math.Min(x.YtPct, x.DcPct)))
                    if (!order.Contains(s.Name)) order.Add(s.Name);
            }
            catch { }
            foreach (var f in Favorites) if (!order.Contains(f)) order.Add(f);
            return order.Where(strategies.Contains).Take(5).ToList();
        }

        void ShowStrategies() { new StrategiesW(this).ShowDialog(); UpdateUi(); }

        // ---------- подбор по расписанию ----------

        public const string ScheduleTask = "ZapretGUI scheduled test";
        public static readonly int WM_PICKNOW = RegisterWindowMessage("ZapretGUI_PickNow");

        static bool ScheduleOn
        {
            get
            {
                try
                {
                    using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", "/Query /TN \"" + ScheduleTask + "\"") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                    { p.WaitForExit(5000); return p.ExitCode == 0; }
                }
                catch { return false; }
            }
        }

        // Задача планировщика Windows: каждое воскресенье в 4:00 — быстрый подбор (только по нажатию в настройках).
        void SetSchedule(bool on)
        {
            string when = (ScheduleDay == "DAILY" ? "/SC DAILY" : "/SC WEEKLY /D " + ScheduleDay) + " /ST " + ScheduleTime;
            string args = on
                ? "/Create /F /TN \"" + ScheduleTask + "\" " + when + " /RL HIGHEST /TR \"\\\"" + WF.Application.ExecutablePath + "\\\" --scheduled-pick\""
                : "/Delete /F /TN \"" + ScheduleTask + "\"";
            try
            {
                using (var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true }))
                {
                    string err = p.StandardError.ReadToEnd();
                    p.WaitForExit(10000);
                    if (p.ExitCode != 0) throw new Exception(err.Trim());
                }
                scheduleOn = on;
                Journal.Add(on ? L.T("Подбор по расписанию включён: ", "Scheduled test on: ") + ScheduleLabel() : L.T("Подбор по расписанию выключен", "Scheduled test off"));
                Toast(on ? L.T("Подбор по расписанию: ", "Scheduled test: ") + DayLabel(ScheduleDay, true).ToLower() + ", " + ScheduleTime : L.T("Подбор по расписанию выключен", "Scheduled test off"));
            }
            catch (Exception ex) { Toast(L.T("Планировщик: ", "Task Scheduler: ") + ex.Message, true); }
            UpdateUi();
        }
        bool scheduleOn;

        // Запуск по расписанию: быстрый подбор без окон.
        internal void ScheduledPick()
        {
            if (busy) { Journal.Add(L.T("Подбор по расписанию пропущен: программа занята", "Scheduled test skipped: the app is busy")); return; }
            strategies = z.Strategies();
            var list = QuickList();
            if (list.Count < 2) list = strategies.Take(8).ToList();
            if (list.Count == 0) return;
            if (!list.Contains(selected) && strategies.Contains(selected)) list.Insert(0, selected);   // текущая — для сравнения
            Journal.Add(L.T("Подбор по расписанию: ", "Scheduled test: ") + string.Join(", ", list));
            RunPick(list, "std", FlowTest.LoadTargets(z), true);
        }

        void FinishScheduledPick(List<PickResult> results, bool partial, bool wasActive, string prev)
        {
            if (!partial && results.Count > 0) PickHistory.Save(results);
            var ranked = PickResult.Rank(results);
            var best = ranked.FirstOrDefault(r => r.Works);
            var cur = results.FirstOrDefault(r => r.Name == prev);
            if (best != null) FlowTest.SaveReport(z, results, best);
            bool better = best != null && best.Name != prev && (cur == null || !cur.Works || best.Ok > cur.Ok || best.Ok == cur.Ok && best.PingOk > cur.PingOk);
            if (better && wasActive)
            {
                selected = best.Name; Prefs.Set("Strategy", best.Name);
                Start();
                Journal.Add(L.T("Подбор по расписанию: переключено на ", "Scheduled test: switched to ") + best.Name + L.T(" (лучше, чем ", " (better than ") + prev + ")");
                Balloon(L.T("Стратегия обновлена", "Strategy updated"), L.T("По расписанию найдена лучше: ", "A better one was found: ") + best.Name, WF.ToolTipIcon.Info);
            }
            else
            {
                RestoreAfterPick(wasActive, prev);
                Journal.Add(L.T("Подбор по расписанию: текущая стратегия осталась (", "Scheduled test: kept the current strategy (") + prev + ")");
            }
        }

        // ---------- свои сайты для проверки (utils\targets.txt) ----------

        void EditTargets()
        {
            var w = new TargetsW(this, z);
            w.ShowDialog();
            if (w.Saved) Journal.Add(L.T("Изменены сайты для проверки (utils\\targets.txt)", "Test sites changed (utils\\targets.txt)"));
        }

        // ---------- поделиться результатом подбора ----------

        public static string ShareText(List<PickResult> ranked)
        {
            var sb = new StringBuilder();
            sb.Append(L.T("Подбор стратегии zapret (ZapretGUI v", "zapret strategy test (ZapretGUI v")).Append(SelfUpdate.Current.ToString(3)).Append(")\n");
            int i = 0;
            foreach (var r in ranked.Where(r => !r.Failed).Take(10))
            {
                i++;
                sb.Append(i).Append(". ").Append(r.Name).Append(" — ");
                if (r.IsNew) sb.Append(r.Ok).Append("/").Append(r.Total).Append(L.T(" успешно", " passed")).Append(r.Mode == "std" ? L.T(", пинг ", ", ping ") + r.PingOk + "/" + (r.PingOk + r.PingFail) : "")
                              .Append(r.SpeedKBps > 0 ? ", " + PickResult.FormatSpeed(r.SpeedKBps) : "");
                else sb.Append("YouTube ").Append(r.YtOk).Append("/").Append(r.YtMax).Append(", Discord ").Append(r.DcOk).Append("/").Append(r.DcMax);
                sb.Append("\n");
            }
            return sb.ToString();
        }
    }

    // ================= Страница «Стратегии» =================
    class StrategiesW : WinBase
    {
        readonly MainWindow owner;
        readonly StackPanel list = new StackPanel();
        readonly Dictionary<string, StatStrategy> stats = new Dictionary<string, StatStrategy>();
        readonly Dictionary<string, Tuple<DateTime, PickResult>> lastPick = new Dictionary<string, Tuple<DateTime, PickResult>>();

        public StrategiesW(MainWindow o) : base(L.T("Стратегии", "Strategies"), o)
        {
            owner = o;
            var sp = new StackPanel { Width = 600, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            var lead = W.T(L.T("★ — избранные: стрелки на главном экране листают только их. Нажмите на заметку, чтобы изменить. Двойной клик — включить стратегию.",
                "★ — favorites: the arrows on the main screen cycle through them only. Click a note to edit it. Double-click to turn a strategy on."), W.Small, W.Muted);
            lead.TextWrapping = TextWrapping.Wrap; lead.Margin = new Thickness(0, 4, 0, 10);
            sp.Children.Add(lead);

            try
            {
                DateTime from = DateTime.Today.AddDays(-Stats.KeepDays);
                foreach (var s in Stats.ByStrategy(Stats.Load(from), from, DateTime.Now)) stats[s.Name] = s;
            }
            catch { }
            foreach (var run in PickHistory.Load())
                foreach (var r in run.Results) lastPick[r.Name] = Tuple.Create(run.When, r);

            var head = Cols(); head.Margin = new Thickness(10, 0, 10, 4);
            string[] hd = { "", L.T("Стратегия", "Strategy"), L.T("За 30 дней", "Last 30 days"), L.T("Прошлый подбор", "Last test"), L.T("Заметка", "Note") };
            for (int i = 0; i < hd.Length; i++) { var t = W.T(hd[i], W.Caption, W.Dim); Grid.SetColumn(t, i); head.Children.Add(t); }
            sp.Children.Add(head);
            sp.Children.Add(new ScrollViewer { Content = list, Height = 420, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            Fill();
            var close = new Pill(L.T("Готово", "Done")) { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            close.Click += delegate { Close(); };
            sp.Children.Add(close);
            Root.Child = sp;
        }

        static Grid Cols()
        {
            var g = new Grid();
            foreach (double w in new[] { 28.0, 190, 110, 110 }) g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(w) });
            g.ColumnDefinitions.Add(new ColumnDefinition());
            return g;
        }

        void Fill()
        {
            list.Children.Clear();
            var fav = MainWindow.FavoritesPublic;
            string cur = owner.Current;
            foreach (string name in owner.Zap.Strategies())
            {
                string n = name;
                var g = Cols();
                bool isFav = fav.Contains(n);
                var star = new Clickable { Child = W.T(isFav ? "★" : "☆", W.BodyStrong, isFav ? W.Accent : W.Dim), VerticalAlignment = VerticalAlignment.Center, ToolTip = L.T("Избранная", "Favorite") };
                star.Click += delegate { MainWindow.ToggleFavoritePublic(n); Fill(); };
                g.Children.Add(star);
                var nm = W.T(n, W.Body, n == cur ? W.Accent : W.Text, n == cur); nm.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(nm, 1); g.Children.Add(nm);
                StatStrategy st;
                string pct = stats.TryGetValue(n, out st) && st.YtPct >= 0
                    ? Math.Round(Math.Min(st.YtPct, st.DcPct < 0 ? 100 : st.DcPct)) + "%  ·  " + Hours(st.OnSec) : "—";
                var pt = W.T(pct, W.Small, pct == "—" ? W.Dim : (st.YtPct >= 95 ? W.Accent : st.YtPct >= 70 ? W.Warn : W.Danger)); pt.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(pt, 2); g.Children.Add(pt);
                Tuple<DateTime, PickResult> lp;
                string ls = lastPick.TryGetValue(n, out lp) ? (lp.Item2.Failed ? L.T("не запустилась", "failed") : lp.Item2.IsNew ? lp.Item2.Ok + "/" + lp.Item2.Total : (lp.Item2.YtOk + lp.Item2.DcOk) + "/" + (lp.Item2.YtMax + lp.Item2.DcMax)) + "  ·  " + lp.Item1.ToString("dd.MM") : "—";
                var lt = W.T(ls, W.Small, ls == "—" ? W.Dim : W.Muted); lt.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(lt, 3); g.Children.Add(lt);
                string note = MainWindow.Note(n);
                var noteLink = new FlatLink(note == "" ? L.T("добавить…", "add…") : note) { VerticalAlignment = VerticalAlignment.Center };
                noteLink.Click += delegate
                {
                    string v = InputW.Show(this, L.T("Заметка к «" + n + "»", "Note for “" + n + "”"), L.T("Например: лучшая вечером", "E.g. best in the evening"), MainWindow.Note(n));
                    if (v != null) { MainWindow.SetNote(n, v); Fill(); }
                };
                Grid.SetColumn(noteLink, 4); g.Children.Add(noteLink);
                var row = new Border { Background = W.Surface, CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 0, 10, 0), Height = 38, Margin = new Thickness(0, 0, 0, 4), Child = g, Cursor = Cursors.Hand };
                row.MouseEnter += delegate { row.Background = W.SurfaceHover; };
                row.MouseLeave += delegate { row.Background = W.Surface; };
                row.MouseLeftButtonDown += (s, e) => { if (e.ClickCount == 2) { owner.SelectStrategyPublic(n); Fill(); } };
                list.Children.Add(row);
            }
        }

        static string Hours(double sec)
        {
            double h = sec / 3600;
            return h < 1 ? Math.Max(1, (int)Math.Round(sec / 60)) + L.T(" мин", " min") : Math.Round(h) + L.T(" ч", " h");
        }
    }

    // ================= Свои сайты для проверки (utils\targets.txt) =================
    class TargetsW : WinBase
    {
        readonly string path;
        readonly ListBox box = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), FontSize = W.Body, Height = 260 };
        readonly List<KeyValuePair<string, string>> items = new List<KeyValuePair<string, string>>();
        readonly TextBox input;
        readonly TextBlock error;
        public bool Saved { get; private set; }

        public TargetsW(Window owner, Zapret z) : base(L.T("Сайты для проверки", "Test sites"), owner)
        {
            path = Path.Combine(z.Utils, "targets.txt");
            foreach (var t in FlowTest.LoadTargets(z))
                items.Add(new KeyValuePair<string, string>(t.Name, t.Url != null ? t.Url : "PING:" + t.Ping));
            var sp = new StackPanel { Width = 440, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            var d = W.T(L.T("Эти сайты проверяет подбор стратегии (как «Run Tests» у Flowseal) · utils\\targets.txt. При обновлении zapret файл может замениться — сохраните копию через «Резервную копию».",
                "The strategy test checks these sites (like Flowseal's “Run Tests”) · utils\\targets.txt. A zapret update may replace the file — keep a copy via Backup."), W.Small, W.Muted);
            d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 4, 0, 12);
            sp.Children.Add(d);
            var addRow = new Grid();
            addRow.ColumnDefinitions.Add(new ColumnDefinition()); addRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input = new TextBox { Tag = L.T("сайт, например twitch.tv или https://site.com/page", "site, e.g. twitch.tv or https://site.com/page") };
            input.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Add(); } };
            addRow.Children.Add(input);
            var add = new Pill(L.T("Добавить", "Add"), false, 36) { Margin = new Thickness(8, 0, 0, 0) };
            add.Click += delegate { Add(); };
            Grid.SetColumn(add, 1); addRow.Children.Add(add);
            sp.Children.Add(addRow);
            error = W.T("", W.Caption, W.Danger); error.Margin = new Thickness(2, 4, 0, 6);
            sp.Children.Add(error);
            sp.Children.Add(new Border { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(6), Child = box });
            box.KeyDown += (s, e) => { if (e.Key == Key.Delete) Remove(); };
            var bar = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            var rm = new FlatLink(L.T("Удалить выбранный", "Remove selected")) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            rm.Click += delegate { Remove(); };
            bar.Children.Add(rm);
            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var cancel = new Pill(L.T("Отмена", "Cancel"), true) { MinWidth = 96 };
            var save = new Pill(L.T("Сохранить", "Save")) { MinWidth = 110, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            save.Click += delegate { Save(); };
            btns.Children.Add(cancel); btns.Children.Add(save);
            bar.Children.Add(btns);
            sp.Children.Add(bar);
            Root.Child = sp;
            Refill();
        }

        void Refill()
        {
            box.Items.Clear();
            foreach (var kv in items) box.Items.Add(kv.Key + "   " + kv.Value);
        }

        void Add()
        {
            string s = input.Text.Trim();
            error.Text = "";
            if (s == "") return;
            string url = s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? s : "https://" + s;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || !u.Host.Contains(".")) { error.Text = L.T("Это не похоже на адрес сайта", "This doesn't look like a site address"); return; }
            string key = Regex.Replace(u.Host.Replace("www.", ""), "[^A-Za-z0-9]", "_");
            if (key.Length > 0 && char.IsDigit(key[0])) key = "S_" + key;
            if (items.Any(kv => kv.Key.Equals(key, StringComparison.OrdinalIgnoreCase))) { error.Text = L.T("Такой сайт уже есть", "This site is already there"); return; }
            items.Add(new KeyValuePair<string, string>(key, url));
            input.Text = "";
            Refill();
        }

        void Remove()
        {
            int i = box.SelectedIndex;
            if (i < 0 || i >= items.Count) return;
            items.RemoveAt(i);
            Refill();
        }

        void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.Append("# Сайты для проверки стратегий (utils\\test zapret.ps1 и ZapretGUI). Формат: ИМЯ = \"https://...\" или ИМЯ = \"PING:1.2.3.4\"\r\n");
                foreach (var kv in items) sb.Append(kv.Key).Append(" = \"").Append(kv.Value).Append("\"\r\n");
                if (File.Exists(path)) File.Copy(path, path + ".bak", true);   // прежний файл — рядом, на всякий случай
                File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
                Saved = true;
                Close();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }
    }
}
