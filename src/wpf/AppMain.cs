// Точка входа версии на WPF: одна копия программы, поиск папки zapret, запуск окна (или сразу в трей).
using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

[assembly: AssemblyTitle("Zapret GUI")]
[assembly: AssemblyProduct("Zapret GUI by ryxIbtw")]
[assembly: AssemblyCompany("ryxIbtw")]
[assembly: AssemblyCopyright("© ryxIbtw")]
[assembly: AssemblyDescription("Интерфейс для zapret-discord-youtube by Flowseal. GUI by ryxIbtw")]
[assembly: AssemblyVersion("2.7.0.0")]
[assembly: AssemblyFileVersion("2.7.0.0")]

namespace ZapretGui
{
    static class AppMain
    {
        [STAThread]
        static int Main(string[] args)
        {
            L.Init();
            string rootArg = null, snap = null;
            bool hidden = false, scheduledPick = false, moved = false;
            string removeOld = null;        // после переезда в папку zapret — старая копия программы, которую нужно убрать
            int waitPid = 0, command = 0;   // команды для ярлыков: 1 — --on, 2 — --off, 3 — --toggle
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--tray") hidden = true;
                else if (args[i] == "--scheduled-pick") { scheduledPick = true; hidden = true; }
                else if (args[i] == "--on") { command = 1; hidden = true; }
                else if (args[i] == "--off") { command = 2; hidden = true; }
                else if (args[i] == "--toggle") { command = 3; hidden = true; }
                else if (args[i] == "--root" && i + 1 < args.Length) rootArg = args[++i];
                else if (args[i] == "--wait-pid" && i + 1 < args.Length) int.TryParse(args[++i], out waitPid);
                else if (args[i] == "--remove-old" && i + 1 < args.Length) removeOld = args[++i];
                else if (args[i] == "--moved") moved = true;
#if TESTHOOKS
                else if (args[i] == "--snap" && i + 1 < args.Length) snap = args[++i];
#endif
            }

            // после самообновления: дождаться, пока старая копия закроется, и убрать её файл
            if (waitPid > 0) { try { System.Diagnostics.Process.GetProcessById(waitPid).WaitForExit(15000); } catch { } }
            SelfUpdate.CleanupOld();
            if (removeOld != null) SelfMove.RemoveOldCopy(removeOld);

