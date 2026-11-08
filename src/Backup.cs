// Экспорт и импорт настроек одним .zip: свои списки, IPSet, игровой фильтр, флаги utils, выбранные fake-файлы и настройки GUI.
// Перед импортом текущие настройки автоматически сохраняются в %APPDATA%\ZapretGUI\backups.
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ZapretGui
{
    static class SettingsBackup
    {
        const string ManifestName = "zapretgui-settings.txt";
        const long MaxEntry = 32 * 1024 * 1024;

        // Какие настройки программы переносятся (путь к zapret, запущенный процесс и т.п. — нет).
        static readonly string[] PrefKeys = { "Strategy", "Theme", "Accent", "Lang", "CloseToTray", "Watchdog" };

        // Файлы внутри zip, которые разрешено записывать при импорте (защита от чужих путей в архиве).
        static readonly Regex Allowed = new Regex(
            @"^(lists/[^/\\:]+-user\.txt|lists/ipset-all\.txt|lists/ipset-all\.txt\.backup|utils/[^/\\:]+\.enabled|bin/ACTIVE_[^/\\:]+\.bin)$",
            RegexOptions.IgnoreCase);

        public static string AutoBackupDirOverride = null;   // для проверки на тестовой папке

        public static string AutoBackupDir
        {
            get { return AutoBackupDirOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZapretGUI", "backups"); }
        }

        public static string DefaultFileName { get { return "ZapretGUI-settings-" + DateTime.Now.ToString("yyyy-MM-dd") + ".zip"; } }

        public static void Export(Zapret z, string zipPath)
        {
            string tmp = zipPath + ".tmp";
            using (var fs = File.Create(tmp))
            using (var zip = new ZipArchive(fs, ZipArchiveMode.Create))
            {
                var files = new List<string>();
                Action<string, string> addDir = (dir, pattern) =>
                {
                    if (!Directory.Exists(Path.Combine(z.Root, dir))) return;
                    foreach (string f in Directory.GetFiles(Path.Combine(z.Root, dir), pattern))
                    {
                        string rel = dir + "/" + Path.GetFileName(f);
                        if (!Allowed.IsMatch(rel)) continue;
                        var e = zip.CreateEntry(rel);
                        using (var w = e.Open()) { byte[] b = File.ReadAllBytes(f); w.Write(b, 0, b.Length); }
                        files.Add(rel);
                    }
                };
                addDir("lists", "*-user.txt");
                addDir("lists", "ipset-all.txt");
                addDir("lists", "ipset-all.txt.backup");
                addDir("utils", "*.enabled");
                addDir("bin", "ACTIVE_*.bin");

                var sb = new StringBuilder();
                sb.Append("format=1\r\n");
                sb.Append("app=").Append(SelfUpdate.Current.ToString(3)).Append("\r\n");
                sb.Append("zapret=").Append(z.LocalVersion).Append("\r\n");
                sb.Append("date=").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm")).Append("\r\n");
                sb.Append("flags=").Append(string.Join(",", files.Where(f => f.StartsWith("utils/")).Select(f => f.Substring(6)))).Append("\r\n");
                foreach (string k in PrefKeys)
                {
                    string v = Prefs.Get(k, null);
                    if (v != null) sb.Append("pref.").Append(k).Append("=").Append(v.Replace("\r", "").Replace("\n", "")).Append("\r\n");
                }
                var m = zip.CreateEntry(ManifestName);
                using (var w = new StreamWriter(m.Open(), new UTF8Encoding(false))) w.Write(sb.ToString());
            }
            if (File.Exists(zipPath)) File.Replace(tmp, zipPath, null);
            else File.Move(tmp, zipPath);
        }

        // Что лежит в файле — для окна подтверждения. Бросает исключение, если это не файл настроек ZapretGUI.
        public static string Describe(string zipPath)
        {
            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var info = ReadManifest(zip);
                var names = zip.Entries.Select(e => e.FullName.Replace('\\', '/')).Where(n => Allowed.IsMatch(n)).ToList();
                var sb = new StringBuilder();
                string date, app, zv;
                info.TryGetValue("date", out date); info.TryGetValue("app", out app); info.TryGetValue("zapret", out zv);
                sb.Append(L.T("Сохранено: ", "Saved: ")).Append(date ?? "?").Append("  ·  ZapretGUI v").Append(app ?? "?")
                  .Append("  ·  zapret v").Append(zv ?? "?").Append("\n\n");
                var lists = names.Where(n => n.EndsWith("-user.txt", StringComparison.OrdinalIgnoreCase)).Select(n => n.Substring(6)).ToList();
                if (lists.Count > 0) sb.Append("•  ").Append(L.T("Свои списки: ", "User lists: ")).Append(string.Join(", ", lists)).Append("\n");
                if (names.Any(n => n.StartsWith("lists/ipset-all", StringComparison.OrdinalIgnoreCase))) sb.Append("•  IPSet\n");
                if (names.Any(n => n.Equals("utils/game_filter.enabled", StringComparison.OrdinalIgnoreCase))) sb.Append("•  ").Append(L.T("Игровой фильтр и порты", "Game filter and ports")).Append("\n");
                if (names.Any(n => n.StartsWith("bin/", StringComparison.OrdinalIgnoreCase))) sb.Append("•  ").Append(L.T("Выбранные fake-файлы", "Selected fake files")).Append("\n");
                if (info.Keys.Any(k => k.StartsWith("pref."))) sb.Append("•  ").Append(L.T("Настройки программы: стратегия, тема, язык, трей, сторож", "App settings: strategy, theme, language, tray, watchdog")).Append("\n");
                return sb.ToString();
            }
        }

        // Импорт. Возвращает путь к автоматической копии прежних настроек.
        public static string Import(Zapret z, string zipPath)
        {
            Directory.CreateDirectory(AutoBackupDir);
            string safety = Path.Combine(AutoBackupDir, "before-import-" + DateTime.Now.ToString("yyyy-MM-dd-HHmmss") + ".zip");
            Export(z, safety);

            using (var zip = ZipFile.OpenRead(zipPath))
            {
                var info = ReadManifest(zip);
                string root = Path.GetFullPath(z.Root).TrimEnd('\\') + "\\";
                foreach (var e in zip.Entries)
                {
                    string name = e.FullName.Replace('\\', '/');
                    if (!Allowed.IsMatch(name) || name.Contains("..") || e.Length > MaxEntry) continue;
                    string target = Path.GetFullPath(Path.Combine(z.Root, name.Replace('/', '\\')));
                    if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (var src = e.Open())
                    using (var dst = File.Create(target)) src.CopyTo(dst);
                }

                // проверка обновлений zapret — это флаг-файл: включаем или выключаем так, как было в сохранённых настройках
                string flags;
                if (info.TryGetValue("flags", out flags))
                    z.UpdatesEnabled = flags.Split(',').Any(f => f.Trim().Equals("check_updates.enabled", StringComparison.OrdinalIgnoreCase));

                foreach (string k in PrefKeys)
                {
                    string v;
                    if (info.TryGetValue("pref." + k, out v) && ValidPref(k, v)) Prefs.Set(k, v);
                }
            }
            return safety;
        }

        static bool ValidPref(string key, string v)
        {
            switch (key)
            {
                case "Theme": return v == "system" || v == "dark" || v == "light";
                case "Accent": return Theme.Accents.Contains(v);
                case "Lang": return v == "ru" || v == "en";
                case "CloseToTray":
                case "Watchdog": return v == "0" || v == "1";
                case "Strategy": return v.Length > 0 && v.Length < 200 && v.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
            }
            return false;
        }

        static Dictionary<string, string> ReadManifest(ZipArchive zip)
        {
            var m = zip.GetEntry(ManifestName);
            if (m == null) throw new Exception(L.T("Это не файл настроек ZapretGUI", "This is not a ZapretGUI settings file"));
            var d = new Dictionary<string, string>();
            using (var r = new StreamReader(m.Open(), Encoding.UTF8))
            {
                string line;
                while ((line = r.ReadLine()) != null)
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0) d[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                }
            }
            return d;
        }
    }
}
