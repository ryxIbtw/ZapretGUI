// «Что нового в zapret»: после обновления zapret (через программу или вручную) — какие стратегии появились и какие убраны.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZapretGui
{
    class WhatsNewW : WinBase
    {
        public bool PickNew { get; private set; }

        public WhatsNewW(Window owner, string oldVer, string newVer, List<string> added, List<string> removed)
            : base(L.T("Что нового в zapret", "What's new in zapret"), owner)
        {
            var sp = new StackPanel { Width = 420, Margin = new Thickness(22, 18, 22, 18) };
            sp.Children.Add(W.T(L.T("Что нового в zapret v", "What's new in zapret v") + newVer, W.Title, W.Text, true));
            var lead = W.T(oldVer == "" ? L.T("Список стратегий изменился.", "The list of strategies has changed.")
                : L.T("Было v" + oldVer + ". Вот что изменилось в стратегиях.", "Was v" + oldVer + ". Here is what changed in the strategies."), W.Small, W.Muted);
            lead.TextWrapping = TextWrapping.Wrap; lead.Margin = new Thickness(0, 4, 0, 14);
            sp.Children.Add(lead);

            Action<string, List<string>, string, Brush> section = (title, items, hint, color) =>
            {
                if (items.Count == 0) return;
                var h = W.T(title + "  ·  " + items.Count, W.Caption, W.Muted, true);
                h.Margin = new Thickness(4, sp.Children.Count > 2 ? 12 : 0, 0, 6);
                sp.Children.Add(h);
                var card = new Border { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 8, 14, 8) };
                var list = new StackPanel();
                foreach (string s in items.Take(12))
                {
                    var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 3, 0, 3) };
                    row.Children.Add(new Border { Width = 7, Height = 7, CornerRadius = new CornerRadius(4), Background = color, VerticalAlignment = VerticalAlignment.Center });
                    var t = W.T(s, W.Body, W.Text); t.Margin = new Thickness(10, 0, 0, 1);
                    row.Children.Add(t);
                    list.Children.Add(row);
                }
                if (items.Count > 12) list.Children.Add(W.T(L.T("и ещё ", "and ") + (items.Count - 12), W.Small, W.Dim));
                card.Child = list;
                sp.Children.Add(card);
                if (hint != null)
                {
                    var ht = W.T(hint, W.Caption, W.Muted); ht.TextWrapping = TextWrapping.Wrap; ht.Margin = new Thickness(4, 6, 0, 0);
                    sp.Children.Add(ht);
                }
            };
            section(L.T("Новые стратегии", "New strategies"), added, null, W.Accent);
            section(L.T("Убраны", "Removed"), removed,
                oldVer == "" ? null : L.T("Файлы не удалены — они в папке old-strategies\\v" + oldVer, "Files are not deleted — they are in old-strategies\\v" + oldVer), W.Dim);

            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var close = new Pill(L.T("Закрыть", "Close"), added.Count > 0) { MinWidth = 100 };
            close.Click += delegate { Close(); };
            bar.Children.Add(close);
            if (added.Count > 0)
            {
                var pick = new Pill(L.T("Проверить новые", "Test the new ones")) { MinWidth = 150, Margin = new Thickness(10, 0, 0, 0) };
                pick.Click += delegate { PickNew = true; Close(); };
                bar.Children.Add(pick);
            }
            sp.Children.Add(bar);
            Root.Child = sp;
        }
    }

    // «Что нового в ZapretGUI»: описание релиза с GitHub после самообновления.
    class GuiNewsW : WinBase
    {
        public GuiNewsW(Window owner, string version, string markdown) : base(L.T("Что нового в ZapretGUI", "What's new in ZapretGUI"), owner)
        {
            var sp = new StackPanel { Width = 460, Margin = new Thickness(22, 18, 22, 18) };
            sp.Children.Add(W.T(L.T("Что нового в ZapretGUI v", "What's new in ZapretGUI v") + version, W.Title, W.Text, true));
            var lead = W.T(L.T("Программа обновилась. Вот что изменилось.", "The app has been updated. Here is what changed."), W.Small, W.Muted);
            lead.Margin = new Thickness(0, 4, 0, 10);
            sp.Children.Add(lead);
            var body = new StackPanel();
            bool skip = false;
            foreach (string raw in (markdown ?? "").Replace("\r", "").Split('\n'))
            {
                string line = raw.Trim();
                if (line == "---") break;   // дальше — английский кратко
                string text = line.Replace("**", "").Replace("`", "");
                if (line.StartsWith("#"))
                {
                    string head = text.TrimStart('#', ' ');
                    skip = head == "Установка" || head == "Installation";   // как обновиться — уже не нужно
                    if (skip) continue;
                }
                if (line == "" || skip) continue;
                if (line.StartsWith("#"))
                {
                    var h = W.T(text.TrimStart('#', ' '), W.Caption, W.Muted, true);
                    h.Margin = new Thickness(2, body.Children.Count == 0 ? 2 : 12, 0, 4);
                    body.Children.Add(h);
                    continue;
                }
                bool bullet = text.StartsWith("- ") || text.StartsWith("* ");
                if (bullet) text = text.Substring(2);
                var row = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(bullet ? 16 : 0) });
                row.ColumnDefinitions.Add(new ColumnDefinition());
                if (bullet) row.Children.Add(new Border { Width = 6, Height = 6, CornerRadius = new CornerRadius(3), Background = W.Accent, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(2, 7, 0, 0) });
                var t = W.T(text, W.Body, W.Text); t.TextWrapping = TextWrapping.Wrap; t.TextTrimming = TextTrimming.None;
                Grid.SetColumn(t, 1); row.Children.Add(t);
                body.Children.Add(row);
            }
            var card = new Border { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(16, 10, 16, 12),
                Child = new ScrollViewer { Content = body, MaxHeight = 380, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled } };
            sp.Children.Add(card);
            var bar = new Grid { Margin = new Thickness(0, 16, 0, 0) };
            var gh = new FlatLink(L.T("Открыть на GitHub", "Open on GitHub")) { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center };
            gh.Click += delegate { try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(AboutW.RepoUrl + "/releases/tag/v" + version) { UseShellExecute = true }); } catch { } };
            bar.Children.Add(gh);
            var ok = new Pill(L.T("Понятно", "Got it")) { MinWidth = 120, HorizontalAlignment = HorizontalAlignment.Right };
            ok.Click += delegate { Close(); };
            bar.Children.Add(ok);
            sp.Children.Add(bar);
            Root.Child = sp;
        }
    }

    partial class MainWindow
    {
        Tuple<string, string> pendingGuiNews;

        // Первый запуск новой версии ZapretGUI — взять описание релиза с GitHub и показать.
        internal void CheckGuiUpdated()
        {
            if (snapPath != null) return;
            var cur = SelfUpdate.Current;
            string last = Prefs.Get("LastVersion", null);
            Prefs.Set("LastVersion", cur.ToString(3));
            // версии до 2.3.1 этого не запоминали: если программой уже пользовались — значит, обновились
            bool updated = last == null ? Prefs.Get("WelcomeShown", "0") == "1" : SelfUpdate.Parse(last) < cur;
            if (!updated) return;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string notes = SelfUpdate.ReleaseNotes(cur);
                if (string.IsNullOrEmpty(notes)) return;
                Post(() => { pendingGuiNews = Tuple.Create(cur.ToString(3), notes); if (IsVisible) ShowGuiNews(); });
            });
        }

        internal void ShowGuiNews()
        {
            var p = pendingGuiNews;
            if (p == null || busy) return;
            pendingGuiNews = null;
            new GuiNewsW(this, p.Item1, p.Item2).ShowDialog();
        }

        Tuple<string, string, List<string>, List<string>> pendingWhatsNew;

        // Сравнивает стратегии с прошлым запуском. Если сменилась версия zapret и список стратегий — покажет «Что нового».
        internal void CheckStrategyChanges()
        {
            if (snapPath != null) return;
            List<string> cur;
            try { cur = z.Strategies(); } catch { return; }
            string ver = z.LocalVersion;
            string known = Prefs.Get("KnownStrategies", null), kv = Prefs.Get("KnownZapretVersion", "");
            Prefs.Set("KnownStrategies", string.Join("|", cur));
            Prefs.Set("KnownZapretVersion", ver);
            if (known == null || ver == kv) return;   // первый запуск или свои .bat — ничего не показываем
            var old = known.Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            var added = cur.Where(s => !old.Contains(s)).ToList();
            var removed = old.Where(s => !cur.Contains(s)).ToList();
            if (added.Count == 0 && removed.Count == 0) return;
            Journal.Add(L.T("zapret v", "zapret v") + kv + " → v" + ver + L.T(": новых стратегий ", ": new strategies ") + added.Count + L.T(", убрано ", ", removed ") + removed.Count);
            pendingWhatsNew = Tuple.Create(kv, ver, added, removed);
            if (IsVisible) ShowWhatsNew();
        }

        void ShowPendingNews() { ShowGuiNews(); ShowWhatsNew(); }

        internal void ShowWhatsNew()
        {
            var p = pendingWhatsNew;
            if (p == null || busy) return;
            pendingWhatsNew = null;
            var w = new WhatsNewW(this, p.Item1, p.Item2, p.Item3, p.Item4);
            w.ShowDialog();
            if (w.PickNew) AutoPick(p.Item3);
        }
    }
}
