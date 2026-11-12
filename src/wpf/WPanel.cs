// Главный экран «Панель»: состояние и кнопка, полоса за сегодня, плашки YouTube и Discord,
// главные переключатели плитками и кнопки разделов — всё на одном экране.
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace ZapretGui
{
    // Кнопка раздела в нижнем ряду: значок и подпись.
    class ActionTile : Clickable
    {
        readonly Border hover;
        readonly Ico icon;
        readonly TextBlock tb;
        public ActionTile(string iconName, string text)
        {
            Background = W.Surface;
            CornerRadius = new CornerRadius(12);
            Height = 42;
            var g = new Grid();
            hover = new Border { Background = W.SurfaceHover, CornerRadius = new CornerRadius(12), Opacity = 0 };
            g.Children.Add(hover);
            var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            icon = new Ico(iconName, 16, W.Muted) { VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(icon);
            tb = W.T(text, W.Body, W.Text);
            tb.Margin = new Thickness(8, 0, 0, 1); tb.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(tb);
            g.Children.Add(row);
            Child = g;
        }
        protected override void OnHot(bool on) { Fx.To(hover, OpacityProperty, on ? 1 : 0, 140); icon.Brush = on ? W.Text : W.Muted; }
    }

    partial class MainWindow
    {
        TextBlock panelTitle, panelSub, todaySum;
        StatBar todayBar;
        StatAxis todayAxis;
        bool netUp = true;
        internal Func<DateTime, List<StatRec>> StatsSource = Stats.Load;

        void BuildPanelPage()
        {
            var sp = new StackPanel { Margin = new Thickness(20, 2, 20, 0) };

            // --- состояние: кнопка, крупный текст, «Подобрать» ---
            var hero = new Border { Background = W.Surface, CornerRadius = new CornerRadius(16), Padding = new Thickness(6, 8, 16, 8) };
            var hg = new Grid();
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hg.ColumnDefinitions.Add(new ColumnDefinition());
            hg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            power = new PowerHero(78, true) { VerticalAlignment = VerticalAlignment.Center };
            power.Click += delegate { TogglePower(); };
            hg.Children.Add(power);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 12, 0) };
            panelTitle = W.T("", W.Status, W.Text, true);
            panelTitle.TextWrapping = TextWrapping.Wrap; panelTitle.TextTrimming = TextTrimming.None;
            panelSub = W.T("", W.Small, W.Muted);
            panelSub.TextWrapping = TextWrapping.Wrap; panelSub.Margin = new Thickness(0, 3, 0, 0);
            texts.Children.Add(panelTitle); texts.Children.Add(panelSub);
            Grid.SetColumn(texts, 1); hg.Children.Add(texts);
            var pick = new Pill(L.T("Подобрать стратегию", "Find a strategy"), true, 38) { VerticalAlignment = VerticalAlignment.Center };
            pick.Click += delegate { AutoPick(); };
            Grid.SetColumn(pick, 2); hg.Children.Add(pick);
            hero.Child = hg;
            sp.Children.Add(hero);

            // --- стратегия ---
            picker = new StrategyPickerW { Margin = new Thickness(0, 10, 0, 0) };
            picker.Prev += delegate { StepStrategy(-1); };
            picker.Next += delegate { StepStrategy(1); };
            picker.Open += delegate { ShowStrategyMenu(picker, false); };
            sp.Children.Add(picker);

            // --- плашки YouTube и Discord ---
            var cards = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            cards.ColumnDefinitions.Add(new ColumnDefinition());
            cardYt = new ServiceCardW("YouTube", "play");
            cardDc = new ServiceCardW("Discord", "chat");
            Grid.SetColumn(cardDc, 2);
            foreach (var c in new[] { cardYt, cardDc })
            {
                c.Click += delegate { CheckNet(); };
                c.ToolTip = "";
                c.ToolTipOpening += (s, e) => { ((FrameworkElement)s).ToolTip = NetTooltip(); };
                cards.Children.Add(c);
            }
            sp.Children.Add(cards);

            // --- сегодня ---
            var today = new Clickable { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 10, 14, 6), Margin = new Thickness(0, 10, 0, 0) };
            today.Click += delegate { ShowStats(); };
            var tg = new StackPanel();
            var th = new Grid();
            var tl = new StackPanel { Orientation = Orientation.Horizontal };
            tl.Children.Add(new Ico("chart", 15, W.Muted) { VerticalAlignment = VerticalAlignment.Center });
            var tt = W.T(L.T("Сегодня", "Today"), W.Body, W.Text); tt.Margin = new Thickness(7, 0, 0, 1);
            tl.Children.Add(tt);
            th.Children.Add(tl);
            todaySum = W.T("", W.Small, W.Muted);
            todaySum.HorizontalAlignment = HorizontalAlignment.Right; todaySum.VerticalAlignment = VerticalAlignment.Center;
            th.Children.Add(todaySum);
            tg.Children.Add(th);
            todayBar = new StatBar(false) { Height = 12, Margin = new Thickness(0, 8, 0, 0) };
            tg.Children.Add(todayBar);
            todayAxis = new StatAxis { Margin = new Thickness(0, 2, 0, 0) };
            tg.Children.Add(todayAxis);
            today.Child = tg;
            sp.Children.Add(today);

            // --- переключатели плитками ---
            rowAuto = new SettingRowW(L.T("Запуск с Windows", "Start with Windows"), true, "power", L.T("обход включится сам", "bypass turns on by itself"), false, 54);
            rowWatch = new SettingRowW(L.T("Сторож", "Watchdog"), true, "shield", L.T("перезапуск, если обход упал", "restart if the bypass crashes"), false, 54);
            rowGame = new SettingRowW(L.T("Игровой фильтр", "Game filter"), false, "monitor", L.T("для онлайн-игр", "for online games"), false, 54);
            rowIpset = new SettingRowW("IPSet", false, "globe", L.T("обход по IP-адресам", "bypass by IP address"), false, 54);
            rowAuto.Click += delegate { ToggleAutostart(); };
            rowWatch.Click += delegate
            {
                WatchdogOn = !WatchdogOn; UpdateUi();
                Journal.Add(WatchdogOn ? L.T("Сторож включён", "Watchdog on") : L.T("Сторож выключен", "Watchdog off"));
            };
            rowGame.Click += delegate { ShowGameMenu(); };
            rowIpset.Click += delegate { ShowIpsetMenu(); };
            var tiles = new UniformGrid { Columns = 2, Margin = new Thickness(-5, 5, -5, 0) };
            foreach (var r in new[] { rowAuto, rowWatch, rowGame, rowIpset })
            {
                var box = new CardBox { Margin = new Thickness(5) };
                box.Add(r);
                tiles.Children.Add(box);
            }
            sp.Children.Add(tiles);

            // --- разделы ---
            var acts = new UniformGrid { Columns = 4, Margin = new Thickness(-5, 5, -5, 0) };
            ActionTile lists = null, more = null;
            Action<ActionTile, Action> add = (t, a) => { t.Margin = new Thickness(5); t.Click += delegate { a(); }; acts.Children.Add(t); };
            lists = new ActionTile("list", L.T("Списки", "Lists"));
            add(lists, () => ShowListsMenu(lists));
            add(new ActionTile("wrench", L.T("Диагностика", "Diagnostics")), OpenDiag);
            add(new ActionTile("file", L.T("Журнал", "Log")), ShowJournal);
            more = new ActionTile("dots", L.T("Ещё", "More"));
            add(more, () => ShowMoreMenu(more));
            sp.Children.Add(acts);

            mainPage.Children.Add(sp);
            RefreshToday();
        }

        void ShowListsMenu(FrameworkElement anchor)
        {
            var m = WMenu.Create();
            WMenu.Add(m, L.T("Мои домены — тоже через обход", "My domains — also bypassed"), false, () => EditList("list-general-user.txt"));
            WMenu.Add(m, L.T("Исключения — сайты без обхода", "Exclusions — sites left alone"), false, () => EditList("list-exclude-user.txt"));
            WMenu.Add(m, L.T("IP-исключения — адреса без обхода", "IP exclusions — IPs left alone"), false, () => EditList("ipset-exclude-user.txt"));
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Обновить список IPSet", "Update IPSet list"), false, UpdateIpset);
            WMenu.ShowAbove(m, anchor);
        }

        // Крупный текст состояния обычными словами.
        void UpdatePanel()
        {
            string t, s; Brush b;
            if (picking || busy) { t = statusText; s = subText; b = W.Muted; }
            else if (running)
            {
                string tail = svc ? L.T(" · служба", " · service") : "";
                if (netChecking && netYt < 0) { t = L.T("Обход включён", "Bypass is on"); s = L.T("проверяю YouTube и Discord…", "checking YouTube and Discord…"); b = W.Accent; }
                else if (netYt < 0) { t = L.T("Обход включён", "Bypass is on"); s = L.T("нажмите на YouTube или Discord, чтобы проверить", "click YouTube or Discord to check") + tail; b = W.Accent; }
                else if (!netUp) { t = L.T("Нет интернета", "No internet"); s = L.T("не открывается ни один сайт — проверьте подключение", "no site opens — check your connection"); b = W.Warn; }
                else if (netYt >= 0.99f && netDc >= 0.99f) { t = L.T("Обход работает", "Bypass works"); s = L.T("YouTube и Discord открываются", "YouTube and Discord open") + tail; b = W.Accent; }
                else
                {
                    var bad = new List<string>();
                    if (netYt < 0.99f) bad.Add("YouTube");
                    if (netDc < 0.99f) bad.Add("Discord");
                    bool none = (netYt >= 0.99f || netYt <= 0.01f) && (netDc >= 0.99f || netDc <= 0.01f);   // плохие не открываются совсем
                    t = string.Join(L.T(" и ", " and "), bad) + (none
                        ? (bad.Count > 1 ? L.T(" не открываются", " don't open") : L.T(" не открывается", " doesn't open"))
                        : L.T(" работает с перебоями", " is unstable"));
                    s = L.T("смените стратегию стрелками или нажмите «Подобрать стратегию»", "switch the strategy or click “Find a strategy”");
                    b = W.Warn;
                }
            }
            else if (svc) { t = L.T("Служба остановлена", "Service stopped"); s = L.T("нажмите кнопку, чтобы перезапустить", "click the button to restart"); b = W.Warn; }
            else { t = L.T("Обход выключен", "Bypass is off"); s = L.T("нажмите кнопку, чтобы включить", "click the button to turn it on"); b = W.Text; }
            panelTitle.Text = t; panelTitle.Foreground = b; panelSub.Text = s;
        }

        // Полоса «Сегодня» и короткая сводка.
        internal void RefreshToday()
        {
            if (todayBar == null) return;
            DateTime from = DateTime.Today, to = from.AddDays(1);
            List<StatRec> recs;
            try { recs = StatsSource(from); } catch { recs = new List<StatRec>(); }
            todayBar.Set(Stats.Segments(recs, 3, from, to), from, to, new List<DateTime>(), false);
            var ticks = new List<Tuple<DateTime, string>>();
            for (int h = 0; h <= 24; h += 6) ticks.Add(Tuple.Create(from.AddHours(h), h.ToString("00") + ":00"));
            todayAxis.Set(ticks, from, to);
            double yt = Stats.UpPct(recs, 0, from, to), dc = Stats.UpPct(recs, 1, from, to);
            if (yt < 0 && dc < 0) { todaySum.Text = L.T("нажмите, чтобы открыть статистику", "click to open statistics"); return; }
            int outs = Stats.Outages(recs, 0, from, to) + Stats.Outages(recs, 1, from, to);
            Func<double, string> pct = v => v < 0 ? "—" : (v > 99 && v < 100 ? "99" : Math.Round(v).ToString()) + "%";
            todaySum.Text = "YouTube " + pct(yt) + "  ·  Discord " + pct(dc) + "  ·  " +
                (outs == 0 ? L.T("без сбоев", "no outages") : L.T("сбоев: ", "outages: ") + outs);
        }
    }
}
