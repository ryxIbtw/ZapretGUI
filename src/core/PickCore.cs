// Подбор стратегии: результат проверки одной стратегии и история подборов.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace ZapretGui
{
    class PickResult
    {
        public string Name;
        public bool Failed;              // стратегия не запустилась

        // Проверки как в «Run Tests» Flowseal (utils\test zapret.ps1). Mode: "std" — HTTP/пинг, "dpi" — обрыв 16–20 КБ.
        public string Mode = "";         // "" — подбор старых версий программы (поля ниже не заполнены)
        public int Ok, Err, Unsup, PingOk, PingFail, Blocked, Total;
        public List<string> Details = new List<string>();   // строки для файла отчёта (как у Flowseal)

        // старый подбор (до 2.1): 3 круга по YouTube и Discord — нужен только для чтения истории
        public int YtOk, DcOk, Rounds, Samples;
        public long TotalMs;
        public double SpeedKBps;

        public const int PlannedRounds = 3;
        public int YtMax { get { return PlannedRounds * NetCheck.YouTube.Length; } }
        public int DcMax { get { return PlannedRounds * NetCheck.Discord.Length; } }
        public bool IsNew { get { return Mode != ""; } }
        // Доля успешных проверок.
        public double Ratio
        {
            get
            {
                if (Failed) return -1;
                if (IsNew) return Total > 0 ? Ok / (double)Total : 0;
                return (YtOk + DcOk) / (double)(PlannedRounds * NetCheck.Max);
            }
        }
        public double AvgMs { get { return Samples > 0 ? TotalMs / (double)Samples : double.MaxValue; } }
        public bool Works { get { return !Failed && (IsNew ? Ok > 0 : YtOk + DcOk > 0); } }
        // 2 — прошла все проверки, 1 — частично, 0 — не работала
        public int Level { get { return Ratio >= 0.999 ? 2 : Works ? 1 : 0; } }

        // Разница в скорости меньше чем в полтора раза — это шум замера, а не разница стратегий.
        public const double SpeedMargin = 1.5;

        // Как у Flowseal: больше всего успешных проверок, при равенстве — больше ответов на пинг.
        // Если и это равно — вперёд та, что заметно (в SpeedMargin раз) быстрее; близкие по скорости остаются
        // в порядке списка zapret, неудачный замер скорости — в конце равных. Старые результаты (до 2.1) — по прежнему правилу.
        public static List<PickResult> Rank(IEnumerable<PickResult> list)
        {
            var all = list.ToList();
            if (all.Any(r => r.IsNew))
            {
                var order = new Dictionary<PickResult, int>();
                for (int k = 0; k < all.Count; k++) order[all[k]] = k;
                var sorted = all.OrderBy(r => r.Failed ? 1 : 0)
                                .ThenByDescending(r => r.Ok)
                                .ThenByDescending(r => r.PingOk)
                                .ThenByDescending(r => r.SpeedKBps)
                                .ToList();
                var res = new List<PickResult>();
                int i = 0;
                while (i < sorted.Count)
                {
                    int j = i;
                    while (j < sorted.Count && sorted[j].Failed == sorted[i].Failed && sorted[j].Ok == sorted[i].Ok && sorted[j].PingOk == sorted[i].PingOk) j++;
                    int a = i;
                    while (a < j)
                    {
                        double top = sorted[a].SpeedKBps;
                        int b = a + 1;
                        if (top > 0) { while (b < j && sorted[b].SpeedKBps > 0 && sorted[b].SpeedKBps * SpeedMargin >= top) b++; }
                        else b = j;
                        res.AddRange(sorted.GetRange(a, b - a).OrderBy(r => order[r]));
                        a = b;
                    }
                    i = j;
                }
                return res;
            }
            return all.OrderByDescending(r => Math.Round(r.Ratio, 3))
                      .ThenByDescending(r => r.SpeedKBps)
                      .ThenBy(r => r.AvgMs)
                      .ToList();
        }

        // Тот же результат проверок, что у b (без учёта скорости) — «наравне».
        public bool SameScore(PickResult b)
        {
            if (b == null || Failed || b.Failed || !IsNew || !b.IsNew) return false;
            return Ok == b.Ok && PingOk == b.PingOk && Blocked == b.Blocked;
        }

        public static string FormatSpeed(double kbps)
        {
            if (kbps <= 0) return "—";
            if (kbps >= 1024) return (kbps / 1024).ToString("0.0") + L.T(" МБ/с", " MB/s");
            return ((int)kbps) + L.T(" КБ/с", " KB/s");
        }
    }

    // История подборов: %APPDATA%\ZapretGUI\pick-history.tsv, последние 10 полных подборов.
    static class PickHistory
    {
        public class Run
        {
            public DateTime When;
            public List<PickResult> Results = new List<PickResult>();
        }

        const int Keep = 10;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        internal static string PathOverride = null;   // только для проверок: другой файл вместо настоящего

        static string FilePath
        {
            get { return PathOverride ?? Path.Combine(Prefs.DataDir, "pick-history.tsv"); }
        }

        public static List<Run> Load()
        {
            var runs = new List<Run>();
            try
            {
                if (!File.Exists(FilePath)) return runs;
                Run cur = null; string curKey = null;
                foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    // дата \t стратегия \t не запустилась \t YouTube \t Discord \t раунды \t замеры \t мс \t скорость
                    string[] p = line.Split('\t');
                    if (p.Length < 9) continue;
                    if (p[0] != curKey)
                    {
                        cur = new Run { When = DateTime.Parse(p[0], Inv, DateTimeStyles.RoundtripKind) };
                        curKey = p[0];
                        runs.Add(cur);
                    }
                    var r = new PickResult
                    {
                        Name = p[1], Failed = p[2] == "1",
                        YtOk = int.Parse(p[3], Inv), DcOk = int.Parse(p[4], Inv), Rounds = int.Parse(p[5], Inv),
                        Samples = int.Parse(p[6], Inv), TotalMs = long.Parse(p[7], Inv), SpeedKBps = double.Parse(p[8], Inv)
                    };
                    // с 2.1: \t режим \t успешно \t ошибки \t нет TLS \t пинг да \t пинг нет \t обрыв \t всего
                    if (p.Length >= 17 && p[9] != "")
                    {
                        r.Mode = p[9];
                        r.Ok = int.Parse(p[10], Inv); r.Err = int.Parse(p[11], Inv); r.Unsup = int.Parse(p[12], Inv);
                        r.PingOk = int.Parse(p[13], Inv); r.PingFail = int.Parse(p[14], Inv); r.Blocked = int.Parse(p[15], Inv);
                        r.Total = int.Parse(p[16], Inv);
                    }
                    cur.Results.Add(r);
                }
            }
            catch { }
            return runs;
        }

        // Добавляет подбор в историю; возвращает true, если сохранить удалось.
        public static bool Save(List<PickResult> results)
        {
            try
            {
                var runs = Load();
                runs.Add(new Run { When = DateTime.Now, Results = results });
                if (runs.Count > Keep) runs = runs.Skip(runs.Count - Keep).ToList();
                var sb = new StringBuilder();
                foreach (var run in runs)
                {
                    string key = run.When.ToString("o", Inv);
                    foreach (var r in run.Results)
                        sb.Append(key).Append('\t').Append(r.Name.Replace('\t', ' ')).Append('\t').Append(r.Failed ? "1" : "0").Append('\t')
                          .Append(r.YtOk.ToString(Inv)).Append('\t').Append(r.DcOk.ToString(Inv)).Append('\t').Append(r.Rounds.ToString(Inv)).Append('\t')
                          .Append(r.Samples.ToString(Inv)).Append('\t').Append(r.TotalMs.ToString(Inv)).Append('\t').Append(r.SpeedKBps.ToString("0.#", Inv))
                          .Append('\t').Append(r.Mode).Append('\t').Append(r.Ok.ToString(Inv)).Append('\t').Append(r.Err.ToString(Inv))
                          .Append('\t').Append(r.Unsup.ToString(Inv)).Append('\t').Append(r.PingOk.ToString(Inv)).Append('\t').Append(r.PingFail.ToString(Inv))
                          .Append('\t').Append(r.Blocked.ToString(Inv)).Append('\t').Append(r.Total.ToString(Inv)).Append("\r\n");
                }
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(false));
                return true;
            }
            catch { return false; }
        }

        // Уровни стратегии в прошлых подборах (от старых к новым): 2/1/0, -1 — в том подборе её не было.
        public static List<int> LevelsFor(string name, List<Run> past, int max)
        {
            return past.Skip(Math.Max(0, past.Count - max))
                       .Select(run => { var r = run.Results.FirstOrDefault(x => x.Name == name); return r == null ? -1 : r.Level; })
                       .ToList();
        }
    }
}
