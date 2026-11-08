// Точка входа версии на WPF: одна копия программы, поиск папки zapret, запуск окна (или сразу в трей).
using System;
using System.IO;
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
[assembly: AssemblyVersion("2.0.0.0")]
[assembly: AssemblyFileVersion("2.0.0.0")]

namespace ZapretGui
{
    static class AppMain
    {
        [STAThread]
        static int Main(string[] args)
        {
            L.Init();
            string rootArg = null, snap = null;
            bool hidden = false;
            int waitPid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--tray") hidden = true;
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
                    PostMessage((IntPtr)0xFFFF, MainWindow.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }
                string root = FindRoot(rootArg);
                if (root == null) return 1;
                var z = new Zapret(root);

                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                W.Install(app);
                var w = new MainWindow(z, snap, hidden);
#if TESTHOOKS
                if (snap != null) { Snap.Run(w, snap); app.Run(); return 0; }
#endif
                if (!w.StartHidden) w.Show();
                app.Run();
            }
            return 0;
        }

        static string FindRoot(string arg)
        {
            string exeDir = Path.GetDirectoryName(WF.Application.ExecutablePath);
            foreach (string c in new[] { arg, exeDir, Prefs.Get("Root", null) })
                if (Zapret.IsValidRoot(c)) { Prefs.Set("Root", Path.GetFullPath(c)); return Path.GetFullPath(c); }

            WF.MessageBox.Show(L.T("Не найдена папка zapret-discord-youtube.\n\nПоложите ZapretGUI.exe рядом с service.bat или укажите папку вручную в следующем окне.",
                "zapret-discord-youtube folder not found.\n\nPut ZapretGUI.exe next to service.bat or pick the folder in the next window."),
                "Zapret", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Information);
            while (true)
            {
                using (var d = new WF.FolderBrowserDialog { Description = L.T("Папка zapret-discord-youtube (где лежит service.bat)", "zapret-discord-youtube folder (with service.bat)") })
                {
                    if (d.ShowDialog() != WF.DialogResult.OK) return null;
                    if (Zapret.IsValidRoot(d.SelectedPath)) { Prefs.Set("Root", d.SelectedPath); return d.SelectedPath; }
                    WF.MessageBox.Show(L.T("В этой папке нет service.bat и папки bin.", "This folder has no service.bat and bin folder."), "Zapret", WF.MessageBoxButtons.OK, WF.MessageBoxIcon.Warning);
                }
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
            w.SnapPrepare();
            Window target = w;
            if (page == "pick")
            {
                var demo = new System.Collections.Generic.List<PickResult> {
                    new PickResult { Name = "general (ALT)", YtOk = 9, DcOk = 9, Rounds = 3, Samples = 18, TotalMs = 18 * 140, SpeedKBps = 820 },
                    new PickResult { Name = "general (SIMPLE FAKE)", YtOk = 9, DcOk = 9, Rounds = 3, Samples = 18, TotalMs = 18 * 95, SpeedKBps = 2400 },
                    new PickResult { Name = "general (ALT2)", YtOk = 6, DcOk = 9, Rounds = 3, Samples = 15, TotalMs = 15 * 210, SpeedKBps = 400 },
                    new PickResult { Name = "general", YtOk = 0, DcOk = 0, Rounds = 1 },
                    new PickResult { Name = "my custom", Failed = true } };
                var hist = new System.Collections.Generic.List<PickHistory.Run> {
                    new PickHistory.Run { When = DateTime.Now.AddDays(-1), Results = new System.Collections.Generic.List<PickResult> {
                        new PickResult { Name = "general (SIMPLE FAKE)", YtOk = 9, DcOk = 9, Rounds = 3 }, new PickResult { Name = "general (ALT)", YtOk = 5, DcOk = 9, Rounds = 3 } } } };
                target = new PickW(w, PickResult.Rank(demo), false, hist, null);
            }
            else if (page == "diag") target = new DiagW(w, w.Zap);
            else if (page == "about") target = new AboutW(w, "1.10.4");
            else if (page == "journal") target = new JournalW(w, () => "header");
            else if (page == "editor")
            {
                string f = Path.Combine(Path.GetTempPath(), "zapretgui-snap-list.txt");
                File.WriteAllText(f, "# Never leave this file empty\r\ndomain.example.abc\r\nexample.org\r\nyoutube.com\r\ndiscord.com\r\nxn--e1afmkfd.xn--p1ai\r\n");
                target = new ListEditorW(w, f, L.T("Мои домены", "My domains"), L.T("Эти сайты тоже пойдут через обход  ·  lists\\list-general-user.txt", "These sites also go through the bypass"), false);
            }
            else if (page == "flyout") target = w.SnapFlyout();
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
