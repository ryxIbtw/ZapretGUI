// Обновления: проверка, обновление zapret и самой программы, перезапуск.
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
        // ---------- Обновления ----------

        void CheckUpdates()
        {
            string local = z.LocalVersion;
            ThreadPool.QueueUserWorkItem(delegate
            {
                string remote = null;
                try { remote = z.RemoteVersion(); } catch { }
                if (!string.IsNullOrEmpty(remote) && remote != local) Post(() => { newVersion = remote; UpdateUi(); OnUpdatesFound(); });

                Version gui = null;
                try { string url, sha; gui = SelfUpdate.Latest(out url, out sha); } catch { }
                if (gui != null && gui > SelfUpdate.Current)
                    Post(() =>
                    {
                        newGui = gui.ToString(3);
                        UpdateUi();
                        OnUpdatesFound();   // баннер на панели или тихое автообновление
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

        void UpdateZapret() { UpdateZapret(false); }

        // silent — автообновление: без вопроса (включается в настройках).
        void UpdateZapret(bool silent)
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
                    if (!silent && AskW.Show(this,
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
                        CheckStrategyChanges();
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
    }
}
