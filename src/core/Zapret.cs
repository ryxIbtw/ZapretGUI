// Логика работы с папкой zapret-discord-youtube: стратегии, служба, настройки, обновление.
// Файлы настроек совместимы с service.bat (utils\game_filter.enabled, utils\check_updates.enabled, lists\ipset-all.txt).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using Microsoft.Win32;

namespace ZapretGui
{
    class Zapret
    {
        public const string ServiceName = "zapret";
        const string ServiceKey = @"SYSTEM\CurrentControlSet\Services\zapret";
        public const string IpsetPlaceholder = "203.0.113.113/32";
        public const string DomainPlaceholder = "domain.example.abc";
        const string VersionUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/.service/version.txt";
        const string IpsetUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/refs/heads/main/.service/ipset-service.txt";
        const string ReleaseApi = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
        public const string ReleasesUrl = "https://github.com/Flowseal/zapret-discord-youtube/releases/latest";
        public const string FakeDiscord = "ACTIVE_DISCORD_UDP.bin", FakeGame = "ACTIVE_GAME_UDP.bin";

        static readonly UTF8Encoding Utf8NoBom = new UTF8Encoding(false);

        public readonly string Root, Bin, Lists, Utils;

        public Zapret(string root)
        {
            Root = Path.GetFullPath(root).TrimEnd('\\');
            Bin = Path.Combine(Root, "bin");
            Lists = Path.Combine(Root, "lists");
            Utils = Path.Combine(Root, "utils");
        }

        public static bool IsValidRoot(string dir)
        {
            if (string.IsNullOrEmpty(dir)) return false;
            try { return File.Exists(Path.Combine(dir, "service.bat")) && Directory.Exists(Path.Combine(dir, "bin")); }
            catch { return false; }
        }

        public string Winws { get { return Path.Combine(Bin, "winws.exe"); } }

        // Версия из service.bat. Файл перечитывается только если он изменился (окно спрашивает версию каждые 2 секунды).
        DateTime versionStamp; string versionCache;
        public string LocalVersion
        {
            get
            {
                try
                {
                    string f = Path.Combine(Root, "service.bat");
                    DateTime t = File.GetLastWriteTimeUtc(f);
                    if (versionCache != null && t == versionStamp) return versionCache;
                    var m = Regex.Match(File.ReadAllText(f), "set \"LOCAL_VERSION=([^\"]+)\"");
                    versionCache = m.Success ? m.Groups[1].Value.Trim() : "";
                    versionStamp = t;
                    return versionCache;
                }
                catch { return ""; }
            }
        }

        // ---------- Стратегии ----------

        public List<string> Strategies()
        {
            return Directory.GetFiles(Root, "*.bat")
                .Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => !n.StartsWith("service", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => Regex.Replace(n, @"\d+", m => m.Value.PadLeft(8, '0')), StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // Достаёт аргументы winws.exe из .bat-файла стратегии и подставляет переменные.
        public string BuildArgs(string strategy)
        {
            string path = Path.Combine(Root, strategy + ".bat");
            var sb = new StringBuilder();
            bool capture = false;
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                if (!capture && raw.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase) >= 0) capture = true;
                if (!capture) continue;
                string line = raw.TrimEnd();
                bool cont = line.EndsWith("^");
                if (cont) line = line.Substring(0, line.Length - 1);
                sb.Append(line).Append(' ');
                if (!cont) break;
            }
            string s = sb.ToString();
            int i = s.IndexOf("winws.exe\"", StringComparison.OrdinalIgnoreCase);
            if (i >= 0) s = s.Substring(i + 10);
            else
            {
                i = s.IndexOf("winws.exe", StringComparison.OrdinalIgnoreCase);
                if (i < 0) throw new Exception(L.T("В файле стратегии не найден запуск winws.exe", "winws.exe launch not found in the strategy file"));
                s = s.Substring(i + 9);
            }

            var vars = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            vars["BIN"] = Bin + "\\";
            vars["LISTS"] = Lists + "\\";
            vars["GameFilter"] = GameMode == "udp" ? GameUdp : (GameMode == "disabled" ? "12" : GameTcp);
            vars["GameFilterTCP"] = (GameMode == "all" || GameMode == "tcp") ? GameTcp : "12";
            vars["GameFilterUDP"] = (GameMode == "all" || GameMode == "udp") ? GameUdp : "12";

            s = s.Replace("%%", "\u0001");
            s = s.Replace("%~dp0", Root + "\\");
            s = Regex.Replace(s, @"%([A-Za-z_][A-Za-z0-9_]*)%", m =>
            {
                string v;
                return vars.TryGetValue(m.Groups[1].Value, out v) ? v : m.Value;
            });
            s = s.Replace("\u0001", "%").Replace("^!", "!");
            return Regex.Replace(s, @"\s+", " ").Trim();
        }

