// Вспомогательные службы: язык, проверка доступности сайтов, автозапуск программы.
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace ZapretGui
{
    // Перевод: L.T("русский", "english").
    static class L
    {
        public static bool En;

        public static void Init()
        {
            string def = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? "ru" : "en";
            En = Prefs.Get("Lang", def) == "en";
        }

        public static string T(string ru, string en) { return En ? en : ru; }
    }

    // Проверка, открываются ли YouTube и Discord (как в utils\test zapret.ps1).
    static class NetCheck
    {
        // Третий адрес YouTube — сервер, с которого вам на самом деле идёт видео (rrN---sn-….googlevideo.com).
        // Он берётся со страницы видео (раз в 6 часов); пока неизвестен — общий redirector.googlevideo.com.
        public const string Redirector = "https://redirector.googlevideo.com";
        public static readonly string[] YouTube = { "https://www.youtube.com", "https://i.ytimg.com", Redirector };
        public static readonly string[] Discord = { "https://discord.com", "https://gateway.discord.gg", "https://cdn.discordapp.com" };
        public static int Max { get { return YouTube.Length + Discord.Length; } }

        static NetCheck()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.DefaultConnectionLimit = 32;
        }

        // Картинка-превью YouTube (~65 КБ) для замера скорости.
        public const string SpeedUrl = "https://i.ytimg.com/vi/dQw4w9WgXcQ/maxresdefault.jpg";
        // Для подбора — три картинки разного размера (65, 180 и 80 КБ), берётся середина: одна картинка слишком случайна.
        public static readonly string[] SpeedUrls = {
            SpeedUrl, "https://i.ytimg.com/vi/9bZkp7q19f0/maxresdefault.jpg", "https://i.ytimg.com/vi/kJQP7kiw5Fk/maxresdefault.jpg" };

        // ---------- сервер видео ----------
        public const string WatchUrl = "https://www.youtube.com/watch?v=dQw4w9WgXcQ";
        static readonly System.Text.RegularExpressions.Regex NodeRx =
            new System.Text.RegularExpressions.Regex(@"rr\d{1,2}---sn-[a-z0-9\-]{4,40}\.googlevideo\.com");
        static DateTime nodeAt = DateTime.MinValue, nodeTry = DateTime.MinValue;
        static int nodeBusy;
        public static string VideoNode { get; private set; }   // null — неизвестен

        public static string VideoNodeUrl(string host) { return "https://" + host + "/generate_204"; }

        // Имя сервера видео из текста страницы YouTube или null.
        public static string FindVideoNode(string html)
        {
            if (string.IsNullOrEmpty(html)) return null;
            var m = NodeRx.Match(html);
            return m.Success ? m.Value : null;
        }

        // В фоне узнать сервер видео, если он неизвестен или устарел (раз в 6 часов; после неудачи — через 30 минут).
        public static void RefreshVideoNodeSoon()
        {
            DateTime now = DateTime.UtcNow;
            if (VideoNode != null && now - nodeAt < TimeSpan.FromHours(6)) return;
            if (now - nodeTry < TimeSpan.FromMinutes(30)) return;
            if (Interlocked.Exchange(ref nodeBusy, 1) == 1) return;
            nodeTry = now;
            new Thread(() =>
            {
                try
                {
                    string host = FindVideoNode(Download(WatchUrl, 15000, 4 * 1024 * 1024));
                    if (host != null && Probe(VideoNodeUrl(host), 5000))
                    {
                        if (host != VideoNode) Journal.Add(L.T("Сервер видео YouTube: ", "YouTube video server: ") + host);
                        VideoNode = host; nodeAt = DateTime.UtcNow;
                        YouTube[2] = VideoNodeUrl(host);
                    }
                }
                catch { }
                finally { Interlocked.Exchange(ref nodeBusy, 0); }
            }) { IsBackground = true }.Start();
        }

        // Текст страницы (не больше max байт) или null.
        static string Download(string url, int timeout, int max)
        {
            try
            {
                var req = Request(url, timeout);
                req.AllowAutoRedirect = true;
                req.Headers["Accept-Language"] = "ru,en;q=0.8";
                using (var resp = req.GetResponse())
                using (var s = resp.GetResponseStream())
                using (var ms = new MemoryStream())
                {
                    var buf = new byte[65536]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0 && ms.Length < max) ms.Write(buf, 0, n);
                    return Encoding.UTF8.GetString(ms.ToArray());
                }
            }
            catch { return null; }
        }

        static HttpWebRequest Request(string url, int timeout)
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeout;
            req.ReadWriteTimeout = timeout;
            req.KeepAlive = false;           // каждый раз новое соединение — так проверяется именно стратегия
            req.AllowAutoRedirect = false;
            req.Proxy = null;                // напрямую: проверяем именно zapret, а не системный прокси
            req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
            return req;
        }

        // Время ответа в мс или -1. Любой HTTP-ответ (даже 404) означает, что соединение и TLS прошли.
        public static long ProbeMs(string url, int timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try { using (Request(url, timeout).GetResponse()) return sw.ElapsedMilliseconds; }
            catch (WebException ex) { return ex.Response != null ? sw.ElapsedMilliseconds : -1; }
            catch { return -1; }
        }

        public static bool Probe(string url, int timeout) { return ProbeMs(url, timeout) >= 0; }

        // Скорость скачивания в КБ/с (0 — не удалось).
        public static double SpeedKBps(string url, int timeout)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                long total = 0;
                var buf = new byte[16384];
                using (var resp = Request(url, timeout).GetResponse())
                using (var s = resp.GetResponseStream())
                {
                    int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0 && total < 4 * 1024 * 1024) total += n;
                }
                double sec = Math.Max(0.001, sw.Elapsed.TotalSeconds);
                return total < 1024 ? 0 : total / 1024.0 / sec;
            }
            catch { return 0; }
        }

        // Проверяет все адреса параллельно; для каждого — время ответа в мс или -1.
        public static long[] RunTimed(int timeout) { return RunTimed(timeout, AllUrls()); }

        static long[] RunTimed(int timeout, string[] urls)
        {
            var ms = new long[urls.Length];
            for (int i = 0; i < ms.Length; i++) ms[i] = -1;
            var threads = new Thread[urls.Length];
            for (int i = 0; i < urls.Length; i++)
            {
                int idx = i;
                threads[i] = new Thread(() => { ms[idx] = ProbeMs(urls[idx], timeout); }) { IsBackground = true };
                threads[i].Start();
            }
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeout + 2000);
            foreach (var t in threads)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left > 0) t.Join(left);
            }
            return ms;
        }

        // Проверка с перепроверкой: адрес, который не ответил, проверяется ещё раз — одиночный тайм-аут не считается сбоем.
        // fails: «i.ytimg.com~» — ответил со второй попытки, «discord.com!» — не ответил и со второй.
        public static long[] RunChecked(int timeout, out string fails)
        {
            RefreshVideoNodeSoon();
            string[] urls = AllUrls();
            long[] ms = RunTimed(timeout, urls);
            var bad = new System.Collections.Generic.List<int>();
            for (int i = 0; i < ms.Length; i++) if (ms[i] < 0) bad.Add(i);
            var sb = new StringBuilder();
            if (bad.Count > 0 && bad.Count < ms.Length)   // не открылось ничего — перепроверять нечего, это не одиночный сбой
            {
                Thread.Sleep(1000);
                string[] again = bad.Select(i => urls[i]).ToArray();
                long[] ms2 = RunTimed(timeout, again);
                for (int k = 0; k < bad.Count; k++)
                {
                    if (ms2[k] >= 0) ms[bad[k]] = ms2[k];
                    if (sb.Length > 0) sb.Append(',');
                    sb.Append(HostOf(urls[bad[k]])).Append(ms2[k] >= 0 ? "~" : "!");
                }
            }
            else
                foreach (int i in bad) { if (sb.Length > 0) sb.Append(','); sb.Append(HostOf(urls[i])).Append('!'); }
            fails = sb.ToString();
            return ms;
        }

        static string HostOf(string url) { try { return new Uri(url).Host; } catch { return url; } }

        static string[] AllUrls()
        {
            var urls = new string[Max];
            YouTube.CopyTo(urls, 0);
            Discord.CopyTo(urls, YouTube.Length);
            return urls;
        }

        // Скорость для подбора: середина из трёх картинок; неудачные пробуются ещё раз. 0 — замер не удался.
        public static double SpeedSample(Func<bool> cancelled)
        {
            var v = new System.Collections.Generic.List<double>();
            foreach (string u in SpeedUrls)
            {
                if (cancelled != null && cancelled()) return 0;
                double s = SpeedKBps(u, 8000);
                if (s <= 0) s = SpeedKBps(u, 8000);
                if (s > 0) v.Add(s);
            }
            if (v.Count < 2) return 0;
            v.Sort();
            return v.Count == 3 ? v[1] : (v[0] + v[1]) / 2;
        }

        // Возвращает число доступных адресов: [YouTube, Discord].
        public static int[] Run(int timeout)
        {
            long[] ms = RunTimed(timeout);
            int yt = 0, dc = 0;
            for (int i = 0; i < ms.Length; i++)
                if (ms[i] >= 0) { if (i < YouTube.Length) yt++; else dc++; }
            return new[] { yt, dc };
        }

        // Оценка качества стратегии: несколько раундов проверки + замер скорости.
        public static void Measure(PickResult r, int rounds, int timeout, Func<bool> cancelled)
        {
            for (int round = 0; round < rounds && !cancelled(); round++)
            {
                long[] ms = RunTimed(timeout);
                int ok = 0;
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i] < 0) continue;
                    ok++;
                    if (i < YouTube.Length) r.YtOk++; else r.DcOk++;
                    r.TotalMs += ms[i]; r.Samples++;
                }
                r.Rounds++;
                if (round == 0 && ok == 0) break; // ничего не открылось — дальше не тратим время
            }
            if (r.YtOk + r.DcOk > 0 && !cancelled()) r.SpeedKBps = SpeedSample(cancelled);
        }
    }

    // Самообновление ZapretGUI из релизов на GitHub.
    // Разбор JSON (ответы GitHub и DNS) настоящим разборщиком из .NET, а не поиском по тексту.
    static class Json
    {
        public static System.Collections.Generic.Dictionary<string, object> Obj(string text)
        {
            var js = new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength = 32 * 1024 * 1024 };
            return js.Deserialize<System.Collections.Generic.Dictionary<string, object>>(text);
        }

        public static string Str(System.Collections.Generic.Dictionary<string, object> o, string key)
        {
            object v;
            return o != null && o.TryGetValue(key, out v) && v != null ? v.ToString() : null;
        }

        public static System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>> Arr(System.Collections.Generic.Dictionary<string, object> o, string key)
        {
            var res = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
            object v;
            if (o == null || !o.TryGetValue(key, out v)) return res;
            var list = v as System.Collections.IEnumerable;
            if (list == null || v is string) return res;
            foreach (object item in list)
            {
                var d = item as System.Collections.Generic.Dictionary<string, object>;
                if (d != null) res.Add(d);
            }
            return res;
        }
    }

    static class SelfUpdate
    {
        const string Api = "https://api.github.com/repos/ryxIbtw/ZapretGUI/releases/latest";
        public const string ReleasesUrl = "https://github.com/ryxIbtw/ZapretGUI/releases/latest";

        public static Version Current
        {
            get { var v = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version; return new Version(v.Major, v.Minor, Math.Max(0, v.Build)); }
        }

        public static Version Parse(string tag)
        {
            var m = System.Text.RegularExpressions.Regex.Match(tag ?? "", @"(\d+)\.(\d+)(?:\.(\d+))?");
            if (!m.Success) return new Version(0, 0, 0);
            return new Version(int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value), m.Groups[3].Success ? int.Parse(m.Groups[3].Value) : 0);
        }

        // Последний релиз: номер версии, ссылка на ZapretGUI.exe и на файл с его SHA256 (null, если в релизе его нет).
        // Бросает исключение, если GitHub недоступен.
        public static Version Latest(out string exeUrl, out string shaUrl)
        {
            var rel = Json.Obj(Zapret.Download(Api, 10000));
            string tag = Json.Str(rel, "tag_name");
            exeUrl = null; shaUrl = null;
            foreach (var a in Json.Arr(rel, "assets"))
            {
                string name = Json.Str(a, "name"), url = Json.Str(a, "browser_download_url");
                if (name == "ZapretGUI.exe") exeUrl = url;
                else if (name == "ZapretGUI.exe.sha256") shaUrl = url;
            }
            if (tag == null || exeUrl == null) throw new Exception(L.T("В релизе не найден ZapretGUI.exe", "ZapretGUI.exe not found in the release"));
            return Parse(tag);
        }

        // Описание релиза версии v с GitHub (Markdown). null — не удалось получить.
        public static string ReleaseNotes(Version v)
        {
            try { return Json.Str(Json.Obj(Zapret.Download("https://api.github.com/repos/ryxIbtw/ZapretGUI/releases/tags/v" + v.ToString(3), 10000)), "body"); }
            catch { return null; }
        }

        // Раздел версии v из встроенного списка изменений (CHANGELOG.md вшит в программу при сборке). null — раздела нет.
        public static string BuiltInNotes(Version v)
        {
            try
            {
                using (var s = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("CHANGELOG.md"))
                {
                    if (s == null) return null;
                    string all = new StreamReader(s, Encoding.UTF8).ReadToEnd().Replace("\r", "");
                    int start = all.IndexOf("\n## " + v.ToString(3));
                    if (start < 0) return null;
                    start = all.IndexOf('\n', start + 1) + 1;
                    int end = all.IndexOf("\n## ", start);
                    string part = (end < 0 ? all.Substring(start) : all.Substring(start, end - start)).Trim();
                    return part == "" ? null : part;
                }
            }
            catch { return null; }
        }

        // Прошлая версия программы хранится рядом (ZapretGUI.exe.prev), чтобы можно было вернуться.
        public static string PrevPath { get { return Application.ExecutablePath + ".prev"; } }

        public static string PrevVersion()
        {
            try
            {
                if (!File.Exists(PrevPath)) return null;
                var fv = System.Diagnostics.FileVersionInfo.GetVersionInfo(PrevPath);
                return fv.FileMajorPart + "." + fv.FileMinorPart + "." + fv.FileBuildPart;
            }
            catch { return null; }
        }

        // Возврат: текущий файл становится .old (после перезапуска — .prev, так что можно вернуться и обратно).
        public static void Rollback()
        {
            string exe = Application.ExecutablePath, old = exe + ".old";
            if (!File.Exists(PrevPath)) throw new Exception(L.T("Прошлая версия не сохранена", "No previous version saved"));
            if (File.Exists(old)) File.Delete(old);
            File.Move(exe, old);
            try { File.Move(PrevPath, exe); }
            catch { File.Move(old, exe); throw; }
        }
        // SHA256 в виде 64 шестнадцатеричных символов в нижнем регистре.
        public static string Sha256(byte[] data)
        {
            using (var h = System.Security.Cryptography.SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "").ToLowerInvariant();
        }

        public static string FileSha256(string path)
        {
            try { return Sha256(File.ReadAllBytes(path)); } catch { return null; }
        }

        // Скачивает новую версию и, если в релизе есть контрольная сумма, сверяет её до установки.
        public static void DownloadAndInstall(string exePath, string exeUrl, string shaUrl)
        {
            string expected = null;
            if (shaUrl != null)
            {
                var m = System.Text.RegularExpressions.Regex.Match(Zapret.Download(shaUrl, 10000), "[0-9a-fA-F]{64}");
                if (!m.Success) throw new Exception(L.T("Файл контрольной суммы в релизе повреждён — обновление отменено", "The checksum file in the release is broken — update cancelled"));
                expected = m.Value.ToLowerInvariant();
            }
            byte[] data = DownloadBytes(exeUrl);
            if (expected != null && Sha256(data) != expected)
                throw new Exception(L.T("Контрольная сумма скачанного файла не совпала — обновление отменено", "Checksum of the downloaded file does not match — update cancelled"));
            Install(exePath, data);
        }

        public static byte[] DownloadBytes(string url)
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072;
            using (var wc = new WebClient())
            {
                wc.Headers["User-Agent"] = "ZapretGUI";
                return wc.DownloadData(url);
            }
        }

        // Кладёт новый exe на место текущего: текущий переименовывается в .old (запущенный файл переименовать можно).
        public static void Install(string exePath, byte[] data)
        {
            if (data == null || data.Length < 32 * 1024 || data[0] != (byte)'M' || data[1] != (byte)'Z')
                throw new Exception(L.T("Скачанный файл не похож на программу — обновление отменено", "The downloaded file is not a program — update cancelled"));
            string fresh = exePath + ".new", old = exePath + ".old";
            File.WriteAllBytes(fresh, data);
            if (File.Exists(old)) File.Delete(old);   // остаток прошлого обновления
            File.Move(exePath, old);
            try { File.Move(fresh, exePath); }
            catch { File.Move(old, exePath); throw; }
        }

        // Убирает файлы, оставшиеся от прошлого обновления.
        public static void CleanupOld()
        {
            string exe = Application.ExecutablePath;
            try { if (File.Exists(exe + ".new")) File.Delete(exe + ".new"); } catch { }
            // прошлая версия не удаляется, а остаётся как .prev — для «Вернуть прошлую версию»
            try
            {
                if (File.Exists(exe + ".old"))
                {
                    if (File.Exists(PrevPath)) File.Delete(PrevPath);
                    File.Move(exe + ".old", PrevPath);
                }
            }
            catch { }
        }
    }

    // Запуск самой программы при входе в Windows (через планировщик — без запроса UAC).
    static class GuiAutostart
    {
        const string TaskName = "ZapretGUI";

        public static bool Enabled { get { return Zapret.Run("schtasks.exe", "/Query /TN " + TaskName) == 0; } }

        public static void Set(bool on)
        {
            if (!on) { Zapret.Run("schtasks.exe", "/Delete /TN " + TaskName + " /F"); return; }
            string exe = Application.ExecutablePath;
            string user = SecurityElement.Escape(WindowsIdentity.GetCurrent().Name);
            string xml =
                "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\r\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\r\n" +
                "  <RegistrationInfo><Description>Zapret GUI</Description></RegistrationInfo>\r\n" +
                "  <Triggers><LogonTrigger><Enabled>true</Enabled><UserId>" + user + "</UserId><Delay>PT5S</Delay></LogonTrigger></Triggers>\r\n" +
                "  <Principals><Principal id=\"Author\"><UserId>" + user + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>\r\n" +
                "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
                "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><Priority>7</Priority></Settings>\r\n" +
                "  <Actions Context=\"Author\"><Exec><Command>" + SecurityElement.Escape(exe) + "</Command><Arguments>--tray</Arguments>" +
                "<WorkingDirectory>" + SecurityElement.Escape(Path.GetDirectoryName(exe)) + "</WorkingDirectory></Exec></Actions>\r\n" +
                "</Task>\r\n";
            string f = Path.Combine(Path.GetTempPath(), "zapretgui-task.xml");
            File.WriteAllText(f, xml, Encoding.Unicode);
            int code = Zapret.Run("schtasks.exe", "/Create /TN " + TaskName + " /XML \"" + f + "\" /F");
            try { File.Delete(f); } catch { }
            if (code != 0) throw new Exception(L.T("Не удалось создать задачу автозапуска", "Could not create the autostart task"));
        }
    }
}
