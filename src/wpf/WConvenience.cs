// Удобство: горячая клавиша, команды --on/--off/--toggle, профили, обход по программам, тихий режим в играх,
// пауза, баннер и автообновление, ярлыки, переносной режим, «Удалить всё», положение окна, меню трея.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace ZapretGui
{
    partial class MainWindow
    {
        // ---------- горячая клавиша Ctrl+Alt+Z ----------

        static bool HotkeyOn { get { return Prefs.Get("Hotkey", "0") == "1"; } set { Prefs.Set("Hotkey", value ? "1" : "0"); } }
        const int HotkeyId = 0x5A47;
        bool hotkeyRegistered;

        void ApplyHotkey()
        {
            IntPtr h = new WindowInteropHelper(this).Handle;
            if (h == IntPtr.Zero) return;
            if (hotkeyRegistered) { UnregisterHotKey(h, HotkeyId); hotkeyRegistered = false; }
            if (!HotkeyOn || snapPath != null) return;
            uint mods, vk;
            HotkeyKeys(out mods, out vk);
            hotkeyRegistered = RegisterHotKey(h, HotkeyId, mods | 0x4000, vk);   // 0x4000 — без повторов при удержании
            if (!hotkeyRegistered) Toast(HotkeyText() + L.T(" уже занята другой программой — выберите другое сочетание", " is already used by another app — pick another one"), true);
        }

        // Сочетание хранится как «модификаторы,код клавиши»: Alt = 1, Ctrl = 2, Shift = 4, Win = 8. По умолчанию Ctrl+Alt+Z.
        static void HotkeyKeys(out uint mods, out uint vk)
        {
            mods = 3; vk = 0x5A;
            var p = Prefs.Get("HotkeyKeys", "3,90").Split(',');
            uint a, b;
            if (p.Length == 2 && uint.TryParse(p[0], out a) && uint.TryParse(p[1], out b) && b > 0 && a < 16) { mods = a; vk = b; }
        }

        internal static string HotkeyText(uint mods, uint vk)
        {
            string s = ((mods & 2) != 0 ? "Ctrl+" : "") + ((mods & 1) != 0 ? "Alt+" : "") + ((mods & 4) != 0 ? "Shift+" : "") + ((mods & 8) != 0 ? "Win+" : "");
            string key = System.Windows.Input.KeyInterop.KeyFromVirtualKey((int)vk).ToString();
            if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key.Substring(1);   // D1 → 1
            return s + key;
        }
        static string HotkeyText() { uint m, v; HotkeyKeys(out m, out v); return HotkeyText(m, v); }

        void ToggleHotkey()
        {
            HotkeyOn = !HotkeyOn;
            ApplyHotkey();
            Journal.Add(HotkeyOn ? L.T("Горячая клавиша включена: ", "Hotkey on: ") + HotkeyText() : L.T("Горячая клавиша выключена", "Hotkey off"));
            UpdateUi();
        }

        void ShowHotkeyMenu()
        {
            var m = WMenu.Create();
            WMenu.Add(m, HotkeyOn ? L.T("Выключить", "Turn off") : L.T("Включить (", "Turn on (") + HotkeyText() + ")", false, ToggleHotkey);
            WMenu.Add(m, L.T("Изменить сочетание…", "Change the shortcut…"), false, () =>
            {
                var w = new HotkeyW(this);
                w.ShowDialog();
                if (w.Vk == 0) return;
                Prefs.Set("HotkeyKeys", w.Mods + "," + w.Vk);
                HotkeyOn = true;
                ApplyHotkey();
                Journal.Add(L.T("Горячая клавиша: ", "Hotkey: ") + HotkeyText());
                UpdateUi();
            });
            WMenu.ShowBelowRight(m, rowHotkey);
        }

        // ---------- команды из командной строки: --on, --off, --toggle ----------

        public static readonly int WM_COMMANDLINE = RegisterWindowMessage("ZapretGUI_Command");

        internal void RunCommand(int cmd)
        {
            if (busy) return;
            if (cmd == 1 && !Active) Start();
            else if (cmd == 2 && Active) Stop();
            else if (cmd == 3) TogglePower();
        }

        // ---------- профили ----------

        public class Profile { public string Name, Strategy, Game, Ipset; }

        static List<Profile> Profiles
        {
            get
            {
                var res = new List<Profile>();
                foreach (string part in Prefs.Get("Profiles", "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = part.Split('|');
                    if (p.Length == 4) res.Add(new Profile { Name = p[0], Strategy = p[1], Game = p[2], Ipset = p[3] });
                }
                return res;
            }
            set { Prefs.Set("Profiles", string.Join(";", value.Select(p => Clean(p.Name) + "|" + Clean(p.Strategy) + "|" + p.Game + "|" + p.Ipset))); }
        }
        static string Clean(string s) { return (s ?? "").Replace("|", " ").Replace(";", " "); }

        void ShowProfilesMenu(FrameworkElement anchor)
        {
            var m = WMenu.Create();
            var list = Profiles;
            string cur = Prefs.Get("Profile", "");
            foreach (var p in list)
            {
                var pr = p;
                WMenu.Add(m, pr.Name + "   ·  " + pr.Strategy, pr.Name == cur, () => ApplyProfile(pr));
            }
            if (list.Count > 0) WMenu.Sep(m);
            NetProfileItems(m, list);   // «в этой сети включать профиль…»
            WMenu.Add(m, L.T("Сохранить текущие настройки как профиль…", "Save current settings as a profile…"), false, SaveProfile);
            var file = WMenu.Sub(m, L.T("Файл", "File"));
            WMenu.AddTo(file, L.T("Сохранить профили в файл…", "Save profiles to a file…"), false, ExportProfiles).IsEnabled = list.Count > 0;
            WMenu.AddTo(file, L.T("Загрузить профили из файла…", "Load profiles from a file…"), false, ImportProfiles);
            foreach (var p in list)
            {
                var pr = p;
                WMenu.Add(m, L.T("Удалить профиль «", "Delete profile “") + pr.Name + L.T("»", "”"), false, () =>
                {
                    if (AskW.Show(this, L.T("Удалить профиль «" + pr.Name + "»?", "Delete profile “" + pr.Name + "”?"), L.T("Профили", "Profiles"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
                    Profiles = Profiles.Where(x => x.Name != pr.Name).ToList();
                });
            }
            if (anchor != null) WMenu.ShowAbove(m, anchor); else WMenu.ShowBelowRight(m, rowProfiles);
        }

        void SaveProfile()
        {
            string name = InputW.Show(this, L.T("Новый профиль", "New profile"), L.T("Название, например «Игры»", "Name, e.g. “Games”"), "");
            if (string.IsNullOrWhiteSpace(name)) return;
            name = Clean(name.Trim());
            var list = Profiles.Where(p => p.Name != name).ToList();
            list.Add(new Profile { Name = name, Strategy = selected, Game = z.GameMode, Ipset = z.IpsetMode() });
            Profiles = list;
            Prefs.Set("Profile", name);
            Journal.Add(L.T("Сохранён профиль «", "Profile saved: “") + name + L.T("»", "”"));
            Toast(L.T("Профиль «" + name + "» сохранён", "Profile “" + name + "” saved"));
        }

        void ApplyProfile(Profile p)
        {
            if (busy) return;
            try
            {
                if (z.GameMode != p.Game) { z.GameMode = p.Game; z.SaveGame(); }
                if (z.IpsetMode() != p.Ipset) z.SetIpset(p.Ipset);
            }
            catch (Exception ex) { Toast(ex.Message, true); }
            Prefs.Set("Profile", p.Name);
            Journal.Add(L.T("Профиль «", "Profile “") + p.Name + L.T("»: ", "”: ") + p.Strategy + L.T(", игровой фильтр ", ", game filter ") + GameLabel(p.Game) + ", IPSet " + IpsetLabel(p.Ipset));
            if (strategies.Contains(p.Strategy) && p.Strategy != selected) { selected = p.Strategy; Prefs.Set("Strategy", p.Strategy); }
            UpdateUi();
            RestartIfActive();
            Toast(L.T("Профиль «" + p.Name + "»", "Profile “" + p.Name + "”"));
        }

        // ---------- обход по программам ----------

        static List<string> AutoApps
        {
            get { return Prefs.Get("AutoApps", "").Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).ToList(); }
            set { Prefs.Set("AutoApps", string.Join("|", value)); }
        }
        bool startedByApp;
        DateTime appsCheckedAt = DateTime.MinValue;

        // Раз в 5 секунд: запущена ли одна из программ из списка — включить обход; закрыли все — выключить (если включали сами).
        void CheckAutoApps()
        {
            if (busy || (DateTime.Now - appsCheckedAt).TotalSeconds < 5) return;
            appsCheckedAt = DateTime.Now;
            var apps = AutoApps;
            if (apps.Count == 0) return;
            string found = apps.FirstOrDefault(a => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(a)).Length > 0);
            if (found != null && !Active)
            {
                startedByApp = true;
                Journal.Add(L.T("Запущена ", "Started: ") + found + L.T(" — включаю обход", " — turning the bypass on"));
                Start();
            }
            else if (found == null && startedByApp && Active)
            {
                startedByApp = false;
                Journal.Add(L.T("Программы из списка закрыты — выключаю обход", "Listed apps closed — turning the bypass off"));
                Stop();
            }
        }

        void ShowAutoAppsMenu()
        {
            var m = WMenu.Create();
            var list = AutoApps;
            foreach (string a in list)
            {
                string app = a;
                WMenu.Add(m, L.T("Убрать ", "Remove ") + app, false, () => { AutoApps = AutoApps.Where(x => x != app).ToList(); UpdateUi(); });
            }
            if (list.Count > 0) WMenu.Sep(m);
            WMenu.Add(m, L.T("Добавить программу…", "Add a program…"), false, () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Filter = L.T("Программы|*.exe", "Programs|*.exe"), Title = L.T("Программа, с которой включать обход", "Program that turns the bypass on") };
                if (d.ShowDialog(this) != true) return;
                string exe = Path.GetFileName(d.FileName);
                var l = AutoApps; if (!l.Contains(exe, StringComparer.OrdinalIgnoreCase)) l.Add(exe);
                AutoApps = l;
                Journal.Add(L.T("Обход будет включаться вместе с ", "The bypass will turn on with ") + exe);
                UpdateUi();
            });
            WMenu.ShowBelowRight(m, rowAutoApps);
        }

        // ---------- не проверять сайты во время полноэкранной игры ----------

        static bool GameQuiet { get { return Prefs.Get("GameQuiet", "1") == "1"; } set { Prefs.Set("GameQuiet", value ? "1" : "0"); } }

        static bool FullscreenAppRunning()
        {
            try
            {
                int state;
                if (SHQueryUserNotificationState(out state) != 0) return false;
                if (state == 3) return true;      // полноэкранная игра (Direct3D)
                if (state != 2) return false;     // 2 — любое окно во весь экран: игра «в окне без рамки» или видео
                // видео в браузере или плеере игрой не считается — иначе в статистике появляются пустые места
                uint pid;
                GetWindowThreadProcessId(GetForegroundWindow(), out pid);
                string name = Process.GetProcessById((int)pid).ProcessName.ToLowerInvariant();
                string[] notGames = { "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "browser", "vivaldi", "yandex", "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64",
                    "potplayer", "potplayermini64", "wmplayer", "explorer", "powerpnt", "obs64", "discord", "telegram", "zapretgui" };
                return Array.IndexOf(notGames, name) < 0;
            }
            catch { return false; }
        }

        // ---------- пауза обхода ----------

        DateTime pauseUntil = DateTime.MinValue;
        string pauseStrategy;

        void Pause(int minutes)
        {
            if (busy || !Active) return;
            pauseUntil = DateTime.Now.AddMinutes(minutes);
            pauseStrategy = selected;
            Journal.Add(L.T("Пауза обхода на ", "Bypass paused for ") + minutes + L.T(" мин", " min"));
            Stop();
            Toast(L.T("Пауза до ", "Paused until ") + pauseUntil.ToString("HH:mm"));
        }

        void CheckPause()
        {
            if (pauseUntil == DateTime.MinValue || busy) return;
            if (Active) { pauseUntil = DateTime.MinValue; return; }   // включили вручную раньше
            if (DateTime.Now < pauseUntil) return;
            pauseUntil = DateTime.MinValue;
            Journal.Add(L.T("Пауза закончилась — обход включается", "Pause over — turning the bypass on"));
            if (pauseStrategy != null && strategies.Contains(pauseStrategy)) selected = pauseStrategy;
            Start();
        }

        bool Paused { get { return pauseUntil != DateTime.MinValue && !Active; } }

        void ShowPauseMenu(FrameworkElement anchor)
        {
            var m = WMenu.Create();
            if (Paused) WMenu.Add(m, L.T("Включить сейчас", "Resume now"), false, () => { pauseUntil = DateTime.Now; CheckPause(); });
            else
            {
                WMenu.Add(m, L.T("Пауза на 15 минут", "Pause for 15 minutes"), false, () => Pause(15));
                WMenu.Add(m, L.T("Пауза на 1 час", "Pause for 1 hour"), false, () => Pause(60));
                WMenu.Add(m, L.T("Пауза на 3 часа", "Pause for 3 hours"), false, () => Pause(180));
            }
            if (anchor != null) WMenu.ShowAbove(m, anchor);
            else { m.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint; m.IsOpen = true; }
        }

        // ---------- баннер и автообновление ----------

        static bool GuiAutoUpdate { get { return Prefs.Get("GuiAutoUpdate", "0") == "1"; } set { Prefs.Set("GuiAutoUpdate", value ? "1" : "0"); } }

        void OnUpdatesFound()
        {
            if (newGui != null && GuiAutoUpdate && !busy)
            {
                Journal.Add(L.T("Автообновление ZapretGUI до v", "Auto-updating ZapretGUI to v") + newGui);
                SilentGuiUpdate();
                return;
            }
            OnNewZapret();   // тихое обновление zapret или уведомление из трея
            if (newGui != null) ShowBanner(L.T("Вышла ZapretGUI v", "ZapretGUI v") + newGui + L.T("", " is out"), L.T("Обновить", "Update"), UpdateGui, W.Accent, 0);
            else if (newVersion != null) ShowBanner(L.T("Вышла zapret v", "zapret v") + newVersion + L.T("", " is out"), L.T("Обновить", "Update"), UpdateZapret, W.Accent, 0);
        }

        void SilentGuiUpdate()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    string url, sha;
                    var latest = SelfUpdate.Latest(out url, out sha);
                    if (latest <= SelfUpdate.Current) return;
                    string exe = WF.Application.ExecutablePath;
                    Post(() =>
                    {
                        if (busy) return;
                        RunOp(L.T("Обновление ZapretGUI…", "Updating ZapretGUI…"), false, () => SelfUpdate.DownloadAndInstall(exe, url, sha), () =>
                        {
                            Journal.Add(L.T("ZapretGUI обновлён автоматически до v", "ZapretGUI auto-updated to v") + latest.ToString(3));
                            RestartSelf();
                        });
                    });
                }
                catch { }
            });
        }

        // ---------- ярлыки ----------

        void CreateShortcuts()
        {
            try
            {
                string exe = WF.Application.ExecutablePath;
                var t = Type.GetTypeFromProgID("WScript.Shell");
                object sh = Activator.CreateInstance(t);
                foreach (string dir in new[] { Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Environment.GetFolderPath(Environment.SpecialFolder.Programs) })
                {
                    object lnk = t.InvokeMember("CreateShortcut", System.Reflection.BindingFlags.InvokeMethod, null, sh, new object[] { Path.Combine(dir, "Zapret GUI.lnk") });
                    var lt = lnk.GetType();
                    lt.InvokeMember("TargetPath", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { exe });
                    lt.InvokeMember("WorkingDirectory", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { Path.GetDirectoryName(exe) });
                    lt.InvokeMember("Description", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { "Zapret GUI" });
                    lt.InvokeMember("IconLocation", System.Reflection.BindingFlags.SetProperty, null, lnk, new object[] { exe + ",0" });
                    lt.InvokeMember("Save", System.Reflection.BindingFlags.InvokeMethod, null, lnk, null);
                }
                Journal.Add(L.T("Созданы ярлыки на рабочем столе и в меню «Пуск»", "Shortcuts created on the desktop and in the Start menu"));
                Toast(L.T("Ярлыки созданы: рабочий стол и «Пуск»", "Shortcuts created: desktop and Start menu"));
            }
            catch (Exception ex) { Toast(L.T("Не удалось создать ярлыки: ", "Could not create shortcuts: ") + ex.Message, true); }
        }

        // ---------- переносной режим ----------

        void TogglePortable()
        {
            bool on = !Prefs.Portable;
            string msg = on
                ? L.T("Настройки будут храниться в файле ZapretGUI.ini рядом с программой. Удобно, если переносите папку на флешке или на другой компьютер.\n\nВключить?",
                      "Settings will be stored in ZapretGUI.ini next to the app — handy for a USB stick or another PC.\n\nTurn on?")
                : L.T("Настройки вернутся в реестр Windows, а ZapretGUI.ini будет переименован (не удалён).\n\nВыключить переносной режим?",
                      "Settings go back to the Windows registry; ZapretGUI.ini will be renamed (not deleted).\n\nTurn portable mode off?");
            if (AskW.Show(this, msg, L.T("Переносной режим", "Portable mode"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
            try { Prefs.SetPortable(on); }
            catch (Exception ex) { Toast(ex.Message, true); return; }
            Journal.Add(on ? L.T("Переносной режим включён (ZapretGUI.ini)", "Portable mode on (ZapretGUI.ini)") : L.T("Переносной режим выключен", "Portable mode off"));
            UpdateUi();
        }

        // ---------- «Удалить всё» ----------

        void RemoveEverything()
        {
            string msg = L.T("Будет сделано:\n• обход выключится, служба zapret и драйвер WinDivert будут удалены;\n• уберутся задачи планировщика ZapretGUI (запуск с Windows и подбор по расписанию);\n• сотрутся все настройки ZapretGUI.\n\nПапка zapret, ваши списки, журнал и статистика останутся. Программа закроется.\n\nПродолжить?",
                "This will:\n• turn the bypass off and remove the zapret service and WinDivert driver;\n• remove ZapretGUI scheduler tasks (start with Windows, scheduled test);\n• erase all ZapretGUI settings.\n\nThe zapret folder, your lists, log and statistics stay. The app will close.\n\nContinue?");
            if (AskW.Show(this, msg, L.T("Удалить всё", "Remove everything"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            if (AskW.Show(this, L.T("Точно? Это нельзя отменить.", "Are you sure? This can't be undone."), L.T("Удалить всё", "Remove everything"), MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
            expectRunning = false;
            RunOp(L.T("Удаление…", "Removing…"), true, () =>
            {
                z.StopAll(true);
                try { GuiAutostart.Set(false); } catch { }
                Zapret.Run("schtasks.exe", "/Delete /F /TN \"" + ScheduleTask + "\"");
                if (hotkeyRegistered) Dispatcher.Invoke((Action)delegate { UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyId); });
                Prefs.DeleteAll();
            }, () =>
            {
                Journal.Add(L.T("«Удалить всё»: служба, задачи и настройки ZapretGUI удалены", "“Remove everything”: service, tasks and settings removed"));
                exitRequested = true;
                Close();
            });
        }

        // ---------- положение окна ----------

        void RestorePosition()
        {
            double x, y;
            if (!double.TryParse(Prefs.Get("WinLeft", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out x) ||
                !double.TryParse(Prefs.Get("WinTop", ""), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out y)) return;
            // только если это место видно на одном из мониторов
            var r = new System.Drawing.Rectangle((int)x + 40, (int)y + 10, 100, 40);
            if (!WF.Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(r))) return;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = x; Top = y;
        }

        void SavePosition()
        {
            if (snapPath != null || WindowState != WindowState.Normal || !IsVisible) return;
            Prefs.Set("WinLeft", Left.ToString(System.Globalization.CultureInfo.InvariantCulture));
            Prefs.Set("WinTop", Top.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
        [DllImport("shell32.dll")] static extern int SHQueryUserNotificationState(out int state);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    }
}