        // ---------- Состояние ----------

        public bool IsRunning() { return Process.GetProcessesByName("winws").Length > 0; }

        public bool IsPidRunning(int pid)
        {
            return Process.GetProcessesByName("winws").Any(p => p.Id == pid);
        }

        public bool ServiceInstalled()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(ServiceKey)) return k != null;
        }

        public string ServiceStrategy()
        {
            using (var k = Registry.LocalMachine.OpenSubKey(ServiceKey))
                return k == null ? null : k.GetValue("zapret-discord-youtube") as string;
        }

        // ---------- Запуск / остановка ----------

        // Возвращает PID процесса (или 0 при запуске службой).
        public int Start(string strategy, bool asService)
        {
            if (!File.Exists(Winws)) throw new Exception(L.T("Не найден bin\\winws.exe", "bin\\winws.exe not found"));
            string args = BuildArgs(strategy);
            StopAll(false);
            EnsureUserLists();
            Run("netsh.exe", "interface tcp set global timestamps=enabled");
            if (asService) { InstallService(strategy, args); return 0; }

            var psi = new ProcessStartInfo(Winws, args)
            {
                WorkingDirectory = Bin,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            var p = Process.Start(psi);
            Thread.Sleep(1200);
            if (p.HasExited)
            {
                int code = p.ExitCode;
                // запустить ещё раз, уже с чтением вывода, — чтобы узнать причину (обычный запуск вывод не читает:
                // winws работает долго, и читать его вывод всё время незачем)
                string output = ProbeWinwsError(args);
                throw new Exception(ExplainWinws(code, output));
            }
            return p.Id;
        }

        string ProbeWinwsError(string args)
        {
            try
            {
                var psi = new ProcessStartInfo(Winws, args)
                {
                    WorkingDirectory = Bin, UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true,
                };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    var outp = p.StandardOutput.ReadToEndAsync();
                    if (!p.WaitForExit(3000)) { try { p.Kill(); } catch { } return ""; }   // в этот раз запустился — значит, сбой был случайный
                    return (outp.Result + "\n" + err.Result).Trim();
                }
            }
            catch { return ""; }
        }

        // Понятная причина по коду выхода и сообщениям winws.exe.
        public static string ExplainWinws(int code, string output)
        {
            string o = (output ?? "").ToLowerInvariant();
            string why;
            if (o.Contains("access is denied") || o.Contains("отказано в доступе") || code == 5)
                why = L.T("Нет прав: программу нужно запускать от имени администратора.", "Access denied: the app must run as administrator.");
            else if (o.Contains("signature") || o.Contains("подпис") || code == 577 || code == 1275)
                why = L.T("Windows или антивирус не дал загрузить драйвер WinDivert. Добавьте папку zapret в исключения антивируса.",
                          "Windows or an antivirus blocked the WinDivert driver. Add the zapret folder to the antivirus exclusions.");
            else if (o.Contains("windivert") && (o.Contains("not found") || o.Contains("cannot find") || o.Contains("не найден") || code == 2))
                why = L.T("Не найден драйвер WinDivert в папке bin. Обновите zapret или проверьте, не удалил ли его антивирус.",
                          "The WinDivert driver is missing from the bin folder. Update zapret or check whether an antivirus removed it.");
            else if (o.Contains("already") || o.Contains("in use") || code == 32)
                why = L.T("Похоже, уже работает другая программа обхода или копия winws. Закройте её и попробуйте снова.",
                          "Another bypass program or winws copy seems to be running. Close it and try again.");
            else if (o.Contains("file") && (o.Contains("not found") || o.Contains("cannot open") || o.Contains("can't open")))
                why = L.T("Стратегия ссылается на файл, которого нет (список или fake-файл). Попробуйте другую стратегию или обновите zapret.",
                          "The strategy refers to a missing file (list or fake file). Try another strategy or update zapret.");
            else if (o.Contains("invalid") || o.Contains("unknown option") || o.Contains("unrecognized"))
                why = L.T("В стратегии есть параметр, который эта версия winws не понимает. Обновите zapret или выберите другую стратегию.",
                          "The strategy has an option this winws version doesn't understand. Update zapret or pick another strategy.");
            else
                why = L.T("Попробуйте другую стратегию или диагностику.", "Try another strategy or run diagnostics.");
            string last = "";
            if (!string.IsNullOrEmpty(output))
            {
                var lines = output.Replace("\r", "").Split('\n').Where(l => l.Trim() != "").ToList();
                if (lines.Count > 0) last = "\n" + L.T("Сообщение winws: ", "winws says: ") + lines[lines.Count - 1].Trim();
            }
            return L.T("winws.exe сразу завершился (код " + code + "). ", "winws.exe exited immediately (code " + code + "). ") + why + last;
        }

        public void StopAll(bool full)
        {
            RemoveService();
            foreach (var p in Process.GetProcessesByName("winws"))
            {
                try { p.Kill(); p.WaitForExit(3000); } catch { }
            }
            if (full) RemoveDriver();
        }

        public static void RemoveDriver()
        {
            Run("sc.exe", "stop WinDivert"); Run("sc.exe", "delete WinDivert");
            Run("sc.exe", "stop WinDivert14"); Run("sc.exe", "delete WinDivert14");
        }

        void InstallService(string strategy, string args)
        {
            string binPath = "\"" + Winws + "\" " + args;
            IntPtr scm = OpenSCManager(null, null, 0xF003F);
            if (scm == IntPtr.Zero) throw new Exception(L.T("Нет доступа к диспетчеру служб (нужны права администратора)", "No access to the service manager (administrator rights required)"));
            try
            {
                IntPtr svc = IntPtr.Zero;
                for (int attempt = 0; attempt < 20; attempt++)
                {
                    svc = CreateService(scm, ServiceName, "zapret", 0xF01FF, 0x10, 2, 1, binPath, null, IntPtr.Zero, null, null, null);
                    if (svc != IntPtr.Zero) break;
                    int err = Marshal.GetLastWin32Error();
                    if (err != 1072) throw new Exception(L.T("Не удалось создать службу (ошибка ", "Could not create the service (error ") + err + ")");
                    Thread.Sleep(300); // служба ещё помечена на удаление
                }
                if (svc == IntPtr.Zero) throw new Exception(L.T("Старая служба ещё удаляется, попробуйте снова", "The old service is still being removed, try again"));
                CloseServiceHandle(svc);
            }
            finally { CloseServiceHandle(scm); }

            Run("sc.exe", "description zapret \"Zapret DPI bypass software\"");
            using (var k = Registry.LocalMachine.OpenSubKey(ServiceKey, true))
                if (k != null) k.SetValue("zapret-discord-youtube", strategy, RegistryValueKind.String);

            using (var sc = new ServiceController(ServiceName))
            {
                sc.Start();
                try { sc.WaitForStatus(ServiceControllerStatus.Running, TimeSpan.FromSeconds(10)); } catch { }
                Thread.Sleep(800);
                sc.Refresh();
                if (sc.Status != ServiceControllerStatus.Running)
                    throw new Exception(L.T("Служба zapret не запустилась. Попробуйте другую стратегию или диагностику.",
                        "The zapret service did not start. Try another strategy or run diagnostics."));
            }
        }

        void RemoveService()
        {
            if (!ServiceInstalled()) return;
            try
            {
                using (var sc = new ServiceController(ServiceName))
                {
                    if (sc.Status != ServiceControllerStatus.Stopped)
                    {
                        sc.Stop();
                        sc.WaitForStatus(ServiceControllerStatus.Stopped, TimeSpan.FromSeconds(10));
                    }
                }
            }
            catch { }
            Run("sc.exe", "delete zapret");
        }

        public void EnsureUserLists()
        {
            Directory.CreateDirectory(Lists);
            WriteIfMissing(Path.Combine(Lists, "ipset-exclude-user.txt"), IpsetPlaceholder + "\r\n");
            WriteIfMissing(Path.Combine(Lists, "list-general-user.txt"), "# Never leave this file empty\r\ndomain.example.abc\r\n");
            WriteIfMissing(Path.Combine(Lists, "list-exclude-user.txt"), "domain.example.abc\r\n");
        }

        static void WriteIfMissing(string f, string content)
        {
            if (!File.Exists(f)) File.WriteAllText(f, content, Utf8NoBom);
        }

        // Запускает программу и возвращает её вывод (или null). Консольные утилиты пишут в кодировке OEM (cp866 для русского).
        public static string RunOutput(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true,
                    StandardOutputEncoding = Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.OEMCodePage),
                };
                using (var p = Process.Start(psi))
                {
                    string s = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(15000);
                    return s;
                }
            }
            catch { return null; }
        }

        public static int Run(string exe, string args)
        {
            try
            {
                var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true };
                using (var p = Process.Start(psi)) { p.WaitForExit(15000); return p.ExitCode; }
            }
            catch { return -1; }
        }

        // ---------- Игровой фильтр ----------

        public string GameMode = "disabled";
        public string GameTcp = "1024-65535", GameUdp = "1024-65535";
        string GameFile { get { return Path.Combine(Utils, "game_filter.enabled"); } }

        public void LoadGame()
        {
            GameMode = "disabled"; GameTcp = "1024-65535"; GameUdp = "1024-65535";
            if (!File.Exists(GameFile)) return;
            string mode = "disabled";
            foreach (string raw in File.ReadAllLines(GameFile))
            {
                string line = raw.Trim();
                int eq = line.IndexOf('=');
                string key = (eq < 0 ? line : line.Substring(0, eq)).Trim().ToLowerInvariant();
                string val = eq < 0 ? "" : line.Substring(eq + 1).Trim();
                if (key == "mode") mode = val.ToLowerInvariant();
                else if (key == "all") mode = "all";
                else if (key == "tcp") { if (val == "") mode = "tcp"; else if (ValidRange(val)) GameTcp = val; }
                else if (key == "udp") { if (val == "") mode = "udp"; else if (ValidRange(val)) GameUdp = val; }
            }
            GameMode = (mode == "all" || mode == "tcp" || mode == "udp") ? mode : "disabled";
        }

        public void SaveGame()
        {
            Directory.CreateDirectory(Utils);
            File.WriteAllText(GameFile, "mode=" + GameMode + "\r\ntcp=" + GameTcp + "\r\nudp=" + GameUdp + "\r\n", Utf8NoBom);
        }

        public static bool ValidRange(string s)
        {
            s = (s ?? "").Replace(" ", "");
            if (!Regex.IsMatch(s, @"^\d{1,5}(-\d{1,5})?(,\d{1,5}(-\d{1,5})?)*$")) return false;
            foreach (string item in s.Split(','))
            {
                string[] p = item.Split('-');
                int a = int.Parse(p[0]), b = p.Length > 1 ? int.Parse(p[1]) : a;
                if (a < 1 || b > 65535 || a > b) return false;
            }
            return true;
        }

        // ---------- IPSet ----------

        string IpsetFile { get { return Path.Combine(Lists, "ipset-all.txt"); } }
        DateTime ipsetStamp; long ipsetLen = -1; string ipsetCache;

        // loaded — по списку, none — выключен, any — все IP
        public string IpsetMode()
        {
            var fi = new FileInfo(IpsetFile);
            if (!fi.Exists) return "any";
            if (ipsetCache != null && fi.LastWriteTimeUtc == ipsetStamp && fi.Length == ipsetLen) return ipsetCache;
            string mode;
            if (fi.Length == 0) mode = "any";
            else mode = File.ReadAllText(IpsetFile).Contains(IpsetPlaceholder) ? "none" : "loaded";
            ipsetStamp = fi.LastWriteTimeUtc; ipsetLen = fi.Length; ipsetCache = mode;
            return mode;
        }

        public void SetIpset(string target)
        {
            string cur = IpsetMode();
            if (cur == target) return;
            string bak = IpsetFile + ".backup";
            if (target == "loaded")
            {
                if (!File.Exists(bak)) throw new Exception(L.T("Нет сохранённого списка — нажмите «Обновить IPSet»", "No saved list — click “Update IPSet” first"));
                File.Copy(bak, IpsetFile, true);
                File.Delete(bak);
            }
            else
            {
                if (cur == "loaded" && File.Exists(IpsetFile)) File.Copy(IpsetFile, bak, true);
                File.WriteAllText(IpsetFile, target == "none" ? IpsetPlaceholder + "\r\n" : "", Utf8NoBom);
            }
            ipsetCache = null;
        }

        public void UpdateIpset()
        {
            string text = Download(IpsetUrl, 20000);
            if (string.IsNullOrWhiteSpace(text)) throw new Exception(L.T("Получен пустой список", "Received an empty list"));
            File.WriteAllText(IpsetFile, text, Utf8NoBom);
            ipsetCache = null;
        }

        // ---------- Fake-файлы ----------

        public List<string> FakeFiles()
        {
            if (!Directory.Exists(Bin)) return new List<string>();
            return Directory.GetFiles(Bin, "*.bin").Select(f => Path.GetFileNameWithoutExtension(f))
                .Where(n => !n.StartsWith("ACTIVE_", StringComparison.OrdinalIgnoreCase))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
        }

        // Какой из fake-файлов сейчас скопирован в ACTIVE_*.bin (по хешу).
        public string CurrentFake(string active)
        {
            string a = Path.Combine(Bin, active);
            if (!File.Exists(a)) return null;
            string h = Hash(a);
            foreach (string n in FakeFiles())
                if (Hash(Path.Combine(Bin, n + ".bin")) == h) return n;
            return null;
        }

        public void SetFake(string active, string name)
        {
            File.Copy(Path.Combine(Bin, name + ".bin"), Path.Combine(Bin, active), true);
        }

        static string Hash(string f)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
            using (var s = File.OpenRead(f))
                return BitConverter.ToString(sha.ComputeHash(s));
        }

        // ---------- Обновления ----------

        string UpdatesFlag { get { return Path.Combine(Utils, "check_updates.enabled"); } }

        public bool UpdatesEnabled
        {
            get { return File.Exists(UpdatesFlag); }
            set
            {
                if (value) { Directory.CreateDirectory(Utils); File.WriteAllText(UpdatesFlag, "ENABLED\r\n"); }
                else if (File.Exists(UpdatesFlag)) File.Delete(UpdatesFlag);
            }
        }

        public string RemoteVersion() { return Download(VersionUrl, 7000).Trim(); }

        public string LatestRelease(out string zipUrl)
        {
            var rel = Json.Obj(Download(ReleaseApi, 10000));
            string tag = Json.Str(rel, "tag_name");
            zipUrl = null;
            foreach (var a in Json.Arr(rel, "assets"))
            {
                string url = Json.Str(a, "browser_download_url");
                if (url != null && url.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) { zipUrl = url; break; }
            }
            if (tag == null || zipUrl == null) throw new Exception(L.T("Не удалось получить данные о релизе", "Could not read release info"));
            return tag.Trim();
        }

        // Первый запуск без zapret: скачать последний релиз Flowseal и распаковать в папку dir.
        public static void InstallFresh(string dir)
        {
            string url;
            new Zapret(dir).LatestRelease(out url);
            string tmp = Path.Combine(Path.GetTempPath(), "zapretgui-install-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string zip = Path.Combine(tmp, "release.zip");
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                using (var wc = new WebClient()) { wc.Headers["User-Agent"] = "ZapretGUI"; wc.DownloadFile(url, zip); }
                string ex = Path.Combine(tmp, "x");
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, ex);
                string src = FindRootIn(ex, 3);
                if (src == null) throw new Exception(L.T("В архиве нет service.bat", "service.bat not found in the archive"));
                Directory.CreateDirectory(dir);
                foreach (string f in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
                {
                    string rel = f.Substring(src.Length).TrimStart('\\');
                    string dst = Path.Combine(dir, rel);
                    Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    if (!File.Exists(dst)) File.Copy(f, dst);   // ничего уже лежащего в папке не перезаписываем
                }
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Скачивает zip релиза и раскладывает поверх текущей папки,
        // сохраняя пользовательские списки, настройки и активные fake-файлы.
        // Обход перед вызовом должен быть остановлен.
        public void UpdateFrom(string zipUrl)
        {
            string tmp = Path.Combine(Path.GetTempPath(), "zapretgui-update-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tmp);
            try
            {
                string zip = Path.Combine(tmp, "release.zip");
                ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
                using (var wc = new WebClient())
                {
                    wc.Headers["User-Agent"] = "ZapretGUI";
                    wc.DownloadFile(zipUrl, zip);
                }
                string ex = Path.Combine(tmp, "x");
                System.IO.Compression.ZipFile.ExtractToDirectory(zip, ex);
                string src = FindRootIn(ex, 3);
                if (src == null) throw new Exception(L.T("В архиве нет service.bat", "service.bat not found in the archive"));

                var keep = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
                Action<string> save = rel =>
                {
                    string p = Path.Combine(Root, rel);
                    if (File.Exists(p)) keep[rel] = File.ReadAllBytes(p);
                };
                foreach (string f in FilesIn(Lists, "*-user.txt")) save(Path.Combine("lists", Path.GetFileName(f)));
                save(@"lists\ipset-all.txt");
                save(@"lists\ipset-all.txt.backup");
                save(@"utils\game_filter.enabled");
                foreach (string f in FilesIn(Bin, "ACTIVE_*.bin")) save(Path.Combine("bin", Path.GetFileName(f)));
                var flags = new HashSet<string>(FilesIn(Utils, "*.enabled").Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);

                // стратегии, которых больше нет в релизе, не удаляем, а переносим в old-strategies\v<старая версия>
                var newBats = new HashSet<string>(Directory.GetFiles(src, "*.bat").Select(f => Path.GetFileName(f)), StringComparer.OrdinalIgnoreCase);
                string oldVer = LocalVersion;
                string archive = Path.Combine(Root, "old-strategies", "v" + (oldVer == "" ? "unknown" : oldVer));
                foreach (string f in Directory.GetFiles(Root, "general*.bat"))
                {
                    if (newBats.Contains(Path.GetFileName(f))) continue;
                    // свои стратегии (созданные в программе) остаются на месте
                    try { if (File.ReadAllText(f).Contains(":: ZapretGUI-custom")) continue; } catch { }
                    Directory.CreateDirectory(archive);
                    File.Move(f, FreeName(archive, Path.GetFileName(f)));
                }

                CopyDir(src, Root);

                foreach (var kv in keep) File.WriteAllBytes(Path.Combine(Root, kv.Key), kv.Value);
                foreach (string f in FilesIn(Utils, "*.enabled"))
                    if (!flags.Contains(Path.GetFileName(f))) File.Delete(f);
                ipsetCache = null;
            }
            finally { try { Directory.Delete(tmp, true); } catch { } }
        }

        // Путь в папке dir, не занятый другим файлом: «name.bat», «name (2).bat», «name (3).bat»…
        static string FreeName(string dir, string fileName)
        {
            string target = Path.Combine(dir, fileName);
            string stem = Path.GetFileNameWithoutExtension(fileName), ext = Path.GetExtension(fileName);
            for (int n = 2; File.Exists(target); n++) target = Path.Combine(dir, stem + " (" + n + ")" + ext);
            return target;
        }

        static IEnumerable<string> FilesIn(string dir, string pattern)
        {
            return Directory.Exists(dir) ? Directory.GetFiles(dir, pattern) : new string[0];
        }

        static string FindRootIn(string dir, int depth)
        {
            if (File.Exists(Path.Combine(dir, "service.bat"))) return dir;
            if (depth == 0) return null;
            foreach (string d in Directory.GetDirectories(dir))
            {
                string r = FindRootIn(d, depth - 1);
                if (r != null) return r;
            }
            return null;
        }

        static void CopyDir(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (string f in Directory.GetFiles(src))
            {
                string target = Path.Combine(dst, Path.GetFileName(f));
                for (int attempt = 0; ; attempt++)
                {
                    try { File.Copy(f, target, true); break; }
                    catch (IOException)
                    {
                        if (attempt >= 10) throw new Exception(L.T("Файл занят: ", "File is in use: ") + target);
                        Thread.Sleep(500);
                    }
                }
            }
            foreach (string d in Directory.GetDirectories(src))
                CopyDir(d, Path.Combine(dst, Path.GetFileName(d)));
        }

        internal static string Download(string url, int timeout)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            req.UserAgent = "ZapretGUI";
            req.Headers["Cache-Control"] = "no-cache";
            using (var resp = req.GetResponse())
            using (var r = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return r.ReadToEnd();
        }

        // ---------- WinAPI ----------

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr OpenSCManager(string machine, string db, uint access);

        [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern IntPtr CreateService(IntPtr scm, string name, string display, uint access, uint type,
            uint start, uint error, string path, string group, IntPtr tag, string deps, string user, string pwd);

        [DllImport("advapi32.dll", SetLastError = true)]
        static extern bool CloseServiceHandle(IntPtr h);
    }
}
