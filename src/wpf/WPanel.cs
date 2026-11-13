// Главный экран «Панель»: состояние и кнопка, полоса за сегодня, плашки YouTube и Discord,
// главные переключатели плитками и кнопки разделов — всё на одном экране.
using System;
using System.Collections.Generic;
using System.Linq;
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
        internal Func<DateTime, List<StatRec>> StatsSource = Stats.LoadCached;

        void BuildPanelPage()
        {
            var sp = new StackPanel { Margin = new Thickness(20, 2, 20, 0) };
            sp.Children.Add(BuildBanner());   // строка-предупреждение (обновление, проблема, сбой) — скрыта, пока не нужна

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

            // --- переключатели плитками (какие — выбирается в настройках) ---
            var grid = new UniformGrid { Columns = 2, Margin = new Thickness(-5, 5, -5, 0) };
            foreach (string key in tiles)
            {
                var r = RowFor(key);
                r.Height = 54;
                var box = new CardBox { Margin = new Thickness(5) };
                box.Add(r);
                grid.Children.Add(box);
            }
            if (tiles.Count > 0) sp.Children.Add(grid);

            // --- разделы ---
            var acts = new UniformGrid { Columns = 4, Margin = new Thickness(-5, tiles.Count > 0 ? 5 : 10, -5, 0) };
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

        // ---------- Свои плитки и простой режим ----------

        public static readonly string[] TileKeys = { "auto", "watch", "game", "ipset", "guistart", "tray" };
        const string DefaultTiles = "auto,watch,game,ipset";
        bool simple;
        List<string> tiles = new List<string>();
        SettingRowW rowSimple, rowTiles, rowNotify, rowAutoFix;

        void LoadLayoutPrefs()
        {
            simple = Prefs.Get("Simple", "0") == "1";
            string t = Prefs.Get("Tiles", DefaultTiles);
#if TESTHOOKS
            if (snapPath != null)
            {
                simple = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_SIMPLE") == "1";
                t = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_TILES") ?? DefaultTiles;
            }
#endif
            tiles = new List<string>();
            foreach (string k in t.Split(','))
                if (Array.IndexOf(TileKeys, k.Trim()) >= 0 && !tiles.Contains(k.Trim())) tiles.Add(k.Trim());
            if (simple) tiles.Clear();   // в простом режиме плиток нет — всё в настройках
        }

        int TileRows { get { return (tiles.Count + 1) / 2; } }

        SettingRowW RowFor(string key)
        {
            switch (key)
            {
                case "auto": return rowAuto;
                case "watch": return rowWatch;
                case "game": return rowGame;
                case "ipset": return rowIpset;
                case "guistart": return rowGuiStart;
                default: return rowTray;
            }
        }

        // Все строки настроек создаются один раз; потом каждая попадает либо на панель плиткой, либо в настройки.
        void CreateRows()
        {
            double rh = 48;
            rowAuto = new SettingRowW(L.T("Обход с Windows", "Bypass with Windows"), true, "power", L.T("включится сам, как служба", "turns on by itself, as a service"), false, rh);
            rowWatch = new SettingRowW(L.T("Сторож", "Watchdog"), true, "shield", L.T("перезапуск, если обход упал", "restart if the bypass crashes"), false, rh);
            rowGame = new SettingRowW(L.T("Игровой фильтр", "Game filter"), false, "monitor", L.T("для онлайн-игр", "for online games"), false, rh);
            rowIpset = new SettingRowW("IPSet", false, "globe", L.T("обход по IP-адресам", "bypass by IP address"), false, rh);
            rowGuiStart = new SettingRowW(L.T("Программа с Windows", "App with Windows"), true, "monitor", L.T("сразу свёрнутой в трей", "minimized to tray"), false, rh);
            rowTray = new SettingRowW(L.T("Крестик — в трей", "Close to tray"), true, "tray", L.T("выход — через значок в трее", "quit via the tray icon"), false, rh);
            rowLang = new SettingRowW(L.T("Язык", "Language"), false, "globe", "", false, rh);
            rowTheme = new SettingRowW(L.T("Оформление", "Appearance"), false, "palette", "", false, rh);
            rowSimple = new SettingRowW(L.T("Простой режим", "Simple mode"), true, "power", L.T("только кнопка и статус", "just the button and status"), false, rh);
            rowTiles = new SettingRowW(L.T("Плитки на главном экране", "Tiles on the main screen"), false, "layers", L.T("что показывать на панели", "what to show on the panel"), false, rh);
            rowFakes = new SettingRowW(L.T("Fake-файлы", "Fake files"), false, "layers", L.T("подмена пакетов для Discord и игр", "fake packets for Discord and games"), false, rh);
            rowDiag = new SettingRowW(L.T("Диагностика", "Diagnostics"), false, "wrench", L.T("конфликты, прокси, DNS, драйвер", "conflicts, proxy, DNS, driver"), true, rh);
            rowUpdate = new SettingRowW(L.T("Обновления", "Updates"), false, "download", "", false, rh);
            rowNotify = new SettingRowW(L.T("Сообщать о сбоях", "Notify about problems"), true, "alert", L.T("уведомление из трея, когда сайты перестали открываться", "tray message when sites stop opening"), false, rh);
            rowAutoFix = new SettingRowW(L.T("Автовосстановление", "Auto-recovery"), true, "shield", L.T("5 минут не работает — пробовать другие стратегии", "5 minutes down — try other strategies"), false, rh);
            rowNotify.Click += delegate { NotifyOn = !NotifyOn; UpdateUi(); };
            rowAutoFix.Click += delegate
            {
                AutoFixOn = !AutoFixOn; autoTried.Clear(); badStreak = 0; UpdateUi();
                Journal.Add(AutoFixOn ? L.T("Автовосстановление включено", "Auto-recovery on") : L.T("Автовосстановление выключено", "Auto-recovery off"));
            };

            rowAuto.Click += delegate { ToggleAutostart(); };
            rowWatch.Click += delegate
            {
                WatchdogOn = !WatchdogOn; UpdateUi();
                Journal.Add(WatchdogOn ? L.T("Сторож включён", "Watchdog on") : L.T("Сторож выключен", "Watchdog off"));
            };
            rowGame.Click += delegate { ShowGameMenu(); };
            rowIpset.Click += delegate { ShowIpsetMenu(); };
            rowGuiStart.Click += delegate { ToggleGuiAutostart(); };
            rowTray.Click += delegate { CloseToTray = !CloseToTray; UpdateUi(); };
            rowLang.Click += delegate { ShowLangMenu(); };
            rowTheme.Click += delegate { ShowThemeMenu(); };
            rowSimple.Click += delegate { SetSimple(!simple); };
            rowTiles.Click += delegate { ShowTilesMenu(); };
            rowFakes.Click += delegate { ShowFakesMenu(); };
            rowDiag.Click += delegate { OpenDiag(); };
            rowUpdate.Click += delegate { ShowUpdatesMenu(); };
        }

        static string TileName(string key)
        {
            switch (key)
            {
                case "auto": return L.T("Обход с Windows", "Bypass with Windows");
                case "watch": return L.T("Сторож", "Watchdog");
                case "game": return L.T("Игровой фильтр", "Game filter");
                case "ipset": return "IPSet";
                case "guistart": return L.T("Программа с Windows", "App with Windows");
                default: return L.T("Крестик — в трей", "Close to tray");
            }
        }

        void ShowTilesMenu()
        {
            var m = WMenu.Create();
            foreach (string key in TileKeys)
            {
                string k = key;
                WMenu.Add(m, TileName(k), tiles.Contains(k), () =>
                {
                    var next = TileKeys.Where(x => x == k ? !tiles.Contains(k) : tiles.Contains(x)).ToList();   // порядок — как в списке
                    Prefs.Set("Tiles", string.Join(",", next));
                    RebuildLayout();
                });
            }
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Как было по умолчанию", "Reset to default"), false, () => { Prefs.Set("Tiles", DefaultTiles); RebuildLayout(); });
            WMenu.ShowBelowRight(m, rowTiles);
        }

        void SetSimple(bool on)
        {
            Prefs.Set("Simple", on ? "1" : "0");
            Journal.Add(on ? L.T("Простой режим включён", "Simple mode on") : L.T("Простой режим выключен", "Simple mode off"));
            RebuildLayout();
        }

        void RebuildLayout()
        {
            BuildUi();
            UpdateUi();
        }

        // Простой режим: только большая кнопка и статус обычными словами.
        void BuildSimplePage()
        {
            var outer = new Grid();
            var top = BuildBanner();
            ((Border)top).Margin = new Thickness(16, 0, 16, 0);
            top.VerticalAlignment = VerticalAlignment.Top;
            outer.Children.Add(top);
            var sp = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 30) };
            outer.Children.Add(sp);
            power = new PowerHero(140);
            power.Click += delegate { TogglePower(); };
            sp.Children.Add(power);
            panelTitle = power.TitleBlock;
            panelSub = power.SubtitleBlock;
            panelTitle.Margin = new Thickness(0, 4, 0, 0);
            panelSub.TextWrapping = TextWrapping.Wrap; panelSub.TextAlignment = TextAlignment.Center; panelSub.TextTrimming = TextTrimming.None;
            panelSub.MaxWidth = 300; panelSub.FontSize = W.Body; panelSub.Margin = new Thickness(0, 6, 0, 0);
            mainPage.Children.Add(outer);
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
                    s = simple ? L.T("выключите и снова включите обход; если не помогло — попросите помочь с настройкой", "turn the bypass off and on again; if that doesn't help, ask for help with the setup")
                               : L.T("смените стратегию стрелками или нажмите «Подобрать стратегию»", "switch the strategy or click “Find a strategy”");
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
