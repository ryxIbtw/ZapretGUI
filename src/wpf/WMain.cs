// Главное окно на WPF: построение страниц, переходы между ними, трей, мини-панель, состояние на экране.
// Действия (включение, подбор, сторож, обновления, меню) — в WMainLogic.cs.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace ZapretGui
{
    partial class MainWindow : Window
    {
        double Wd { get { return simple ? 380 : 600; } }
        double Ht { get { return simple ? 624 : 474 + 72 * TileRows + ServiceRowHeight; } }   // 2 ряда плиток — 618
        readonly Zapret z;
        readonly string snapPath;
        bool startHidden;

        // элементы
        readonly ScaleTransform rootScale = new ScaleTransform(1, 1);
        Grid pages, mainPage, settingsPage, welcomePage;
        PowerHero power;
        ServiceCardW cardYt, cardDc;
        StrategyPickerW picker;
        SettingRowW rowAuto, rowGame, rowIpset, rowLang, rowTheme, rowGuiStart, rowTray, rowWatch, rowDiag, rowFakes, rowUpdate;
        IconButton gear, themeBtn;
        TextBlock version, hint;
        WF.NotifyIcon tray;
        System.Drawing.Icon iconOn, iconOff, iconBusy, iconBad, iconApp;
        FlyoutW flyout;
        DateTime flyoutClosedAt = DateTime.MinValue;
        bool settingsOpen, welcomeActive;
        int settingsTab;   // открытая вкладка настроек
        readonly DispatcherTimer poll = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        readonly DispatcherTimer hintTimer = new DispatcherTimer();

        // текст состояния (его же показывает мини-панель)
        string statusText = "", subText = "";
        Brush statusBrush = W.Text;

        public static readonly int WM_SHOWME = RegisterWindowMessage("ZapretGUI_ShowMe");

        static bool CloseToTray { get { return Prefs.Get("CloseToTray", "1") == "1"; } set { Prefs.Set("CloseToTray", value ? "1" : "0"); } }
        static bool WatchdogOn { get { return Prefs.Get("Watchdog", "1") == "1"; } set { Prefs.Set("Watchdog", value ? "1" : "0"); } }
        static string ThemeMode { get { string m = Prefs.Get("Theme", "system"); return Theme.Modes.Contains(m) ? m : "system"; } }
        static int UiScalePref { get { int v; return int.TryParse(Prefs.Get("Scale", "100"), out v) && Theme.Scales.Contains(v) ? v : 100; } }

        public MainWindow(Zapret zapret, string snap, bool hidden)
        {
            z = zapret;
            snapPath = snap;
            startHidden = hidden && snap == null;
            Journal.Enabled = snap == null;
            Stats.Enabled = snap == null;
            Fx.Off = Prefs.Get("NoAnim", "0") == "1";
            L.Init();
            Theme.SetScale(1f, UiScalePref / 100f);   // DPI WPF учитывает сам
            Theme.AccentName = Prefs.Get("Accent", "auto");
            Theme.ApplyMode(ThemeMode);
#if TESTHOOKS
            if (snap != null)
            {
                settingsOpen = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_PAGE") == "settings";
                string acc = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_ACCENT"), th = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_THEME");
                string sc = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_SCALE");
                string lang = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_LANG");
                if (lang != null) L.En = lang == "en";
                if (acc != null) Theme.AccentName = acc;
                if (sc != null) Theme.SetScale(1f, int.Parse(sc) / 100f);
                if (th != null) Theme.ApplyMode(th); else Theme.Apply(Theme.IsLight);
            }
#endif
            W.Sync();
            SystemEvents.UserPreferenceChanged += OnSystemPrefs;

            Title = "Zapret by " + BrandHeader.ZapretAuthor + "  ·  GUI by " + BrandHeader.Author;
            Background = W.Bg;
            ResizeMode = ResizeMode.CanMinimize;
            SizeToContent = SizeToContent.WidthAndHeight;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            FontFamily = W.TextFont;

            BuildTray();
            MakeIcons();
            LoadState();
            BuildUi();
            RefreshState();
            expectRunning = running;
            if (snapPath == null) WatchServiceOnStart();
            power.SetOn(running, false);
            Journal.Add(L.T("Программа запущена, ZapretGUI v", "App started, ZapretGUI v") + SelfUpdate.Current.ToString(3) + ", zapret v" + z.LocalVersion +
                (running ? L.T(" · обход уже включён: ", " · bypass already on: ") + (activeStrategy ?? "?") : L.T(" · обход выключен", " · bypass off")));

            poll.Tick += delegate
            {
                if (busy) return;
                RefreshState();
                Watchdog();
                MaybeAutoUpdateIpset();
                CheckAutoApps();
                CheckPause();
                MaybeWeeklySummary();
                // раз в минуту проверить сайты (но не во время полноэкранной игры, если включена «Тишина в играх»)
                if (!netChecking && (DateTime.Now - lastNet).TotalSeconds > 60 && !(GameQuiet && FullscreenAppRunning())) CheckNet();
            };
            hintTimer.Tick += delegate { hintTimer.Stop(); if (hint != null) hint.Text = ""; };

            SourceInitialized += delegate
            {
                WinBase.TitleBar(this);
                var src = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                if (src != null) src.AddHook(WndProc);
                ApplyHotkey();
            };
            StateChanged += delegate { if (WindowState == WindowState.Minimized && tray != null && tray.Visible) Hide(); };
            if (snapPath == null) RestorePosition();   // открыть там же, где закрыли
            // Ctrl + колесо мыши — масштаб интерфейса
            PreviewMouseWheel += (s, e) =>
            {
                if ((System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Control) == 0) return;
                e.Handled = true;
                int i = Array.IndexOf(Theme.Scales, UiScalePref);
                int n = Math.Max(0, Math.Min(Theme.Scales.Length - 1, i + (e.Delta > 0 ? 1 : -1)));
                if (n != i) { SetScale(Theme.Scales[n]); Toast(L.T("Масштаб ", "Scale ") + Theme.Scales[n] + "%"); }
            };
            // выход из Windows / перезагрузка: закрываться полностью, а не прятаться в трей
            if (Application.Current != null) Application.Current.SessionEnding += delegate { exitRequested = true; };

            if (snapPath == null)
            {
                poll.Start();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    bool a = GuiAutostart.Enabled;
                    bool sch = ScheduleOn;
                    Post(() => { guiAutostart = a; scheduleOn = sch; UpdateUi(); });
                });
                CheckNet();
                if (z.UpdatesEnabled) CheckUpdates();
                CheckStrategyChanges();   // zapret обновили, пока программы не было? — покажем, когда окно появится
                CheckGuiUpdated();        // сама программа обновилась — покажем описание релиза
                QuietDiagSoon();          // через 8 секунд тихо проверить конфликты (только чтение)
                WatchNetwork();           // сменилась сеть — включить профиль, выбранный для неё
                ContentRendered += delegate { Dispatcher.BeginInvoke((Action)ShowPendingNews); };
            }
            new WindowInteropHelper(this).EnsureHandle();   // окно существует даже при запуске в трее
        }

        public bool StartHidden { get { return startHidden; } }

        void Post(Action a)
        {
            try { Dispatcher.BeginInvoke(a); } catch { }
        }

        // Тема Windows поменялась — подстроиться, если выбрано «Как в Windows».
        void OnSystemPrefs(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General && e.Category != UserPreferenceCategory.Accessibility && e.Category != UserPreferenceCategory.Color) return;
            Post(() =>
            {
                if (ThemeMode != "system") return;
                bool hc = Theme.SystemHighContrast;
                if ((Theme.Flavor == "contrast") == hc && (hc || Theme.ResolveLight("system") == Theme.IsLight)) return;
                Theme.ApplyMode("system");
                Recolor();
            });
        }

        // ================= Построение интерфейса =================

        void BuildUi()
        {
            LoadLayoutPrefs();
            cardYt = cardDc = null; picker = null; todayBar = null; banner = null;
            var root = new Grid { Width = Wd, Height = Ht, Background = W.Bg, LayoutTransform = rootScale };
            rootGrid = root;
            rootScale.ScaleX = rootScale.ScaleY = Theme.UiScale;
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition());
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(26) });

            // --- шапка ---
            var head = new Grid { Margin = new Thickness(22, 10, 16, 6) };
            head.Children.Add(new BrandHeader { HorizontalAlignment = HorizontalAlignment.Left });
            var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 6, 0, 0) };
            version = W.T("", W.Small, W.Dim);
            version.VerticalAlignment = VerticalAlignment.Center;
            version.Margin = new Thickness(0, 0, 8, 0);
            version.MouseLeftButtonUp += delegate { if (newVersion != null) UpdateZapret(); };
            gear = new IconButton("gear");
            gear.Click += delegate
            {
                if (welcomeActive) { DismissWelcome(); return; }
                settingsOpen = !settingsOpen;
                ShowPage(true);
            };
            themeBtn = new IconButton(Theme.IsLight ? "sun" : "moon") { Margin = new Thickness(4, 0, 0, 0) };
            themeBtn.Click += delegate { SetThemeMode(Theme.IsLight ? (Theme.Flavor == "warm" ? "amoled" : "dark") : (Theme.Flavor == "amoled" ? "warm" : "light")); };
            right.Children.Add(version); right.Children.Add(gear); right.Children.Add(themeBtn);
            head.Children.Add(right);
            root.Children.Add(head);

            // --- страницы ---
            pages = new Grid { ClipToBounds = true };
            Grid.SetRow(pages, 1);
            root.Children.Add(pages);
            mainPage = Page(); settingsPage = Page(); welcomePage = Page();
            pages.Children.Add(mainPage); pages.Children.Add(settingsPage); pages.Children.Add(welcomePage);
            CreateRows();
            if (simple) BuildSimplePage(); else BuildPanelPage();
            BuildSettingsPage();
            BuildWelcomePage();

            hint = W.T("", W.Small, W.Muted);
            hint.HorizontalAlignment = HorizontalAlignment.Center;
            hint.VerticalAlignment = VerticalAlignment.Center;
            hint.Margin = new Thickness(16, 0, 16, 4);
            Grid.SetRow(hint, 2);
            root.Children.Add(hint);

            Content = root;
            ShowPage(false);
        }

        static Grid Page()
        {
            return new Grid { RenderTransform = new TranslateTransform(), Visibility = Visibility.Collapsed, Background = W.Bg };
        }

        // Строка ссылок через «·».
        static StackPanel Footer(params Tuple<string, Action<FlatLink>>[] links)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            for (int i = 0; i < links.Length; i++)
            {
                if (i > 0) { var dot = W.T("·", W.Small, W.Dim); dot.Margin = new Thickness(6, 2, 6, 0); sp.Children.Add(dot); }
                var l = new FlatLink(links[i].Item1);
                var act = links[i].Item2;
                l.Click += delegate { act(l); };
                sp.Children.Add(l);
            }
            return sp;
        }

        void BuildSettingsPage()
        {
            var sp = new StackPanel { Margin = new Thickness(20, 0, 20, 0) };
            // строки, которые не стоят плитками на панели, — здесь
            Func<string, bool> here = k => !tiles.Contains(k);

            // вкладки вместо одной длинной страницы
            var cGeneral = new CardBox(); var cBypass = new CardBox(); var cLook = new CardBox(); var cService = new CardBox();
            cGeneral.Add(rowLang); cGeneral.Add(rowSimple);
            if (here("guistart")) cGeneral.Add(rowGuiStart);
            if (here("tray")) cGeneral.Add(rowTray);
            cGeneral.Add(rowHotkey); cGeneral.Add(rowProfiles); cGeneral.Add(rowAutoApps); cGeneral.Add(rowNotify); cGeneral.Add(rowWeekly);

            foreach (string k in new[] { "auto", "watch", "game", "ipset" }) if (here(k)) cBypass.Add(RowFor(k));
            cBypass.Add(rowAutoFix); cBypass.Add(rowSchedule); cBypass.Add(rowIpsetAuto); cBypass.Add(rowGameQuiet); cBypass.Add(rowFakes);

            cLook.Add(rowTheme);
            if (!simple) { cLook.Add(rowTiles); cLook.Add(rowActions); cLook.Add(rowServices); }
            cLook.Add(rowNoAnim);

            cService.Add(rowDiag); cService.Add(rowUpdate); cService.Add(rowGuiAutoUpdate); cService.Add(rowZapretAuto); cService.Add(rowRollback);
            cService.Add(rowVerify); cService.Add(rowCleanOld); cService.Add(rowPortable); cService.Add(rowShortcuts); cService.Add(rowRemoveAll);

            var cards = new[] { cGeneral, cBypass, cLook, cService };
            string[] names = { L.T("Основное", "General"), L.T("Обход", "Bypass"), L.T("Вид", "Look"), L.T("Обслуживание", "Maintenance") };
            var tabRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
            var tabsUi = new RangeTab[cards.Length];
            Action<int> pickTab = i =>
            {
                settingsTab = i;
                for (int n = 0; n < cards.Length; n++) { cards[n].Visibility = n == i ? Visibility.Visible : Visibility.Collapsed; tabsUi[n].On = n == i; }
            };
            for (int i = 0; i < cards.Length; i++)
            {
                int idx = i;
                tabsUi[i] = new RangeTab(names[i]) { Margin = new Thickness(0, 0, 4, 0) };
                tabsUi[i].Click += delegate { pickTab(idx); };
                tabRow.Children.Add(tabsUi[i]);
                }
            sp.Children.Add(tabRow);
            foreach (var c in cards) sp.Children.Add(c);
            pickTab(Math.Max(0, Math.Min(cards.Length - 1, settingsTab)));

            var foot = Footer(
                Tuple.Create<string, Action<FlatLink>>(L.T("Журнал событий", "Event log"), l => ShowJournal()),
                Tuple.Create<string, Action<FlatLink>>(L.T("Резервная копия", "Backup"), l => ShowBackupMenu(l)),
                Tuple.Create<string, Action<FlatLink>>(L.T("О программе", "About"), l => ShowAbout()),
                Tuple.Create<string, Action<FlatLink>>(L.T("Сообщить о проблеме", "Report a problem"), l => ShowReport()));
            foot.Margin = new Thickness(0, 14, 0, 12);
            sp.Children.Add(foot);
            // настроек может быть больше, чем помещается, — с прокруткой
            var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = sp };
            settingsPage.Children.Add(scroll);
        }

        // Экран приветствия при первом запуске.
        void BuildWelcomePage()
        {
            bool show = Prefs.Get("WelcomeShown", "0") != "1" && snapPath == null;
#if TESTHOOKS
            if (snapPath != null && Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_PAGE") == "welcome") show = true;
#endif
            if (!show) return;
            var sp = new StackPanel { Margin = new Thickness(20, 0, 20, 0), MaxWidth = 420 };
            var logo = new Image { Source = LogoImage(64), Width = 64, Height = 64, Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(logo);
            var t = W.T(L.T("Добро пожаловать!", "Welcome!"), W.Status, W.Text, true);
            t.HorizontalAlignment = HorizontalAlignment.Center; t.Margin = new Thickness(0, 16, 0, 0);
            sp.Children.Add(t);
            var lead = new TextBlock
            {
                Text = L.T("Zapret GUI включает обход блокировок YouTube и Discord одной кнопкой.", "Zapret GUI turns on the YouTube and Discord bypass with one button."),
                TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Foreground = W.Muted, FontSize = W.Body, Margin = new Thickness(10, 6, 10, 0),
            };
            sp.Children.Add(lead);
            var tips = new Border { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 12, 14, 4), Margin = new Thickness(0, 18, 0, 0) };
            var tl = new StackPanel();
            string[][] items = {
                new[] { "1", L.T("Подберите стратегию", "Find a strategy"), L.T("программа сама проверит все и покажет лучшие", "the app tests all and shows the best") },
                new[] { "2", L.T("Включите обход", "Turn the bypass on"), L.T("большой кнопкой или из значка в трее", "with the big button or from the tray icon") },
                new[] { "3", L.T("Программа следит сама", "The app keeps watch"), L.T("скажет из трея, если YouTube или Discord перестали открываться", "tells you from the tray if YouTube or Discord stop opening") },
                new[] { "4", L.T("Нужен другой сайт?", "Need another site?"), L.T("«Списки» → «Проверить сайт»", "Lists → Check a site") },
                new[] { "5", L.T("Ставите родителям?", "Setting it up for family?"), L.T("в настройках есть простой режим: одна кнопка", "there is a simple mode in settings: one button") },
            };
            foreach (var it in items)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 12) };
                var num = W.T(it[0], W.BodyStrong, W.Accent, true); num.Width = 26; num.TextAlignment = TextAlignment.Center; num.VerticalAlignment = VerticalAlignment.Center;
                row.Children.Add(num);
                var tx = new StackPanel { Margin = new Thickness(12, 0, 0, 0) };
                tx.Children.Add(W.T(it[1], W.Body, W.Text)); tx.Children.Add(W.T(it[2], W.Caption, W.Muted));
                row.Children.Add(tx);
                tl.Children.Add(row);
            }
            tips.Child = tl;
            sp.Children.Add(tips);
            var go = new Pill(L.T("Подобрать стратегию", "Find a strategy"), false, 44) { Margin = new Thickness(20, 16, 20, 0) };
            go.Click += delegate { DismissWelcome(); AutoPick(); };
            sp.Children.Add(go);
            var later = new FlatLink(L.T("Позже", "Later")) { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 12, 0, 0) };
            later.Click += delegate { DismissWelcome(); };
            sp.Children.Add(later);
            welcomePage.Children.Add(sp);
            welcomeActive = true;
        }

        void DismissWelcome()
        {
            Prefs.Set("WelcomeShown", "1");
            if (!welcomeActive) return;
            welcomeActive = false;
            settingsOpen = false;
            if (IsVisible && !Fx.Off) SlideTo(mainPage, welcomePage, -1);
            else { welcomePage.Visibility = Visibility.Collapsed; mainPage.Visibility = Visibility.Visible; }
            UpdateUi();
        }

        void ShowPage(bool animate)
        {
            gear.SetIcon(settingsOpen ? "chevronLeft" : "gear");
            if (welcomeActive)
            {
                foreach (var p in new[] { mainPage, settingsPage }) p.Visibility = Visibility.Collapsed;
                welcomePage.Visibility = Visibility.Visible;
                UpdateUi();
                return;
            }
            Grid show = settingsOpen ? settingsPage : mainPage, hide = settingsOpen ? mainPage : settingsPage;
            if (animate && !Fx.Off && IsVisible && hide.Visibility == Visibility.Visible) SlideTo(show, hide, settingsOpen ? 1 : -1);
            else
            {
                Fx.Set((TranslateTransform)show.RenderTransform, TranslateTransform.XProperty, 0);
                show.Visibility = Visibility.Visible; hide.Visibility = Visibility.Collapsed; welcomePage.Visibility = Visibility.Collapsed;
            }
            UpdateUi();
        }

        // Плавный сдвиг страниц средствами WPF: новая въезжает, старая уезжает.
        int slideVer;   // если щёлкнули снова посреди сдвига, концовка старого сдвига не должна прятать страницу
        void SlideTo(Grid show, Grid hide, int dir)
        {
            var ts = (TranslateTransform)show.RenderTransform;
            var th = (TranslateTransform)hide.RenderTransform;
            show.Visibility = Visibility.Visible;
            int ver = ++slideVer;
            var a1 = new System.Windows.Media.Animation.DoubleAnimation(dir * Wd, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = Fx.Out };
            var a2 = new System.Windows.Media.Animation.DoubleAnimation(0, -dir * Wd, TimeSpan.FromMilliseconds(300)) { EasingFunction = Fx.Out };
            a2.Completed += delegate
            {
                if (ver != slideVer) return;
                hide.Visibility = Visibility.Collapsed;
                th.BeginAnimation(TranslateTransform.XProperty, null);
                th.X = 0;
            };
            ts.BeginAnimation(TranslateTransform.XProperty, a1);
            th.BeginAnimation(TranslateTransform.XProperty, a2);
        }

        // ================= Трей и мини-панель =================

        void BuildTray()
        {
            tray = new WF.NotifyIcon { Text = "Zapret", Visible = snapPath == null };
            tray.MouseClick += (s, e) =>
            {
                if (e.Button == WF.MouseButtons.Left) ToggleFlyout();
                else if (e.Button == WF.MouseButtons.Right) ShowTrayMenu();
            };
        }

        void ShowTrayMenu()
        {
            var m = WMenu.Create();
            m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
            WMenu.Add(m, L.T("Открыть", "Open"), false, ShowFromTray);
            var tog = WMenu.Add(m, picking ? L.T("Остановить подбор", "Stop testing") : Active ? L.T("Выключить", "Turn off") : L.T("Включить", "Turn on"), false, TogglePower);
            tog.IsEnabled = !busy || picking;
            // стратегия — подменю (сначала избранные)
            var sm = new MenuItem { Header = L.T("Стратегия: ", "Strategy: ") + selected, IsEnabled = !busy };
            var fav = Favorites;
            foreach (string s in strategies.Where(fav.Contains).Concat(strategies.Where(x => !fav.Contains(x))))
            {
                string name = s;
                var it = new MenuItem { Header = (fav.Contains(name) ? "★ " : "") + name, IsChecked = name == selected };
                it.Click += delegate { SelectStrategy(name); };
                sm.Items.Add(it);
            }
            m.Items.Add(sm);
            var pm = new MenuItem { Header = Paused ? L.T("Пауза до ", "Paused until ") + pauseUntil.ToString("HH:mm") : L.T("Пауза", "Pause"), IsEnabled = !busy && (Active || Paused) };
            if (Paused) { var r = new MenuItem { Header = L.T("Включить сейчас", "Resume now") }; r.Click += delegate { pauseUntil = DateTime.Now; CheckPause(); }; pm.Items.Add(r); }
            else foreach (int min in new[] { 15, 60, 180 })
                {
                    int mm = min;
                    var it = new MenuItem { Header = min < 60 ? L.T("на " + min + " минут", "for " + min + " minutes") : L.T("на " + (min / 60) + " ч", "for " + (min / 60) + " h") };
                    it.Click += delegate { Pause(mm); };
                    pm.Items.Add(it);
                }
            m.Items.Add(pm);
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Подобрать стратегию", "Find a strategy"), false, () => { ShowFromTray(); AutoPick(); }).IsEnabled = !busy;
            WMenu.Add(m, L.T("Статистика", "Statistics"), false, () => { ShowFromTray(); ShowStats(); });
            WMenu.Add(m, L.T("Проверить сайт…", "Check a site…"), false, () => { ShowFromTray(); CheckSite(); });
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Выход", "Quit"), false, () => { exitRequested = true; Close(); });
            // чтобы меню закрывалось кликом мимо, его окно должно стать активным
            m.Opened += delegate
            {
                var src = PresentationSource.FromVisual(m) as HwndSource;
                if (src != null) SetForegroundWindow(src.Handle);
            };
            m.IsOpen = true;
        }

        void ToggleFlyout()
        {
            if (flyout != null) { flyout.Close(); return; }
            if ((DateTime.Now - flyoutClosedAt).TotalMilliseconds < 300) return;   // только что закрылась от этого же клика
            flyout = new FlyoutW(this);
            flyout.Closed += delegate { flyoutClosedAt = DateTime.Now; flyout = null; };
            SyncFlyout();
            flyout.ShowNearCursor();
        }

        void SyncFlyout()
        {
            if (flyout == null) return;
            // тот же понятный статус, что на главном экране («Обход работает», «YouTube не открывается»)
            string st = panelTitle != null ? panelTitle.Text : statusText;
            Brush sb = panelTitle != null ? panelTitle.Foreground : statusBrush;
            string sub = panelSub != null ? panelSub.Text : subText;
            flyout.Sync(running, busy, picking ? pickProgress : -1, st, sb, sub, netYt, netDc, netChecking,
                NetMeta(0, NetCheck.YouTube.Length), NetMeta(NetCheck.YouTube.Length, NetCheck.Discord.Length), selected);
        }

        void MakeIcons()
        {
            // значок в трее — под масштаб экрана (16 px при 100 %, 24 px при 150 % …)
            float dpi = 1f;
            try { using (var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero)) dpi = g.DpiX / 96f; } catch { }
            int small = Math.Max(16, (int)Math.Round(16 * dpi));
            var old = new[] { iconApp, iconOn, iconOff, iconBusy, iconBad };
            iconApp = Theme.MakeIcon((int)Math.Round(32 * dpi), 1);
            iconOn = Theme.MakeIcon(small, 1);
            iconOff = Theme.MakeIcon(small, 0);
            iconBusy = Theme.MakeIcon(small, 2);
            iconBad = Theme.MakeIcon(small, 3);
            Icon = Imaging.CreateBitmapSourceFromHIcon(iconApp.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            // старые значки (после смены темы) освобождаем — трей сначала получает новый
            if (tray != null) tray.Icon = TrayIcon();
            foreach (var ic in old) if (ic != null) { try { DestroyIcon(ic.Handle); } catch { } ic.Dispose(); }
        }

        public static BitmapSource LogoImage(int px)
        {
            using (var bmp = Theme.Logo(px * 2, Theme.Accent, Theme.Lerp(Theme.Accent, System.Drawing.Color.Black, 0.28f), System.Drawing.Color.Empty))
            {
                IntPtr h = bmp.GetHbitmap();
                try { return Imaging.CreateBitmapSourceFromHBitmap(h, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions()); }
                finally { DeleteObject(h); }
            }
        }

        // Перекраска после смены темы или акцента: кисти меняются на месте, окна не пересобираются.
        void Recolor()
        {
            W.Sync();
            WinBase.TitleBar(this);
            foreach (Window w in Application.Current.Windows) if (w != this) WinBase.TitleBar(w);
            themeBtn.SetIcon(Theme.IsLight ? "sun" : "moon");
            MakeIcons();
            if (welcomeActive) { welcomePage.Children.Clear(); welcomeActive = false; BuildWelcomePage(); }   // логотип — картинка, перерисовать
            UpdateUi();
        }

        // ================= Окно =================

        internal void ShowFromTray()
        {
            Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
            if (pendingWhatsNew != null || pendingGuiNews != null) Dispatcher.BeginInvoke((Action)ShowPendingNews);
            if (pendingPickResults) { pendingPickResults = false; Dispatcher.BeginInvoke((Action)ShowLastPick); }
        }

        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr w, IntPtr l, ref bool handled)
        {
            if (msg == WM_SHOWME) ShowFromTray();
            else if (msg == WM_PICKNOW) Dispatcher.BeginInvoke((Action)ScheduledPick);   // задача планировщика
            else if (msg == WM_COMMANDLINE) { int c = (int)w; Dispatcher.BeginInvoke((Action)(() => RunCommand(c))); }   // --on / --off / --toggle
            else if (msg == 0x0312 && (int)w == HotkeyId) TogglePower();   // WM_HOTKEY: Ctrl+Alt+Z
            return IntPtr.Zero;
        }

        bool exitRequested;
        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            // Крестик прячет окно в трей, если включена настройка. Выход из меню трея закрывает полностью.
            SavePosition();
            if (!exitRequested && CloseToTray && snapPath == null)
            {
                e.Cancel = true;
                Hide();
                if (Prefs.Get("TrayHintShown", "0") != "1")
                {
                    Prefs.Set("TrayHintShown", "1");
                    tray.ShowBalloonTip(4000, L.T("Zapret работает в трее", "Zapret is in the tray"),
                        L.T("Чтобы закрыть приложение полностью, нажмите правой кнопкой на значок → Выход.", "To quit completely, right-click the icon → Quit."), WF.ToolTipIcon.Info);
                }
                return;
            }
            base.OnClosing(e);
        }

        protected override void OnClosed(EventArgs e)
        {
            // закрыли посреди подбора: остановить его, дождаться конца и вернуть обход, если он был включён
            if (picking)
            {
                cancelPick = true;
                pickDone.WaitOne(15000);
                if (pickWasActive)
                    try
                    {
                        int pid = z.Start(pickPrev, autostart);
                        Prefs.Set("RunPid", pid.ToString());
                        Prefs.Set("RunStrategy", pickPrev);
                        running = true;
                    }
                    catch { }
                Journal.Add(L.T("Подбор прерван закрытием программы", "Strategy test stopped because the app was closed") +
                    (pickWasActive ? L.T(", обход возвращён: ", ", bypass restored: ") + pickPrev : ""));
            }
            Journal.Add(L.T("Программа закрыта", "App closed") + (running ? L.T(" (обход продолжает работать)", " (bypass keeps running)") : ""));
            tray.Visible = false;
            tray.Dispose();
            SystemEvents.UserPreferenceChanged -= OnSystemPrefs;
            if (flyout != null) flyout.Close();
            base.OnClosed(e);
            Application.Current.Shutdown();
        }

        public void RequestExit() { exitRequested = true; Close(); }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int RegisterWindowMessage(string msg);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool SetForegroundWindow(IntPtr h);
        [System.Runtime.InteropServices.DllImport("gdi32.dll")]
        static extern bool DeleteObject(IntPtr h);
        [System.Runtime.InteropServices.DllImport("user32.dll")]
        static extern bool DestroyIcon(IntPtr h);
    }
}
