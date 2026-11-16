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
[assembly: AssemblyVersion("2.4.0.0")]
[assembly: AssemblyFileVersion("2.4.0.0")]

namespace ZapretGui
{
    static class AppMain
    {
        [STAThread]
        static int Main(string[] args)
        {
            L.Init();
            string rootArg = null, snap = null;
            bool hidden = false, scheduledPick = false;
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
#if TESTHOOKS
                else if (args[i] == "--snap" && i + 1 < args.Length) snap = args[++i];
#endif
            }

            // после самообновления: дождаться, пока старая копия закроется, и убрать её файл
            if (waitPid > 0) { try { System.Diagnostics.Process.GetProcessById(waitPid).WaitForExit(15000); } catch { } }
            SelfUpdate.CleanupOld();

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
                if (root == null) return 1;
                var z = new Zapret(root);

                var w = new MainWindow(z, snap, hidden);
#if TESTHOOKS
                if (snap != null) { Snap.Run(w, snap); app.Run(); return 0; }
#endif
                if (!w.StartHidden) w.Show();
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

            // папки нет — предложить скачать zapret самим (Да) или указать папку (Нет)
            var ans = AskW.Show(null, L.T("Не найдена папка zapret-discord-youtube.\n\nСкачать последнюю версию zapret от Flowseal с GitHub в папку C:\\zapret?\n\n«Да» — скачать, «Нет» — указать папку вручную.",
                "zapret-discord-youtube folder not found.\n\nDownload the latest zapret by Flowseal from GitHub to C:\\zapret?\n\n“Yes” — download, “No” — pick the folder yourself."),
                "Zapret", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            if (ans == MessageBoxResult.Cancel) return null;
            if (ans == MessageBoxResult.Yes)
            {
                string target = @"C:\zapret";
                if (Directory.Exists(target) && Directory.EnumerateFileSystemEntries(target).Any() && !Zapret.IsValidRoot(target))
                    target = @"C:\zapret-discord-youtube";
                Exception err = null;
                var wait = new Window { Title = "Zapret", Width = 360, Height = 120, WindowStartupLocation = WindowStartupLocation.CenterScreen, ResizeMode = ResizeMode.NoResize,
                    Background = W.Bg, Content = new System.Windows.Controls.TextBlock { Text = L.T("Скачиваю zapret…", "Downloading zapret…"), Foreground = W.Text, FontSize = 14,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } };
                var th = new Thread(() => { try { Zapret.InstallFresh(target); } catch (Exception ex) { err = ex; } wait.Dispatcher.BeginInvoke((Action)wait.Close); }) { IsBackground = true };
                wait.Loaded += delegate { th.Start(); };
                wait.ShowDialog();
                if (err == null && Zapret.IsValidRoot(target))
                {
                    Journal.Add(L.T("zapret скачан в ", "zapret downloaded to ") + target);
                    Prefs.Set("Root", target);
                    return target;
                }
                AskW.Show(null, L.T("Не удалось скачать zapret: ", "Could not download zapret: ") + (err != null ? err.Message : "?") +
                    L.T("\n\nУкажите папку вручную.", "\n\nPick the folder yourself."), "Zapret", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            while (true)
            {
                string picked = FolderPicker.Pick(L.T("Папка zapret-discord-youtube (где лежит service.bat)", "zapret-discord-youtube folder (with service.bat)"));
                if (picked == null) return null;
                if (Zapret.IsValidRoot(picked)) { Prefs.Set("Root", picked); return picked; }
                AskW.Show(null, L.T("В этой папке нет service.bat и папки bin.", "This folder has no service.bat and bin folder."), "Zapret", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
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
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(page == "diag" ? 4000 : 900) };
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
