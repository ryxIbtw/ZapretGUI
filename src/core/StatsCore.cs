// Статистика доступности: каждая проверка YouTube и Discord (раз в минуту) — одна строка в %APPDATA%\ZapretGUI\stats.tsv.
// Хранятся последние 30 дней. Из записей строятся полосы для окна «Статистика» и сводка по стратегиям.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ZapretGui
{
    class StatRec
    {
        public DateTime T;
        public bool On;            // обход был включён
        public string Strategy = "";
        public int Yt, YtMax, Dc, DcMax;
        public int YtMs = -1, DcMs = -1;   // среднее время ответа открывшихся адресов, -1 — ни один не открылся
        public bool Net = true;    // интернет вообще был (если не открылось ничего, проверяется обычный сайт)
    }

    // Отрезок на полосе. Kind: 0 — нет данных, 1 — обход выключен, 2 — нет интернета, 3 — работает, 4 — частично, 5 — не работает.
    class StatSeg
    {
        public DateTime From, To;
        public int Kind;
        public string Strategy = "";
        public long MsSum; public int MsN;
        public int AvgMs { get { return MsN > 0 ? (int)(MsSum / MsN) : -1; } }
    }

    class StatStrategy
    {
        public string Name;
        public double OnSec, YtSec, YtOkSec, DcSec, DcOkSec;
        public long MsSum; public int MsN;
        public double YtPct { get { return YtSec > 0 ? 100 * YtOkSec / YtSec : -1; } }
        public double DcPct { get { return DcSec > 0 ? 100 * DcOkSec / DcSec : -1; } }
        public int AvgMs { get { return MsN > 0 ? (int)(MsSum / MsN) : -1; } }
    }

    static class Stats
    {
        public const int KeepDays = 30;
        public const int MaxGapSec = 150;     // проверка раз в минуту; дольше — значит, программа не работала
        public static bool Enabled = true;    // в тестовых снимках не пишется
        public static string PathOverride = null;
        static readonly object sync = new object();
        static DateTime lastTrim = DateTime.MinValue;

        public static string FilePath
        {
            get { return PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZapretGUI", "stats.tsv"); }
        }

        public static void Add(StatRec r)
        {
            if (!Enabled || r == null) return;
            string line = string.Join("\t", new[] {
                r.T.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture), r.On ? "1" : "0", (r.Strategy ?? "").Replace("\t", " "),
                r.Yt.ToString(), r.YtMax.ToString(), r.Dc.ToString(), r.DcMax.ToString(), r.YtMs.ToString(), r.DcMs.ToString(), r.Net ? "1" : "0" });
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.AppendAllText(FilePath, line + "\r\n", Encoding.UTF8);
                    if (lastTrim.Date != DateTime.Today) { lastTrim = DateTime.Now; Trim(); }
                }
                catch { }
            }
        }

        // Убирает записи старше KeepDays (раз в день).
        static void Trim()
        {
            var lines = File.ReadAllLines(FilePath, Encoding.UTF8);
            DateTime cut = DateTime.Today.AddDays(-KeepDays);
            int first = 0;
            while (first < lines.Length)
            {
                StatRec r = Parse(lines[first]);
                if (r != null && r.T >= cut) break;
                first++;
            }
            if (first > 0) File.WriteAllLines(FilePath, lines.Skip(first).ToArray(), Encoding.UTF8);
        }

        static StatRec Parse(string line)
        {
            var p = line.Split('\t');
            if (p.Length < 10) return null;
            DateTime t;
            if (!DateTime.TryParseExact(p[0], "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out t)) return null;
            var r = new StatRec { T = t, On = p[1] == "1", Strategy = p[2], Net = p[9] == "1" };
            int.TryParse(p[3], out r.Yt); int.TryParse(p[4], out r.YtMax);
            int.TryParse(p[5], out r.Dc); int.TryParse(p[6], out r.DcMax);
            int.TryParse(p[7], out r.YtMs); int.TryParse(p[8], out r.DcMs);
            return r;
        }

        // Как Load, но дочитывает только новые строки файла (для полосы «Сегодня», которая обновляется раз в минуту).
        static List<StatRec> cache;
        static long cachePos;
        static DateTime cacheFrom = DateTime.MaxValue;
        static string cachePath;
        public static List<StatRec> LoadCached(DateTime from)
        {
            lock (sync)
            {
                try
                {
                    if (!File.Exists(FilePath)) return new List<StatRec>();
                    long len = new FileInfo(FilePath).Length;
                    bool fresh = cache == null || cachePath != FilePath || from < cacheFrom || len < cachePos;
                    if (fresh) { cache = new List<StatRec>(); cachePos = 0; cacheFrom = from; cachePath = FilePath; }
                    if (len > cachePos)
                        using (var fs = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                        {
                            fs.Seek(cachePos, SeekOrigin.Begin);
                            var buf = new byte[len - cachePos];
                            int n = 0, r;
                            while (n < buf.Length && (r = fs.Read(buf, n, buf.Length - n)) > 0) n += r;
                            string text = Encoding.UTF8.GetString(buf, 0, n);
                            int lastNl = text.LastIndexOf('\n');
                            if (lastNl >= 0)
                            {
                                foreach (string l in text.Substring(0, lastNl).Split('\n'))
                                {
                                    StatRec rec = Parse(l.TrimEnd('\r').TrimStart('﻿'));
                                    if (rec != null && rec.T >= cacheFrom) cache.Add(rec);
                                }
                                cachePos += Encoding.UTF8.GetByteCount(text.Substring(0, lastNl + 1));
                            }
                        }
                    return cache.Where(x => x.T >= from).ToList();
                }
                catch { return Load(from); }
            }
        }

        // Записи начиная с from, по времени.
        public static List<StatRec> Load(DateTime from)
        {
            var list = new List<StatRec>();
            lock (sync)
            {
                try
                {
                    if (!File.Exists(FilePath)) return list;
                    foreach (string l in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        StatRec r = Parse(l);
                        if (r != null && r.T >= from) list.Add(r);
                    }
                }
                catch { }
            }
            list.Sort((a, b) => a.T.CompareTo(b.T));
            return list;
        }

        // Состояние сервиса (0 — YouTube, 1 — Discord, 3 — оба: худшее из двух) в одной записи.
        public static int KindOf(StatRec r, int service)
        {
            if (!r.On) return 1;
            if (!r.Net) return 2;
            if (service == 3) return Math.Max(KindOf(r, 0), KindOf(r, 1));
            int ok = service == 0 ? r.Yt : r.Dc, max = service == 0 ? r.YtMax : r.DcMax;
            if (max <= 0) return 0;
            return ok >= max ? 3 : ok > 0 ? 4 : 5;
        }

        // Сколько времени покрывает запись i (до следующей проверки, но не больше MaxGapSec).
        static void Span(List<StatRec> recs, int i, DateTime from, DateTime to, out DateTime a, out DateTime b)
        {
            a = recs[i].T < from ? from : recs[i].T;
            b = recs[i].T.AddSeconds(MaxGapSec);
            if (i + 1 < recs.Count && recs[i + 1].T < b) b = recs[i + 1].T;
            if (b > to) b = to;
            if (b > DateTime.Now) b = DateTime.Now;
        }

        // Отрезки для полосы: service 0 — YouTube, 1 — Discord, 2 — стратегия (Kind 3 = обход включён).
        public static List<StatSeg> Segments(List<StatRec> recs, int service, DateTime from, DateTime to)
        {
            var segs = new List<StatSeg>();
            for (int i = 0; i < recs.Count; i++)
            {
                DateTime a, b;
                Span(recs, i, from, to, out a, out b);
                if (b <= a) continue;
                var r = recs[i];
                int kind = service == 2 ? (r.On ? 3 : 1) : KindOf(r, service);
                string strat = r.On ? r.Strategy : "";
                int ms = service == 0 ? r.YtMs : service == 1 ? r.DcMs : -1;
                StatSeg last = segs.Count > 0 ? segs[segs.Count - 1] : null;
                bool same = last != null && last.Kind == kind && Math.Abs((a - last.To).TotalSeconds) < 1 &&
                            (service != 2 || last.Strategy == strat);
                if (!same) { last = new StatSeg { From = a, Kind = kind, Strategy = strat }; segs.Add(last); }
                last.To = b;
                if (ms >= 0 && kind >= 3) { last.MsSum += ms; last.MsN++; }
            }
            return segs;
        }

        // Доля времени, когда сервис работал полностью (из времени, когда обход был включён и был интернет); -1 — нет данных.
        public static double UpPct(List<StatRec> recs, int service, DateTime from, DateTime to)
        {
            double all = 0, ok = 0;
            for (int i = 0; i < recs.Count; i++)
            {
                DateTime a, b;
                Span(recs, i, from, to, out a, out b);
                if (b <= a) continue;
                int k = KindOf(recs[i], service);
                if (k < 3) continue;
                double s = (b - a).TotalSeconds;
                all += s;
                if (k == 3) ok += s;
            }
            return all > 0 ? 100 * ok / all : -1;
        }

        // Сколько раз сервис переставал работать полностью при включённом обходе.
        // Перебои, между которыми меньше 10 минут, считаются одним сбоем.
        public static int Outages(List<StatRec> recs, int service, DateTime from, DateTime to)
        {
            int n = 0;
            DateTime lastEnd = DateTime.MinValue;
            foreach (var s in Segments(recs, service, from, to))
            {
                if (s.Kind != 4 && s.Kind != 5) continue;
                if ((s.From - lastEnd).TotalMinutes >= 10) n++;
                lastEnd = s.To;
            }
            return n;
        }

        // Сводка по стратегиям, от самой долгой.
        public static List<StatStrategy> ByStrategy(List<StatRec> recs, DateTime from, DateTime to)
        {
            var map = new Dictionary<string, StatStrategy>();
            for (int i = 0; i < recs.Count; i++)
            {
                var r = recs[i];
                if (!r.On || string.IsNullOrEmpty(r.Strategy)) continue;
                DateTime a, b;
                Span(recs, i, from, to, out a, out b);
                if (b <= a) continue;
                double s = (b - a).TotalSeconds;
                StatStrategy st;
                if (!map.TryGetValue(r.Strategy, out st)) { st = new StatStrategy { Name = r.Strategy }; map[r.Strategy] = st; }
                st.OnSec += s;
                if (!r.Net) continue;
                if (r.YtMax > 0) { st.YtSec += s; if (r.Yt >= r.YtMax) st.YtOkSec += s; }
                if (r.DcMax > 0) { st.DcSec += s; if (r.Dc >= r.DcMax) st.DcOkSec += s; }
                if (r.YtMs >= 0) { st.MsSum += r.YtMs; st.MsN++; }
                if (r.DcMs >= 0) { st.MsSum += r.DcMs; st.MsN++; }
            }
            return map.Values.OrderByDescending(x => x.OnSec).ToList();
        }
    }
}
