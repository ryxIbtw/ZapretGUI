// Дополнения: своя горячая клавиша, расписание подбора, выбор цвета и папки в стиле программы, сводка за неделю,
// автообновление zapret, возврат прошлой версии, проверка файлов zapret, очистка старых стратегий,
// прошлая стратегия, редактор стратегии, профили по сети.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace ZapretGui
{
    partial class MainWindow
    {
        // ---------- расписание подбора: день и время ----------

        static readonly string[] SchedDays = { "DAILY", "MON", "TUE", "WED", "THU", "FRI", "SAT", "SUN" };
        static string ScheduleDay { get { string d = Prefs.Get("ScheduleDay", "SUN"); return Array.IndexOf(SchedDays, d) >= 0 ? d : "SUN"; } }
        static string ScheduleTime { get { string t = Prefs.Get("ScheduleTime", "04:00"); return Regex.IsMatch(t, "^([01]\\d|2[0-3]):[0-5]\\d$") ? t : "04:00"; } }

        static string DayLabel(string d, bool full)
        {
            switch (d)
            {
                case "DAILY": return L.T(full ? "Каждый день" : "ежедневно", full ? "Every day" : "daily");
                case "MON": return L.T(full ? "Понедельник" : "Пн", full ? "Monday" : "Mon");
                case "TUE": return L.T(full ? "Вторник" : "Вт", full ? "Tuesday" : "Tue");
                case "WED": return L.T(full ? "Среда" : "Ср", full ? "Wednesday" : "Wed");
                case "THU": return L.T(full ? "Четверг" : "Чт", full ? "Thursday" : "Thu");
                case "FRI": return L.T(full ? "Пятница" : "Пт", full ? "Friday" : "Fri");
                case "SAT": return L.T(full ? "Суббота" : "Сб", full ? "Saturday" : "Sat");
                default: return L.T(full ? "Воскресенье" : "Вс", full ? "Sunday" : "Sun");
            }
        }

        static string ScheduleLabel() { return DayLabel(ScheduleDay, false) + " " + ScheduleTime; }

        void ShowScheduleMenu()
        {
            var m = WMenu.Create();
            WMenu.Add(m, scheduleOn ? L.T("Выключить", "Turn off") : L.T("Включить (", "Turn on (") + ScheduleLabel() + ")", false, () => SetSchedule(!scheduleOn));
            var day = WMenu.Sub(m, L.T("День: ", "Day: ") + DayLabel(ScheduleDay, true));
            foreach (string d in SchedDays)
            {
                string dd = d;
                WMenu.AddTo(day, DayLabel(dd, true), dd == ScheduleDay, () => { Prefs.Set("ScheduleDay", dd); if (scheduleOn) SetSchedule(true); else UpdateUi(); });
            }
            WMenu.Add(m, L.T("Время: ", "Time: ") + ScheduleTime + "…", false, () =>
            {
                string v = InputW.Show(this, L.T("Время подбора", "Test time"), L.T("Часы и минуты, например 04:00", "Hours and minutes, e.g. 04:00"), ScheduleTime);
                if (v == null) return;
                v = v.Trim();
                if (Regex.IsMatch(v, "^\\d:[0-5]\\d$")) v = "0" + v;
                if (!Regex.IsMatch(v, "^([01]\\d|2[0-3]):[0-5]\\d$")) { Toast(L.T("Время нужно в виде 04:00", "Enter the time as 04:00"), true); return; }
                Prefs.Set("ScheduleTime", v);
                if (scheduleOn) SetSchedule(true); else UpdateUi();
            });
            WMenu.ShowBelowRight(m, rowSchedule);
        }

        // ---------- сводка за неделю ----------

        static bool WeeklyOn { get { return Prefs.Get("WeeklySummary", "1") == "1"; } set { Prefs.Set("WeeklySummary", value ? "1" : "0"); } }
        DateTime weeklyCheckedAt = DateTime.MinValue;

        void MaybeWeeklySummary()
        {
            if (!WeeklyOn || (DateTime.Now - weeklyCheckedAt).TotalMinutes < 60) return;
            weeklyCheckedAt = DateTime.Now;
            DateTime last;
            if (!DateTime.TryParse(Prefs.Get("WeeklyLast", ""), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out last))
            { Prefs.Set("WeeklyLast", DateTime.Today.ToString("yyyy-MM-dd")); return; }   // первый раз — отсчёт начинается сегодня
            if ((DateTime.Today - last.Date).TotalDays < 7) return;
            Prefs.Set("WeeklyLast", DateTime.Today.ToString("yyyy-MM-dd"));
            DateTime from = DateTime.Today.AddDays(-7), to = DateTime.Today;
            var recs = Stats.Load(from).Where(r => r.T < to).ToList();
            double yt = Stats.UpPct(recs, 0, from, to), dc = Stats.UpPct(recs, 1, from, to);
            if (yt < 0 && dc < 0) return;
            int outs = Stats.Outages(recs, 0, from, to) + Stats.Outages(recs, 1, from, to);
            string text = "YouTube " + (yt < 0 ? "—" : Math.Round(yt) + "%") + ", Discord " + (dc < 0 ? "—" : Math.Round(dc) + "%") + ", " +
                (outs == 0 ? L.T("без сбоев", "no outages") : L.T("сбоев: ", "outages: ") + outs);
            Journal.Add(L.T("Сводка за неделю: ", "Weekly summary: ") + text);
            Balloon(L.T("Zapret за неделю", "Zapret this week"), text, outs > 3 ? WF.ToolTipIcon.Warning : WF.ToolTipIcon.Info);
        }

        // ---------- автообновление zapret ----------

        static bool ZapretAutoUpdate { get { return Prefs.Get("ZapretAutoUpdate", "0") == "1"; } set { Prefs.Set("ZapretAutoUpdate", value ? "1" : "0"); } }
        bool zapretAutoTried;

        // Вызывается, когда найдена новая версия zapret: тихое обновление или уведомление из трея (один раз на версию).
        void OnNewZapret()
        {
            if (newVersion == null) return;
            if (ZapretAutoUpdate && !busy && !zapretAutoTried)
            {
                zapretAutoTried = true;
                Journal.Add(L.T("Автообновление zapret до v", "Auto-updating zapret to v") + newVersion);
                UpdateZapret(true);
                return;
            }
            if (Prefs.Get("ZapretNotified", "") == newVersion) return;
            Prefs.Set("ZapretNotified", newVersion);
            if (!IsVisible) Balloon(L.T("Вышла zapret v", "zapret v") + newVersion + L.T("", " is out"),
                L.T("Откройте программу, чтобы обновить", "Open the app to update"), WF.ToolTipIcon.Info);
        }

        // ---------- возврат прошлой версии программы ----------

        void RollbackGui()
        {
            if (busy) return;
            string prev = SelfUpdate.PrevVersion();
            if (prev == null) { Toast(L.T("Прошлая версия не сохранена — она появится после следующего обновления", "No previous version saved — it appears after the next update")); return; }
            if (AskW.Show(this, L.T("Вернуть ZapretGUI v" + prev + " вместо v" + SelfUpdate.Current.ToString(3) + "?\n\nПрограмма перезапустится, обход продолжит работать. Автообновление программы выключится, чтобы она не обновилась снова.",
                    "Go back to ZapretGUI v" + prev + " instead of v" + SelfUpdate.Current.ToString(3) + "?\n\nThe app restarts, the bypass keeps running. App auto-update is turned off so it doesn't update again."),
                    L.T("Прошлая версия", "Previous version"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { SelfUpdate.Rollback(); }
            catch (Exception ex) { Toast(ex.Message, true); return; }
            GuiAutoUpdate = false;
            Journal.Add(L.T("Возврат на ZapretGUI v", "Rolled back to ZapretGUI v") + prev);
            RestartSelf();
        }

        // ---------- проверка файлов zapret ----------

        static readonly string[] NeededFiles = { @"bin\winws.exe", @"bin\WinDivert.dll", @"bin\WinDivert64.sys", @"bin\cygwin1.dll", "service.bat", @"lists\list-general.txt" };

        void VerifyZapret()
        {
            if (busy) return;
            var missing = NeededFiles.Where(f => { var fi = new FileInfo(Path.Combine(z.Root, f)); return !fi.Exists || fi.Length == 0; }).ToList();
            int bats = z.Strategies().Count;
            if (missing.Count == 0 && bats > 0)
            {
                AskW.Show(this, L.T("Все нужные файлы zapret на месте. Стратегий: ", "All required zapret files are in place. Strategies: ") + bats,
                    L.T("Файлы zapret", "zapret files"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            string msg = L.T("Не хватает файлов:\n", "Missing files:\n") + string.Join("\n", missing.Select(f => "•  " + f)) + (bats == 0 ? L.T("\n•  стратегии (.bat)", "\n•  strategies (.bat)") : "") +
                L.T("\n\nЧасто их удаляет антивирус — добавьте папку zapret в его исключения.\n\nСкачать недостающие файлы из последнего релиза zapret? Ваши списки и настройки не меняются.",
                    "\n\nAn antivirus often removes them — add the zapret folder to its exclusions.\n\nDownload the missing files from the latest zapret release? Your lists and settings are not changed.");
            if (AskW.Show(this, msg, L.T("Файлы zapret", "zapret files"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            string root = z.Root;
            RunOp(L.T("Восстановление файлов zapret…", "Restoring zapret files…"), false, () => Zapret.InstallFresh(root), () =>
            {
                Journal.Add(L.T("Файлы zapret восстановлены: ", "zapret files restored: ") + string.Join(", ", missing));
                Toast(L.T("Файлы zapret восстановлены", "zapret files restored"));
                strategies = z.Strategies();
            });
        }

        // ---------- очистка old-strategies ----------

        void CleanOldStrategies()
        {
            string dir = Path.Combine(z.Root, "old-strategies");
            if (!Directory.Exists(dir) || !Directory.EnumerateFileSystemEntries(dir).Any()) { Toast(L.T("Старых стратегий нет", "There are no old strategies")); return; }
            var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories);
            var versions = Directory.GetDirectories(dir).Select(Path.GetFileName).ToList();
            if (AskW.Show(this, L.T("В old-strategies лежит " + files.Length + " файлов от прошлых версий zapret (" + string.Join(", ", versions) + ").\n\nПереместить папку в Корзину? Из Корзины её можно вернуть.",
                    "old-strategies holds " + files.Length + " files from earlier zapret versions (" + string.Join(", ", versions) + ").\n\nMove the folder to the Recycle Bin? You can restore it from there."),
                    L.T("Старые стратегии", "Old strategies"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            if (RecycleBin.Send(dir))
            {
                Journal.Add(L.T("Папка old-strategies перемещена в Корзину (", "old-strategies moved to the Recycle Bin (") + files.Length + L.T(" файлов)", " files)"));
                Toast(L.T("Старые стратегии перемещены в Корзину", "Old strategies moved to the Recycle Bin"));
            }
            else Toast(L.T("Не удалось переместить в Корзину", "Could not move to the Recycle Bin"), true);
        }

        // ---------- прошлая стратегия и редактор стратегии ----------

        void RestorePrevStrategy()
        {
            string p = Prefs.Get("PrevStrategy", "");
            strategies = z.Strategies();
            if (p == "" || p == selected || !strategies.Contains(p)) { Toast(L.T("Прошлой стратегии нет", "There is no previous strategy")); return; }
            SelectStrategy(p);
            Toast(L.T("Возвращена стратегия «" + p + "»", "Back to “" + p + "”"));
        }

        void MoreStrategyItems(MenuItem strat)
        {
            string p = Prefs.Get("PrevStrategy", "");
            var back = WMenu.AddTo(strat, p != "" && p != selected ? L.T("Вернуть прошлую: ", "Back to previous: ") + p : L.T("Вернуть прошлую стратегию", "Back to the previous strategy"), false, RestorePrevStrategy);
            back.IsEnabled = p != "" && p != selected && !busy;
            WMenu.AddTo(strat, L.T("Создать свою из «" + selected + "»…", "Make your own from “" + selected + "”…"), false, EditStrategy);
        }

        void EditStrategy()
        {
            if (busy || selected == "") return;
            var w = new StrategyEditW(this, z, selected);
            w.ShowDialog();
            if (w.SavedName == null) return;
            strategies = z.Strategies();
            Journal.Add(L.T("Сохранена своя стратегия: ", "Own strategy saved: ") + w.SavedName);
            if (AskW.Show(this, L.T("Стратегия «" + w.SavedName + "» сохранена. Включить её сейчас?", "Strategy “" + w.SavedName + "” saved. Use it now?"),
                    L.T("Своя стратегия", "Own strategy"), MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) SelectStrategy(w.SavedName);
        }

        // ---------- профили по сети ----------

        string netKey, netLabel;
        DispatcherTimer netDebounce;

        static Dictionary<string, string> NetProfiles
        {
            get
            {
                var d = new Dictionary<string, string>();
                foreach (string part in Prefs.Get("NetProfiles", "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    int i = part.LastIndexOf('|');
                    if (i > 0) d[part.Substring(0, i)] = part.Substring(i + 1);
                }
                return d;
            }
            set { Prefs.Set("NetProfiles", string.Join(";", value.Select(kv => kv.Key.Replace(";", " ") + "|" + kv.Value))); }
        }

        // Как отличить одну сеть от другой: имя Wi-Fi, а если его нет — адрес роутера (шлюза).
        static void DetectNetwork(out string key, out string label)
        {
            key = null; label = null;
            try
            {
                string o = Zapret.RunOutput("netsh.exe", "wlan show interfaces") ?? "";
                var m = Regex.Match(o, @"^\s*SSID\s*:\s*(.+?)\s*$", RegexOptions.Multiline);
                if (m.Success && m.Groups[1].Value != "") { key = "wifi:" + m.Groups[1].Value.Replace("|", " ").Replace(";", " "); label = "Wi-Fi «" + m.Groups[1].Value + "»"; return; }
            }
            catch { }
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var gw = ni.GetIPProperties().GatewayAddresses.FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any));
                    if (gw == null) continue;
                    var mac = new byte[6]; int len = 6;
                    string id = gw.Address.ToString();
                    try { if (SendARP(BitConverter.ToInt32(gw.Address.GetAddressBytes(), 0), 0, mac, ref len) == 0) id = BitConverter.ToString(mac, 0, len); } catch { }
                    key = "gw:" + id; label = ni.Name + " (" + gw.Address + ")";
                    return;
                }
            }
            catch { }
        }

        void WatchNetwork()
        {
            netDebounce = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6) };
            netDebounce.Tick += delegate { netDebounce.Stop(); RefreshNetwork(true); };
            NetworkChange.NetworkAddressChanged += delegate { Post(() => { netDebounce.Stop(); netDebounce.Start(); }); };
            RefreshNetwork(true);
        }

        void RefreshNetwork(bool apply)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string key, label;
                DetectNetwork(out key, out label);
                Post(() =>
                {
                    bool changed = key != netKey;
                    netKey = key; netLabel = label;
                    if (!apply || !changed || key == null || busy) return;
                    string name;
                    if (!NetProfiles.TryGetValue(key, out name)) return;
                    var p = Profiles.FirstOrDefault(x => x.Name == name);
                    if (p == null || (p.Strategy == selected && p.Game == z.GameMode && p.Ipset == z.IpsetMode())) return;   // уже так и настроено
                    Journal.Add(L.T("Сеть ", "Network ") + label + L.T(" — включаю профиль «", " — applying profile “") + name + L.T("»", "”"));
                    ApplyProfile(p);
                });
            });
        }

        // Пункты «для этой сети» в меню профилей.
        void NetProfileItems(ContextMenu m, List<Profile> list)
        {
            if (netKey == null || list.Count == 0) return;
            string cur;
            NetProfiles.TryGetValue(netKey, out cur);
            var sub = WMenu.Sub(m, L.T("Для сети ", "For network ") + netLabel + ": " + (cur ?? L.T("не выбран", "none")));
            WMenu.AddTo(sub, L.T("Не переключать", "Don't switch"), cur == null, () => { var d = NetProfiles; d.Remove(netKey); NetProfiles = d; });
            foreach (var p in list)
            {
                var pr = p;
                WMenu.AddTo(sub, pr.Name, cur == pr.Name, () =>
                {
                    var d = NetProfiles; d[netKey] = pr.Name; NetProfiles = d;
                    Journal.Add(L.T("Для сети ", "For network ") + netLabel + L.T(" выбран профиль «", " the profile is “") + pr.Name + L.T("»", "”"));
                    Toast(L.T("В этой сети будет включаться профиль «" + pr.Name + "»", "Profile “" + pr.Name + "” will be applied on this network"));
                });
            }
        }

        // ---------- экспорт и импорт профилей ----------

        void ExportProfiles()
        {
            var d = new Microsoft.Win32.SaveFileDialog { Filter = L.T("Профили ZapretGUI|*.zgprofiles", "ZapretGUI profiles|*.zgprofiles"), FileName = "zapretgui-profiles.zgprofiles" };
            if (d.ShowDialog(this) != true) return;
            try
            {
                var sb = new StringBuilder("# ZapretGUI profiles: name|strategy|game filter|ipset\r\n");
                foreach (var p in Profiles) sb.Append(Clean(p.Name)).Append('|').Append(Clean(p.Strategy)).Append('|').Append(p.Game).Append('|').Append(p.Ipset).Append("\r\n");
                File.WriteAllText(d.FileName, sb.ToString(), new UTF8Encoding(false));
                Toast(L.T("Профили сохранены в файл", "Profiles saved to a file"));
            }
            catch (Exception ex) { Toast(ex.Message, true); }
        }

        void ImportProfiles()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Профили ZapretGUI|*.zgprofiles|Все файлы|*.*", "ZapretGUI profiles|*.zgprofiles|All files|*.*") };
            if (d.ShowDialog(this) != true) return;
            try
            {
                var list = Profiles;
                int n = 0;
                foreach (string line in File.ReadAllLines(d.FileName, Encoding.UTF8))
                {
                    var p = line.Split('|');
                    if (line.StartsWith("#") || p.Length != 4 || p[0].Trim() == "" || p[0].Length > 60) continue;
                    if (Array.IndexOf(new[] { "disabled", "all", "tcp", "udp" }, p[2]) < 0 || Array.IndexOf(new[] { "any", "none", "loaded" }, p[3]) < 0) continue;
                    list.RemoveAll(x => x.Name == p[0].Trim());
                    list.Add(new Profile { Name = p[0].Trim(), Strategy = p[1].Trim(), Game = p[2], Ipset = p[3] });
                    n++;
                }
                Profiles = list;
                Journal.Add(L.T("Загружено профилей из файла: ", "Profiles loaded from a file: ") + n);
                Toast(n > 0 ? L.T("Загружено профилей: ", "Profiles loaded: ") + n : L.T("В файле нет профилей", "No profiles in the file"), n == 0);
            }
            catch (Exception ex) { Toast(ex.Message, true); }
        }

        [DllImport("iphlpapi.dll", ExactSpelling = true)] static extern int SendARP(int destIp, int srcIp, byte[] mac, ref int len);
    }

    // ================= Перемещение в Корзину =================
    static class RecycleBin
    {
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd; public uint wFunc;
            [MarshalAs(UnmanagedType.LPWStr)] public string pFrom;
            [MarshalAs(UnmanagedType.LPWStr)] public string pTo;
            public ushort fFlags; public bool fAnyOperationsAborted; public IntPtr hNameMappings;
            [MarshalAs(UnmanagedType.LPWStr)] public string lpszProgressTitle;
        }
        [DllImport("shell32.dll", CharSet = CharSet.Unicode)] static extern int SHFileOperation(ref SHFILEOPSTRUCT op);

        public static bool Send(string path)
        {
            try
            {
                var op = new SHFILEOPSTRUCT { wFunc = 3 /* удалить */, pFrom = path + "\0\0", fFlags = 0x40 | 0x10 | 0x400 /* в Корзину, без вопросов и окон */ };
                return SHFileOperation(ref op) == 0 && !Directory.Exists(path) && !File.Exists(path);
            }
            catch { return false; }
        }
    }

    // ================= Выбор папки: современное окно Windows =================
    static class FolderPicker
    {
        public static string Pick(string title)
        {
            try
            {
                var dlg = (IFileDialog)new FileOpenDialogRCW();
                try
                {
                    uint opts; dlg.GetOptions(out opts);
                    dlg.SetOptions(opts | 0x20 /* выбирать папки */ | 0x40 /* только настоящие папки */);
                    dlg.SetTitle(title);
                    if (dlg.Show(IntPtr.Zero) != 0) return null;
                    IShellItem item; dlg.GetResult(out item);
                    string path; item.GetDisplayName(0x80058000, out path);
                    return path;
                }
                finally { Marshal.ReleaseComObject(dlg); }
            }
            catch
            {
                // на всякий случай — старое окно
                using (var d = new WF.FolderBrowserDialog { Description = title })
                    return d.ShowDialog() == WF.DialogResult.OK ? d.SelectedPath : null;
            }
        }

        [ComImport, Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7")] class FileOpenDialogRCW { }

        [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint count, IntPtr specs);
            void SetFileTypeIndex(uint index);
            void GetFileTypeIndex(out uint index);
            void Advise(IntPtr events, out uint cookie);
            void Unadvise(uint cookie);
            void SetOptions(uint options);
            void GetOptions(out uint options);
            void SetDefaultFolder(IShellItem item);
            void SetFolder(IShellItem item);
            void GetFolder(out IShellItem item);
            void GetCurrentSelection(out IShellItem item);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string name);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
            void GetResult(out IShellItem item);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem parent);
            void GetDisplayName(uint sigdn, [MarshalAs(UnmanagedType.LPWStr)] out string name);
        }
    }

    // ================= Своё сочетание клавиш =================
    class HotkeyW : WinBase
    {
        public uint Mods, Vk;
        uint mods, vk;
        readonly TextBlock shown, hint;

        public HotkeyW(Window owner) : base(L.T("Горячая клавиша", "Hotkey"), owner)
        {
            var sp = new StackPanel { Width = 340, Margin = new Thickness(22, 18, 22, 18) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            var d = W.T(L.T("Нажмите новое сочетание, например Ctrl+Alt+Z или Ctrl+Shift+F9.", "Press the new shortcut, e.g. Ctrl+Alt+Z or Ctrl+Shift+F9."), W.Small, W.Muted);
            d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 4, 0, 12);
            sp.Children.Add(d);
            shown = W.T("—", W.Status, W.Accent, true); shown.HorizontalAlignment = HorizontalAlignment.Center;
            sp.Children.Add(new Border { Background = W.Surface, CornerRadius = new CornerRadius(14), Padding = new Thickness(14, 16, 14, 16), Child = shown });
            hint = W.T("", W.Caption, W.Warn); hint.Margin = new Thickness(2, 6, 0, 0); hint.TextWrapping = TextWrapping.Wrap;
            sp.Children.Add(hint);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            var cancel = new Pill(L.T("Отмена", "Cancel"), true) { MinWidth = 96 };
            var save = new Pill(L.T("Сохранить", "Save")) { MinWidth = 110, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            save.Click += delegate { if (vk != 0) { Mods = mods; Vk = vk; Close(); } };
            bar.Children.Add(cancel); bar.Children.Add(save);
            sp.Children.Add(bar);
            Root.Child = sp;
            Focusable = true;
            Loaded += delegate { Focus(); };
            PreviewKeyDown += (s, e) =>
            {
                Key k = e.Key == Key.System ? e.SystemKey : e.Key;
                if (k == Key.Escape || k == Key.Tab || k == Key.Enter || k == Key.Space) return;
                e.Handled = true;
                if (k == Key.LeftCtrl || k == Key.RightCtrl || k == Key.LeftAlt || k == Key.RightAlt || k == Key.LeftShift || k == Key.RightShift || k == Key.LWin || k == Key.RWin) return;
                uint m = (uint)Keyboard.Modifiers & 15;
                bool fkey = k >= Key.F1 && k <= Key.F24;
                if (m == 0 && !fkey || m == 4 && !fkey)
                {
                    hint.Text = L.T("Нужна клавиша Ctrl, Alt или Win вместе с буквой — иначе сочетание будет мешать печатать.", "Use Ctrl, Alt or Win with the key — otherwise the shortcut gets in the way of typing.");
                    return;
                }
                mods = m; vk = (uint)KeyInterop.VirtualKeyFromKey(k);
                shown.Text = MainWindow.HotkeyText(mods, vk);
                hint.Text = "";
            };
        }
    }

    // ================= Выбор своего цвета =================
    class ColorW : WinBase
    {
        public int Rgb = -1;
        int cur;
        readonly Border preview;
        readonly TextBox hex;
        bool typing;

        static readonly int[] Swatches = {
            0xEF4444, 0xF97316, 0xF59E0B, 0xEAB308, 0x84CC16, 0x22C55E, 0x10B981, 0x14B8A6,
            0x06B6D4, 0x0EA5E9, 0x3B82F6, 0x6366F1, 0x8B5CF6, 0xA855F7, 0xD946EF, 0xEC4899,
            0xF43F5E, 0xFB7185, 0xFDBA74, 0xFDE047, 0x86EFAC, 0x67E8F9, 0x93C5FD, 0xC4B5FD };

        public ColorW(Window owner, int start) : base(L.T("Свой цвет", "Custom color"), owner)
        {
            cur = start & 0xFFFFFF;
            var sp = new StackPanel { Width = 320, Margin = new Thickness(22, 18, 22, 18) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            var d = W.T(L.T("Выберите цвет или впишите код, например 34D399.", "Pick a color or type a code, e.g. 34D399."), W.Small, W.Muted);
            d.Margin = new Thickness(0, 4, 0, 12);
            sp.Children.Add(d);
            var grid = new UniformGrid { Columns = 8, Margin = new Thickness(-4, 0, -4, 0) };
            foreach (int c in Swatches)
            {
                int cc = c;
                var sw = new Clickable { Width = 32, Height = 32, CornerRadius = new CornerRadius(9), Margin = new Thickness(4), Background = new SolidColorBrush(FromRgb(cc)) };
                sw.Click += delegate { Set(cc, true); };
                grid.Children.Add(sw);
            }
            sp.Children.Add(grid);
            var row = new Grid { Margin = new Thickness(0, 12, 0, 0) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) });
            row.ColumnDefinitions.Add(new ColumnDefinition());
            preview = new Border { Width = 36, Height = 36, CornerRadius = new CornerRadius(18), HorizontalAlignment = HorizontalAlignment.Left };
            row.Children.Add(preview);
            hex = new TextBox { MaxLength = 7, FontFamily = W.MonoFont };
            hex.TextChanged += delegate
            {
                if (typing) return;
                string t = hex.Text.Trim().TrimStart('#');
                int v;
                if (t.Length == 6 && int.TryParse(t, System.Globalization.NumberStyles.HexNumber, null, out v)) Set(v, false);
            };
            Grid.SetColumn(hex, 1); row.Children.Add(hex);
            sp.Children.Add(row);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 16, 0, 0) };
            var cancel = new Pill(L.T("Отмена", "Cancel"), true) { MinWidth = 96 };
            var ok = new Pill(L.T("Выбрать", "Choose")) { MinWidth = 110, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            ok.Click += delegate { Rgb = cur; Close(); };
            bar.Children.Add(cancel); bar.Children.Add(ok);
            sp.Children.Add(bar);
            Root.Child = sp;
            Set(cur, true);
        }

        static Color FromRgb(int c) { return Color.FromRgb((byte)(c >> 16), (byte)(c >> 8), (byte)c); }

        void Set(int c, bool updateText)
        {
            cur = c & 0xFFFFFF;
            preview.Background = new SolidColorBrush(FromRgb(cur));
            if (updateText) { typing = true; hex.Text = cur.ToString("X6"); typing = false; }
        }
    }

    // ================= Своя стратегия =================
    class StrategyEditW : WinBase
    {
        public const string Marker = ":: ZapretGUI-custom";   // по этой строке программа узнаёт свои стратегии и не убирает их при обновлении zapret
        public string SavedName;
        readonly TextBox name, body;
        readonly TextBlock error;
        readonly Zapret z;

        public StrategyEditW(Window owner, Zapret zapret, string source) : base(L.T("Своя стратегия", "Own strategy"), owner)
        {
            z = zapret;
            var sp = new StackPanel { Width = 620, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            var d = W.T(L.T("Копия стратегии «" + source + "». Меняйте параметры winws.exe и сохраните под своим именем — исходная стратегия не изменится. Для опытных: ошибка в параметрах — и обход не запустится.",
                "A copy of “" + source + "”. Change the winws.exe options and save under your own name — the original stays as is. For advanced users: a wrong option and the bypass won't start."), W.Small, W.Muted);
            d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 4, 0, 10);
            sp.Children.Add(d);
            string baseName = Regex.Replace(source, @"\s*\((моя|my)( \d+)?\)$", "");
            string suggested = baseName + L.T(" (моя)", " (my)");
            for (int i = 2; File.Exists(Path.Combine(z.Root, suggested + ".bat")); i++) suggested = baseName + L.T(" (моя " + i + ")", " (my " + i + ")");
            name = new TextBox { Text = suggested, Tag = L.T("название стратегии", "strategy name") };
            sp.Children.Add(name);
            string text = "";
            try { text = File.ReadAllText(Path.Combine(z.Root, source + ".bat")); } catch { }
            body = new TextBox { Text = text, AcceptsReturn = true, AcceptsTab = false, TextWrapping = TextWrapping.NoWrap, Height = 380, FontFamily = W.MonoFont, FontSize = 12, Margin = new Thickness(0, 8, 0, 0),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
            sp.Children.Add(body);
            error = W.T("", W.Caption, W.Danger); error.Margin = new Thickness(2, 6, 0, 0);
            sp.Children.Add(error);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 10, 0, 0) };
            var cancel = new Pill(L.T("Отмена", "Cancel"), true) { MinWidth = 96 };
            var save = new Pill(L.T("Сохранить", "Save")) { MinWidth = 110, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Click += delegate { Close(); };
            save.Click += delegate { Save(); };
            bar.Children.Add(cancel); bar.Children.Add(save);
            sp.Children.Add(bar);
            Root.Child = sp;
        }

        void Save()
        {
            string n = name.Text.Trim();
            if (n == "" || n.Length > 80 || n.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) { error.Text = L.T("В названии есть недопустимые знаки", "The name has invalid characters"); return; }
            if (n.Equals("service", StringComparison.OrdinalIgnoreCase)) { error.Text = L.T("Это имя занято", "This name is taken"); return; }
            string path = Path.Combine(z.Root, n + ".bat");
            if (File.Exists(path))
            {
                string old = "";
                try { old = File.ReadAllText(path); } catch { }
                if (!old.Contains(Marker)) { error.Text = L.T("Так называется стратегия Flowseal — выберите другое имя", "A Flowseal strategy has this name — pick another"); return; }
            }
            if (body.Text.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) < 0) { error.Text = L.T("В тексте нет запуска winws.exe", "The text doesn't start winws.exe"); return; }
            try
            {
                string text = body.Text.Replace("\r\n", "\n").Replace("\n", "\r\n");
                if (!text.Contains(Marker)) text = Marker + "\r\n" + text;
                File.WriteAllText(path, text, new UTF8Encoding(false));
                SavedName = n;
                Close();
            }
            catch (Exception ex) { error.Text = ex.Message; }
        }
    }
}
