using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZapretGui
{
    class MainForm : Form
    {
        readonly Zapret z;
        readonly string snapPath;
        bool startHidden;

        // элементы (пересоздаются при смене языка/темы)
        Panel mainPage, settingsPage, welcomePage;
        TrayFlyout flyout;
        bool welcomeActive;      // показан экран приветствия
        int pageTop, pageH;      // где начинаются страницы (сразу под шапкой)
        DateTime flyoutClosedAt = DateTime.MinValue;
        readonly System.Windows.Forms.Timer slide = new System.Windows.Forms.Timer { Interval = 15 };
        PowerButton power;
        Label status, sub, hint, version;
        ServiceCards cards;
        StrategyPicker picker;
        SettingRow rowAuto, rowGame, rowIpset;
        SettingRow rowLang, rowTheme, rowGuiStart, rowTray, rowWatch, rowDiag, rowFakes, rowUpdate;
        GearButton gear;
        NotifyIcon tray;
        ToolStripMenuItem miToggle, miShow, miExit;
        Icon iconOn, iconOff, iconBusy, iconApp;

        readonly System.Windows.Forms.Timer poll = new System.Windows.Forms.Timer { Interval = 2000 };
        readonly System.Windows.Forms.Timer hintTimer = new System.Windows.Forms.Timer { Interval = 5000 };

        // состояние
        List<string> strategies = new List<string>();
        string selected = "";
        bool autostart;
        bool busy; string busyText = "";
        bool picking, cancelPick; string pickName;
        float pickProgress;   // доля проверенных стратегий — для кольца вокруг кнопки
        bool running, svc;
        string activeStrategy;
        string newVersion;   // новая версия zapret
        string newGui;       // новая версия ZapretGUI
        bool settingsOpen;
        bool guiAutostart;
        bool exitRequested;
        bool expectRunning;
        readonly List<DateTime> restarts = new List<DateTime>();
        float netYt = -1, netDc = -1; bool netChecking; DateTime lastNet = DateTime.MinValue;
        long[] lastNetMs;   // время ответа каждого адреса в последней проверке (-1 — не открылся)
        string lastNetSig;  // итог прошлой проверки — в журнал пишем только изменения

        public static readonly int WM_SHOWME = RegisterWindowMessage("ZapretGUI_ShowMe");

        static bool CloseToTray { get { return Prefs.Get("CloseToTray", "1") == "1"; } set { Prefs.Set("CloseToTray", value ? "1" : "0"); } }
        static bool WatchdogOn { get { return Prefs.Get("Watchdog", "1") == "1"; } set { Prefs.Set("Watchdog", value ? "1" : "0"); } }

        public MainForm(Zapret zapret, string snap, bool hidden)
        {
            z = zapret;
            snapPath = snap;
            startHidden = hidden && snap == null;
            Journal.Enabled = snap == null;
            L.Init();
            using (var g = CreateGraphics()) Theme.SetScale(g.DpiX / 96f, UiScalePref / 100f);
            Theme.AccentName = Prefs.Get("Accent", "auto");
            Theme.ApplyMode(ThemeMode);
            SystemEvents.UserPreferenceChanged += OnSystemPrefs;
#if TESTHOOKS
            if (snap != null)
            {
                settingsOpen = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_PAGE") == "settings";
                string acc = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_ACCENT"), th = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_THEME");
                string sc = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_SCALE");
                if (acc != null) Theme.AccentName = acc;
                if (sc != null) Theme.SetScale(Theme.Dpi, int.Parse(sc) / 100f);
                if (th != null) Theme.ApplyMode(th); else Theme.Apply(Theme.IsLight);
            }
#endif

            BuildTray();
            LoadState();
            BuildUi();
            RefreshState();
            expectRunning = running;
            Journal.Add(L.T("Программа запущена, ZapretGUI v", "App started, ZapretGUI v") + SelfUpdate.Current.ToString(3) + ", zapret v" + z.LocalVersion +
                (running ? L.T(" · обход уже включён: ", " · bypass already on: ") + (activeStrategy ?? "?") : L.T(" · обход выключен", " · bypass off")));

            poll.Tick += delegate
            {
                if (busy) return;
                RefreshState();
                Watchdog();
                if (!netChecking && (DateTime.Now - lastNet).TotalSeconds > 60) CheckNet();
            };
            hintTimer.Tick += delegate { hintTimer.Stop(); if (hint != null) hint.Text = ""; };
            if (snapPath == null)
            {
                poll.Start();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    bool a = GuiAutostart.Enabled;
                    Post(() => { guiAutostart = a; UpdateUi(); });
                });
                CheckNet();
                if (z.UpdatesEnabled) CheckUpdates();
            }
            var h = Handle; // окно должно существовать даже при запуске в трее
        }

        // "system" (как в Windows, по умолчанию), "dark", "light", "amoled" или "warm"
        static string ThemeMode { get { string m = Prefs.Get("Theme", "system"); return Theme.Modes.Contains(m) ? m : "system"; } }

        // Масштаб интерфейса в процентах (75, 90, 100, 110, 125).
        static int UiScalePref
        {
            get { int v; return int.TryParse(Prefs.Get("Scale", "100"), out v) && Theme.Scales.Contains(v) ? v : 100; }
        }

        // Тема Windows поменялась — подстроиться, если выбрано «Как в Windows».
        void OnSystemPrefs(object sender, UserPreferenceChangedEventArgs e)
        {
            if (e.Category != UserPreferenceCategory.General) return;
            Post(() =>
            {
                if (ThemeMode != "system") return;
                bool light = Theme.ResolveLight("system");
                if (light == Theme.IsLight) return;
                Rebuild(() => Theme.ApplyMode("system"));
            });
        }

        void Post(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke((MethodInvoker)delegate { a(); }); } catch { }
        }

        // ================= Построение интерфейса =================

        void BuildUi()
        {
            Func<float, int> P = Theme.P;
            SuspendLayout();
            foreach (Control c in Controls.Cast<Control>().ToList()) c.Dispose();
            Controls.Clear();

            Text = "Zapret by " + BrandTitle.ZapretAuthor + "  ·  GUI by " + BrandTitle.Author;
            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size(P(380), P(624));
            int W = ClientSize.Width;
            MakeIcons();

            // --- шапка ---
            var title = new BrandTitle { Location = new Point(P(22), P(10)) };
            pageTop = Math.Max(P(60), title.Bottom + P(2));
            pageH = P(596) - pageTop;
            var themeBtn = new ThemeToggle { Bounds = new Rectangle(W - P(16) - P(30), P(16), P(30), P(30)) };
            // быстрая смена светлая ↔ тёмная (чёрная ↔ тёплая для их любителей)
            themeBtn.Click += delegate { SetThemeMode(Theme.IsLight ? (Theme.Flavor == "warm" ? "amoled" : "dark") : (Theme.Flavor == "amoled" ? "warm" : "light")); };
            gear = new GearButton { Bounds = new Rectangle(themeBtn.Left - P(4) - P(30), P(16), P(30), P(30)), Back = settingsOpen };
            gear.Click += delegate
            {
                if (welcomeActive) { DismissWelcome(); return; }
                settingsOpen = !settingsOpen;
                ShowPage(true);
            };
            version = new Label { Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Bg, AutoSize = false, TextAlign = ContentAlignment.MiddleRight,
                Bounds = new Rectangle(title.Right + P(4), P(20), gear.Left - P(4) - title.Right - P(4), P(22)) };
            version.Click += delegate { if (newVersion != null) UpdateZapret(); };
            Controls.AddRange(new Control[] { title, themeBtn, gear, version });

            hint = new Label { Font = Theme.Small, ForeColor = Theme.Muted, BackColor = Theme.Bg, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false,
                Bounds = new Rectangle(P(16), P(598), W - P(32), P(20)), AutoEllipsis = true };
            Controls.Add(hint);

            BuildMainPage(P, W);
            BuildSettingsPage(P, W);
            BuildWelcomePage(P, W);
            ShowPage(false);
            power.Settle();   // новая кнопка сразу в текущем состоянии: без волны и без проявления света при пересборке
            ResumeLayout();
        }

        // Экран приветствия при первом запуске.
        void BuildWelcomePage(Func<float, int> P, int W)
        {
            welcomePage = new BufferedPanel { Bounds = new Rectangle(0, pageTop, W, pageH), BackColor = Theme.Bg, Visible = false };
            Controls.Add(welcomePage);
            bool show = Prefs.Get("WelcomeShown", "0") != "1" && snapPath == null;
#if TESTHOOKS
            if (snapPath != null && Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_PAGE") == "welcome") show = true;
#endif
            if (!show) return;

            int logo = P(76);
            var pic = new PictureBox { Image = Theme.Logo(logo, Theme.Accent, Theme.Lerp(Theme.Accent, Color.Black, 0.28f), Color.Empty),
                Bounds = new Rectangle((W - logo) / 2, P(36), logo, logo), BackColor = Theme.Bg };
            var title = new Label { Text = L.T("Добро пожаловать!", "Welcome!"), Font = Theme.Status, TextAlign = ContentAlignment.MiddleCenter,
                AutoSize = false, Bounds = new Rectangle(0, P(128), W, P(34)), BackColor = Theme.Bg };
            var lead = new Label { Text = L.T("Zapret GUI включает обход блокировок YouTube и Discord одной кнопкой.",
                "Zapret GUI turns on the YouTube and Discord bypass with one button."),
                Font = Theme.Body, ForeColor = Theme.Muted, TextAlign = ContentAlignment.TopCenter, AutoSize = false,
                Bounds = new Rectangle(P(30), P(166), W - P(60), P(40)), BackColor = Theme.Bg };
            welcomePage.Controls.AddRange(new Control[] { pic, title, lead });

            var tips = new Card { Bounds = new Rectangle(P(20), P(218), W - P(40), P(176)) };
            string[][] items = {
                new[] { "1", L.T("Подберите стратегию", "Find a strategy"), L.T("программа сама проверит все и покажет лучшие", "the app tests all and shows the best") },
                new[] { "2", L.T("Включите обход", "Turn the bypass on"), L.T("большой кнопкой или из значка в трее", "with the big button or from the tray icon") },
                new[] { "3", L.T("Загляните в настройки", "Check the settings"), L.T("автозапуск, оформление, диагностика", "autostart, appearance, diagnostics") },
            };
            for (int i = 0; i < items.Length; i++)
            {
                int y = P(14) + i * P(54);
                var num = new Label { Text = items[i][0], Font = Theme.BodyStrong, ForeColor = Theme.Accent, BackColor = Theme.Surface, AutoSize = false,
                    TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(P(14), y + P(4), P(26), P(26)) };
                var t1 = new Label { Text = items[i][1], Font = Theme.Body, ForeColor = Theme.Text, BackColor = Theme.Surface, AutoSize = true, Location = new Point(P(52), y) };
                var t2 = new Label { Text = items[i][2], Font = Theme.Caption, ForeColor = Theme.Muted, BackColor = Theme.Surface, AutoSize = true, Location = new Point(P(52), y + P(20)) };
                tips.Controls.AddRange(new Control[] { num, t1, t2 });
            }
            welcomePage.Controls.Add(tips);

            var go = new AccentButton(L.T("Подобрать стратегию", "Find a strategy")) { Bounds = new Rectangle(P(40), P(420), W - P(80), P(44)) };
            go.Click += delegate { DismissWelcome(); AutoPick(); };
            var later = new FlatLink(L.T("Позже", "Later"));
            later.Click += delegate { DismissWelcome(); };
            welcomePage.Controls.AddRange(new Control[] { go, later });
            later.Location = new Point((W - later.PreferredSize.Width) / 2, P(478));
            welcomeActive = true;
            welcomePage.Visible = true;
            welcomePage.BringToFront();
        }

        void DismissWelcome()
        {
            Prefs.Set("WelcomeShown", "1");
            if (!welcomeActive) return;
            welcomeActive = false;
            settingsOpen = false;
            if (IsHandleCreated && Visible) SlideTo(mainPage, welcomePage, -1);
            else { welcomePage.Visible = false; mainPage.Visible = true; }
        }

        void BuildMainPage(Func<float, int> P, int W)
        {
            mainPage = new BufferedPanel { Bounds = new Rectangle(0, pageTop, W, pageH), BackColor = Theme.Bg };
            Controls.Add(mainPage);

            // кнопка вместе с подписями и светом за ней — один элемент (свет ложится и под текст)
            power = new PowerButton { Bounds = new Rectangle(0, 0, W, P(208)), Hero = true, CircleSize = P(120), CircleCenterY = P(80) };
            power.Click += delegate { TogglePower(); };
            // status/sub хранят текст состояния (его же показывает мини-панель), на экран выводит power
            if (status != null) status.Dispose();
            if (sub != null) sub.Dispose();
            status = new Label();
            sub = new Label();
            cards = new ServiceCards { Bounds = new Rectangle(P(20), P(212), W - P(40), P(50)), TooltipText = NetTooltip };
            cards.Click += delegate { CheckNet(); };

            picker = new StrategyPicker { Bounds = new Rectangle(P(20), P(270), W - P(40), P(62)) };
            picker.Prev += delegate { StepStrategy(-1); };
            picker.Next += delegate { StepStrategy(1); };
            picker.Open += delegate { ShowStrategyMenu(picker); };

            rowAuto = new SettingRow(L.T("Автозапуск обхода с Windows", "Start bypass with Windows"), true) { Hint = L.T("устанавливается как служба", "installed as a service") };
            rowGame = new SettingRow(L.T("Игровой фильтр", "Game filter"), false) { Hint = L.T("обход для игр и портов 1024+", "bypass for games, ports 1024+") };
            rowIpset = new SettingRow("IPSet", false) { Hint = L.T("фильтр по IP-адресам", "filter by IP address") };
            rowAuto.Click += delegate { ToggleAutostart(); };
            rowGame.Click += delegate { ShowGameMenu(); };
            rowIpset.Click += delegate { ShowIpsetMenu(); };
            var card = MakeCard(P, W, P(340), P(52), rowAuto, rowGame, rowIpset);

            mainPage.Controls.AddRange(new Control[] { power, cards, picker, card });

            // подвал
            var items = new List<Control>();
            Action<string, Action> link = (text, act) => { var l = new FlatLink(text); l.Click += delegate { act(); }; items.Add(l); };
            Func<Label> dot = () => new Label { Text = "·", AutoSize = true, Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Bg };
            link(L.T("Подобрать стратегию", "Find a strategy"), AutoPick);
            items.Add(dot());
            link(L.T("Обновить IPSet", "Update IPSet"), UpdateIpset);
            items.Add(dot());
            Control more = null;
            link(L.T("Ещё", "More"), () => ShowMoreMenu(more));
            more = items[items.Count - 1];
            int total = items.Sum(c => c.PreferredSize.Width);
            int x = (W - total) / 2;
            foreach (var c in items) { c.Location = new Point(x, P(512)); x += c.PreferredSize.Width; mainPage.Controls.Add(c); }
        }

        void BuildSettingsPage(Func<float, int> P, int W)
        {
            settingsPage = new BufferedPanel { Bounds = new Rectangle(0, pageTop, W, pageH), BackColor = Theme.Bg };
            Controls.Add(settingsPage);
            // заголовки групп, как в Параметрах Windows 11
            Action<string, int> group = (text, y) => settingsPage.Controls.Add(new Label
            {
                Text = text.ToUpper(), Font = Theme.Badge, ForeColor = Theme.Muted, BackColor = Theme.Bg, AutoSize = true, Location = new Point(P(26), y)
            });
            int rh = P(48);

            rowLang = new SettingRow(L.T("Язык", "Language"), false) { Icon = "globe" };
            rowTheme = new SettingRow(L.T("Оформление", "Appearance"), false) { Icon = "palette" };
            rowGuiStart = new SettingRow(L.T("Запускать программу с Windows", "Launch app with Windows"), true) { Icon = "monitor", Hint = L.T("сразу свёрнутой в трей", "minimized to tray") };
            rowTray = new SettingRow(L.T("Крестик сворачивает в трей", "Close button hides to tray"), true) { Icon = "tray", Hint = L.T("закрыть полностью — через значок в трее", "quit via the tray icon") };
            rowWatch = new SettingRow(L.T("Сторож", "Watchdog"), true) { Icon = "shield", Hint = L.T("перезапускать обход, если он упал", "restart the bypass if it crashes") };
            rowLang.Click += delegate { ShowLangMenu(); };
            rowTheme.Click += delegate { ShowThemeMenu(); };
            rowGuiStart.Click += delegate { ToggleGuiAutostart(); };
            rowTray.Click += delegate { CloseToTray = !CloseToTray; UpdateUi(); };
            rowWatch.Click += delegate
            {
                WatchdogOn = !WatchdogOn; UpdateUi();
                Journal.Add(WatchdogOn ? L.T("Сторож включён", "Watchdog on") : L.T("Сторож выключен", "Watchdog off"));
            };
            rowDiag = new SettingRow(L.T("Диагностика", "Diagnostics"), false) { Icon = "wrench", Nav = true, Hint = L.T("конфликты, прокси, DNS, драйвер", "conflicts, proxy, DNS, driver") };
            rowFakes = new SettingRow(L.T("Fake-файлы", "Fake files"), false) { Icon = "layers", Hint = L.T("подмена пакетов для Discord и игр", "fake packets for Discord and games") };
            rowUpdate = new SettingRow(L.T("Обновления", "Updates"), false) { Icon = "download" };
            rowDiag.Click += delegate { OpenDiag(); };
            rowFakes.Click += delegate { ShowFakesMenu(); };
            rowUpdate.Click += delegate { ShowUpdatesMenu(); };

            group(L.T("Основное", "General"), P(4));
            settingsPage.Controls.Add(MakeCard(P, W, P(24), rh, rowLang, rowTheme, rowGuiStart, rowTray));
            group(L.T("Обход", "Bypass"), P(236));
            settingsPage.Controls.Add(MakeCard(P, W, P(256), rh, rowWatch, rowFakes));
            group(L.T("Обслуживание", "Maintenance"), P(372));
            settingsPage.Controls.Add(MakeCard(P, W, P(392), rh, rowDiag, rowUpdate));

            // подвал настроек
            var items = new List<Control>();
            Action<string, Action> link = (text, act) => { var l = new FlatLink(text); l.Click += delegate { act(); }; items.Add(l); };
            Func<Label> dot = () => new Label { Text = "·", AutoSize = true, Font = Theme.Small, ForeColor = Theme.Dim, BackColor = Theme.Bg };
            link(L.T("Журнал событий", "Event log"), ShowJournal);
            items.Add(dot());
            Control backup = null;
            link(L.T("Резервная копия", "Backup"), () => ShowBackupMenu(backup));
            backup = items[items.Count - 1];
            items.Add(dot());
            link(L.T("О программе", "About"), ShowAbout);
            int total = items.Sum(c => c.PreferredSize.Width);
            int x = (W - total) / 2;
            foreach (var c in items) { c.Location = new Point(x, P(512)); x += c.PreferredSize.Width; settingsPage.Controls.Add(c); }
        }

        static Card MakeCard(Func<float, int> P, int W, int y, int rowH, params SettingRow[] rows)
        {
            var card = new Card { Bounds = new Rectangle(P(20), y, W - P(40), rows.Length * rowH + P(8)) };
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Bounds = new Rectangle(P(4), P(4) + i * rowH, card.Width - P(8), rowH);
                rows[i].Divider = i > 0;
                card.Controls.Add(rows[i]);
            }
            return card;
        }

        void ShowPage(bool animate)
        {
            gear.Back = settingsOpen;
            gear.Invalidate();
            Panel show = settingsOpen ? settingsPage : mainPage, hide = settingsOpen ? mainPage : settingsPage;
            if (animate && IsHandleCreated && Visible && hide.Visible) SlideTo(show, hide, settingsOpen ? 1 : -1);
            else
            {
                show.Left = 0; hide.Left = 0;
                show.Visible = true; hide.Visible = false;
                if (welcomeActive) { welcomePage.BringToFront(); mainPage.Visible = false; settingsPage.Visible = false; }
            }
            UpdateUi();
        }

        // Плавный сдвиг: show въезжает справа (dir = 1) или слева (dir = -1), hide уезжает в другую сторону.
        void SlideTo(Panel show, Panel hide, int dir)
        {
            slide.Stop();
            int W = ClientSize.Width;
            float t = 0;
            show.Left = dir * W; show.Visible = true;
            show.BringToFront(); hint.BringToFront();
            EventHandler tick = null;
            tick = delegate
            {
                t = Math.Min(1f, t + 0.09f);
                float e = 1 - (float)Math.Pow(1 - t, 3);   // плавное замедление в конце
                show.Left = (int)(dir * W * (1 - e));
                hide.Left = (int)(-dir * W * e);
                if (t >= 1)
                {
                    slide.Stop(); slide.Tick -= tick;
                    show.Left = 0; hide.Visible = false; hide.Left = 0;
                }
            };
            slide.Tick += tick;
            slide.Start();
        }

        void BuildTray()
        {
            var menu = new ContextMenuStrip { Renderer = new DarkRenderer(), ShowImageMargin = false };
            miShow = new ToolStripMenuItem(); miShow.Click += delegate { ShowFromTray(); };
            miToggle = new ToolStripMenuItem(); miToggle.Click += delegate { TogglePower(); };
            miExit = new ToolStripMenuItem(); miExit.Click += delegate { exitRequested = true; Close(); };
            menu.Items.AddRange(new ToolStripItem[] { miShow, miToggle, new ToolStripSeparator(), miExit });
            menu.Opening += delegate
            {
                menu.Font = Theme.Body;
                miShow.Text = L.T("Открыть", "Open");
                miToggle.Text = picking ? L.T("Остановить подбор", "Stop testing") : Active ? L.T("Выключить", "Turn off") : L.T("Включить", "Turn on");
                miToggle.Enabled = !busy || picking;
                miExit.Text = L.T("Выход", "Quit");
            };
            tray = new NotifyIcon { Text = "Zapret", ContextMenuStrip = menu, Visible = snapPath == null };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) ToggleFlyout(); };
        }

        // ---------- Мини-панель у трея ----------

        void ToggleFlyout()
        {
            if (flyout != null && !flyout.IsDisposed) { flyout.Close(); return; }
            if ((DateTime.Now - flyoutClosedAt).TotalMilliseconds < 300) return; // только что закрылась от этого же клика
            flyout = new TrayFlyout(this, iconApp);
            flyout.FormClosed += delegate { flyoutClosedAt = DateTime.Now; flyout = null; };
            flyout.PlaceNearCursor();
            SyncFlyout();
            flyout.Show();
            flyout.Activate();
        }

        void SyncFlyout()
        {
            if (flyout == null || flyout.IsDisposed || status == null) return;
            flyout.Power.On = running;
            flyout.Power.Busy = busy;
            flyout.Power.Progress = picking ? pickProgress : -1;
            flyout.Status.Text = status.Text; flyout.Status.ForeColor = status.ForeColor;
            flyout.Sub.Text = sub.Text;
            flyout.Dots.YouTube = netYt; flyout.Dots.Discord = netDc; flyout.Dots.Checking = netChecking;
            flyout.Dots.TooltipText = NetTooltip;
            flyout.Dots.Invalidate();
            flyout.Picker.SetValue(selected);
        }

        void MakeIcons()
        {
            int small = Math.Max(16, Theme.P(16));
            iconApp = Theme.MakeIcon(Theme.P(32), 1);
            iconOn = Theme.MakeIcon(small, 1);
            iconOff = Theme.MakeIcon(small, 0);
            iconBusy = Theme.MakeIcon(small, 2);
            Icon = iconApp;
        }

        // Пересобирает окно (новые цвета, язык, масштаб). change — что поменять перед пересборкой.
        // Пока окно пересобирается, его перерисовка выключена: новое оформление появляется сразу целиком,
        // без мелькания недорисованных кусков.
        void Rebuild(Action change = null)
        {
            bool hold = IsHandleCreated && Visible && WindowState != FormWindowState.Minimized;
            if (hold) SendMessage(Handle, WM_SETREDRAW, IntPtr.Zero, IntPtr.Zero);
            try
            {
                if (change != null) change();
                if (flyout != null && !flyout.IsDisposed) flyout.Close();   // пересоздастся в новых цветах при следующем клике
                BuildUi();
                ApplyTitleBar(Handle);
                UpdateUi();
            }
            finally
            {
                if (hold) { SendMessage(Handle, WM_SETREDRAW, (IntPtr)1, IntPtr.Zero); Refresh(); }
            }
        }

        const int WM_SETREDRAW = 0x000B;

        [DllImport("user32.dll")]
        static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr w, IntPtr l);

        void SetThemeMode(string mode)
        {
            if (mode == ThemeMode) return;
            Prefs.Set("Theme", mode);
            Post(() => Rebuild(() => Theme.ApplyMode(mode)));
        }

        void SetAccent(string name)
        {
            if (name == Theme.AccentName) return;
            Prefs.Set("Accent", name);
            Post(() => Rebuild(() => { Theme.AccentName = name; Theme.Apply(Theme.IsLight); }));
        }

        void SetScale(int percent)
        {
            if (percent == UiScalePref) return;
            Prefs.Set("Scale", percent.ToString());
            Post(() => Rebuild(() => Theme.SetScale(Theme.Dpi, percent / 100f)));
        }

        void SetLang(bool en)
        {
            if (en == L.En) return;
            Prefs.Set("Lang", en ? "en" : "ru");
            Post(() => Rebuild(() => { L.En = en; }));
        }

        // ================= Состояние =================

        void LoadState()
        {
            strategies = z.Strategies();
            z.LoadGame();
            selected = Prefs.Get("Strategy", "");
            svc = z.ServiceInstalled();
            if (svc)
            {
                string s = z.ServiceStrategy();
                if (s != null && strategies.Contains(s)) selected = s;
                autostart = true;
            }
            else autostart = Prefs.Get("Autostart", "0") == "1";
            if (!strategies.Contains(selected))
                selected = strategies.Contains("general") ? "general" : (strategies.Count > 0 ? strategies[0] : "");
        }

        void RefreshState()
        {
            running = z.IsRunning();
            svc = z.ServiceInstalled();
            activeStrategy = null;
            if (svc) activeStrategy = z.ServiceStrategy();
            else if (running)
            {
                int pid;
                if (int.TryParse(Prefs.Get("RunPid", ""), out pid) && z.IsPidRunning(pid)) activeStrategy = Prefs.Get("RunStrategy", null);
            }
            UpdateUi();
        }

        bool Active { get { return running || svc; } }

        void UpdateUi()
        {
            if (power == null) return;
            power.On = running;
            power.Busy = busy;

            if (picking)
            {
                status.Text = busyText; status.ForeColor = Theme.Muted;
                sub.Text = (pickName ?? "") + L.T("  ·  нажмите кнопку, чтобы отменить", "  ·  click to cancel");
            }
            else if (busy)
            {
                status.Text = busyText; status.ForeColor = Theme.Muted;
                sub.Text = selected;
            }
            else if (running)
            {
                status.Text = L.T("Включено", "On"); status.ForeColor = Theme.Accent;
                sub.Text = (activeStrategy ?? L.T("запущено вне приложения", "started outside the app")) + (svc ? L.T("  ·  служба", "  ·  service") : "");
            }
            else if (svc)
            {
                status.Text = L.T("Служба остановлена", "Service stopped"); status.ForeColor = Theme.Warn;
                sub.Text = L.T("нажмите кнопку, чтобы перезапустить", "click the button to restart");
            }
            else
            {
                status.Text = L.T("Выключено", "Off"); status.ForeColor = Theme.Text;
                sub.Text = L.T("нажмите, чтобы включить", "click to turn on");
            }

            power.SetText(status.Text, status.ForeColor, sub.Text);
            power.Progress = picking ? pickProgress : -1;
            cards.YouTube = netYt; cards.Discord = netDc; cards.Checking = netChecking;
            cards.YouTubeMeta = NetMeta(0, NetCheck.YouTube.Length);
            cards.DiscordMeta = NetMeta(NetCheck.YouTube.Length, NetCheck.Discord.Length);
            cards.Invalidate();
            picker.SetValue(selected);
            rowAuto.Checked = autostart;
            rowGame.Value = GameLabel(z.GameMode);
            rowIpset.Value = IpsetLabel(z.IpsetMode());

            rowLang.Value = L.En ? "English" : "Русский";
            rowTheme.Value = ThemeLabel(ThemeMode);
            rowTheme.Hint = L.T("цвет: ", "color: ") + Theme.AccentLabel(Theme.AccentName).ToLower() + L.T("  ·  масштаб ", "  ·  scale ") + UiScalePref + "%";
            rowGuiStart.Checked = guiAutostart;
            rowTray.Checked = CloseToTray;
            rowWatch.Checked = WatchdogOn;
            string lv = z.LocalVersion;
            rowUpdate.Hint = "zapret v" + lv + "  ·  ZapretGUI v" + SelfUpdate.Current.ToString(3);
            rowUpdate.Value = (newVersion != null || newGui != null) ? L.T("есть новые", "available") : "";
            rowUpdate.Invalidate();

            if (newVersion != null)
            {
                version.Text = "↑ v" + newVersion;
                version.ForeColor = Theme.Accent;
                version.Cursor = Cursors.Hand;
            }
            else
            {
                version.Text = lv == "" ? "" : "v" + lv;
                version.ForeColor = Theme.Dim;
                version.Cursor = Cursors.Default;
            }

            tray.Icon = busy || picking ? iconBusy : running ? iconOn : iconOff;
            SyncFlyout();
            tray.Text = "Zapret" + (running ? L.T(" — включено", " — on") : L.T(" — выключено", " — off")) + "\nGUI by " + BrandTitle.Author;
        }

        static string GameLabel(string m)
        {
            switch (m) { case "all": return "TCP + UDP"; case "tcp": return "TCP"; case "udp": return "UDP"; default: return L.T("Выкл", "Off"); }
        }

        static string IpsetLabel(string m)
        {
            switch (m) { case "loaded": return L.T("По списку", "List"); case "none": return L.T("Выкл", "Off"); default: return L.T("Все IP", "Any IP"); }
        }

        // ================= Действия =================

        void RunOp(string text, bool spinner, Action work, Action done)
        {
            if (busy) return;
            busy = true; busyText = text;
            if (!spinner) { hint.ForeColor = Theme.Muted; hint.Text = text; hintTimer.Stop(); }
            UpdateUi();
            ThreadPool.QueueUserWorkItem(delegate
            {
                Exception err = null;
                try { work(); } catch (Exception ex) { err = ex; }
                Post(() =>
                {
                    busy = false;
                    if (!spinner) hint.Text = "";
                    if (err != null)
                    {
                        Toast(err.Message, true);
                        Journal.Add(L.T("Ошибка (", "Error (") + text.TrimEnd('…', '.') + "): " + err.Message);
                    }
                    else if (done != null) done();
                    RefreshState();
                });
            });
        }

        internal void TogglePower()
        {
            if (picking) { cancelPick = true; busyText = L.T("Отмена…", "Cancelling…"); UpdateUi(); return; }
            if (busy) return;
            if (Active) Stop();
            else Start();
        }

        void Start()
        {
            if (selected == "") { Toast(L.T("Не найдено ни одной стратегии (.bat)", "No strategies (.bat) found"), true); return; }
            string name = selected;
            bool asSvc = autostart;
            expectRunning = false;
            RunOp(Active ? L.T("Перезапуск…", "Restarting…") : L.T("Запуск…", "Starting…"), true, () =>
            {
                int pid = z.Start(name, asSvc);
                Prefs.Set("RunPid", pid.ToString());
                Prefs.Set("RunStrategy", name);
            }, () =>
            {
                expectRunning = true;
                Journal.Add(L.T("Обход включён: ", "Bypass on: ") + name + (asSvc ? L.T(" (служба)", " (service)") : ""));
                CheckNetSoon();
            });
        }

        void Stop()
        {
            expectRunning = false;
            RunOp(L.T("Остановка…", "Stopping…"), true, () => z.StopAll(true), () =>
            {
                Journal.Add(L.T("Обход выключен", "Bypass off"));
                CheckNetSoon();
            });
        }

        void RestartIfActive() { if (Active) Start(); }

        void SelectStrategy(string name)
        {
            if (busy || name == selected) return;
            Journal.Add(L.T("Стратегия сменена: ", "Strategy changed: ") + selected + " → " + name);
            selected = name;
            Prefs.Set("Strategy", name);
            picker.SetValue(name);
            RestartIfActive();
        }

        internal void StepStrategy(int d)
        {
            if (busy || strategies.Count == 0) return;
            int i = strategies.IndexOf(selected);
            i = ((i < 0 ? 0 : i + d) % strategies.Count + strategies.Count) % strategies.Count;
            SelectStrategy(strategies[i]);
        }

        void ToggleAutostart()
        {
            if (busy) return;
            autostart = !autostart;
            Prefs.Set("Autostart", autostart ? "1" : "0");
            rowAuto.Checked = autostart;
            Journal.Add(autostart ? L.T("Автозапуск обхода с Windows включён", "Start with Windows on") : L.T("Автозапуск обхода с Windows выключен", "Start with Windows off"));
            if (Active) Start();
            else Toast(autostart ? L.T("Включите обход — он будет запускаться вместе с Windows", "Turn the bypass on — it will start with Windows")
                                 : L.T("Автозапуск выключен", "Autostart disabled"));
        }

        void ToggleGuiAutostart()
        {
            bool target = !guiAutostart;
            try
            {
                GuiAutostart.Set(target); guiAutostart = target;
                Journal.Add(target ? L.T("Запуск программы с Windows включён", "Launch app with Windows on") : L.T("Запуск программы с Windows выключен", "Launch app with Windows off"));
            }
            catch (Exception ex) { Toast(ex.Message, true); }
            UpdateUi();
        }

        // ---------- Сторож ----------

        void Watchdog()
        {
            if (busy || !expectRunning || running) return;
            if (!WatchdogOn)
            {
                expectRunning = false;
                Journal.Add(L.T("winws.exe завершился (сторож выключен, перезапуска нет)", "winws.exe exited (watchdog off, no restart)"));
                Balloon(L.T("Обход остановился", "Bypass stopped"), L.T("winws.exe завершился", "winws.exe has exited"), ToolTipIcon.Warning);
                return;
            }
            restarts.RemoveAll(t => (DateTime.Now - t).TotalMinutes > 5);
            if (restarts.Count >= 3)
            {
                expectRunning = false;
                Journal.Add(L.T("winws.exe снова завершился — 3 перезапуска за 5 минут не помогли, сторож остановился",
                                "winws.exe exited again — 3 restarts in 5 minutes didn't help, watchdog gave up"));
                Balloon(L.T("Обход остановился", "Bypass stopped"),
                    L.T("Перезапуск не помог. Попробуйте другую стратегию или диагностику.", "Restarting didn't help. Try another strategy or run diagnostics."), ToolTipIcon.Error);
                return;
            }
            restarts.Add(DateTime.Now);
            Journal.Add(L.T("winws.exe завершился — сторож перезапускает (", "winws.exe exited — watchdog restarting (") + restarts.Count + "/3)");
            Balloon(L.T("Обход остановился", "Bypass stopped"), L.T("Перезапускаю…", "Restarting…"), ToolTipIcon.Warning);
            Start();
        }

        void Balloon(string title, string text, ToolTipIcon icon)
        {
            if (tray.Visible) tray.ShowBalloonTip(5000, title, text, icon);
        }

        // ---------- Доступность сайтов ----------

        void CheckNetSoon()
        {
            var t = new System.Windows.Forms.Timer { Interval = 2500 };
            t.Tick += delegate { t.Stop(); t.Dispose(); CheckNet(); };
            t.Start();
        }

        internal void CheckNet()
        {
            if (netChecking || picking || snapPath != null) return;
            netChecking = true;
            UpdateUi();
            bool wasOk = netYt >= 0.99f && netDc >= 0.99f;
            ThreadPool.QueueUserWorkItem(delegate
            {
                long[] ms = NetCheck.RunTimed(5000);
                int yt = 0, dc = 0;
                for (int i = 0; i < ms.Length; i++) if (ms[i] >= 0) { if (i < NetCheck.YouTube.Length) yt++; else dc++; }
                Post(() =>
                {
                    netChecking = false;
                    lastNet = DateTime.Now;
                    lastNetMs = ms;
                    netYt = yt / (float)NetCheck.YouTube.Length;
                    netDc = dc / (float)NetCheck.Discord.Length;
                    bool ok = netYt >= 0.99f && netDc >= 0.99f;
                    string sig = "YouTube " + yt + "/" + NetCheck.YouTube.Length + ", Discord " + dc + "/" + NetCheck.Discord.Length;
                    if (sig != lastNetSig)
                    {
                        Journal.Add(L.T("Доступность: ", "Availability: ") + sig + (running ? "" : L.T(" (обход выключен)", " (bypass off)")));
                        lastNetSig = sig;
                    }
                    if (running && wasOk && !ok)
                        Toast(L.T("YouTube или Discord перестал открываться — попробуйте «Подобрать стратегию»",
                                  "YouTube or Discord stopped working — try “Find a strategy”"), true);
                    UpdateUi();
                });
            });
        }

        // Подпись на плашке: «3/3 · 124 мс» (среднее время по открывшимся адресам).
        string NetMeta(int offset, int count)
        {
            float v = offset == 0 ? netYt : netDc;
            if (v < 0) return "";
            int ok = (int)Math.Round(v * count);
            string s = ok + "/" + count;
            if (lastNetMs == null) return s;
            long sum = 0; int n = 0;
            for (int i = offset; i < offset + count && i < lastNetMs.Length; i++) if (lastNetMs[i] >= 0) { sum += lastNetMs[i]; n++; }
            if (n == 0) return s + L.T(" · не открывается", " · unreachable");
            return s + " · " + (sum / n) + L.T(" мс", " ms");
        }

        // Подсказка на индикаторах: какие адреса открылись и когда была проверка.
        string NetTooltip()
        {
            if (netChecking) return L.T("Идёт проверка…", "Checking…");
            if (lastNetMs == null) return L.T("Проверки ещё не было.\nНажмите, чтобы проверить.", "Not checked yet.\nClick to check.");
            var sb = new System.Text.StringBuilder();
            sb.Append(L.T("Проверено в ", "Checked at ")).Append(lastNet.ToString("HH:mm:ss")).Append("\n");
            Action<string, string[], int> group = (name, urls, offset) =>
            {
                sb.Append("\n").Append(name).Append("\n");
                for (int i = 0; i < urls.Length; i++)
                {
                    long ms = lastNetMs[offset + i];
                    string host = new Uri(urls[i]).Host;
                    sb.Append(ms >= 0 ? "   ✓  " : "   ✗  ").Append(host).Append("  —  ")
                      .Append(ms >= 0 ? ms + L.T(" мс", " ms") : L.T("не открылся", "failed")).Append("\n");
                }
            };
            group("YouTube", NetCheck.YouTube, 0);
            group("Discord", NetCheck.Discord, NetCheck.YouTube.Length);
            sb.Append("\n").Append(L.T("Нажмите, чтобы проверить снова", "Click to check again"));
            return sb.ToString();
        }

        // ---------- Автоподбор стратегии ----------

        void AutoPick()
        {
            if (busy) return;
            strategies = z.Strategies();
            if (strategies.Count == 0) { Toast(L.T("Не найдено ни одной стратегии (.bat)", "No strategies (.bat) found"), true); return; }
            var list = new List<string>(strategies);
            int minutes = Math.Max(2, (int)Math.Ceiling(list.Count * 10 / 60.0));
            if (Ask.Show(this,
                L.T("Программа по очереди включит все стратегии (" + list.Count + " шт.), для каждой 3 раза проверит YouTube и Discord " +
                    "и замерит скорость. В конце покажет таблицу результатов, где можно выбрать стратегию.\n\n" +
                    "Это займёт до " + minutes + " мин., интернет может ненадолго прерываться.\n\nНачать?",
                    "The app will try every strategy (" + list.Count + "), check YouTube and Discord 3 times for each and measure speed. " +
                    "At the end it shows a results table where you can pick a strategy.\n\n" +
                    "This takes up to " + minutes + " min; the connection may drop briefly.\n\nStart?"),
                L.T("Подбор стратегии", "Find a strategy"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            bool wasActive = Active;
            string prev = selected;
            busy = true; picking = true; cancelPick = false; expectRunning = false; pickProgress = 0;
            busyText = L.T("Подбор…", "Testing…");
            UpdateUi();
            Journal.Add(L.T("Подбор стратегии начат: ", "Strategy test started: ") + list.Count + L.T(" шт.", " strategies"));

            ThreadPool.QueueUserWorkItem(delegate
            {
                var results = new List<PickResult>();
                Exception err = null;
                try
                {
                    for (int i = 0; i < list.Count && !cancelPick; i++)
                    {
                        int idx = i; string name = list[i];
                        Post(() =>
                        {
                            if (!cancelPick) busyText = L.T("Подбор ", "Testing ") + (idx + 1) + " / " + list.Count;
                            pickName = name;
                            pickProgress = idx / (float)list.Count;
                            UpdateUi();
                        });
                        var res = new PickResult { Name = name };
                        try
                        {
                            z.Start(name, false);
                            Thread.Sleep(1500);
                            NetCheck.Measure(res, PickResult.PlannedRounds, 5000, () => cancelPick);
                        }
                        catch { res.Failed = true; }
                        if (cancelPick && res.Rounds < PickResult.PlannedRounds && res.YtOk + res.DcOk > 0) break; // недопроверенную не показываем
                        results.Add(res);
                    }
                    z.StopAll(false); // выбор — за пользователем, до него обход выключен
                }
                catch (Exception ex) { err = ex; }

                Post(() =>
                {
                    busy = false; picking = false; pickName = null;
                    RefreshState();
                    if (err != null) Toast(err.Message, true);
                    ShowPickResults(results, cancelPick, wasActive, prev);
                });
            });
        }

        void ShowPickResults(List<PickResult> results, bool partial, bool wasActive, string prev)
        {
            // прошлые подборы — для колонки «История»; текущий сохраняем, только если он прошёл до конца
            var past = PickHistory.Load();
            if (!partial && results.Count > 0) PickHistory.Save(results);
            var ranked = PickResult.Rank(results);
            var best = ranked.FirstOrDefault(r => r.Works);
            Journal.Add((partial ? L.T("Подбор отменён", "Strategy test cancelled") : L.T("Подбор завершён", "Strategy test finished")) +
                L.T(": проверено ", ": tested ") + results.Count +
                (best != null ? L.T(", лучшая — ", ", best — ") + best.Name + " (YouTube " + best.YtOk + "/" + best.YtMax + ", Discord " + best.DcOk + "/" + best.DcMax + ")"
                              : L.T(", рабочих нет", ", none worked")));
            if (!ranked.Any(r => r.Works))
            {
                Toast(partial ? L.T("Подбор отменён", "Testing cancelled")
                              : L.T("Рабочая стратегия не найдена — запустите диагностику", "No working strategy found — run diagnostics"), !partial);
                RestoreAfterPick(wasActive, prev);
                return;
            }
            ShowFromTray();
            string chosen = null;
            using (var f = new PickForm(ranked, iconApp, partial, past, null))
                if (f.ShowDialog(this) == DialogResult.OK) chosen = f.Chosen;
            if (chosen == null) { RestoreAfterPick(wasActive, prev); return; }

            var r0 = ranked.First(r => r.Name == chosen);
            selected = chosen;
            Prefs.Set("Strategy", chosen);
            netYt = r0.YtOk / (float)r0.YtMax;
            netDc = r0.DcOk / (float)r0.DcMax;
            lastNet = DateTime.Now;
            Start();
            Toast(L.T("Применена стратегия «" + chosen + "»", "Applied strategy “" + chosen + "”"));
        }

        // Таблица последнего полного подбора без нового подбора.
        void ShowLastPick()
        {
            if (busy) return;
            var runs = PickHistory.Load();
            if (runs.Count == 0) { Toast(L.T("Подбор ещё не запускался", "No strategy test has been run yet")); return; }
            var last = runs[runs.Count - 1];
            runs.RemoveAt(runs.Count - 1);
            string chosen = null;
            using (var f = new PickForm(PickResult.Rank(last.Results), iconApp, false, runs,
                L.T("Прошлый подбор · ", "Last test · ") + last.When.ToString("dd.MM.yyyy HH:mm")))
                if (f.ShowDialog(this) == DialogResult.OK) chosen = f.Chosen;
            if (chosen == null || !z.Strategies().Contains(chosen)) return;
            selected = chosen;
            Prefs.Set("Strategy", chosen);
            Start();
        }

        void RestoreAfterPick(bool wasActive, string prev)
        {
            if (!wasActive) return;
            selected = prev;
            Start();
        }

        // ================= Меню =================

        internal void ShowStrategyMenu(Control anchor)
        {
            if (busy) return;
            strategies = z.Strategies();
            var m = Menus.Create();
            foreach (string s in strategies)
            {
                string name = s;
                Menus.Add(m, name, name == selected, delegate { SelectStrategy(name); });
            }
            // в мини-панели меню открывается вверх и не должно её закрывать
            var fl = anchor.FindForm() as TrayFlyout;
            if (fl != null)
            {
                fl.MenuOpen = true;
                m.Closed += delegate { fl.MenuOpen = false; };
                m.Show(anchor, new Point(0, -Theme.P(4)), ToolStripDropDownDirection.AboveRight);
            }
            else m.Show(anchor, new Point(0, anchor.Height + Theme.P(4)));
        }

        void ShowBelow(ContextMenuStrip m, Control row)
        {
            m.Show(row, new Point(row.Width - Theme.P(8), row.Height), ToolStripDropDownDirection.BelowLeft);
        }

        void ShowGameMenu()
        {
            if (busy) return;
            var m = Menus.Create();
            foreach (string mode in new[] { "disabled", "all", "tcp", "udp" })
            {
                string md = mode;
                Menus.Add(m, GameLabel(md), z.GameMode == md, delegate
                {
                    if (z.GameMode == md) return;
                    z.GameMode = md; z.SaveGame();
                    Journal.Add(L.T("Игровой фильтр: ", "Game filter: ") + GameLabel(md));
                    UpdateUi();
                    RestartIfActive();
                });
            }
            m.Items.Add(new ToolStripSeparator());
            Menus.Add(m, L.T("Порты: TCP ", "Ports: TCP ") + z.GameTcp + ", UDP " + z.GameUdp + "…", false, delegate { EditGamePorts(); });
            ShowBelow(m, rowGame);
        }

        void EditGamePorts()
        {
            string tcp = InputBox.Show(this, L.T("Порты TCP для игрового фильтра", "TCP ports for the game filter"),
                L.T("Например: 1024-65535 или 1024-1934,1936-65535", "E.g. 1024-65535 or 1024-1934,1936-65535"), z.GameTcp);
            if (tcp == null) return;
            string udp = InputBox.Show(this, L.T("Порты UDP для игрового фильтра", "UDP ports for the game filter"), L.T("Например: 1024-65535", "E.g. 1024-65535"), z.GameUdp);
            if (udp == null) return;
            tcp = tcp.Replace(" ", ""); udp = udp.Replace(" ", "");
            if (!Zapret.ValidRange(tcp) || !Zapret.ValidRange(udp)) { Toast(L.T("Неверный формат портов", "Invalid port format"), true); return; }
            z.GameTcp = tcp; z.GameUdp = udp; z.SaveGame();
            Journal.Add(L.T("Порты игрового фильтра: TCP ", "Game filter ports: TCP ") + tcp + ", UDP " + udp);
            Toast(L.T("Порты сохранены", "Ports saved"));
            if (z.GameMode != "disabled") RestartIfActive();
        }

        void ShowIpsetMenu()
        {
            if (busy) return;
            var m = Menus.Create();
            string cur = z.IpsetMode();
            var items = new[] {
                new[] { "loaded", L.T("По списку (ipset-all)", "List (ipset-all)") },
                new[] { "none", L.T("Выкл", "Off") },
                new[] { "any", L.T("Все IP-адреса", "Any IP address") } };
            foreach (var it in items)
            {
                string md = it[0];
                Menus.Add(m, it[1], cur == md, delegate
                {
                    try { z.SetIpset(md); }
                    catch (Exception ex) { Toast(ex.Message, true); return; }
                    Journal.Add("IPSet: " + IpsetLabel(md));
                    UpdateUi();
                    RestartIfActive();
                });
            }
            ShowBelow(m, rowIpset);
        }

        void ShowLangMenu()
        {
            var m = Menus.Create();
            Menus.Add(m, "Русский", !L.En, delegate { SetLang(false); });
            Menus.Add(m, "English", L.En, delegate { SetLang(true); });
            ShowBelow(m, rowLang);
        }

        static string ThemeLabel(string mode)
        {
            switch (mode)
            {
                case "light": return L.T("Светлая", "Light");
                case "dark": return L.T("Тёмная", "Dark");
                case "amoled": return L.T("Чёрная (AMOLED)", "Black (AMOLED)");
                case "warm": return L.T("Тёплая светлая", "Warm light");
                default: return L.T("Как в Windows", "Like Windows");
            }
        }

        // Меню «Оформление»: тема и цвет акцента (с цветными кружками).
        void ShowThemeMenu()
        {
            var m = Menus.Create();
            m.ShowImageMargin = true;
            string mode = ThemeMode;
            var h1 = Menus.Add(m, L.T("Тема", "Theme"), false, null); h1.Enabled = false;
            foreach (string md in Theme.Modes)
            {
                string x = md;
                Menus.Add(m, ThemeLabel(x), mode == x, delegate { SetThemeMode(x); });
            }
            m.Items.Add(new ToolStripSeparator());
            var h2 = Menus.Add(m, L.T("Цвет", "Color"), false, null); h2.Enabled = false;
            int d = Theme.P(12);
            foreach (string ac in Theme.Accents)
            {
                string x = ac;
                var it = Menus.Add(m, Theme.AccentLabel(x), Theme.AccentName == x, delegate { SetAccent(x); });
                var bmp = new Bitmap(d, d);
                using (var g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    if (x == "auto")
                    {
                        // половина зелёная, половина синяя — «авто» = зелёный в тёмной теме, синий в светлой
                        using (var b1 = new SolidBrush(Color.FromArgb(52, 211, 153))) g.FillPie(b1, 0, 0, d - 1, d - 1, 90, 180);
                        using (var b2 = new SolidBrush(Color.FromArgb(37, 99, 235))) g.FillPie(b2, 0, 0, d - 1, d - 1, 270, 180);
                    }
                    else using (var b = new SolidBrush(Theme.AccentSample(x))) g.FillEllipse(b, 0, 0, d - 1, d - 1);
                }
                it.Image = bmp;
            }
            m.Items.Add(new ToolStripSeparator());
            var h3 = Menus.Add(m, L.T("Масштаб", "Scale"), false, null); h3.Enabled = false;
            int cur = UiScalePref;
            foreach (int sc in Theme.Scales)
            {
                int x = sc;
                Menus.Add(m, x + "%", cur == x, delegate { SetScale(x); });
            }
            ShowBelow(m, rowTheme);
        }

        void ShowFakesMenu()
        {
            if (busy) return;
            var fakes = z.FakeFiles();
            if (fakes.Count == 0) { Toast(L.T("В папке bin нет .bin файлов", "No .bin files in the bin folder"), true); return; }
            var m = Menus.Create();
            AddFakeSection(m, "Discord UDP", Zapret.FakeDiscord, fakes);
            m.Items.Add(new ToolStripSeparator());
            AddFakeSection(m, L.T("Игровой фильтр UDP", "Game filter UDP"), Zapret.FakeGame, fakes);
            ShowBelow(m, rowFakes);
        }

        void AddFakeSection(ContextMenuStrip m, string title, string active, List<string> fakes)
        {
            var head = Menus.Add(m, title, false, null);
            head.Enabled = false;
            string cur = null;
            try { cur = z.CurrentFake(active); } catch { }
            foreach (string f in fakes)
            {
                string name = f;
                Menus.Add(m, name, name == cur, delegate
                {
                    try { z.SetFake(active, name); }
                    catch (Exception ex) { Toast(ex.Message, true); return; }
                    Journal.Add(L.T("Fake-файл ", "Fake file ") + title + ": " + name);
                    Toast(title + ": " + name);
                    RestartIfActive();
                });
            }
        }

        void ShowMoreMenu(Control anchor)
        {
            var m = Menus.Create();
            Menus.Add(m, L.T("Мои домены (list-general-user)", "My domains (list-general-user)"), false, delegate { EditList("list-general-user.txt"); });
            Menus.Add(m, L.T("Исключения (list-exclude-user)", "Exclusions (list-exclude-user)"), false, delegate { EditList("list-exclude-user.txt"); });
            Menus.Add(m, L.T("IP-исключения (ipset-exclude-user)", "IP exclusions (ipset-exclude-user)"), false, delegate { EditList("ipset-exclude-user.txt"); });
            m.Items.Add(new ToolStripSeparator());
            Menus.Add(m, L.T("Результаты прошлого подбора", "Last strategy test results"), false, delegate { ShowLastPick(); });
            Menus.Add(m, L.T("Журнал событий", "Event log"), false, delegate { ShowJournal(); });
            Menus.Add(m, L.T("Диагностика", "Diagnostics"), false, delegate { OpenDiag(); });
            Menus.Add(m, L.T("Классическое меню (service.bat)", "Classic menu (service.bat)"), false, delegate { OpenServiceBat(); });
            Menus.Add(m, L.T("Открыть папку zapret", "Open zapret folder"), false, delegate { Shell("explorer.exe", "\"" + z.Root + "\""); });
            Menus.Add(m, L.T("Страница zapret на GitHub", "zapret on GitHub"), false, delegate { OpenUrl("https://github.com/Flowseal/zapret-discord-youtube"); });
            m.Show(anchor, new Point(anchor.Width / 2, 0), ToolStripDropDownDirection.AboveLeft);
        }

        void EditList(string file)
        {
            try { z.EnsureUserLists(); } catch { }
            string path = Path.Combine(z.Lists, file);
            string title, desc;
            bool ip = file.StartsWith("ipset", StringComparison.OrdinalIgnoreCase);
            if (file == "list-general-user.txt")
            { title = L.T("Мои домены", "My domains"); desc = L.T("Эти сайты тоже пойдут через обход  ·  lists\\", "These sites also go through the bypass  ·  lists\\") + file; }
            else if (file == "list-exclude-user.txt")
            { title = L.T("Исключения", "Exclusions"); desc = L.T("Эти сайты обход не трогает  ·  lists\\", "The bypass leaves these sites alone  ·  lists\\") + file; }
            else
            { title = L.T("IP-исключения", "IP exclusions"); desc = L.T("Эти IP-адреса обход не трогает  ·  lists\\", "The bypass leaves these IPs alone  ·  lists\\") + file; }

            bool saved, notepad;
            using (var f = new ListEditorForm(path, title, desc, ip, iconApp))
            {
                f.ShowDialog(this);
                saved = f.Saved; notepad = f.OpenInNotepad;
            }
            if (saved) Journal.Add(L.T("Список изменён: ", "List changed: ") + file);
            if (notepad)
            {
                Shell("notepad.exe", "\"" + path + "\"");
                Toast(L.T("После сохранения списка перезапустите обход", "Restart the bypass after saving the list"));
                return;
            }
            if (!saved) return;
            if (Active && !busy && Ask.Show(this, L.T("Список сохранён. Перезапустить обход, чтобы изменения заработали?", "List saved. Restart the bypass to apply the changes?"),
                    title, MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Start();
            else Toast(L.T("Список сохранён", "List saved"));
        }

        // ---------- Журнал, резервная копия, «О программе» ----------

        void ShowJournal()
        {
            using (var f = new JournalForm(iconApp, JournalHeader)) f.ShowDialog(this);
        }

        // Сведения о системе в начале скопированного журнала — чтобы тому, кто помогает, сразу было всё видно.
        string JournalHeader()
        {
            string win = "Windows";
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                    if (k != null)
                    {
                        string build = k.GetValue("CurrentBuild") as string ?? "";
                        object ubr = k.GetValue("UBR");
                        int b; int.TryParse(build, out b);
                        win = (b >= 22000 ? "Windows 11" : "Windows 10") + " " + (k.GetValue("DisplayVersion") as string ?? "") + " (build " + build + (ubr != null ? "." + ubr : "") + ")";
                    }
            }
            catch { }
            return "ZapretGUI v" + SelfUpdate.Current.ToString(3) + "  ·  zapret v" + z.LocalVersion + "  ·  " + win + "\r\n" +
                L.T("Обход: ", "Bypass: ") + (running ? L.T("включён", "on") : L.T("выключен", "off")) +
                L.T("  ·  стратегия: ", "  ·  strategy: ") + (activeStrategy ?? selected) + (svc ? L.T(" (служба)", " (service)") : "") +
                L.T("  ·  игровой фильтр: ", "  ·  game filter: ") + GameLabel(z.GameMode) + "  ·  IPSet: " + IpsetLabel(z.IpsetMode()) +
                L.T("  ·  сторож: ", "  ·  watchdog: ") + (WatchdogOn ? L.T("вкл", "on") : L.T("выкл", "off"));
        }

        void ShowAbout()
        {
            using (var f = new AboutForm(iconApp, z.LocalVersion)) f.ShowDialog(this);
        }

        void ShowBackupMenu(Control anchor)
        {
            if (busy) return;
            var m = Menus.Create();
            Menus.Add(m, L.T("Сохранить настройки в файл…", "Save settings to a file…"), false, delegate { ExportSettings(); });
            Menus.Add(m, L.T("Загрузить настройки из файла…", "Load settings from a file…"), false, delegate { ImportSettings(); });
            m.Show(anchor, new Point(anchor.Width / 2, 0), ToolStripDropDownDirection.AboveLeft);
        }

        void ExportSettings()
        {
            using (var d = new SaveFileDialog
            {
                Title = L.T("Сохранить настройки", "Save settings"),
                Filter = L.T("Настройки ZapretGUI (*.zip)|*.zip", "ZapretGUI settings (*.zip)|*.zip"),
                FileName = SettingsBackup.DefaultFileName,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                OverwritePrompt = true,
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                try { SettingsBackup.Export(z, d.FileName); }
                catch (Exception ex) { Toast(L.T("Не удалось сохранить: ", "Could not save: ") + ex.Message, true); return; }
                Journal.Add(L.T("Настройки сохранены в файл: ", "Settings saved to: ") + d.FileName);
                Toast(L.T("Настройки сохранены: ", "Settings saved: ") + Path.GetFileName(d.FileName));
            }
        }

        void ImportSettings()
        {
            string file;
            using (var d = new OpenFileDialog
            {
                Title = L.T("Загрузить настройки", "Load settings"),
                Filter = L.T("Настройки ZapretGUI (*.zip)|*.zip", "ZapretGUI settings (*.zip)|*.zip"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            })
            {
                if (d.ShowDialog(this) != DialogResult.OK) return;
                file = d.FileName;
            }
            string what;
            try { what = SettingsBackup.Describe(file); }
            catch (Exception ex) { Toast(ex.Message, true); return; }
            if (Ask.Show(this,
                L.T("Загрузить настройки из файла «" + Path.GetFileName(file) + "»?\n\n" + what +
                    "\nТекущие файлы с этими настройками будут заменены. Перед этим их копия автоматически сохранится в\n" + SettingsBackup.AutoBackupDir,
                    "Load settings from “" + Path.GetFileName(file) + "”?\n\n" + what +
                    "\nThe current files with these settings will be replaced. A copy of them is saved first to\n" + SettingsBackup.AutoBackupDir),
                L.T("Загрузка настроек", "Load settings"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;

            string safety;
            try { safety = SettingsBackup.Import(z, file); }
            catch (Exception ex) { Toast(L.T("Не удалось загрузить: ", "Could not load: ") + ex.Message, true); return; }
            Journal.Add(L.T("Настройки загружены из файла: ", "Settings loaded from: ") + file + L.T(" (прежние сохранены в ", " (previous saved to ") + safety + ")");

            // применить: язык, тема, стратегия, игровой фильтр
            bool wasActive = Active;
            string before = activeStrategy ?? selected;
            LoadState();
            Rebuild(() =>
            {
                L.Init();
                Theme.AccentName = Prefs.Get("Accent", "auto");
                Theme.SetScale(Theme.Dpi, UiScalePref / 100f);
                Theme.ApplyMode(ThemeMode);
            });
            RefreshState();
            Toast(L.T("Настройки загружены", "Settings loaded"));
            if (wasActive && Ask.Show(this, L.T("Перезапустить обход с новыми настройками?", "Restart the bypass with the new settings?") +
                    (selected != before ? "\n\n" + L.T("Стратегия: ", "Strategy: ") + before + " → " + selected : ""),
                    L.T("Загрузка настроек", "Load settings"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes) Start();
        }

        void OpenDiag()
        {
            using (var f = new DiagForm(z, iconApp)) f.ShowDialog(this);
            RefreshState();
        }

        void OpenServiceBat()
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + Path.Combine(z.Root, "service.bat") + "\" admin\"")
                { WorkingDirectory = z.Root, UseShellExecute = true });
            }
            catch (Exception ex) { Toast(ex.Message, true); }
        }

        void UpdateIpset()
        {
            RunOp(L.T("Загрузка списка IPSet…", "Downloading IPSet list…"), false, z.UpdateIpset,
                () => { Journal.Add(L.T("Список IPSet обновлён", "IPSet list updated")); Toast(L.T("Список IPSet обновлён", "IPSet list updated")); });
        }

        // ---------- Обновления ----------

        void CheckUpdates()
        {
            string local = z.LocalVersion;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string remote = null;
                try { remote = z.RemoteVersion(); } catch { }
                if (!string.IsNullOrEmpty(remote) && remote != local) Post(() => { newVersion = remote; UpdateUi(); });

                // своя версия: молча, ошибки сети не показываем
                Version gui = null;
                try { string url, sha; gui = SelfUpdate.Latest(out url, out sha); } catch { }
                if (gui != null && gui > SelfUpdate.Current)
                    Post(() =>
                    {
                        newGui = gui.ToString(3);
                        UpdateUi();
                        Toast(L.T("Доступна ZapretGUI v" + newGui + " — Настройки → Обновления", "ZapretGUI v" + newGui + " is available — Settings → Updates"));
                    });
            });
        }

        void ShowUpdatesMenu()
        {
            if (busy) return;
            var m = Menus.Create();
            Menus.Add(m, L.T("Обновить zapret", "Update zapret") + (newVersion != null ? "  →  v" + newVersion : "  (v" + z.LocalVersion + ")"),
                false, delegate { UpdateZapret(); });
            Menus.Add(m, L.T("Обновить ZapretGUI", "Update ZapretGUI") + (newGui != null ? "  →  v" + newGui : "  (v" + SelfUpdate.Current.ToString(3) + ")"),
                false, delegate { UpdateGui(); });
            m.Items.Add(new ToolStripSeparator());
            Menus.Add(m, L.T("Проверять обновления при запуске", "Check for updates on start"), z.UpdatesEnabled, delegate
            {
                try { z.UpdatesEnabled = !z.UpdatesEnabled; } catch (Exception ex) { Toast(ex.Message, true); return; }
                Toast(z.UpdatesEnabled ? L.T("Обновления будут проверяться при запуске", "Updates will be checked on start")
                                       : L.T("Проверка обновлений при запуске выключена", "Update check on start is off"));
            });
            ShowBelow(m, rowUpdate);
        }

        void UpdateGui()
        {
            if (busy) return;
            hint.ForeColor = Theme.Muted;
            hint.Text = L.T("Проверяю обновления ZapretGUI…", "Checking for ZapretGUI updates…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                Version latest = null; string url = null, sha = null; Exception err = null;
                try { latest = SelfUpdate.Latest(out url, out sha); } catch (Exception ex) { err = ex; }
                Post(() =>
                {
                    hint.Text = "";
                    if (err != null)
                    {
                        var we = err as System.Net.WebException;
                        var resp = we == null ? null : we.Response as System.Net.HttpWebResponse;
                        Toast(resp != null && resp.StatusCode == System.Net.HttpStatusCode.NotFound
                            ? L.T("Релизы ZapretGUI на GitHub сейчас недоступны", "ZapretGUI releases on GitHub are not available right now")
                            : L.T("Не удалось проверить обновления ZapretGUI: ", "ZapretGUI update check failed: ") + err.Message, true);
                        return;
                    }
                    string cur = SelfUpdate.Current.ToString(3);
                    if (latest <= SelfUpdate.Current) { newGui = null; UpdateUi(); Toast(L.T("Установлена последняя версия ZapretGUI v", "Latest ZapretGUI installed: v") + cur); return; }
                    newGui = latest.ToString(3);
                    UpdateUi();
                    if (Ask.Show(this,
                        L.T("Обновить ZapretGUI с v" + cur + " до v" + newGui + "?\n\nПрограмма перезапустится. Обход продолжит работать, настройки сохранятся.",
                            "Update ZapretGUI from v" + cur + " to v" + newGui + "?\n\nThe app will restart. The bypass keeps running and settings are kept."),
                        L.T("Обновление программы", "App update"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    string exe = Application.ExecutablePath;
                    RunOp(L.T("Скачиваю ZapretGUI v", "Downloading ZapretGUI v") + newGui + "…", false,
                        () => SelfUpdate.DownloadAndInstall(exe, url, sha),
                        () =>
                        {
                            Journal.Add(L.T("ZapretGUI обновлён: v", "ZapretGUI updated: v") + cur + " → v" + newGui +
                                (sha != null ? L.T(" (контрольная сумма совпала)", " (checksum verified)") : L.T(" (в релизе нет контрольной суммы)", " (no checksum in the release)")));
                            RestartSelf();
                        });
                });
            });
        }

        // Запускает новую копию программы и закрывает текущую.
        void RestartSelf()
        {
            try
            {
                Process.Start(new ProcessStartInfo(Application.ExecutablePath,
                    "--wait-pid " + Process.GetCurrentProcess().Id + (Visible ? "" : " --tray")) { UseShellExecute = false });
            }
            catch (Exception ex) { Toast(L.T("Обновление установлено, но перезапуск не удался: ", "Update installed, but restart failed: ") + ex.Message, true); return; }
            exitRequested = true;
            Close();
        }

        void UpdateZapret()
        {
            if (busy) return;
            string local = z.LocalVersion;
            hint.ForeColor = Theme.Muted;
            hint.Text = L.T("Проверяю обновления…", "Checking for updates…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string tag = null, url = null; Exception err = null;
                try { tag = z.LatestRelease(out url); } catch (Exception ex) { err = ex; }
                Post(() =>
                {
                    hint.Text = "";
                    if (err != null) { Toast(L.T("Не удалось проверить обновления: ", "Update check failed: ") + err.Message, true); return; }
                    if (tag == local) { newVersion = null; UpdateUi(); Toast(L.T("Установлена последняя версия ", "Latest version installed: ") + local); return; }
                    newVersion = tag;
                    UpdateUi();
                    if (Ask.Show(this,
                        L.T("Обновить zapret с v" + local + " до v" + tag + "?\n\nОбход ненадолго выключится. Ваши списки, настройки и fake-файлы сохранятся.\n" +
                            "Стратегии, которых нет в новой версии, будут перенесены в папку old-strategies.",
                            "Update zapret from v" + local + " to v" + tag + "?\n\nThe bypass will stop briefly. Your lists, settings and fake files are kept.\n" +
                            "Strategies missing from the new version will be moved to the old-strategies folder."),
                        L.T("Обновление", "Update"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    bool wasActive = Active;
                    string name = selected;
                    bool asSvc = autostart;
                    expectRunning = false;
                    RunOp(L.T("Обновление…", "Updating…"), true, () =>
                    {
                        z.StopAll(true);
                        z.UpdateFrom(url);
                        if (wasActive)
                        {
                            if (!File.Exists(Path.Combine(z.Root, name + ".bat"))) name = "general";
                            int pid = z.Start(name, asSvc);
                            Prefs.Set("RunPid", pid.ToString());
                            Prefs.Set("RunStrategy", name);
                        }
                    }, () =>
                    {
                        newVersion = null;
                        strategies = z.Strategies();
                        if (!strategies.Contains(selected)) selected = strategies.Contains("general") ? "general" : (strategies.Count > 0 ? strategies[0] : "");
                        expectRunning = wasActive;
                        Journal.Add(L.T("zapret обновлён: v", "zapret updated: v") + local + " → v" + z.LocalVersion);
                        Toast(L.T("zapret обновлён до v", "zapret updated to v") + z.LocalVersion);
                        CheckNetSoon();
                    });
                });
            });
        }

        void Toast(string msg, bool error = false)
        {
            hint.ForeColor = error ? Theme.Danger : Theme.Muted;
            hint.Text = msg;
            hintTimer.Stop();
            hintTimer.Interval = error ? 8000 : 4000;
            hintTimer.Start();
        }

        static void OpenUrl(string url) { Shell(url, null); }

        static void Shell(string file, string args)
        {
            try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); } catch { }
        }

        // ================= Окно и трей =================

        protected override void SetVisibleCore(bool value)
        {
            if (startHidden) { startHidden = false; value = false; if (!IsHandleCreated) CreateHandle(); }
            base.SetVisibleCore(value);
        }

        internal void ShowFromTray()
        {
            Show();
            if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal;
            Activate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (WindowState == FormWindowState.Minimized && tray != null && tray.Visible) Hide();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_SHOWME) ShowFromTray();
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyTitleBar(Handle);
        }

        public static void ApplyTitleBar(IntPtr handle)
        {
            try
            {
                int dark = Theme.IsLight ? 0 : 1;
                DwmSetWindowAttribute(handle, 20, ref dark, 4);         // тёмный/светлый заголовок
                int color = Theme.Bg.R | (Theme.Bg.G << 8) | (Theme.Bg.B << 16);
                DwmSetWindowAttribute(handle, 35, ref color, 4);        // цвет заголовка (Win11)
                int text = Theme.Text.R | (Theme.Text.G << 8) | (Theme.Text.B << 16);
                DwmSetWindowAttribute(handle, 36, ref text, 4);         // цвет текста заголовка
            }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
#if TESTHOOKS
            // тестовый режим снимка окна (только в сборке build.ps1 -NoAdmin)
            if (snapPath == null) return;
            if (Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_NET") == "ok") { netYt = 1; netDc = 1; UpdateUi(); }
            power.Settle();
            Form target = this;
            int delay = 400;
            if (Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_PAGE") == "pick")
            {
                // выдуманные результаты — только для снимка окна
                var demo = new List<PickResult> {
                    new PickResult { Name = "general (ALT)", YtOk = 9, DcOk = 9, Rounds = 3, Samples = 18, TotalMs = 18 * 140, SpeedKBps = 820 },
                    new PickResult { Name = "general (SIMPLE FAKE)", YtOk = 9, DcOk = 9, Rounds = 3, Samples = 18, TotalMs = 18 * 95, SpeedKBps = 2400 },
                    new PickResult { Name = "general (ALT2)", YtOk = 6, DcOk = 9, Rounds = 3, Samples = 15, TotalMs = 15 * 210, SpeedKBps = 400 },
                    new PickResult { Name = "general", YtOk = 0, DcOk = 0, Rounds = 1 },
                    new PickResult { Name = "my custom", Failed = true },
                };
                var demoHistory = new List<PickHistory.Run> {
                    new PickHistory.Run { When = DateTime.Now.AddDays(-2), Results = new List<PickResult> {
                        new PickResult { Name = "general (SIMPLE FAKE)", YtOk = 9, DcOk = 9, Rounds = 3 },
                        new PickResult { Name = "general (ALT)", YtOk = 5, DcOk = 9, Rounds = 3 } } },
                    new PickHistory.Run { When = DateTime.Now.AddDays(-1), Results = new List<PickResult> {
                        new PickResult { Name = "general (SIMPLE FAKE)", YtOk = 9, DcOk = 9, Rounds = 3 },
                        new PickResult { Name = "general (ALT)", YtOk = 9, DcOk = 9, Rounds = 3 },
                        new PickResult { Name = "general", YtOk = 0, DcOk = 0, Rounds = 1 } } },
                };
                target = new PickForm(PickResult.Rank(demo), iconApp, false, demoHistory, null) { StartPosition = FormStartPosition.Manual, Location = Location };
                target.Show(this);
                delay = 800;
            }
            else if (Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_PAGE") == "diag")
            {
                target = new DiagForm(z, iconApp) { StartPosition = FormStartPosition.Manual, Location = Location };
                target.Show(this);
                delay = 4000;
            }
            var t = new System.Windows.Forms.Timer { Interval = delay };
            t.Tick += delegate
            {
                t.Stop();
                using (var bmp = new Bitmap(target.Width, target.Height))
                {
                    target.DrawToBitmap(bmp, new Rectangle(0, 0, bmp.Width, bmp.Height));
                    if (Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_CLIENT") == "1")
                    {
                        // только содержимое окна, без рамки и заголовка
                        Point o = target.PointToScreen(Point.Empty);
                        var r = new Rectangle(o.X - target.Left, o.Y - target.Top, target.ClientSize.Width, target.ClientSize.Height);
                        using (var client = bmp.Clone(r, bmp.PixelFormat)) client.Save(snapPath, System.Drawing.Imaging.ImageFormat.Png);
                    }
                    else bmp.Save(snapPath, System.Drawing.Imaging.ImageFormat.Png);
                }
                exitRequested = true;
                Close();
            };
            t.Start();
#endif
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            // Крестик (или Alt+F4) прячет окно в трей, если включена настройка.
            // Выход из меню трея и завершение работы Windows закрывают полностью.
            if (!exitRequested && e.CloseReason == CloseReason.UserClosing && CloseToTray)
            {
                e.Cancel = true;
                Hide();
                if (Prefs.Get("TrayHintShown", "0") != "1")
                {
                    Prefs.Set("TrayHintShown", "1");
                    tray.ShowBalloonTip(4000, L.T("Zapret работает в трее", "Zapret is in the tray"),
                        L.T("Чтобы закрыть приложение полностью, нажмите правой кнопкой на значок → Выход.",
                            "To quit completely, right-click the icon → Quit."), ToolTipIcon.Info);
                }
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            Journal.Add(L.T("Программа закрыта", "App closed") + (running ? L.T(" (обход продолжает работать)", " (bypass keeps running)") : ""));
            tray.Visible = false;
            tray.Dispose();
            SystemEvents.UserPreferenceChanged -= OnSystemPrefs;
            base.OnFormClosed(e);
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int RegisterWindowMessage(string msg);
    }

    static class Prefs
    {
        const string Key = @"Software\ZapretGUI";
        public static string Get(string name, string def)
        {
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) { var v = k == null ? null : k.GetValue(name) as string; return v ?? def; } }
            catch { return def; }
        }
        public static void Set(string name, string value)
        {
            try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, value ?? ""); } catch { }
        }
    }

    // Простое окно ввода в цветах темы.
    static class InputBox
    {
        public static string Show(IWin32Window owner, string title, string hintText, string value)
        {
            Func<float, int> P = Theme.P;
            using (var f = new Form())
            {
                f.Text = "Zapret";
                f.FormBorderStyle = FormBorderStyle.FixedDialog;
                f.MinimizeBox = f.MaximizeBox = false;
                f.ShowInTaskbar = false;
                f.StartPosition = FormStartPosition.CenterParent;
                f.BackColor = Theme.Bg;
                f.ForeColor = Theme.Text;
                f.Font = Theme.Body;
                f.ClientSize = new Size(P(360), P(170));
                f.HandleCreated += delegate { MainForm.ApplyTitleBar(f.Handle); };
                var lt = new Label { Text = title, AutoSize = false, Bounds = new Rectangle(P(20), P(16), P(320), P(22)) };
                var lh = new Label { Text = hintText, AutoSize = false, ForeColor = Theme.Muted, Font = Theme.Small, Bounds = new Rectangle(P(20), P(40), P(320), P(20)) };
                var tb = new TextBox { Text = value, BorderStyle = BorderStyle.FixedSingle, BackColor = Theme.Surface, ForeColor = Theme.Text,
                    Bounds = new Rectangle(P(20), P(70), P(320), P(28)) };
                var ok = new Button { Text = L.T("Сохранить", "Save"), DialogResult = DialogResult.OK, FlatStyle = FlatStyle.Flat, BackColor = Theme.Accent,
                    ForeColor = Theme.IsLight ? Color.White : Theme.Bg, Bounds = new Rectangle(P(240), P(118), P(100), P(32)) };
                ok.FlatAppearance.BorderSize = 0;
                var cancel = new Button { Text = L.T("Отмена", "Cancel"), DialogResult = DialogResult.Cancel, FlatStyle = FlatStyle.Flat, BackColor = Theme.Surface,
                    ForeColor = Theme.Text, Bounds = new Rectangle(P(130), P(118), P(100), P(32)) };
                cancel.FlatAppearance.BorderSize = 0;
                f.Controls.AddRange(new Control[] { lt, lh, tb, ok, cancel });
                f.AcceptButton = ok; f.CancelButton = cancel;
                return f.ShowDialog(owner) == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }
    }
}
