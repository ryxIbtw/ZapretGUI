// Главное окно на WPF: действия — включение/выключение, подбор, сторож, проверка сайтов, обновления, меню.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace ZapretGui
{
    partial class MainWindow
    {
        // состояние
        List<string> strategies = new List<string>();
        string selected = "";
        bool autostart;
        bool busy; string busyText = "";
        bool picking; volatile bool cancelPick; string pickName;
        double pickProgress;
        // чтобы при выходе посреди подбора дождаться его конца и вернуть обход, как был
        readonly ManualResetEvent pickDone = new ManualResetEvent(true);
        bool pickWasActive; string pickPrev;
        bool running, svc;
        string activeStrategy;
        string newVersion, newGui;
        bool guiAutostart;
        bool expectRunning;
        readonly List<DateTime> restarts = new List<DateTime>();
        float netYt = -1, netDc = -1; bool netChecking; DateTime lastNet = DateTime.MinValue;
        long[] lastNetMs;
        string lastNetSig;

        bool Active { get { return running || svc; } }

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

        void UpdateUi()
        {
            if (power == null) return;
            if (picking)
            {
                statusText = busyText; statusBrush = W.Muted;
                subText = (pickName ?? "") + L.T("  ·  нажмите кнопку, чтобы отменить", "  ·  click to cancel");
            }
            else if (busy) { statusText = busyText; statusBrush = W.Muted; subText = selected; }
            else if (running)
            {
                statusText = L.T("Включено", "On"); statusBrush = W.Accent;
                subText = (activeStrategy ?? L.T("запущено вне приложения", "started outside the app")) + (svc ? L.T("  ·  служба", "  ·  service") : "");
            }
            else if (svc)
            {
                statusText = L.T("Служба остановлена", "Service stopped"); statusBrush = W.Warn;
                subText = L.T("нажмите кнопку, чтобы перезапустить", "click the button to restart");
            }
            else { statusText = L.T("Выключено", "Off"); statusBrush = W.Text; subText = L.T("нажмите, чтобы включить", "click to turn on"); }

            power.SetOn(running, IsVisible);
            power.SetBusy(busy);
            power.SetProgress(picking ? pickProgress : -1);
            power.SetText(statusText, statusBrush, subText);
            UpdatePanel();
            if (cardYt != null)   // в простом режиме плашек и стратегии нет
            {
                cardYt.Set(netYt, netChecking, NetMeta(0, NetCheck.YouTube.Length));
                cardDc.Set(netDc, netChecking, NetMeta(NetCheck.YouTube.Length, NetCheck.Discord.Length));
            }
            if (picker != null) picker.SetValue(selected);
            UpdateServiceCards();
            rowServices.Value = Services.Count == 0 ? L.T("нет", "none") : string.Join(", ", Services.Select(s => s.Name));
            rowIpsetAuto.Checked = IpsetAuto;
            rowHotkey.Value = HotkeyOn ? HotkeyText() : L.T("выкл", "off");
            rowActions.Value = string.Join(", ", ActionKeys.Select(ActionName));
            rowGameQuiet.Checked = GameQuiet;
            rowGuiAutoUpdate.Checked = GuiAutoUpdate;
            rowPortable.Checked = Prefs.Portable;
            rowProfiles.Value = Profiles.Count == 0 ? L.T("нет", "none") : Prefs.Get("Profile", "");
            rowAutoApps.Value = AutoApps.Count == 0 ? L.T("нет", "none") : string.Join(", ", AutoApps.Select(a => Path.GetFileNameWithoutExtension(a)));
            rowSimple.Checked = simple;
            rowNotify.Checked = NotifyOn;
            rowAutoFix.Checked = AutoFixOn;
            rowSchedule.Value = scheduleOn ? ScheduleLabel() : L.T("выкл", "off");
            rowWeekly.Checked = WeeklyOn;
            rowNoAnim.Checked = Fx.Off;
            rowZapretAuto.Checked = ZapretAutoUpdate;
            if (picker != null) picker.SetNote(Note(selected));
            rowTiles.Value = tiles.Count + L.T(" из ", " of ") + TileKeys.Length;
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
            if (newVersion == null && newGui == null) HideBanner(0);   // обновились — строка об обновлении больше не нужна

            // это версия zapret, а не программы — так и подписано
            if (newVersion != null) { version.Text = "↑ zapret v" + newVersion; version.Foreground = W.Accent; version.Cursor = System.Windows.Input.Cursors.Hand; }
            else { version.Text = lv == "" ? "" : "zapret v" + lv; version.Foreground = W.Dim; version.Cursor = null; }
            version.ToolTip = L.T("Версия zapret от Flowseal. Версия программы: ZapretGUI v", "zapret by Flowseal version. App version: ZapretGUI v") + SelfUpdate.Current.ToString(3);

            tray.Icon = TrayIcon();
            tray.Text = TrayTip();
            SyncFlyout();
        }

        static string GameLabel(string m)
        {
            switch (m) { case "all": return "TCP + UDP"; case "tcp": return "TCP"; case "udp": return "UDP"; default: return L.T("Выкл", "Off"); }
        }

        static string IpsetLabel(string m)
        {
            switch (m) { case "loaded": return L.T("По списку", "List"); case "none": return L.T("Выкл", "Off"); default: return L.T("Все IP", "Any IP"); }
        }

        static string ThemeLabel(string mode)
        {
            switch (mode)
            {
                case "light": return L.T("Светлая", "Light");
                case "dark": return L.T("Тёмная", "Dark");
                case "amoled": return L.T("Чёрная (AMOLED)", "Black (AMOLED)");
                case "warm": return L.T("Тёплая светлая", "Warm light");
                case "contrast": return L.T("Высокий контраст", "High contrast");
                default: return L.T("Как в Windows", "Like Windows");
            }
        }

        // ================= Действия =================

        void RunOp(string text, bool spinner, Action work, Action done)
        {
            if (busy) return;
            busy = true; busyText = text;
            if (!spinner) { hint.Foreground = W.Muted; hint.Text = text; hintTimer.Stop(); }
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
                        // длинное объяснение (например, почему winws не запустился) — окном, а не строкой внизу
                        if (!IsVisible)
                            Balloon(text.TrimEnd('…', '.'), err.Message.Length > 200 ? err.Message.Substring(0, 199) + "…" : err.Message, WF.ToolTipIcon.Warning);   // программа в трее — окно само не выскакивает
                        else if (err.Message.Length > 90 || err.Message.Contains("\n"))
                        {
                            Toast(L.T("Не получилось — подробности в окне", "Failed — see the details"), true);
                            AskW.Show(this, err.Message, text.TrimEnd('…', '.'), MessageBoxButton.OK, MessageBoxImage.Warning);
                        }
                        else Toast(err.Message, true);
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
            if (Active) Stop(); else Start();
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
                justStarted = true;   // через несколько секунд проверить, открываются ли сайты
                CheckNetSoon(6000);
            });
        }

        void Stop()
        {
            expectRunning = false;
            RunOp(L.T("Остановка…", "Stopping…"), true, () => z.StopAll(true), () => { Journal.Add(L.T("Обход выключен", "Bypass off")); CheckNetSoon(); });
        }

        void RestartIfActive() { if (Active) Start(); }

        void SelectStrategy(string name)
        {
            if (busy || name == selected) return;
            Journal.Add(L.T("Стратегия сменена: ", "Strategy changed: ") + selected + " → " + name);
            if (selected != "") Prefs.Set("PrevStrategy", selected);   // для «Вернуть прошлую стратегию»
            selected = name;
            Prefs.Set("Strategy", name);
            if (picker != null) picker.SetValue(name);
            RestartIfActive();
            SyncFlyout();
        }

        internal void StepStrategy(int d)
        {
            if (busy || strategies.Count == 0) return;
            var lst = StepList();   // избранные, если их две и больше
            int i = lst.IndexOf(selected);
            i = i < 0 ? (d > 0 ? 0 : lst.Count - 1) : ((i + d) % lst.Count + lst.Count) % lst.Count;
            SelectStrategy(lst[i]);
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
                Balloon(L.T("Обход остановился", "Bypass stopped"), L.T("winws.exe завершился", "winws.exe has exited"), WF.ToolTipIcon.Warning);
                return;
            }
            restarts.RemoveAll(t => (DateTime.Now - t).TotalMinutes > 5);
            if (restarts.Count >= 3)
            {
                expectRunning = false;
                Journal.Add(L.T("winws.exe снова завершился — 3 перезапуска за 5 минут не помогли, сторож остановился",
                                "winws.exe exited again — 3 restarts in 5 minutes didn't help, watchdog gave up"));
                Balloon(L.T("Обход остановился", "Bypass stopped"),
                    L.T("Перезапуск не помог. Попробуйте другую стратегию или диагностику.", "Restarting didn't help. Try another strategy or run diagnostics."), WF.ToolTipIcon.Error);
                return;
            }
            restarts.Add(DateTime.Now);
            Journal.Add(L.T("winws.exe завершился — сторож перезапускает (", "winws.exe exited — watchdog restarting (") + restarts.Count + "/3)");
            Balloon(L.T("Обход остановился", "Bypass stopped"), L.T("Перезапускаю…", "Restarting…"), WF.ToolTipIcon.Warning);
            Start();
        }

        void Balloon(string title, string text, WF.ToolTipIcon icon)
        {
            if (tray.Visible) tray.ShowBalloonTip(5000, title, text, icon);
        }

        // ---------- Доступность сайтов ----------

        void CheckNetSoon() { CheckNetSoon(2500); }

        void CheckNetSoon(int ms)
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += delegate { t.Stop(); CheckNet(); };
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
                string fails;
                long[] ms = NetCheck.RunChecked(5000, out fails);   // одиночный тайм-аут перепроверяется
                int yt = 0, dc = 0;
                for (int i = 0; i < ms.Length; i++) if (ms[i] >= 0) { if (i < NetCheck.YouTube.Length) yt++; else dc++; }
                // ничего не открылось — проверить обычный сайт: может, пропал весь интернет, а не обход
                bool net = yt + dc > 0 || NetCheck.Probe("http://www.msftconnecttest.com/connecttest.txt", 4000) || NetCheck.Probe("https://ya.ru", 4000);
                ProbeServices();   // свои плашки сервисов
                Post(() =>
                {
                    Stats.Add(new StatRec
                    {
                        T = DateTime.Now, On = Active, Strategy = Active ? (activeStrategy ?? selected) : "", Net = net,
                        Yt = yt, YtMax = NetCheck.YouTube.Length, Dc = dc, DcMax = NetCheck.Discord.Length,
                        YtMs = AvgMs(ms, 0, NetCheck.YouTube.Length), DcMs = AvgMs(ms, NetCheck.YouTube.Length, NetCheck.Discord.Length),
                        Extra = ServicesExtra(),   // свои сервисы — тоже в статистику
                        Fails = fails,
                    });
                    CheckServiceAlerts(net);
                    netChecking = false;
                    netUp = net;
                    RefreshToday();
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
                    if (running && wasOk && !ok && net)
                        Toast(L.T("YouTube или Discord перестал открываться — попробуйте «Подобрать стратегию»",
                                  "YouTube or Discord stopped working — try “Find a strategy”"), true);
                    OnNetResult(ok, net);   // уведомления, автовосстановление, проверка после включения
                    UpdateUi();
                });
            });
        }

        static int AvgMs(long[] ms, int offset, int count)
        {
            long sum = 0; int n = 0;
            for (int i = offset; i < offset + count && i < ms.Length; i++) if (ms[i] >= 0) { sum += ms[i]; n++; }
            return n > 0 ? (int)(sum / n) : -1;
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

        // Подсказка на плашках: какие адреса открылись и когда была проверка.
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
                    if (host.StartsWith("rr") && host.Contains("---sn-")) host = L.T("сервер видео ", "video server ") + host;
                    sb.Append(ms >= 0 ? "   ✓  " : "   ✗  ").Append(host).Append("  —  ")
                      .Append(ms >= 0 ? ms + L.T(" мс", " ms") : L.T("не открылся", "failed")).Append("\n");
                }
            };
            group("YouTube", NetCheck.YouTube, 0);
            group("Discord", NetCheck.Discord, NetCheck.YouTube.Length);
            sb.Append("\n").Append(L.T("Нажмите, чтобы проверить снова", "Click to check again"));
            return sb.ToString();
        }

        static void Shell(string file, string args)
        {
            try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); } catch { }
        }
    }
}