            bool first;
            using (var mutex = new Mutex(true, "ZapretGUI_SingleInstance", out first))
            {
                if (!first && snap == null)
                {
                    // программа уже запущена — передать ей просьбу (показать окно или подбор по расписанию)
                    if (command > 0) PostMessage((IntPtr)0xFFFF, MainWindow.WM_COMMANDLINE, (IntPtr)command, IntPtr.Zero);
                    else PostMessage((IntPtr)0xFFFF, scheduledPick ? MainWindow.WM_PICKNOW : MainWindow.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                // приложение и тема — до поиска папки, чтобы окна первого запуска были в стиле программы
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                W.Install(app);
                string mode = Prefs.Get("Theme", "system");
                Theme.AccentName = Prefs.Get("Accent", "auto");
                Theme.ApplyMode(Array.IndexOf(Theme.Modes, mode) >= 0 ? mode : "system");
                W.Sync();

                string root = FindRoot(rootArg);
                if (root == null) return relaunched ? 0 : 1;   // relaunched — программа переехала в папку zapret и запустилась оттуда
                var z = new Zapret(root);

                var w = new MainWindow(z, snap, hidden);
#if TESTHOOKS
                if (snap != null) { Snap.Run(w, snap); app.Run(); return 0; }
#endif
                if (!w.StartHidden) w.Show();
                if (moved)
                {
                    // только что переехали в папку zapret — предложить ярлык, чтобы программу было легко найти
                    var tm = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                    tm.Tick += delegate
                    {
                        tm.Stop();
                        if (AskW.Show(w, L.T("Программа теперь лежит в папке ", "The app now lives in ") + Path.GetDirectoryName(WF.Application.ExecutablePath) +
                                L.T(".\n\nСоздать ярлык на рабочем столе и в меню «Пуск»?", ".\n\nCreate a shortcut on the desktop and in the Start menu?"),
                                "Zapret", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) w.CreateShortcutsPublic();
                    };
                    tm.Start();
                }
                if (command > 0)
                {
                    int cmd = command;
                    var tc = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    tc.Tick += delegate { tc.Stop(); w.RunCommand(cmd); };
                    tc.Start();
                }
                if (scheduledPick)
                {
                    // запустились ради подбора по расписанию — дать обходу и сети подняться, потом начать
                    var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
                    t.Tick += delegate { t.Stop(); w.ScheduledPick(); };
                    t.Start();
                }
                app.Run();
            }
            return 0;
        }

        static string FindRoot(string arg)
        {
            string exeDir = Path.GetDirectoryName(WF.Application.ExecutablePath);
            foreach (string c in new[] { arg, exeDir, Prefs.Get("Root", null) })
                if (Zapret.IsValidRoot(c)) { Prefs.Set("Root", Path.GetFullPath(c)); return Path.GetFullPath(c); }

            // zapret не найден — человек сам выбирает: установить zapret в свою папку или указать, где он уже лежит.
            // В обоих случаях программа переезжает в папку zapret. "" — вернуться к выбору, null — выйти.
            while (true)
            {
                string text = L.T("zapret-discord-youtube не найден.\n\n" +
                        "«Установить zapret» — выбрать папку: программа скачает туда zapret от Flowseal с GitHub и переедет туда же.\n\n" +
                        "«У меня уже есть zapret» — указать папку, где лежит service.bat: программа предложит перенести себя туда.",
                    "zapret-discord-youtube was not found.\n\n" +
                        "“Install zapret” — pick a folder: the app downloads zapret by Flowseal from GitHub there and moves there too.\n\n" +
                        "“I already have zapret” — pick the folder with service.bat: the app offers to move there.");
                int c = AskW.Choose(null, text, "Zapret", MessageBoxImage.Question,
                    L.T("Отмена", "Cancel"), L.T("У меня уже есть zapret", "I already have zapret"), L.T("Установить zapret", "Install zapret"));
                string r = c == 2 ? InstallFlow() : c == 1 ? ExistingFlow() : null;
                if (r != "") return r;
            }
        }

        // Установка: выбрать папку, подтвердить итоговый путь, скачать zapret, переехать.
        static string InstallFlow()
        {
            while (true)
            {
                string picked = FolderPicker.Pick(L.T("Куда установить zapret (например, диск C:\\ — тогда будет C:\\zapret)", "Where to install zapret (e.g. drive C:\\ gives C:\\zapret)"));
                if (picked == null) return "";
                string problem;
                string target = SelfMove.InstallTarget(picked, out problem);
                if (target == null) { AskW.Show(null, problem, "Zapret", MessageBoxButton.OK, MessageBoxImage.Warning); continue; }
                if (Zapret.IsValidRoot(target))
                {
                    AskW.Show(null, L.T("Здесь уже есть zapret: ", "zapret is already here: ") + target + L.T("\n\nПрограмма будет работать с ним и переедет туда.", "\n\nThe app will use it and move there."),
                        "Zapret", MessageBoxButton.OK, MessageBoxImage.Information);
                    return UseRoot(target);
                }
                string warn = SelfMove.PathWarning(target);
                var a = AskW.Show(null, L.T("zapret будет установлен в папку:\n", "zapret will be installed to:\n") + target +
                        (warn != null ? "\n\n" + warn : "") +
                        L.T("\n\nПрограмма скачает zapret с GitHub и переедет в эту папку. Продолжить?", "\n\nThe app downloads zapret from GitHub and moves to this folder. Continue?"),
                    "Zapret", MessageBoxButton.YesNo, warn != null ? MessageBoxImage.Warning : MessageBoxImage.Question);
                if (a != MessageBoxResult.Yes) continue;

                Exception err = null;
                var wait = new Window { Title = "Zapret", Width = 360, Height = 120, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize,
                    Background = W.Bg, Content = new System.Windows.Controls.TextBlock { Text = L.T("Скачиваю zapret…", "Downloading zapret…"), Foreground = W.Text, FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                var th = new Thread(() =>
                {
                    try
                    {
                        // папка новая или пустая (так выбирает InstallTarget): сначала закрыть её от записи без прав администратора, потом класть файлы
                        Directory.CreateDirectory(target);
                        try { FolderAcl.Harden(target); }
                        catch (Exception ex) { Journal.Add(L.T("Не удалось защитить права на папку ", "Could not protect permissions of ") + target + ": " + ex.Message); }
                        Zapret.InstallFresh(target);
                    }
                    catch (Exception ex) { err = ex; }
                    wait.Dispatcher.BeginInvoke((Action)wait.Close);
                }) { IsBackground = true };
                wait.Loaded += delegate { th.Start(); };
                wait.ShowDialog();
                if (err == null && Zapret.IsValidRoot(target))
                {
                    Journal.Add(L.T("zapret скачан в ", "zapret downloaded to ") + target);
                    return UseRoot(target);
                }
                AskW.Show(null, L.T("Не удалось скачать zapret: ", "Could not download zapret: ") + (err != null ? err.Message : "?") +
                    L.T("\n\nПроверьте интернет и попробуйте ещё раз или укажите готовую папку zapret.", "\n\nCheck your connection and try again, or pick an existing zapret folder."),
                    "Zapret", MessageBoxButton.OK, MessageBoxImage.Warning);
                return "";
            }
        }

        // zapret уже есть: выбрать его папку и переехать туда.
        static string ExistingFlow()
        {
            while (true)
            {
                string picked = FolderPicker.Pick(L.T("Папка zapret-discord-youtube (где лежит service.bat)", "zapret-discord-youtube folder (with service.bat)"));
                if (picked == null) return "";
                string root = Zapret.IsValidRoot(picked) ? Path.GetFullPath(picked)
                            : Zapret.IsValidRoot(Path.Combine(picked, "zapret")) ? Path.GetFullPath(Path.Combine(picked, "zapret")) : null;
                if (root != null)
                {
                    var a = AskW.Show(null, L.T("Папка zapret: ", "zapret folder: ") + root +
                            L.T("\n\nПеренести программу в эту папку?\n\n«Да» — перенести (программа запустится уже оттуда), «Нет» — выбрать другую папку.",
                                "\n\nMove the app to this folder?\n\n“Yes” — move (the app restarts from there), “No” — pick another folder."),
                        "Zapret", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (a == MessageBoxResult.Yes) return UseRoot(root);
                    continue;
                }
                AskW.Show(null, L.T("В этой папке нет service.bat и папки bin.", "This folder has no service.bat and bin folder."), "Zapret", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // Запомнить папку zapret и перенести программу туда; не вышло — работать с того места, где программа лежит сейчас.
        static string UseRoot(string root)
        {
            Prefs.Set("Root", root);
            Journal.Add(L.T("Папка zapret: ", "zapret folder: ") + root);
            string err;
            if (SelfMove.MoveTo(root, out err)) { relaunched = true; return null; }
            if (err != null)
                AskW.Show(null, L.T("Перенести программу в папку zapret не удалось: ", "Could not move the app to the zapret folder: ") + err +
                    L.T("\n\nПрограмма продолжит работать с того места, где лежит сейчас.", "\n\nThe app keeps working from where it is now."), "Zapret", MessageBoxButton.OK, MessageBoxImage.Warning);
            return root;
        }

        static bool relaunched;

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    }

    // Переезд программы в папку zapret при первом запуске (например, из «Загрузок»).
    static class SelfMove
    {
        // Куда ставить zapret, если человек выбрал папку picked:
        // там уже zapret — она; папки нет или пустая — она; в ней чужие файлы — вложенная «zapret», чтобы не смешивать.
        // null и problem — если и вложенная «zapret» занята чем-то другим.
        public static string InstallTarget(string picked, out string problem)
        {
            problem = null;
            string p = Path.GetFullPath(picked);
            if (Zapret.IsValidRoot(p) || IsEmptyOrMissing(p)) return p;
            string sub = Path.Combine(p, "zapret");
            if (Zapret.IsValidRoot(sub) || IsEmptyOrMissing(sub)) return sub;
            problem = L.T("В папке " + p + " уже есть папка zapret с другими файлами.\n\nВыберите другую папку.",
                          "The folder " + p + " already has a zapret folder with other files.\n\nPick another folder.");
            return null;
        }

        static bool IsEmptyOrMissing(string dir)
        {
            try { return !Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any(); }
            catch { return false; }
        }

        // Предупреждение о пути, где zapret может не работать, или null.
        public static string PathWarning(string dir)
        {
            if (System.Text.RegularExpressions.Regex.IsMatch(dir, "[а-яА-ЯёЁ]"))
                return L.T("В пути есть русские буквы — zapret в такой папке может не работать. Лучше выбрать, например, C:\\.",
                           "The path has Cyrillic letters — zapret may not work there. Better pick, e.g., C:\\.");
            string od = Environment.GetEnvironmentVariable("OneDrive");
            if (!string.IsNullOrEmpty(od) && (dir + "\\").StartsWith(od.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))
                return L.T("Это папка OneDrive — zapret в ней не работает. Лучше выбрать, например, C:\\.",
                           "This is a OneDrive folder — zapret doesn't work there. Better pick, e.g., C:\\.");
            return null;
        }

        // Копирует себя в dir и запускает копию; старая копия будет удалена новой, когда эта закроется.
        // true — новая копия запущена (этой нужно закрыться); false — переезд не нужен (err == null) или не удался (err — причина).
        public static bool MoveTo(string dir, out string err)
        {
            err = null;
            try
            {
                string exe = WF.Application.ExecutablePath;
                string dst = Path.Combine(dir, "ZapretGUI.exe");
                if (string.Equals(Path.GetFullPath(Path.GetDirectoryName(exe)).TrimEnd('\\'), Path.GetFullPath(dir).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;
                File.Copy(exe, dst, true);
                // переносной режим: файл настроек тоже переезжает (старый остаётся на месте)
                string ini = Path.Combine(Path.GetDirectoryName(exe), "ZapretGUI.ini");
                if (File.Exists(ini) && !File.Exists(Path.Combine(dir, "ZapretGUI.ini"))) File.Copy(ini, Path.Combine(dir, "ZapretGUI.ini"));
                Journal.Add(L.T("Программа переехала: ", "The app moved: ") + exe + " → " + dst);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dst,
                    "--wait-pid " + System.Diagnostics.Process.GetCurrentProcess().Id + " --remove-old \"" + exe + "\" --moved") { UseShellExecute = false, WorkingDirectory = dir });
                return true;
            }
            catch (Exception ex) { err = ex.Message; return false; }
        }

        // Удаляет старую копию программы — только если это точно та же программа (совпадает контрольная сумма) и не мы сами.
        public static void RemoveOldCopy(string path)
        {
            try
            {
                string self = WF.Application.ExecutablePath;
                if (!path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return;
                if (string.Equals(Path.GetFullPath(path), Path.GetFullPath(self), StringComparison.OrdinalIgnoreCase)) return;
                string a = SelfUpdate.FileSha256(path), b = SelfUpdate.FileSha256(self);
                if (a == null || a != b) { Journal.Add(L.T("Старая копия программы оставлена на месте (файлы отличаются): ", "The old app copy was kept (files differ): ") + path); return; }
                for (int i = 0; i < 10; i++)   // старая копия может ещё закрываться
                {
                    try { File.Delete(path); Journal.Add(L.T("Старая копия программы удалена: ", "The old app copy was removed: ") + path); return; }
                    catch { Thread.Sleep(500); }
                }
            }
            catch { }
        }
    }

#if TESTHOOKS
    // Тестовый режим (только сборка -NoAdmin): снимок окна в PNG. ZAPRETGUI_SNAP_PAGE: main/settings/welcome/pick/diag/about/journal/ask/editor/flyout.
    static class Snap
    {
        public static void Run(MainWindow w, string path)
        {
            string page = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_PAGE") ?? "";
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            w.Left = -3000; w.Top = 0;
            w.Show();
            w.StatsSource = DemoStats;
            w.RefreshToday();
            w.SnapPrepare();
            Window target = w;
            if (page == "pick")
            {
                // выдуманные результаты — только для снимка окна
                var demo = new System.Collections.Generic.List<PickResult> {
                    new PickResult { Name = "general (ALT)", Mode = "std", Ok = 33, Err = 3, Total = 36, PingOk = 16, PingFail = 1, SpeedKBps = 820 },
                    new PickResult { Name = "general (SIMPLE FAKE)", Mode = "std", Ok = 34, Err = 0, Unsup = 2, Total = 36, PingOk = 17, SpeedKBps = 2400 },
                    new PickResult { Name = "general (ALT2)", Mode = "std", Ok = 34, Err = 2, Total = 36, PingOk = 15, PingFail = 2, SpeedKBps = 400 },
                    new PickResult { Name = "general", Mode = "std", Ok = 12, Err = 24, Total = 36, PingOk = 17 },
                    new PickResult { Name = "my custom", Mode = "std", Failed = true } };
                var hist = new System.Collections.Generic.List<PickHistory.Run> {
                    new PickHistory.Run { When = DateTime.Now.AddDays(-1), Results = new System.Collections.Generic.List<PickResult> {
                        new PickResult { Name = "general (SIMPLE FAKE)", YtOk = 9, DcOk = 9, Rounds = 3 }, new PickResult { Name = "general (ALT)", YtOk = 5, DcOk = 9, Rounds = 3 } } } };
                target = new PickW(w, PickResult.Rank(demo), false, hist, null, @"C:\zapret\utils\test results\test_results.txt");
            }
            else if (page == "picksetup")
                target = new PickSetupW(w, w.Zap.Strategies(), FlowTest.LoadTargets(w.Zap).Count, FlowTest.TargetsFromFile(w.Zap));
            else if (page == "diag") target = new DiagW(w, w.Zap);
            else if (page == "about") target = new AboutW(w, "1.10.4");
            else if (page == "journal") target = new JournalW(w, () => "header");
            else if (page == "whatsnew")
                target = new WhatsNewW(w, "1.10.4", "1.10.5", new System.Collections.Generic.List<string> { "general (ALT12)", "general (FAKE TLS AUTO ALT4)" },
                    new System.Collections.Generic.List<string> { "general (ALT3)" });
            else if (page == "strategies") target = new StrategiesW(w);
            else if (page == "hotkey") target = new HotkeyW(w);
            else if (page == "color") target = new ColorW(w, 0x34D399);
            else if (page == "stratedit") target = new StrategyEditW(w, w.Zap, w.Zap.Strategies().FirstOrDefault() ?? "general");
            else if (page == "help") target = new HelpW(w);
            else if (page == "checksite") target = new CheckSiteW(w);
            else if (page == "targets") target = new TargetsW(w, w.Zap);
            else if (page == "guinews")
                target = new GuiNewsW(w, "2.3.0", File.ReadAllText(Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_NOTES")));
            else if (page == "report") target = new ReportW(w, () => "ZapretGUI v2.2.0  ·  zapret v1.10.4  ·  Windows 11 25H2 (build 26300.1)\r\nОбход: включён  ·  стратегия: general (ALT)");
            else if (page == "editor")
            {
                string f = Path.Combine(Path.GetTempPath(), "zapretgui-snap-list.txt");
                File.WriteAllText(f, "# Never leave this file empty\r\ndomain.example.abc\r\nexample.org\r\nyoutube.com\r\ndiscord.com\r\nxn--e1afmkfd.xn--p1ai\r\n");
                target = new ListEditorW(w, f, L.T("Мои домены", "My domains"), L.T("Эти сайты тоже пойдут через обход  ·  lists\\list-general-user.txt", "These sites also go through the bypass"), false);
            }
            else if (page == "flyout") target = w.SnapFlyout();
            else if (page == "stats")
            {
                var sw = new StatsW(w, DemoStats);
                int range; int.TryParse(Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_RANGE") ?? "0", out range);
                sw.Select(range);
                target = sw;
            }
            if (target != w && !(target is FlyoutW))
            {
                target.WindowStartupLocation = WindowStartupLocation.Manual; target.Left = -3000; target.Top = 0; target.Show();
            }
            if (page == "ask")
            {
                var t0 = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
                t0.Tick += delegate
                {
                    t0.Stop();
                    foreach (Window x in Application.Current.Windows) if (x != w) { Save(x, path); x.Close(); }
                    Application.Current.Shutdown();
                };
                t0.Start();
                AskW.Show(w, "Программа по очереди включит все стратегии (18 шт.), для каждой 3 раза проверит YouTube и Discord и замерит скорость.\n\nНачать?",
                    "Подбор стратегии", MessageBoxButton.YesNo, MessageBoxImage.Question);
                return;
            }
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(page == "diag" ? 9000 : 900) };
            t.Tick += delegate { t.Stop(); Save(target, path); Application.Current.Shutdown(); };
            t.Start();
        }

        // Выдуманные записи статистики за месяц — только для снимка окна.
        static System.Collections.Generic.List<StatRec> DemoStats(DateTime from)
        {
            var list = new System.Collections.Generic.List<StatRec>();
            var rnd = new Random(7);
            DateTime now = DateTime.Now;
            for (DateTime t = DateTime.Today.AddDays(-29); t < now; t = t.AddMinutes(1))
            {
                if (t < from) continue;
                int h = t.Hour, day = (now.Date - t.Date).Days;
                if (h >= 2 && h < 9) continue;                          // ПК выключен ночью
                if (day % 6 == 3 && h >= 13 && h < 17) continue;        // иногда днём тоже
                bool on = !(h >= 9 && h < 10);                           // утром обход включают не сразу
                string strat = day > 2 ? "general (ALT)" : (h < 15 ? "general (ALT)" : "general (FAKE TLS AUTO ALT3)");
                var r = new StatRec { T = t, On = on, Strategy = on ? strat : "", YtMax = 3, DcMax = 3, Yt = 3, Dc = 3,
                    YtMs = 90 + rnd.Next(80), DcMs = 70 + rnd.Next(60) };
                if (!on) { r.Yt = 0; r.Dc = 2; r.YtMs = -1; }
                else if (strat == "general (ALT)" && h >= 19 && h < 22 && rnd.Next(4) > 0) { r.Yt = rnd.Next(3) == 0 ? 1 : 0; r.YtMs = r.Yt > 0 ? 300 : -1; }
                else if (rnd.Next(400) == 0) r.Dc = 2;
                if (day == 0 && h == 12 && t.Minute < 8) { r.Net = false; r.Yt = 0; r.Dc = 0; r.YtMs = r.DcMs = -1; }
                if (on) r.Extra = "Twitch=" + (rnd.Next(25) == 0 ? -1 : 140 + rnd.Next(60));
                list.Add(r);
            }
            return list;
        }

        static void Save(Window win, string path)
        {
            var el = (FrameworkElement)win.Content;
            el.UpdateLayout();
            int wpx = (int)Math.Ceiling(el.ActualWidth * (el.LayoutTransform is ScaleTransform ? ((ScaleTransform)el.LayoutTransform).ScaleX : 1));
            int hpx = (int)Math.Ceiling(el.ActualHeight * (el.LayoutTransform is ScaleTransform ? ((ScaleTransform)el.LayoutTransform).ScaleY : 1));
            double k = 1;   // ZAPRETGUI_SNAP_DPI=2 — снимок в двойном разрешении (для картинок в README)
            double.TryParse(Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_DPI") ?? "1", System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out k);
            if (k < 1) k = 1;
            var rtb = new RenderTargetBitmap(Math.Max(1, (int)(wpx * k)), Math.Max(1, (int)(hpx * k)), 96 * k, 96 * k, PixelFormats.Pbgra32);
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(win.Background, null, new Rect(0, 0, wpx, hpx));
                dc.DrawRectangle(new VisualBrush(el), null, new Rect(0, 0, wpx, hpx));
            }
            rtb.Render(dv);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(path)) enc.Save(fs);
        }
    }
#endif
}
