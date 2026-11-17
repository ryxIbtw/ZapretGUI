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

            // TCP timestamps (нужны стратегиям). Диагностика только проверяет; включить — кнопкой, по решению пользователя.
            bool? ts = TcpTimestamps();
            if (ts == true) list.Add(Ok(L.T("Метки времени TCP включены", "TCP timestamps enabled")));
            else
            {
                var it = Warn(ts == false ? L.T("Метки времени TCP выключены", "TCP timestamps are disabled")
                                          : L.T("Не удалось узнать, включены ли метки времени TCP", "Could not tell whether TCP timestamps are enabled"),
                              L.T("Они нужны стратегиям zapret; zapret и сам включает их при запуске", "zapret strategies need them; zapret also turns them on when it starts"));
                it.FixText = L.T("Включить", "Enable");
                it.Fix = () =>
                {
                    if (Zapret.Run("netsh.exe", "interface tcp set global timestamps=enabled") != 0)
                        throw new Exception(L.T("Не удалось включить метки времени TCP (нужны права администратора)", "Could not enable TCP timestamps (administrator rights required)"));
                };
                list.Add(it);
            }

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

            // сетевые проверки — одновременно, чтобы диагностика не ждала каждую по очереди
            var checks = new Func<DiagItem>[] { DnsCheck, ClockCheck, Ipv6Check, QuicCheck };
            var tasks = checks.Select(c => System.Threading.Tasks.Task.Factory.StartNew(() => { try { return c(); } catch { return null; } })).ToArray();
            System.Threading.Tasks.Task.WaitAll(tasks, 12000);
            foreach (var t in tasks) if (t.IsCompleted && t.Result != null) list.Add(t.Result);

            return list.OrderByDescending(i => i.Level).ToList();
        }

        // Часы компьютера: если они сильно ошибаются, HTTPS-сайты не открываются (сертификат «ещё не действует» или «истёк»).
        public static DiagItem ClockCheck()
        {
            DateTime? server = null;
            foreach (string url in new[] { "http://www.msftconnecttest.com/connecttest.txt", "https://www.google.com/generate_204" })
            {
                try
                {
                    var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create(url);
                    req.Method = "HEAD"; req.Timeout = 5000; req.Proxy = null; req.AllowAutoRedirect = false; req.UserAgent = "ZapretGUI";
                    using (var resp = (System.Net.HttpWebResponse)req.GetResponse())
                    {
                        string d = resp.Headers["Date"];
                        DateTime t;
                        if (d != null && DateTime.TryParse(d, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal, out t)) { server = t; break; }
                    }
                }
                catch { }
            }
            if (server == null) return null;   // не удалось узнать точное время — пропускаем
            double diff = (DateTime.UtcNow - server.Value).TotalMinutes;
            if (Math.Abs(diff) < 5) return Ok(L.T("Часы компьютера точные", "The PC clock is accurate"));
            var it = Bad(L.T("Часы компьютера ", "The PC clock is ") + (diff > 0 ? L.T("спешат", "ahead") : L.T("отстают", "behind")) + L.T(" на ", " by ") + FormatSpan(Math.Abs(diff)),
                L.T("Из-за этого HTTPS-сайты могут не открываться. Включите «Устанавливать время автоматически».", "HTTPS sites may fail because of it. Turn on “Set time automatically”."));
            it.FixText = L.T("Настройки времени", "Time settings");
            it.Fix = () => Process.Start(new ProcessStartInfo("ms-settings:dateandtime") { UseShellExecute = true });   // только открыть окно Windows — сами ничего не меняем
            return it;
        }

        static string FormatSpan(double minutes)
        {
            if (minutes < 120) return Math.Round(minutes) + L.T(" мин", " min");
            if (minutes < 60 * 48) return Math.Round(minutes / 60) + L.T(" ч", " h");
            return Math.Round(minutes / 1440) + L.T(" дн.", " days");
        }

        // IPv6: если он есть, но не работает, сайты открываются с долгой задержкой.
        public static DiagItem Ipv6Check()
        {
            bool has = false;
            foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                foreach (var a in ni.GetIPProperties().UnicastAddresses)
                {
                    var ip = a.Address;
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && !ip.IsIPv6LinkLocal && !ip.IsIPv6SiteLocal && !System.Net.IPAddress.IsLoopback(ip) &&
                        !ip.ToString().StartsWith("fd", StringComparison.OrdinalIgnoreCase) && !ip.ToString().StartsWith("fc", StringComparison.OrdinalIgnoreCase)) has = true;
                }
            }
            if (!has) return Ok(L.T("IPv6 не используется", "IPv6 is not used"));
            System.Net.IPAddress target = null;
            try { target = System.Net.Dns.GetHostAddresses("www.google.com").FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6); } catch { }
            if (target == null) return Ok(L.T("IPv6 есть, DNS отвечает только IPv4", "IPv6 present, DNS answers IPv4 only"));
            bool works = false;
            try
            {
                using (var c = new System.Net.Sockets.TcpClient(System.Net.Sockets.AddressFamily.InterNetworkV6))
                    works = c.ConnectAsync(target, 443).Wait(3000) && c.Connected;
            }
            catch { }
            return works ? Ok(L.T("IPv6 работает", "IPv6 works"))
                         : Warn(L.T("IPv6 есть, но не работает", "IPv6 is present but doesn't work"),
                                L.T("Сайты могут открываться с задержкой. Если это мешает, отключите IPv6 в свойствах сетевого подключения.", "Sites may open slowly. If it bothers you, turn IPv6 off in the network connection properties."));
        }

        // QUIC (UDP 443) — по нему YouTube отдаёт видео. Отправляем пакет с выдуманной версией QUIC:
        // по правилам протокола сервер обязан ответить списком версий. Ответа нет — UDP до YouTube не проходит.
        public static DiagItem QuicCheck() { return QuicCheck("www.youtube.com"); }

        public static DiagItem QuicCheck(string host)
        {
            bool ok = QuicProbe(host, 3000) >= 0;
            return ok ? Ok(L.T("QUIC (UDP) до YouTube проходит", "QUIC (UDP) to YouTube works"))
                      : Warn(L.T("QUIC (UDP) до YouTube не проходит", "QUIC (UDP) to YouTube is blocked"),
                             L.T("Видео всё равно пойдёт по TCP, но может грузиться медленнее. Помогает другая стратегия.", "Video still goes over TCP but may load slower. Another strategy may help."));
        }

        // Время ответа в мс или -1.
        public static long QuicProbe(string host, int timeout)
        {
            try
            {
                var ip = System.Net.Dns.GetHostAddresses(host).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                if (ip == null) return -1;
                var rnd = new Random();
                var pkt = new byte[1200];   // Initial-пакеты QUIC должны быть не короче 1200 байт
                pkt[0] = 0xC0;              // длинный заголовок
                pkt[1] = 0x1A; pkt[2] = 0x2A; pkt[3] = 0x3A; pkt[4] = 0x4A;   // версия, которой нет — сервер отвечает Version Negotiation
                pkt[5] = 8; var dcid = new byte[8]; rnd.NextBytes(dcid); Array.Copy(dcid, 0, pkt, 6, 8);
                pkt[14] = 8; var scid = new byte[8]; rnd.NextBytes(scid); Array.Copy(scid, 0, pkt, 15, 8);
                using (var udp = new System.Net.Sockets.UdpClient(System.Net.Sockets.AddressFamily.InterNetwork))
                {
                    udp.Client.ReceiveTimeout = timeout;
                    var sw = Stopwatch.StartNew();
                    udp.Send(pkt, pkt.Length, new System.Net.IPEndPoint(ip, 443));
                    var from = new System.Net.IPEndPoint(System.Net.IPAddress.Any, 0);
                    var resp = udp.Receive(ref from);
                    // Version Negotiation: длинный заголовок и версия 0
                    if (resp.Length >= 7 && (resp[0] & 0x80) != 0 && resp[1] == 0 && resp[2] == 0 && resp[3] == 0 && resp[4] == 0) return sw.ElapsedMilliseconds;
                    return -1;
                }
            }
            catch { return -1; }
        }

        // Подменяет ли DNS провайдера адреса YouTube: сравнить ответ системного DNS с ответом Cloudflare (DNS через HTTPS).
        public static DiagItem DnsCheck()
        {
            List<string> sys;
            try { sys = System.Net.Dns.GetHostAddresses("www.youtube.com").Where(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork).Select(a => a.ToString()).ToList(); }
            catch { return Warn(L.T("DNS не ответил про youtube.com", "DNS did not resolve youtube.com"), L.T("Проверьте подключение или DNS-сервер", "Check the connection or the DNS server")); }
            Func<string, bool> bogus = ip => ip.StartsWith("0.") || ip.StartsWith("127.") || ip.StartsWith("10.") || ip.StartsWith("192.168.") ||
                Regex.IsMatch(ip, "^172\\.(1[6-9]|2\\d|3[01])\\.");
            if (sys.Count == 0 || sys.All(bogus))
                return Bad(L.T("DNS подменяет адрес YouTube", "DNS spoofs the YouTube address"),
                    L.T("Системный DNS вернул ", "System DNS returned ") + (sys.Count == 0 ? "—" : string.Join(", ", sys)) +
                    L.T(". Включите DNS через HTTPS в Windows или в браузере.", ". Turn on DNS over HTTPS in Windows or the browser."));
            List<string> doh = new List<string>();
            try
            {
                var req = (System.Net.HttpWebRequest)System.Net.WebRequest.Create("https://cloudflare-dns.com/dns-query?name=www.youtube.com&type=A");
                req.Accept = "application/dns-json"; req.Timeout = 5000; req.Proxy = null; req.UserAgent = "ZapretGUI";
                using (var resp = req.GetResponse())
                using (var rd = new StreamReader(resp.GetResponseStream()))
                    foreach (var a in Json.Arr(Json.Obj(rd.ReadToEnd()), "Answer"))
                    {
                        string ip = Json.Str(a, "data");
                        if (ip != null && Regex.IsMatch(ip, "^\\d+\\.\\d+\\.\\d+\\.\\d+$")) doh.Add(ip);
                    }
            }
            catch { }
            // у YouTube адреса Google: при любом честном DNS они из диапазонов Google
            Func<string, bool> google = ip => new[] { "142.250.", "142.251.", "172.217.", "172.253.", "216.58.", "216.239.", "173.194.", "74.125.", "64.233.", "108.177.", "209.85.", "192.178." }.Any(ip.StartsWith);
            if (sys.Any(google) || doh.Count > 0 && sys.Intersect(doh).Any())
                return Ok(L.T("DNS отвечает честно (youtube.com → ", "DNS answers honestly (youtube.com → ") + sys[0] + ")");
            return Warn(L.T("DNS может подменять адреса", "DNS may be spoofing addresses"),
                L.T("youtube.com → ", "youtube.com → ") + string.Join(", ", sys.Take(3)) + (doh.Count > 0 ? L.T(", а Cloudflare отвечает ", ", while Cloudflare says ") + doh[0] : "") +
                L.T(". Если сайты не открываются, включите DNS через HTTPS.", ". If sites don't open, turn on DNS over HTTPS."));
        }

        static void AddConflict(List<DiagItem> list, List<ServiceController> found, string name, string detail)
        {
            list.Add(found.Count > 0 ? Bad(L.T("Найдено: ", "Found: ") + name, detail) : Ok(name + L.T(" не мешает", ": no conflict")));
        }

        // Состояние «RFC 1323 Timestamps» из «netsh interface tcp show global»: true/false, null — не удалось понять.
        static bool? TcpTimestamps()
        {
            string s = Zapret.RunOutput("netsh.exe", "interface tcp show global");
            if (s == null) return null;
            foreach (string raw in s.Split('\n'))
            {
                if (raw.IndexOf("1323", StringComparison.Ordinal) < 0) continue;
                int colon = raw.LastIndexOf(':');
                string v = (colon >= 0 ? raw.Substring(colon + 1) : raw).Trim().ToLowerInvariant();
                if (v.StartsWith("enabled") || v.StartsWith("включ")) return true;
                if (v.StartsWith("disabled") || v.StartsWith("allowed") || v.StartsWith("отключ") || v.StartsWith("выключ") || v.StartsWith("разреш")) return false;
                return null;
            }
            return null;
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
