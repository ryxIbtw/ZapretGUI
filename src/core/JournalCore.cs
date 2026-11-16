// Журнал событий: что и когда делала программа. Файл %APPDATA%\ZapretGUI\events.log, последние ~500 записей.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ZapretGui
{
    static class Journal
    {
        public const int Keep = 500;
        public static bool Enabled = true;      // в тестовых снимках окна журнал не пишется
        public static string PathOverride = null;
        public static event Action<string> Added;
        static readonly object sync = new object();

        public static string FilePath
        {
            get { return PathOverride ?? Path.Combine(Prefs.DataDir, "events.log"); }
        }

        public static void Add(string text)
        {
            if (!Enabled || string.IsNullOrEmpty(text)) return;
            string line = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss") + "   " + text.Replace("\r", " ").Replace("\n", " ");
            lock (sync)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                    File.AppendAllText(FilePath, line + "\r\n", Encoding.UTF8);
                    // файл не растёт бесконечно: когда записей стало втрое больше нормы, оставляем последние Keep
                    if (new FileInfo(FilePath).Length > 150 * 1024)
                    {
                        var lines = File.ReadAllLines(FilePath, Encoding.UTF8);
                        if (lines.Length > Keep) File.WriteAllLines(FilePath, lines.Skip(lines.Length - Keep).ToArray(), Encoding.UTF8);
                    }
                }
                catch { }
            }
            var h = Added;
            if (h != null) try { h(line); } catch { }
        }

        public static List<string> Read()
        {
            lock (sync)
            {
                try
                {
                    if (!File.Exists(FilePath)) return new List<string>();
                    var lines = File.ReadAllLines(FilePath, Encoding.UTF8).Where(l => l.Trim() != "").ToList();
                    return lines.Count > Keep ? lines.Skip(lines.Count - Keep).ToList() : lines;
                }
                catch { return new List<string>(); }
            }
        }
    }
}
