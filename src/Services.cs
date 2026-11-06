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

        // Любой HTTP-ответ (даже 404) означает, что соединение и TLS прошли.
        public static bool Probe(string url, int timeout)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(url);
                req.Timeout = timeout;
                req.ReadWriteTimeout = timeout;
                req.KeepAlive = false;
                req.AllowAutoRedirect = false;
                req.Proxy = null; // напрямую: проверяем именно zapret, а не системный прокси
                req.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)";
                using (req.GetResponse()) return true;
            }
            catch (WebException ex) { return ex.Response != null; }
            catch { return false; }
        }

        // Возвращает число доступных адресов: [YouTube, Discord].
        public static int[] Run(int timeout)
        {
            var urls = new string[Max];
            YouTube.CopyTo(urls, 0);
            Discord.CopyTo(urls, YouTube.Length);
            var ok = new bool[urls.Length];
            var threads = new Thread[urls.Length];
            for (int i = 0; i < urls.Length; i++)
            {
                int idx = i;
                threads[i] = new Thread(() => { ok[idx] = Probe(urls[idx], timeout); }) { IsBackground = true };
                threads[i].Start();
            }
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeout + 2000);
            foreach (var t in threads)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left > 0) t.Join(left);
            }
            int yt = 0, dc = 0;
            for (int i = 0; i < urls.Length; i++)
                if (ok[i]) { if (i < YouTube.Length) yt++; else dc++; }
            return new[] { yt, dc };
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
