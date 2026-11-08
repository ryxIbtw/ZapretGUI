// Диагностика (перенесена из service.bat → Run Diagnostics).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace ZapretGui
{
    class DiagItem
    {
        public int Level;          // 0 — ок, 1 — предупреждение, 2 — проблема
        public string Title, Detail = "";
        public string FixText;     // текст кнопки, если можно исправить
        public Action Fix;
    }

    static class Diag
    {
        static DiagItem Ok(string t) { return new DiagItem { Level = 0, Title = t }; }
        static DiagItem Warn(string t, string d) { return new DiagItem { Level = 1, Title = t, Detail = d }; }
        static DiagItem Bad(string t, string d) { return new DiagItem { Level = 2, Title = t, Detail = d }; }

        public static List<DiagItem> Run(Zapret z)
        {
            var list = new List<DiagItem>();
            ServiceController[] all;
            try { all = ServiceController.GetServices(); } catch { all = new ServiceController[0]; }
            Func<Func<string, bool>, List<ServiceController>> running = pred => all.Where(s =>
            {
                try { return s.Status == ServiceControllerStatus.Running && (pred(s.ServiceName) || pred(s.DisplayName)); }
                catch { return false; }
            }).ToList();
            Func<string, Func<string, bool>> has = word => n => n.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

            // Base Filtering Engine
            bool bfe = running(n => n.Equals("BFE", StringComparison.OrdinalIgnoreCase)).Count > 0;
            list.Add(bfe ? Ok(L.T("Служба Base Filtering Engine работает", "Base Filtering Engine is running"))
                         : Bad(L.T("Base Filtering Engine не запущена", "Base Filtering Engine is not running"),
                               L.T("Без этой службы zapret не работает", "zapret requires this service")));

            // Прокси
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
            {
                bool proxy = k != null && (k.GetValue("ProxyEnable") as int? ?? 0) == 1;
                if (proxy)
                {
                    var it = Warn(L.T("Включён системный прокси", "System proxy is enabled"),
                        (k.GetValue("ProxyServer") as string ?? "") + L.T(" — отключите, если не пользуетесь", " — disable it if you don't use one"));
                    it.FixText = L.T("Отключить", "Disable");
                    it.Fix = () =>
                    {
                        using (var w = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                            if (w != null) w.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    };
                    list.Add(it);
                }
                else list.Add(Ok(L.T("Системный прокси не используется", "No system proxy")));
            }

            // TCP timestamps — включаем всегда, как это делает service.bat
            int ts = Zapret.Run("netsh.exe", "interface tcp set global timestamps=enabled");
            list.Add(ts == 0 ? Ok(L.T("Метки времени TCP включены", "TCP timestamps enabled"))
                             : Bad(L.T("Не удалось включить метки времени TCP", "Failed to enable TCP timestamps"), "netsh interface tcp set global timestamps=enabled"));

            // Конфликтующие программы
            bool adguard = Process.GetProcessesByName("AdguardSvc").Length > 0;
            list.Add(adguard ? Bad(L.T("Найден AdGuard", "AdGuard found"), L.T("Может мешать работе Discord", "May break Discord"))
                             : Ok(L.T("AdGuard не мешает", "No AdGuard conflict")));
            AddConflict(list, running(has("Killer")), "Killer", L.T("Сетевые службы Killer конфликтуют с zapret", "Killer network services conflict with zapret"));
            AddConflict(list, running(n => has("Intel")(n) && has("Connectivity")(n) && has("Network")(n)), "Intel Connectivity",
                L.T("Intel Connectivity Network Service конфликтует с zapret", "Intel Connectivity Network Service conflicts with zapret"));
            AddConflict(list, running(n => has("TracSrvWrapper")(n) || has("EPWD")(n)), "Check Point", L.T("Попробуйте удалить Check Point", "Try uninstalling Check Point"));
            AddConflict(list, running(has("SmartByte")), "SmartByte", L.T("Отключите SmartByte в services.msc", "Disable SmartByte in services.msc"));

            var vpn = running(n => has("VPN")(n) || has("HappService")(n));
            list.Add(vpn.Count > 0
                ? Warn(L.T("Найдены службы VPN", "VPN services found"), string.Join(", ", vpn.Select(s => s.ServiceName)) + L.T(" — отключите VPN", " — disable VPNs"))
                : Ok(L.T("VPN не мешает", "No VPN conflict")));

            // Защищённый DNS
            list.Add(HasDoh() ? Ok(L.T("Защищённый DNS настроен", "Secure DNS is configured"))
                              : Warn(L.T("Защищённый DNS не настроен", "Secure DNS is not configured"),
                                     L.T("Включите DNS-over-HTTPS в браузере или в параметрах Windows 11", "Enable DNS-over-HTTPS in your browser or in Windows 11 settings")));

            // Путь установки
            bool cyr = Regex.IsMatch(z.Root, "[а-яА-ЯёЁ]");
            list.Add(cyr ? Warn(L.T("В пути к zapret есть кириллица", "The zapret path contains Cyrillic characters"), L.T("Если не работает — перенесите, например, в C:\\zapret", "If it doesn't work, move it, e.g. to C:\\zapret"))
                         : Ok(L.T("Путь к zapret без кириллицы", "zapret path is fine")));
            string od = Environment.GetEnvironmentVariable("OneDrive");
            bool inOd = !string.IsNullOrEmpty(od) && (z.Root + "\\").StartsWith(od.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            list.Add(inOd ? Bad(L.T("zapret лежит в папке OneDrive", "zapret is inside OneDrive"), L.T("Перенесите, например, в C:\\zapret", "Move it, e.g. to C:\\zapret"))
                          : Ok(L.T("zapret не в OneDrive", "zapret is not in OneDrive")));

            bool sys = Directory.Exists(z.Bin) && Directory.GetFiles(z.Bin, "*.sys").Length > 0;
            list.Add(sys ? Ok(L.T("Драйвер WinDivert на месте", "WinDivert driver present"))
                         : Bad(L.T("Не найден WinDivert64.sys", "WinDivert64.sys not found"), L.T("Перекачайте zapret или проверьте антивирус", "Re-download zapret or check your antivirus")));

            // hosts
            try
            {
                string hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                string h = File.Exists(hosts) ? File.ReadAllText(hosts) : "";
                bool yt = Regex.IsMatch(h, @"^\s*[^#\s].*(youtube\.com|youtu\.be)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                list.Add(yt ? Warn(L.T("В файле hosts есть записи YouTube", "hosts file has YouTube entries"), L.T("Они могут мешать открытию YouTube", "They may break YouTube"))
                            : Ok(L.T("Файл hosts в порядке", "hosts file is fine")));
            }
            catch { }

            // WinDivert без winws
            bool winws = Process.GetProcessesByName("winws").Length > 0;
            bool divert = all.Any(s => { try { return s.ServiceName.Equals("WinDivert", StringComparison.OrdinalIgnoreCase) && s.Status != ServiceControllerStatus.Stopped; } catch { return false; } });
            if (!winws && divert)
            {
                var it = Warn(L.T("WinDivert активен без winws.exe", "WinDivert is active without winws.exe"), L.T("Остался от другой программы обхода", "Left over by another bypass tool"));
                it.FixText = L.T("Удалить", "Remove");
                it.Fix = Zapret.RemoveDriver;
                list.Add(it);
            }

            // Другие программы обхода
            var conflicts = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2" }
                .Where(n => all.Any(s => { try { return s.ServiceName.Equals(n, StringComparison.OrdinalIgnoreCase); } catch { return false; } })).ToList();
            if (conflicts.Count > 0)
            {
                var it = Bad(L.T("Найдены другие программы обхода", "Other bypass services found"), string.Join(", ", conflicts));
                it.FixText = L.T("Удалить", "Remove");
                it.Fix = () =>
                {
                    foreach (string n in conflicts) { Zapret.Run("sc.exe", "stop \"" + n + "\""); Zapret.Run("sc.exe", "delete \"" + n + "\""); }
                    Zapret.RemoveDriver();
                };
                list.Add(it);
            }
            else list.Add(Ok(L.T("Других программ обхода нет", "No other bypass services")));

            return list.OrderByDescending(i => i.Level).ToList();
        }

        static void AddConflict(List<DiagItem> list, List<ServiceController> found, string name, string detail)
        {
            list.Add(found.Count > 0 ? Bad(L.T("Найдено: ", "Found: ") + name, detail) : Ok(name + L.T(" не мешает", ": no conflict")));
        }

        static bool HasDoh()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters"))
                    return k != null && Scan(k);
            }
            catch { return false; }
        }

        static bool Scan(RegistryKey k)
        {
            object v = k.GetValue("DohFlags");
            if (v != null && Convert.ToInt64(v) > 0) return true;
            foreach (string n in k.GetSubKeyNames())
                using (var c = k.OpenSubKey(n))
                    if (c != null && Scan(c)) return true;
            return false;
        }

        // Очистка кэша Discord (Stable, PTB, Canary, Development). Возвращает число найденных установок.
        public static int ClearDiscordCache()
        {
            var apps = new[] { new[] { "discord", "Discord" }, new[] { "discordptb", "DiscordPTB" },
                               new[] { "discordcanary", "DiscordCanary" }, new[] { "discorddevelopment", "DiscordDevelopment" } };
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            int found = 0;
            foreach (var a in apps)
            {
                string dir = Path.Combine(appdata, a[0]);
                if (!Directory.Exists(dir)) continue;
                found++;
                foreach (var p in Process.GetProcessesByName(a[1])) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
                foreach (string c in new[] { "Cache", "Code Cache", "GPUCache" })
                {
                    string cd = Path.Combine(dir, c);
                    try { if (Directory.Exists(cd)) Directory.Delete(cd, true); } catch { }
                }
            }
            return found;
        }
    }
}
