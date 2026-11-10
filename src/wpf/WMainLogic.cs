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
            cardYt.Set(netYt, netChecking, NetMeta(0, NetCheck.YouTube.Length));
            cardDc.Set(netDc, netChecking, NetMeta(NetCheck.YouTube.Length, NetCheck.Discord.Length));
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

            if (newVersion != null) { version.Text = "↑ v" + newVersion; version.Foreground = W.Accent; version.Cursor = System.Windows.Input.Cursors.Hand; }
            else { version.Text = lv == "" ? "" : "v" + lv; version.Foreground = W.Dim; version.Cursor = null; }

            tray.Icon = busy || picking ? iconBusy : running ? iconOn : iconOff;
            tray.Text = "Zapret" + (running ? L.T(" — включено", " — on") : L.T(" — выключено", " — off")) + "\nGUI by " + BrandHeader.Author;
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
                CheckNetSoon();
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
            selected = name;
            Prefs.Set("Strategy", name);
            picker.SetValue(name);
            RestartIfActive();
            SyncFlyout();
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

        void CheckNetSoon()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2500) };
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
                    sb.Append(ms >= 0 ? "   ✓  " : "   ✗  ").Append(new Uri(urls[i]).Host).Append("  —  ")
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
            if (!FlowTest.CurlAvailable) { Toast(L.T("Не найден curl.exe — он нужен для проверок", "curl.exe not found — it is needed for the checks"), true); return; }
            FlowTest.RestoreLeftoverIpset(z);   // прошлый тест DPI прервался — вернуть IPSet
            var targets = FlowTest.LoadTargets(z);
            var setup = new PickSetupW(this, strategies, targets.Count, FlowTest.TargetsFromFile(z));
            setup.ShowDialog();
            if (setup.Mode == null) return;
            var list = setup.Selected;
            string testMode = setup.Mode;

            bool wasActive = Active;
            string prev = selected;
            busy = true; picking = true; cancelPick = false; expectRunning = false; pickProgress = 0;
            pickWasActive = wasActive; pickPrev = prev;
            pickDone.Reset();
            busyText = L.T("Подбор…", "Testing…");
            UpdateUi();
            Journal.Add(L.T("Подбор стратегии начат: ", "Strategy test started: ") + list.Count + L.T(" шт., режим ", " strategies, mode ") +
                (testMode == "dpi" ? "DPI" : L.T("обычный", "standard")));

            ThreadPool.QueueUserWorkItem(delegate
            {
                var results = new List<PickResult>();
                Exception err = null;
                bool ipsetSwitched = false;
                string payload = null;
                List<DpiTarget> dpiTargets = null;
                try
                {
                    try
                    {
                    if (testMode == "dpi")
                    {
                        Post(() => { busyText = L.T("Загрузка списка серверов…", "Loading the server list…"); UpdateUi(); });
                        dpiTargets = FlowTest.LoadDpiSuite();
                        if (dpiTargets.Count == 0) throw new Exception(L.T("Не удалось скачать список серверов для проверки DPI", "Could not download the server list for the DPI check"));
                        payload = FlowTest.MakePayload();
                        ipsetSwitched = FlowTest.SwitchIpsetToAny(z);
                    }
                    for (int i = 0; i < list.Count && !cancelPick; i++)
                    {
                        int idx = i; string name = list[i];
                        Post(() =>
                        {
                            if (!cancelPick) busyText = L.T("Подбор ", "Testing ") + (idx + 1) + " / " + list.Count;
                            pickName = name;
                            pickProgress = idx / (double)list.Count;
                            UpdateUi();
                        });
                        var res = new PickResult { Name = name, Mode = testMode };
                        try { z.Start(name, false); }   // Start сам ждёт, пока winws поднимется
                        catch { res.Failed = true; }
                        if (!res.Failed)
                        {
                            if (testMode == "dpi") FlowTest.RunDpi(res, dpiTargets, payload, () => cancelPick);
                            else
                            {
                                FlowTest.RunStandard(res, targets, () => cancelPick);
                                // скорость — только для сведения, на выбор лучшей не влияет
                                if (res.Ok > 0 && !cancelPick) res.SpeedKBps = NetCheck.SpeedKBps(NetCheck.SpeedUrl, 8000);
                            }
                        }
                        if (cancelPick && !res.Failed) break;   // недопроверенную не показываем
                        results.Add(res);
                    }
                    z.StopAll(false);   // выбор — за пользователем, до него обход выключен
                    }
                    catch (Exception ex) { err = ex; }
                }
                finally
                {
                    if (ipsetSwitched) FlowTest.RestoreIpset(z);
                    if (payload != null) try { File.Delete(payload); } catch { }
                    pickDone.Set();
                }

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
            var past = PickHistory.Load();
            if (!partial && results.Count > 0) PickHistory.Save(results);
            var ranked = PickResult.Rank(results);
            var best = ranked.FirstOrDefault(r => r.Works);
            // отчёт в utils\test results — как у Flowseal
            string report = results.Count > 0 ? FlowTest.SaveReport(z, results, best) : null;
            Journal.Add((partial ? L.T("Подбор отменён", "Strategy test cancelled") : L.T("Подбор завершён", "Strategy test finished")) +
                L.T(": проверено ", ": tested ") + results.Count +
                (best != null ? L.T(", лучшая — ", ", best — ") + best.Name + L.T(" (успешно ", " (passed ") + best.Ok + "/" + best.Total + ")"
                              : L.T(", рабочих нет", ", none worked")) +
                (report != null ? L.T(" · отчёт: ", " · report: ") + report : ""));
            if (best == null)
            {
                Toast(partial ? L.T("Подбор отменён", "Testing cancelled")
                              : L.T("Рабочая стратегия не найдена — запустите диагностику", "No working strategy found — run diagnostics"), !partial);
                RestoreAfterPick(wasActive, prev);
                return;
            }
            ShowFromTray();
            var w = new PickW(this, ranked, partial, past, null, report);
            w.ShowDialog();
            string chosen = w.Chosen;
            if (chosen == null) { RestoreAfterPick(wasActive, prev); return; }

            selected = chosen;
            Prefs.Set("Strategy", chosen);
            Start();   // после запуска плашки YouTube/Discord проверятся сами
            Toast(L.T("Применена стратегия «" + chosen + "»", "Applied strategy “" + chosen + "”"));
        }

        void ShowLastPick()
        {
            if (busy) return;
            var runs = PickHistory.Load();
            if (runs.Count == 0) { Toast(L.T("Подбор ещё не запускался", "No strategy test has been run yet")); return; }
            var last = runs[runs.Count - 1];
            runs.RemoveAt(runs.Count - 1);
            var w = new PickW(this, PickResult.Rank(last.Results), false, runs, L.T("Прошлый подбор · ", "Last test · ") + last.When.ToString("dd.MM.yyyy HH:mm"), null);
            w.ShowDialog();
            string chosen = w.Chosen;
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

        internal System.Windows.Controls.ContextMenu ShowStrategyMenu(FrameworkElement anchor, bool above)
        {
            if (busy) return null;
            strategies = z.Strategies();
            var m = WMenu.Create();
            foreach (string s in strategies)
            {
                string name = s;
                WMenu.Add(m, name, name == selected, () => SelectStrategy(name));
            }
            m.MaxHeight = 420;
            if (above) WMenu.ShowAbove(m, anchor); else WMenu.ShowBelow(m, anchor);
            return m;
        }

        void ShowGameMenu()
        {
            if (busy) return;
            var m = WMenu.Create();
            foreach (string mode in new[] { "disabled", "all", "tcp", "udp" })
            {
                string md = mode;
                WMenu.Add(m, GameLabel(md), z.GameMode == md, () =>
                {
                    if (z.GameMode == md) return;
                    z.GameMode = md; z.SaveGame();
                    Journal.Add(L.T("Игровой фильтр: ", "Game filter: ") + GameLabel(md));
                    UpdateUi();
                    RestartIfActive();
                });
            }
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Порты: TCP ", "Ports: TCP ") + z.GameTcp + ", UDP " + z.GameUdp + "…", false, EditGamePorts);
            WMenu.ShowBelowRight(m, rowGame);
        }

        void EditGamePorts()
        {
            string tcp = InputW.Show(this, L.T("Порты TCP для игрового фильтра", "TCP ports for the game filter"),
                L.T("Например: 1024-65535 или 1024-1934,1936-65535", "E.g. 1024-65535 or 1024-1934,1936-65535"), z.GameTcp);
            if (tcp == null) return;
            string udp = InputW.Show(this, L.T("Порты UDP для игрового фильтра", "UDP ports for the game filter"), L.T("Например: 1024-65535", "E.g. 1024-65535"), z.GameUdp);
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
            var m = WMenu.Create();
            string cur = z.IpsetMode();
            var items = new[] {
                new[] { "loaded", L.T("По списку (ipset-all)", "List (ipset-all)") },
                new[] { "none", L.T("Выкл", "Off") },
                new[] { "any", L.T("Все IP-адреса", "Any IP address") } };
            foreach (var it in items)
            {
                string md = it[0];
                WMenu.Add(m, it[1], cur == md, () =>
                {
                    try { z.SetIpset(md); } catch (Exception ex) { Toast(ex.Message, true); return; }
                    Journal.Add("IPSet: " + IpsetLabel(md));
                    UpdateUi();
                    RestartIfActive();
                });
            }
            WMenu.ShowBelowRight(m, rowIpset);
        }

        void ShowLangMenu()
        {
            var m = WMenu.Create();
            WMenu.Add(m, "Русский", !L.En, () => SetLang(false));
            WMenu.Add(m, "English", L.En, () => SetLang(true));
            WMenu.ShowBelowRight(m, rowLang);
        }

        // Меню «Оформление»: тема, цвет акцента (с цветными кружками), масштаб.
        void ShowThemeMenu()
        {
            var m = WMenu.Create();
            string mode = ThemeMode;
            WMenu.Header(m, L.T("Тема", "Theme"));
            foreach (string md in Theme.Modes) { string x = md; WMenu.Add(m, ThemeLabel(x), mode == x, () => SetThemeMode(x)); }
            WMenu.Sep(m);
            WMenu.Header(m, L.T("Цвет", "Color"));
            foreach (string ac in Theme.Accents)
            {
                string x = ac;
                var it = WMenu.Add(m, Theme.AccentLabel(x), Theme.AccentName == x, () => SetAccent(x));
                if (x == "auto")
                {
                    // половина зелёная, половина синяя — «авто» = зелёный в тёмной теме, синий в светлой
                    var lg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    lg.GradientStops.Add(new GradientStop(Color.FromRgb(52, 211, 153), 0.5));
                    lg.GradientStops.Add(new GradientStop(Color.FromRgb(37, 99, 235), 0.5));
                    it.Icon = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = lg };
                }
                else it.Icon = WMenu.Dot(W.C(Theme.AccentSample(x)));
            }
            WMenu.Sep(m);
            WMenu.Header(m, L.T("Масштаб", "Scale"));
            int cur = UiScalePref;
            foreach (int sc in Theme.Scales) { int x = sc; WMenu.Add(m, x + "%", cur == x, () => SetScale(x)); }
            WMenu.ShowBelowRight(m, rowTheme);
        }

        void SetThemeMode(string mode)
        {
            if (mode == ThemeMode) return;
            Prefs.Set("Theme", mode);
            Theme.ApplyMode(mode);
            Recolor();
        }

        void SetAccent(string name)
        {
            if (name == Theme.AccentName) return;
            Prefs.Set("Accent", name);
            Theme.AccentName = name;
            Theme.Apply(Theme.IsLight);
            Recolor();
        }

        void SetScale(int percent)
        {
            if (percent == UiScalePref) return;
            Prefs.Set("Scale", percent.ToString());
            Theme.SetScale(1f, percent / 100f);
            rootScale.ScaleX = rootScale.ScaleY = Theme.UiScale;
            UpdateUi();
        }

        void SetLang(bool en)
        {
            if (en == L.En) return;
            Prefs.Set("Lang", en ? "en" : "ru");
            L.En = en;
            BuildUi();   // все подписи заново
            UpdateUi();
        }

        void ShowFakesMenu()
        {
            if (busy) return;
            var fakes = z.FakeFiles();
            if (fakes.Count == 0) { Toast(L.T("В папке bin нет .bin файлов", "No .bin files in the bin folder"), true); return; }
            var m = WMenu.Create();
            AddFakeSection(m, "Discord UDP", Zapret.FakeDiscord, fakes);
            WMenu.Sep(m);
            AddFakeSection(m, L.T("Игровой фильтр UDP", "Game filter UDP"), Zapret.FakeGame, fakes);
            m.MaxHeight = 480;
            WMenu.ShowBelowRight(m, rowFakes);
        }

        void AddFakeSection(System.Windows.Controls.ContextMenu m, string title, string active, List<string> fakes)
        {
            WMenu.Header(m, title);
            string cur = null;
            try { cur = z.CurrentFake(active); } catch { }
            foreach (string f in fakes)
            {
                string name = f;
                WMenu.Add(m, name, name == cur, () =>
                {
                    try { z.SetFake(active, name); } catch (Exception ex) { Toast(ex.Message, true); return; }
                    Journal.Add(L.T("Fake-файл ", "Fake file ") + title + ": " + name);
                    Toast(title + ": " + name);
                    RestartIfActive();
                });
            }
        }

        void ShowMoreMenu(FrameworkElement anchor)
        {
            var m = WMenu.Create();
            WMenu.Add(m, L.T("Мои домены (list-general-user)", "My domains (list-general-user)"), false, () => EditList("list-general-user.txt"));
            WMenu.Add(m, L.T("Исключения (list-exclude-user)", "Exclusions (list-exclude-user)"), false, () => EditList("list-exclude-user.txt"));
            WMenu.Add(m, L.T("IP-исключения (ipset-exclude-user)", "IP exclusions (ipset-exclude-user)"), false, () => EditList("ipset-exclude-user.txt"));
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Результаты прошлого подбора", "Last strategy test results"), false, ShowLastPick);
            WMenu.Add(m, L.T("Журнал событий", "Event log"), false, ShowJournal);
            WMenu.Add(m, L.T("Диагностика", "Diagnostics"), false, OpenDiag);
            WMenu.Add(m, L.T("Классическое меню (service.bat)", "Classic menu (service.bat)"), false, OpenServiceBat);
            WMenu.Add(m, L.T("Открыть папку zapret", "Open zapret folder"), false, () => Shell("explorer.exe", "\"" + z.Root + "\""));
            WMenu.Add(m, L.T("Страница zapret на GitHub", "zapret on GitHub"), false, () => Shell(AboutW.ZapretUrl, null));
            WMenu.ShowAbove(m, anchor);
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

            var w = new ListEditorW(this, path, title, desc, ip);
            w.ShowDialog();
            if (w.Saved) Journal.Add(L.T("Список изменён: ", "List changed: ") + file);
            if (w.OpenInNotepad)
            {
                Shell("notepad.exe", "\"" + path + "\"");
                Toast(L.T("После сохранения списка перезапустите обход", "Restart the bypass after saving the list"));
                return;
            }
            if (!w.Saved) return;
            if (Active && !busy && AskW.Show(this, L.T("Список сохранён. Перезапустить обход, чтобы изменения заработали?", "List saved. Restart the bypass to apply the changes?"),
                    title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Start();
            else Toast(L.T("Список сохранён", "List saved"));
        }

        // ---------- Журнал, резервная копия, «О программе» ----------

        void ShowJournal() { new JournalW(this, JournalHeader).ShowDialog(); }

        // Сведения о системе в начале скопированного журнала.
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

        void ShowAbout() { new AboutW(this, z.LocalVersion).ShowDialog(); }

        void ShowBackupMenu(FrameworkElement anchor)
        {
            if (busy) return;
            var m = WMenu.Create();
            WMenu.Add(m, L.T("Сохранить настройки в файл…", "Save settings to a file…"), false, ExportSettings);
            WMenu.Add(m, L.T("Загрузить настройки из файла…", "Load settings from a file…"), false, ImportSettings);
            WMenu.ShowAbove(m, anchor);
        }

        void ExportSettings()
        {
            var d = new SaveFileDialog
            {
                Title = L.T("Сохранить настройки", "Save settings"),
                Filter = L.T("Настройки ZapretGUI (*.zip)|*.zip", "ZapretGUI settings (*.zip)|*.zip"),
                FileName = SettingsBackup.DefaultFileName,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                OverwritePrompt = true,
            };
            if (d.ShowDialog(this) != true) return;
            try { SettingsBackup.Export(z, d.FileName); }
            catch (Exception ex) { Toast(L.T("Не удалось сохранить: ", "Could not save: ") + ex.Message, true); return; }
            Journal.Add(L.T("Настройки сохранены в файл: ", "Settings saved to: ") + d.FileName);
            Toast(L.T("Настройки сохранены: ", "Settings saved: ") + Path.GetFileName(d.FileName));
        }

        void ImportSettings()
        {
            var d = new OpenFileDialog
            {
                Title = L.T("Загрузить настройки", "Load settings"),
                Filter = L.T("Настройки ZapretGUI (*.zip)|*.zip", "ZapretGUI settings (*.zip)|*.zip"),
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            };
            if (d.ShowDialog(this) != true) return;
            string file = d.FileName;
            string what;
            try { what = SettingsBackup.Describe(file); }
            catch (Exception ex) { Toast(ex.Message, true); return; }
            if (AskW.Show(this,
                L.T("Загрузить настройки из файла «" + Path.GetFileName(file) + "»?\n\n" + what +
                    "\nТекущие файлы с этими настройками будут заменены. Перед этим их копия автоматически сохранится в\n" + SettingsBackup.AutoBackupDir,
                    "Load settings from “" + Path.GetFileName(file) + "”?\n\n" + what +
                    "\nThe current files with these settings will be replaced. A copy of them is saved first to\n" + SettingsBackup.AutoBackupDir),
                L.T("Загрузка настроек", "Load settings"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;

            string safety;
            try { safety = SettingsBackup.Import(z, file); }
            catch (Exception ex) { Toast(L.T("Не удалось загрузить: ", "Could not load: ") + ex.Message, true); return; }
            Journal.Add(L.T("Настройки загружены из файла: ", "Settings loaded from: ") + file + L.T(" (прежние сохранены в ", " (previous saved to ") + safety + ")");

            bool wasActive = Active;
            string before = activeStrategy ?? selected;
            LoadState();
            L.Init();
            Theme.AccentName = Prefs.Get("Accent", "auto");
            Theme.SetScale(1f, UiScalePref / 100f);
            Theme.ApplyMode(ThemeMode);
            W.Sync();
            BuildUi();
            Recolor();
            RefreshState();
            Toast(L.T("Настройки загружены", "Settings loaded"));
            if (wasActive && AskW.Show(this, L.T("Перезапустить обход с новыми настройками?", "Restart the bypass with the new settings?") +
                    (selected != before ? "\n\n" + L.T("Стратегия: ", "Strategy: ") + before + " → " + selected : ""),
                    L.T("Загрузка настроек", "Load settings"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Start();
        }

        void OpenDiag()
        {
            new DiagW(this, z).ShowDialog();
            RefreshState();
        }

        void OpenServiceBat()
        {
            try
            {
                Process.Start(new ProcessStartInfo("cmd.exe", "/c \"\"" + Path.Combine(z.Root, "service.bat") + "\" admin\"") { WorkingDirectory = z.Root, UseShellExecute = true });
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
            var m = WMenu.Create();
            WMenu.Add(m, L.T("Обновить zapret", "Update zapret") + (newVersion != null ? "  →  v" + newVersion : "  (v" + z.LocalVersion + ")"), false, UpdateZapret);
            WMenu.Add(m, L.T("Обновить ZapretGUI", "Update ZapretGUI") + (newGui != null ? "  →  v" + newGui : "  (v" + SelfUpdate.Current.ToString(3) + ")"), false, UpdateGui);
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Проверять обновления при запуске", "Check for updates on start"), z.UpdatesEnabled, () =>
            {
                try { z.UpdatesEnabled = !z.UpdatesEnabled; } catch (Exception ex) { Toast(ex.Message, true); return; }
                Toast(z.UpdatesEnabled ? L.T("Обновления будут проверяться при запуске", "Updates will be checked on start")
                                       : L.T("Проверка обновлений при запуске выключена", "Update check on start is off"));
            });
            WMenu.ShowBelowRight(m, rowUpdate);
        }

        void UpdateGui()
        {
            if (busy) return;
            hint.Foreground = W.Muted;
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
                    if (AskW.Show(this,
                        L.T("Обновить ZapretGUI с v" + cur + " до v" + newGui + "?\n\nПрограмма перезапустится. Обход продолжит работать, настройки сохранятся.",
                            "Update ZapretGUI from v" + cur + " to v" + newGui + "?\n\nThe app will restart. The bypass keeps running and settings are kept."),
                        L.T("Обновление программы", "App update"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    string exe = WF.Application.ExecutablePath;
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
                Process.Start(new ProcessStartInfo(WF.Application.ExecutablePath,
                    "--wait-pid " + Process.GetCurrentProcess().Id + (IsVisible ? "" : " --tray")) { UseShellExecute = false });
            }
            catch (Exception ex) { Toast(L.T("Обновление установлено, но перезапуск не удался: ", "Update installed, but restart failed: ") + ex.Message, true); return; }
            RequestExit();
        }

        void UpdateZapret()
        {
            if (busy) return;
            string local = z.LocalVersion;
            hint.Foreground = W.Muted;
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
                    if (AskW.Show(this,
                        L.T("Обновить zapret с v" + local + " до v" + tag + "?\n\nОбход ненадолго выключится. Ваши списки, настройки и fake-файлы сохранятся.\n" +
                            "Стратегии, которых нет в новой версии, будут перенесены в папку old-strategies.",
                            "Update zapret from v" + local + " to v" + tag + "?\n\nThe bypass will stop briefly. Your lists, settings and fake files are kept.\n" +
                            "Strategies missing from the new version will be moved to the old-strategies folder."),
                        L.T("Обновление", "Update"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
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
            hint.Foreground = error ? W.Danger : W.Muted;
            hint.Text = msg;
            hintTimer.Stop();
            hintTimer.Interval = TimeSpan.FromMilliseconds(error ? 8000 : 4000);
            hintTimer.Start();
        }

        static void Shell(string file, string args)
        {
            try { Process.Start(new ProcessStartInfo(file, args ?? "") { UseShellExecute = true }); } catch { }
        }
    }
}
