// Подбор стратегии: запуск проверок, окно результатов, применение выбранной.
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
        // ---------- Автоподбор стратегии ----------

        void AutoPick() { AutoPick(null); }

        void AutoPick(List<string> preselect)
        {
            if (busy) return;
            strategies = z.Strategies();
            if (strategies.Count == 0) { Toast(L.T("Не найдено ни одной стратегии (.bat)", "No strategies (.bat) found"), true); return; }
            if (!FlowTest.CurlAvailable &&
                AskW.Show(this, L.T("В Windows нет curl.exe (бывает на старых Windows 10). Подбор пройдёт упрощённо: по одной проверке HTTPS на сайт вместо трёх (HTTP, TLS 1.2, TLS 1.3). Режим DPI будет недоступен.\n\nПродолжить?",
                        "Windows has no curl.exe (happens on older Windows 10). The test will be simplified: one HTTPS check per site instead of three (HTTP, TLS 1.2, TLS 1.3). DPI mode won't be available.\n\nContinue?"),
                    L.T("Подбор стратегии", "Find a strategy"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            FlowTest.RestoreLeftoverIpset(z);   // прошлый тест DPI прервался — вернуть IPSet
            var targets = FlowTest.LoadTargets(z);
            var setup = new PickSetupW(this, strategies, targets.Count, FlowTest.TargetsFromFile(z), preselect, QuickList(), () => EditTargets());
            setup.ShowDialog();
            if (setup.Mode == null) return;
            // без curl.exe режим DPI невозможен — тогда обычный (упрощённый)
            RunPick(setup.Selected, setup.Mode == "dpi" && !FlowTest.CurlAvailable ? "std" : setup.Mode, FlowTest.LoadTargets(z), false);
        }

        // unattended — подбор по расписанию: без окон, лучшая применяется сама, если она лучше текущей.
        void RunPick(List<string> list, string testMode, List<TestTarget> targets, bool unattended)
        {
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
                    if (unattended) FinishScheduledPick(results, cancelPick, wasActive, prev);
                    else ShowPickResults(results, cancelPick, wasActive, prev);
                });
            });
        }

        void ShowPickResults(List<PickResult> results, bool partial, bool wasActive, string prev)
        {
            // окно спрятано в трей — подбор шёл в фоне: вернуть обход как был, а результаты показать, когда откроют окно
            if (!IsVisible && results.Count > 0)
            {
                if (!partial) PickHistory.Save(results);
                var rk = PickResult.Rank(results);
                var bst = rk.FirstOrDefault(r => r.Works);
                FlowTest.SaveReport(z, results, bst);
                Journal.Add(L.T("Подбор завершён в фоне", "Strategy test finished in the background") + (bst != null ? L.T(", лучшая — ", ", best — ") + bst.Name : L.T(", рабочих нет", ", none worked")));
                RestoreAfterPick(wasActive, prev);
                pendingPickResults = true;
                Balloon(L.T("Подбор закончен", "Strategy test finished"),
                    bst != null ? L.T("Лучшая — " + bst.Name + ". Откройте программу, чтобы применить.", "Best — " + bst.Name + ". Open the app to apply it.")
                                : L.T("Рабочая стратегия не найдена.", "No working strategy found."), WF.ToolTipIcon.Info);
                return;
            }
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
    }
}
