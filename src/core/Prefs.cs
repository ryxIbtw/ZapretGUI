// Настройки программы: обычно в реестре (HKCU\Software\ZapretGUI), в переносном режиме — в ZapretGUI.ini рядом с exe.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Win32;

namespace ZapretGui
{
    static class Prefs
    {
        const string Key = @"Software\ZapretGUI";
        static readonly object sync = new object();
        static Dictionary<string, string> ini;   // переносной режим: копия файла в памяти

        public static string IniPath
        {
            get
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                return Path.Combine(Path.GetDirectoryName(exe) ?? "", "ZapretGUI.ini");
            }
        }

        public static bool Portable { get { try { return File.Exists(IniPath); } catch { return false; } } }

        // Где лежат журнал, статистика, история подборов и копии настроек:
        // обычно %APPDATA%\ZapretGUI, в переносном режиме — папка ZapretGUI-data рядом с программой.
        static string RoamingDir { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZapretGUI"); } }
        static string PortableDir { get { return Path.Combine(Path.GetDirectoryName(IniPath) ?? "", "ZapretGUI-data"); } }
        public static string DataDir { get { return Portable ? PortableDir : RoamingDir; } }

        // Все настройки (имя → значение) — для резервной копии.
        public static Dictionary<string, string> All()
        {
            if (Portable) lock (sync) { LoadIni(); return new Dictionary<string, string>(ini); }
            return RegistryValues();
        }

        // При смене режима данные копируются в новое место (старые файлы остаются, ничего не удаляется).
        static void CopyData(string from, string to)
        {
            try
            {
                if (!Directory.Exists(from)) return;
                Directory.CreateDirectory(to);
                foreach (string name in new[] { "events.log", "stats.tsv", "pick-history.tsv" })
                {
                    string s = Path.Combine(from, name), d = Path.Combine(to, name);
                    if (File.Exists(s) && !File.Exists(d)) File.Copy(s, d);
                }
            }
            catch { }
        }

        public static string Get(string name, string def)
        {
            if (Portable)
                lock (sync)
                {
                    LoadIni();
                    string v;
                    return ini.TryGetValue(name, out v) ? v : def;
                }
            try { using (var k = Registry.CurrentUser.OpenSubKey(Key)) { var v = k == null ? null : k.GetValue(name) as string; return v ?? def; } }
            catch { return def; }
        }

        public static void Set(string name, string value)
        {
            if (Portable)
                lock (sync)
                {
                    LoadIni();
                    ini[name] = value ?? "";
                    SaveIni();
                    return;
                }
            try { using (var k = Registry.CurrentUser.CreateSubKey(Key)) k.SetValue(name, value ?? ""); } catch { }
        }

        static void LoadIni()
        {
            if (ini != null) return;
            ini = new Dictionary<string, string>();
            try
            {
                foreach (string line in File.ReadAllLines(IniPath, Encoding.UTF8))
                {
                    int i = line.IndexOf('=');
                    if (i <= 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    ini[line.Substring(0, i).Trim()] = Unescape(line.Substring(i + 1));
                }
            }
            catch { }
        }

        static void SaveIni()
        {
            var sb = new StringBuilder("# Настройки ZapretGUI (переносной режим). Удалите файл, чтобы вернуться к настройкам в реестре.\r\n");
            foreach (var kv in ini.OrderBy(k => k.Key)) sb.Append(kv.Key).Append('=').Append(Escape(kv.Value)).Append("\r\n");
            try { File.WriteAllText(IniPath, sb.ToString(), new UTF8Encoding(false)); } catch { }
        }

        static string Escape(string s) { return s.Replace("\\", "\\\\").Replace("\r", "").Replace("\n", "\\n"); }
        static string Unescape(string s) { return s.Replace("\\n", "\n").Replace("\\\\", "\\"); }

        // Все настройки из реестра.
        static Dictionary<string, string> RegistryValues()
        {
            var d = new Dictionary<string, string>();
            try
            {
                using (var k = Registry.CurrentUser.OpenSubKey(Key))
                    if (k != null) foreach (string n in k.GetValueNames()) { var v = k.GetValue(n) as string; if (v != null) d[n] = v; }
            }
            catch { }
            return d;
        }

        // Включить переносной режим: настройки переносятся в ZapretGUI.ini. Выключить: обратно в реестр, файл переименовывается в .bak.
        public static void SetPortable(bool on)
        {
            lock (sync)
            {
                if (on == Portable) return;
                if (on)
                {
                    ini = RegistryValues();
                    SaveIni();
                    CopyData(RoamingDir, PortableDir);
                }
                else
                {
                    CopyData(PortableDir, RoamingDir);
                    LoadIni();
                    using (var k = Registry.CurrentUser.CreateSubKey(Key))
                        foreach (var kv in ini) k.SetValue(kv.Key, kv.Value);
                    File.Move(IniPath, IniPath + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));   // файл не удаляем — только переименовываем
                    ini = null;
                }
            }
        }

        // «Удалить всё»: стереть все настройки программы (реестр и файл переносного режима).
        public static void DeleteAll()
        {
            lock (sync)
            {
                try { Registry.CurrentUser.DeleteSubKeyTree(Key, false); } catch { }
                try { if (File.Exists(IniPath)) File.Move(IniPath, IniPath + ".deleted"); } catch { }
                ini = null;
            }
        }
    }
}
