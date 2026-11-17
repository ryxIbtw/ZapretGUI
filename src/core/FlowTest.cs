// Проверка стратегий так же, как «Run Tests» в service.bat (utils\test zapret.ps1 от Flowseal):
//  • обычный режим — сайты из utils\targets.txt, для каждого curl HTTP/1.1, TLS 1.2, TLS 1.3 (таймаут 4 с) и пинг;
//  • режим DPI — серверы из списка hyperion-cs/dpi-checkers, POST 64 КБ, признак обрыва на 16–20 КБ;
//    на время режима DPI IPSet переключается в «все IP» (те же файлы, что у скрипта Flowseal).
// Лучшая стратегия — больше всего успешных проверок, при равенстве больше ответов на пинг.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ZapretGui
{
    class TestTarget { public string Name, Url, Ping; }
    class DpiTarget { public string Id, Provider, Country, Host; }

    static class FlowTest
    {
        public const int StdTimeout = 4, DpiTimeout = 5, DpiRange = 65536;
        const string DpiSuiteUrl = "https://hyperion-cs.github.io/dpi-checkers/ru/tcp-16-20/suite.v2.json";
        static readonly int MaxParallel = Math.Min(16, Math.Max(8, Environment.ProcessorCount * 2));
        static readonly string[][] Tests = {
            new[] { "HTTP", "--http1.1" },
            new[] { "TLS1.2", "--tlsv1.2 --tls-max 1.2" },
            new[] { "TLS1.3", "--tlsv1.3 --tls-max 1.3" },
        };

        public static string Curl
        {
            get
            {
                string sys = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe");
                return File.Exists(sys) ? sys : "curl.exe";
            }
        }

        public static bool CurlAvailable
        {
            get
            {
                if (File.Exists(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "curl.exe"))) return true;
                try { using (var p = Process.Start(new ProcessStartInfo("curl.exe", "--version") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true })) { p.WaitForExit(3000); return true; } }
                catch { return false; }
            }
        }

        // ---------- список сайтов ----------

        // utils\targets.txt: КЛЮЧ = "https://..." или КЛЮЧ = "PING:1.2.3.4"; если файла нет — те же сайты, что у Flowseal.
        public static List<TestTarget> LoadTargets(Zapret z)
        {
            var raw = new List<KeyValuePair<string, string>>();
            string f = Path.Combine(z.Utils, "targets.txt");
            try
            {
                if (File.Exists(f))
                    foreach (string line in File.ReadAllLines(f))
                    {
                        var m = Regex.Match(line, "^\\s*(\\w+)\\s*=\\s*\"(.+)\"\\s*$");
                        if (!m.Success) continue;
                        raw.RemoveAll(kv => kv.Key == m.Groups[1].Value);
                        raw.Add(new KeyValuePair<string, string>(m.Groups[1].Value, m.Groups[2].Value));
                    }
            }
            catch { }
            if (raw.Count == 0)
                foreach (var d in new[] {
                    "Discord Main|https://discord.com", "Discord Gateway|https://gateway.discord.gg", "Discord CDN|https://cdn.discordapp.com",
                    "Discord Updates|https://updates.discord.com", "YouTube Web|https://www.youtube.com", "YouTube Short|https://youtu.be",
                    "YouTube Image|https://i.ytimg.com", "YouTube Video Redirect|https://redirector.googlevideo.com", "Google Main|https://www.google.com",
                    "Google Gstatic|https://www.gstatic.com", "Cloudflare Web|https://www.cloudflare.com", "Cloudflare CDN|https://cdnjs.cloudflare.com",
                    "Cloudflare DNS 1.1.1.1|PING:1.1.1.1", "Cloudflare DNS 1.0.0.1|PING:1.0.0.1", "Google DNS 8.8.8.8|PING:8.8.8.8",
                    "Google DNS 8.8.4.4|PING:8.8.4.4", "Quad9 DNS 9.9.9.9|PING:9.9.9.9" })
                {
                    string[] p = d.Split('|');
                    raw.Add(new KeyValuePair<string, string>(p[0], p[1]));
                }
            var list = new List<TestTarget>();
            foreach (var kv in raw)
            {
                if (kv.Value.StartsWith("PING:", StringComparison.OrdinalIgnoreCase))
                    list.Add(new TestTarget { Name = kv.Key, Ping = Regex.Replace(kv.Value, "^PING:\\s*", "", RegexOptions.IgnoreCase) });
                else
                    list.Add(new TestTarget { Name = kv.Key, Url = kv.Value, Ping = Regex.Replace(Regex.Replace(kv.Value, "^https?://", ""), "/.*$", "") });
            }
            return list;
        }

        public static bool TargetsFromFile(Zapret z)
        {
            try { return File.Exists(Path.Combine(z.Utils, "targets.txt")) && LoadTargets(z).Count > 0 && File.ReadAllLines(Path.Combine(z.Utils, "targets.txt")).Any(l => Regex.IsMatch(l, "^\\s*\\w+\\s*=\\s*\".+\"\\s*$")); }
            catch { return false; }
        }

        // Список серверов для режима DPI (скачивается, как у Flowseal). Пустой — не удалось скачать.
        public static List<DpiTarget> LoadDpiSuite()
        {
            var list = new List<DpiTarget>();
            string json;
            try { json = Zapret.Download(DpiSuiteUrl, DpiTimeout * 1000); } catch { return list; }
            foreach (Match m in Regex.Matches(json, "\\{[^{}]*\\}"))
            {
                Func<string, string> get = key => { var x = Regex.Match(m.Value, "\"" + key + "\"\\s*:\\s*\"((?:[^\"\\\\]|\\\\.)*)\""); return x.Success ? Regex.Unescape(x.Groups[1].Value) : null; };
                string host = get("host");
                if (string.IsNullOrEmpty(host)) continue;
                list.Add(new DpiTarget { Id = get("id") ?? host, Provider = get("provider") ?? "", Country = get("country") ?? "", Host = host });
            }
            return list;
        }

        // ---------- запуск curl ----------

        static int RunCurl(string args, int timeoutSec, out string stdout, out string stderr)
        {
            stdout = ""; stderr = "";
            try
            {
                var psi = new ProcessStartInfo(Curl, args)
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
                };
                using (var p = Process.Start(psi))
                {
                    var err = p.StandardError.ReadToEndAsync();
                    string o = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit((timeoutSec * 3 + 5) * 1000)) { try { p.Kill(); } catch { } return -1; }
                    stdout = o.Trim();
                    stderr = err.Result.Trim();
                    return p.ExitCode;
                }
            }
            catch (Exception ex) { stderr = ex.Message; return -1; }
        }

        static readonly Regex SslRx = new Regex("Could not resolve host|certificate|SSL certificate problem|self[- ]?signed|certificate verify failed|unable to get local issuer certificate", RegexOptions.IgnoreCase);
        static readonly Regex UnsupRx = new Regex("does not support|not supported|protocol\\s+'?.+'?\\s+not\\s+supported|unsupported protocol|TLS.*not supported|Unrecognized option|Unknown option|unsupported option|unsupported feature|schannel", RegexOptions.IgnoreCase);
        static readonly Regex UnsupDpiRx = new Regex("not supported|does not support|protocol\\s+'.+'\\s+not\\s+supported|protocol\\s+.+\\s+not\\s+supported|unsupported protocol|TLS.not supported|Unrecognized option|Unknown option|unsupported option|unsupported feature|schannel|SSL", RegexOptions.IgnoreCase);

        // ---------- обычный режим ----------

        public static void RunStandard(PickResult r, List<TestTarget> targets, Func<bool> cancelled)
        {
            r.Mode = "std";
            var lines = new string[targets.Count];
            int ok = 0, err = 0, unsup = 0, pingOk = 0, pingFail = 0, total = 0;
            object sync = new object();
            Parallel.For(0, targets.Count, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel }, i =>
            {
                var t = targets[i];
                var tokens = new List<string>();
                int lOk = 0, lErr = 0, lUnsup = 0;
                bool noCurl = !CurlAvailable;   // на старых Windows 10 curl.exe нет — тогда одна проверка HTTPS средствами .NET
                if (t.Url != null && noCurl && !cancelled())
                {
                    long ms = NetCheck.ProbeMs(t.Url, StdTimeout * 1000);
                    if (ms >= 0) { tokens.Add("HTTPS:OK   "); lOk++; } else { tokens.Add("HTTPS:ERROR"); lErr++; }
                }
                if (t.Url != null && !noCurl)
                    foreach (var test in Tests)
                    {
                        if (cancelled()) break;
                        string so, se;
                        int code = RunCurl("-I -s -m " + StdTimeout + " --connect-timeout " + Math.Min(2, StdTimeout) + " -o NUL -w %{http_code} --show-error " + test[1] + " \"" + t.Url + "\"",
                            StdTimeout, out so, out se);
                        string tok;
                        if (SslRx.IsMatch(se)) { tok = test[0] + ":SSL  "; lErr++; }
                        else if (code == 35 || UnsupRx.IsMatch(se)) { tok = test[0] + ":UNSUP"; lUnsup++; }
                        else if (code == 0) { tok = test[0] + ":OK   "; lOk++; }
                        else { tok = test[0] + ":ERROR"; lErr++; }
                        tokens.Add(tok);
                    }
                string ping = "n/a";
                if (!string.IsNullOrEmpty(t.Ping))
                {
                    try
                    {
                        using (var pg = new Ping())
                        {
                            var reply = pg.Send(t.Ping, 1000);
                            ping = reply.Status == IPStatus.Success ? reply.RoundtripTime + " ms" : "Timeout";
                        }
                    }
                    catch { ping = "Timeout"; }
                }
                lock (sync)
                {
                    ok += lOk; err += lErr; unsup += lUnsup;
                    if (t.Url != null) total += noCurl ? 1 : Tests.Length;
                    // как у Flowseal: всё, что не «Timeout» и не «n/a», — пинг прошёл; «n/a» тоже считается неудачей
                    if (ping != "Timeout" && ping != "n/a") pingOk++; else pingFail++;
                }
                lines[i] = t.Url != null ? "  " + t.Name + " : " + string.Join(" ", tokens) + " | Ping: " + ping : "  " + t.Name + " : | Ping: " + ping;
            });
            r.Ok = ok; r.Err = err; r.Unsup = unsup; r.PingOk = pingOk; r.PingFail = pingFail; r.Total = total;
            r.Details = lines.Where(x => x != null).ToList();
        }

        // ---------- режим DPI (обрыв на 16–20 КБ) ----------

        public static string MakePayload()
        {
            var data = new byte[DpiRange];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(data);
            string f = Path.Combine(Path.GetTempPath(), "zapretgui-dpi-" + Guid.NewGuid().ToString("N") + ".bin");
            File.WriteAllBytes(f, data);
            return f;
        }

        public static void RunDpi(PickResult r, List<DpiTarget> targets, string payloadFile, Func<bool> cancelled)
        {
            r.Mode = "dpi";
            var lines = new List<string>[targets.Count];
            int ok = 0, fail = 0, unsup = 0, blocked = 0, total = 0;
            object sync = new object();
            var rx = new Regex("^(?<code>\\d{3})\\s+(?<up>\\d+)\\s+(?<down>\\d+)\\s+(?<time>[\\d\\.]+)$");
            Parallel.For(0, targets.Count, new ParallelOptions { MaxDegreeOfParallelism = MaxParallel }, i =>
            {
                var t = targets[i];
                var my = new List<string>();
                my.Add(string.IsNullOrEmpty(t.Country) ? "  Target: " + t.Id + " (" + t.Provider + ")" : "  Target: [" + t.Country + "] " + t.Id + " (" + t.Provider + ")");
                int lOk = 0, lFail = 0, lUnsup = 0, lBlocked = 0, lTotal = 0;
                foreach (var test in Tests)
                {
                    if (cancelled()) break;
                    string so, se;
                    int exit = RunCurl("--range 0-" + (DpiRange - 1) + " -m " + DpiTimeout + " --connect-timeout " + Math.Min(3, DpiTimeout) +
                        " -w \"%{http_code} %{size_upload} %{size_download} %{time_total}\" -o NUL -X POST --data-binary \"@" + payloadFile + "\" -s " +
                        test[1] + " \"https://" + t.Host + "\"", DpiTimeout, out so, out se);
                    string text = (so + (se.Length > 0 ? " " + se : "")).Trim();
                    string code = "NA"; long up = 0, down = 0; double time = -1;
                    var m = rx.Match(so);
                    if (m.Success)
                    {
                        code = m.Groups["code"].Value;
                        up = long.Parse(m.Groups["up"].Value); down = long.Parse(m.Groups["down"].Value);
                        double.TryParse(m.Groups["time"].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out time);
                    }
                    else if (exit == 35 || UnsupDpiRx.IsMatch(text)) code = "UNSUP";
                    else if (text.Length > 0) code = "ERR";

                    string status = "OK";
                    if (code == "UNSUP") status = "UNSUPPORTED";
                    else if (exit != 0 || code == "ERR" || code == "NA") status = "FAIL";
                    if (up > 0 && down == 0 && time >= DpiTimeout && exit != 0) status = "LIKELY_BLOCKED";

                    lTotal++;
                    if (status == "OK") lOk++; else if (status == "FAIL") lFail++; else if (status == "UNSUPPORTED") lUnsup++; else lBlocked++;
                    my.Add("    " + test[0] + ": code=" + code + "  up=" + Math.Round(up / 1024.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) +
                        " KB  down=" + Math.Round(down / 1024.0, 1).ToString(System.Globalization.CultureInfo.InvariantCulture) + " KB  time=" +
                        time.ToString(System.Globalization.CultureInfo.InvariantCulture) + "s  status=" + status);
                }
                lock (sync) { ok += lOk; fail += lFail; unsup += lUnsup; blocked += lBlocked; total += lTotal; }
                lines[i] = my;
            });
            r.Ok = ok; r.Err = fail; r.Unsup = unsup; r.Blocked = blocked; r.Total = total;
            r.Details = lines.Where(x => x != null).SelectMany(x => x).ToList();
        }

        // ---------- IPSet на время режима DPI (те же файлы, что у скрипта Flowseal) ----------

        static string IpsetFile(Zapret z) { return Path.Combine(z.Lists, "ipset-all.txt"); }
        static string IpsetBackup(Zapret z) { return Path.Combine(z.Lists, "ipset-all.test-backup.txt"); }
        static string IpsetFlag(Zapret z) { return Path.Combine(z.Root, "ipset_switched.flag"); }

        // Прошлый тест прервался посреди режима DPI — вернуть IPSet (как делает скрипт Flowseal при запуске).
        public static void RestoreLeftoverIpset(Zapret z)
        {
            if (!File.Exists(IpsetFlag(z))) return;
            RestoreIpset(z);
        }

        // true — IPSet переключён в «все IP» и потом его надо вернуть.
        public static bool SwitchIpsetToAny(Zapret z)
        {
            if (z.IpsetMode() == "any") return false;
            if (File.Exists(IpsetFile(z))) File.Copy(IpsetFile(z), IpsetBackup(z), true);
            else File.WriteAllText(IpsetBackup(z), "", new UTF8Encoding(false));
            File.WriteAllText(IpsetFile(z), "", new UTF8Encoding(false));
            File.WriteAllText(IpsetFlag(z), "", new UTF8Encoding(false));
            return true;
        }

        public static void RestoreIpset(Zapret z)
        {
            try
            {
                if (File.Exists(IpsetBackup(z)))
                {
                    File.Copy(IpsetBackup(z), IpsetFile(z), true);
                    File.Delete(IpsetBackup(z));
                }
                if (File.Exists(IpsetFlag(z))) File.Delete(IpsetFlag(z));
            }
            catch { }
        }

        // ---------- отчёт (как utils\test results\test_results_*.txt у Flowseal) ----------

        public static string SaveReport(Zapret z, List<PickResult> results, PickResult best)
        {
            try
            {
                string dir = Path.Combine(z.Utils, "test results");
                Directory.CreateDirectory(dir);
                string file = Path.Combine(dir, "test_results_" + DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss") + ".txt");
                var sb = new StringBuilder();
                foreach (var r in results.Where(x => !x.Failed))
                {
                    sb.Append("Config: ").Append(r.Name).Append(".bat (Type: ").Append(r.Mode == "dpi" ? "dpi" : "standard").Append(")\r\n");
                    foreach (string l in r.Details) sb.Append(l).Append("\r\n");
                    sb.Append("\r\n");
                }
                sb.Append("=== ANALYTICS ===\r\n");
                var ok = results.Where(x => !x.Failed).ToList();
                int w = ok.Count == 0 ? 10 : ok.Max(x => (x.Name + ".bat").Length);
                foreach (var r in ok)
                {
                    string name = (r.Name + ".bat").PadRight(w);
                    if (r.Mode == "dpi")
                        sb.AppendFormat("{0} : OK: {1,3}, FAIL: {2,3}, UNSUP: {3,3}, BLOCKED: {4,3}\r\n", name, r.Ok, r.Err, r.Unsup, r.Blocked);
                    else
                        sb.AppendFormat("{0} : HTTP OK: {1,3}, ERR: {2,3}, UNSUP: {3,3}, Ping OK: {4,3}, Fail: {5,3}\r\n", name, r.Ok, r.Err, r.Unsup, r.PingOk, r.PingFail);
                }
                sb.Append("Best strategy: ").Append(best != null ? best.Name + ".bat" : "").Append("\r\n");
                File.WriteAllText(file, sb.ToString(), new UTF8Encoding(true));
                return file;
            }
            catch { return null; }
        }
    }
}
