// Вспомогательные службы: язык, проверка доступности сайтов, автозапуск программы.
using System;
using System.Globalization;
using System.IO;
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
        public static readonly string[] YouTube = { "https://www.youtube.com", "https://i.ytimg.com", "https://redirector.googlevideo.com" };
        public static readonly string[] Discord = { "https://discord.com", "https://gateway.discord.gg", "https://cdn.discordapp.com" };
        public static int Max { get { return YouTube.Length + Discord.Length; } }

        static NetCheck()
        {
            ServicePointManager.SecurityProtocol |= (SecurityProtocolType)3072; // TLS 1.2
            ServicePointManager.DefaultConnectionLimit = 32;
        }

        // Картинка-превью YouTube (~65 КБ) для замера скорости.
        public const string SpeedUrl = "https://i.ytimg.com/vi/dQw4w9WgXcQ/maxresdefault.jpg";

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
        public static long[] RunTimed(int timeout)
        {
            var urls = new string[Max];
            YouTube.CopyTo(urls, 0);
            Discord.CopyTo(urls, YouTube.Length);
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
            if (r.YtOk + r.DcOk > 0 && !cancelled()) r.SpeedKBps = SpeedKBps(SpeedUrl, 8000);
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
