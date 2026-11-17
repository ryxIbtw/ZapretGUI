// Журнал событий, резервная копия настроек, диагностика, «О программе».
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
        // ---------- Журнал, резервная копия, «О программе» ----------

        void ShowJournal() { new JournalW(this, JournalHeader).ShowDialog(); }
        void ShowStats() { new StatsW(this, Stats.Load).ShowDialog(); }
        void ShowReport() { new ReportW(this, JournalHeader).ShowDialog(); }

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
    }
}
