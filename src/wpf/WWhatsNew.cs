// «Что нового в zapret»: после обновления zapret (через программу или вручную) — какие стратегии появились и какие убраны.
using System;
using System.Collections.Generic;
using System.Linq;
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

    partial class MainWindow
    {
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
