// Пользовательские списки lists\*-user.txt: чтение, запись и проверка введённых доменов/IP.
// Комментарии и заглушки (domain.example.abc, 203.0.113.113/32) сохраняются как были — service.bat не любит пустые файлы.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretGui
{
    static class ListFile
    {
        public static bool IsPlaceholder(string s)
        {
            return s.Equals(Zapret.DomainPlaceholder, StringComparison.OrdinalIgnoreCase) || s == Zapret.IpsetPlaceholder;
        }

        // keep — комментарии и заглушки, items — записи (без повторов).
        public static void Load(string path, List<string> keep, List<string> items)
        {
            if (!File.Exists(path)) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string t = raw.Trim();
                if (t == "") continue;
                if (t.StartsWith("#") || IsPlaceholder(t)) { keep.Add(t); continue; }
                if (seen.Add(t)) items.Add(t);
            }
        }

        public static void Write(string path, List<string> keep, List<string> items, bool ipList)
        {
            var lines = new List<string>(keep);
            lines.AddRange(items);
            // файл не должен остаться пустым: если записей нет, оставляем заглушку
            if (!lines.Any(l => !l.StartsWith("#"))) lines.Add(ipList ? Zapret.IpsetPlaceholder : Zapret.DomainPlaceholder);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
        }

        // Приводит ввод к виду, который понимает zapret: без http://, пути и порта; кириллические домены — в punycode.
        // null — не похоже на домен/IP (problem — что именно не подошло).
        public static string Normalize(string s, bool ipList, out string problem)
        {
            problem = null;
            s = s.Trim().Trim('"', '\'');
            if (ipList)
            {
                string ipPart = s, prefix = null;
                int slash = s.IndexOf('/');
                if (slash >= 0) { ipPart = s.Substring(0, slash); prefix = s.Substring(slash + 1); }
                IPAddress addr;
                int bits;
                int max = ipPart.Contains(":") ? 128 : 32;
                if (!IPAddress.TryParse(ipPart, out addr) || (!ipPart.Contains(":") && ipPart.Count(c => c == '.') != 3) ||
                    (prefix != null && (!int.TryParse(prefix, out bits) || bits < 0 || bits > max)))
                { problem = s; return null; }
                return addr.ToString() + (prefix != null ? "/" + prefix : "");
            }
            s = s.ToLowerInvariant();
            s = Regex.Replace(s, @"^[a-z]+://", "");
            int cut = s.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            int at = s.LastIndexOf('@');
            if (at >= 0) s = s.Substring(at + 1);
            int colon = s.IndexOf(':');
            if (colon >= 0) s = s.Substring(0, colon);
            s = s.Trim('.');
            if (s.StartsWith("*.")) s = s.Substring(2);
            try { s = new IdnMapping().GetAscii(s); } catch { problem = s; return null; }
            if (!Regex.IsMatch(s, @"^([a-z0-9_]([a-z0-9_-]*[a-z0-9_])?\.)+[a-z0-9-]{2,}$")) { problem = s; return null; }
            return s;
        }

        // Добавляет записи из строки ввода (через пробел, запятую, точку с запятой).
        public static void AddFrom(string input, bool ipList, List<string> items, out int added, out int dup, out List<string> bad)
        {
            bad = new List<string>(); added = 0; dup = 0;
            foreach (string part in Regex.Split(input, @"[\s,;]+"))
            {
                if (part.Trim() == "") continue;
                string problem;
                string v = Normalize(part, ipList, out problem);
                if (v == null) { bad.Add(problem); continue; }
                if (items.Any(i => i.Equals(v, StringComparison.OrdinalIgnoreCase))) { dup++; continue; }
                items.Add(v);
                added++;
            }
        }
    }
}
