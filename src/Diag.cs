// Диагностика (перенесена из service.bat → Run Diagnostics) и окно с результатами.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ZapretGui
{
    class DiagItem
    {
        public int Level;          // 0 — ок, 1 — предупреждение, 2 — проблема
        public string Title, Detail = "";
        public string FixText;     // текст кнопки, если можно исправить
        public Action Fix;
    }

    static class Diag
    {
        static DiagItem Ok(string t) { return new DiagItem { Level = 0, Title = t }; }
        static DiagItem Warn(string t, string d) { return new DiagItem { Level = 1, Title = t, Detail = d }; }
        static DiagItem Bad(string t, string d) { return new DiagItem { Level = 2, Title = t, Detail = d }; }

        public static List<DiagItem> Run(Zapret z)
        {
            var list = new List<DiagItem>();
            ServiceController[] all;
            try { all = ServiceController.GetServices(); } catch { all = new ServiceController[0]; }
            Func<Func<string, bool>, List<ServiceController>> running = pred => all.Where(s =>
            {
                try { return s.Status == ServiceControllerStatus.Running && (pred(s.ServiceName) || pred(s.DisplayName)); }
                catch { return false; }
            }).ToList();
            Func<string, Func<string, bool>> has = word => n => n.IndexOf(word, StringComparison.OrdinalIgnoreCase) >= 0;

            // Base Filtering Engine
            bool bfe = running(n => n.Equals("BFE", StringComparison.OrdinalIgnoreCase)).Count > 0;
            list.Add(bfe ? Ok(L.T("Служба Base Filtering Engine работает", "Base Filtering Engine is running"))
                         : Bad(L.T("Base Filtering Engine не запущена", "Base Filtering Engine is not running"),
                               L.T("Без этой службы zapret не работает", "zapret requires this service")));

            // Прокси
            using (var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
            {
                bool proxy = k != null && (k.GetValue("ProxyEnable") as int? ?? 0) == 1;
                if (proxy)
                {
                    var it = Warn(L.T("Включён системный прокси", "System proxy is enabled"),
                        (k.GetValue("ProxyServer") as string ?? "") + L.T(" — отключите, если не пользуетесь", " — disable it if you don't use one"));
                    it.FixText = L.T("Отключить", "Disable");
                    it.Fix = () =>
                    {
                        using (var w = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings", true))
                            if (w != null) w.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                    };
                    list.Add(it);
                }
                else list.Add(Ok(L.T("Системный прокси не используется", "No system proxy")));
            }

            // TCP timestamps — включаем всегда, как это делает service.bat
            int ts = Zapret.Run("netsh.exe", "interface tcp set global timestamps=enabled");
            list.Add(ts == 0 ? Ok(L.T("Метки времени TCP включены", "TCP timestamps enabled"))
                             : Bad(L.T("Не удалось включить метки времени TCP", "Failed to enable TCP timestamps"), "netsh interface tcp set global timestamps=enabled"));

            // Конфликтующие программы
            bool adguard = Process.GetProcessesByName("AdguardSvc").Length > 0;
            list.Add(adguard ? Bad(L.T("Найден AdGuard", "AdGuard found"), L.T("Может мешать работе Discord", "May break Discord"))
                             : Ok(L.T("AdGuard не мешает", "No AdGuard conflict")));
            AddConflict(list, running(has("Killer")), "Killer", L.T("Сетевые службы Killer конфликтуют с zapret", "Killer network services conflict with zapret"));
            AddConflict(list, running(n => has("Intel")(n) && has("Connectivity")(n) && has("Network")(n)), "Intel Connectivity",
                L.T("Intel Connectivity Network Service конфликтует с zapret", "Intel Connectivity Network Service conflicts with zapret"));
            AddConflict(list, running(n => has("TracSrvWrapper")(n) || has("EPWD")(n)), "Check Point", L.T("Попробуйте удалить Check Point", "Try uninstalling Check Point"));
            AddConflict(list, running(has("SmartByte")), "SmartByte", L.T("Отключите SmartByte в services.msc", "Disable SmartByte in services.msc"));

            var vpn = running(n => has("VPN")(n) || has("HappService")(n));
            list.Add(vpn.Count > 0
                ? Warn(L.T("Найдены службы VPN", "VPN services found"), string.Join(", ", vpn.Select(s => s.ServiceName)) + L.T(" — отключите VPN", " — disable VPNs"))
                : Ok(L.T("VPN не мешает", "No VPN conflict")));

            // Защищённый DNS
            list.Add(HasDoh() ? Ok(L.T("Защищённый DNS настроен", "Secure DNS is configured"))
                              : Warn(L.T("Защищённый DNS не настроен", "Secure DNS is not configured"),
                                     L.T("Включите DNS-over-HTTPS в браузере или в параметрах Windows 11", "Enable DNS-over-HTTPS in your browser or in Windows 11 settings")));

            // Путь установки
            bool cyr = Regex.IsMatch(z.Root, "[а-яА-ЯёЁ]");
            list.Add(cyr ? Warn(L.T("В пути к zapret есть кириллица", "The zapret path contains Cyrillic characters"), L.T("Если не работает — перенесите, например, в C:\\zapret", "If it doesn't work, move it, e.g. to C:\\zapret"))
                         : Ok(L.T("Путь к zapret без кириллицы", "zapret path is fine")));
            string od = Environment.GetEnvironmentVariable("OneDrive");
            bool inOd = !string.IsNullOrEmpty(od) && (z.Root + "\\").StartsWith(od.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
            list.Add(inOd ? Bad(L.T("zapret лежит в папке OneDrive", "zapret is inside OneDrive"), L.T("Перенесите, например, в C:\\zapret", "Move it, e.g. to C:\\zapret"))
                          : Ok(L.T("zapret не в OneDrive", "zapret is not in OneDrive")));

            bool sys = Directory.Exists(z.Bin) && Directory.GetFiles(z.Bin, "*.sys").Length > 0;
            list.Add(sys ? Ok(L.T("Драйвер WinDivert на месте", "WinDivert driver present"))
                         : Bad(L.T("Не найден WinDivert64.sys", "WinDivert64.sys not found"), L.T("Перекачайте zapret или проверьте антивирус", "Re-download zapret or check your antivirus")));

            // hosts
            try
            {
                string hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");
                string h = File.Exists(hosts) ? File.ReadAllText(hosts) : "";
                bool yt = Regex.IsMatch(h, @"^\s*[^#\s].*(youtube\.com|youtu\.be)", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                list.Add(yt ? Warn(L.T("В файле hosts есть записи YouTube", "hosts file has YouTube entries"), L.T("Они могут мешать открытию YouTube", "They may break YouTube"))
                            : Ok(L.T("Файл hosts в порядке", "hosts file is fine")));
            }
            catch { }

            // WinDivert без winws
            bool winws = Process.GetProcessesByName("winws").Length > 0;
            bool divert = all.Any(s => { try { return s.ServiceName.Equals("WinDivert", StringComparison.OrdinalIgnoreCase) && s.Status != ServiceControllerStatus.Stopped; } catch { return false; } });
            if (!winws && divert)
            {
                var it = Warn(L.T("WinDivert активен без winws.exe", "WinDivert is active without winws.exe"), L.T("Остался от другой программы обхода", "Left over by another bypass tool"));
                it.FixText = L.T("Удалить", "Remove");
                it.Fix = Zapret.RemoveDriver;
                list.Add(it);
            }

            // Другие программы обхода
            var conflicts = new[] { "GoodbyeDPI", "discordfix_zapret", "winws1", "winws2" }
                .Where(n => all.Any(s => { try { return s.ServiceName.Equals(n, StringComparison.OrdinalIgnoreCase); } catch { return false; } })).ToList();
            if (conflicts.Count > 0)
            {
                var it = Bad(L.T("Найдены другие программы обхода", "Other bypass services found"), string.Join(", ", conflicts));
                it.FixText = L.T("Удалить", "Remove");
                it.Fix = () =>
                {
                    foreach (string n in conflicts) { Zapret.Run("sc.exe", "stop \"" + n + "\""); Zapret.Run("sc.exe", "delete \"" + n + "\""); }
                    Zapret.RemoveDriver();
                };
                list.Add(it);
            }
            else list.Add(Ok(L.T("Других программ обхода нет", "No other bypass services")));

            return list.OrderByDescending(i => i.Level).ToList();
        }

        static void AddConflict(List<DiagItem> list, List<ServiceController> found, string name, string detail)
        {
            list.Add(found.Count > 0 ? Bad(L.T("Найдено: ", "Found: ") + name, detail) : Ok(name + L.T(" не мешает", ": no conflict")));
        }

        static bool HasDoh()
        {
            try
            {
                using (var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters"))
                    return k != null && Scan(k);
            }
            catch { return false; }
        }

        static bool Scan(RegistryKey k)
        {
            object v = k.GetValue("DohFlags");
            if (v != null && Convert.ToInt64(v) > 0) return true;
            foreach (string n in k.GetSubKeyNames())
                using (var c = k.OpenSubKey(n))
                    if (c != null && Scan(c)) return true;
            return false;
        }

        // Очистка кэша Discord (Stable, PTB, Canary, Development). Возвращает число найденных установок.
        public static int ClearDiscordCache()
        {
            var apps = new[] { new[] { "discord", "Discord" }, new[] { "discordptb", "DiscordPTB" },
                               new[] { "discordcanary", "DiscordCanary" }, new[] { "discorddevelopment", "DiscordDevelopment" } };
            string appdata = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            int found = 0;
            foreach (var a in apps)
            {
                string dir = Path.Combine(appdata, a[0]);
                if (!Directory.Exists(dir)) continue;
                found++;
                foreach (var p in Process.GetProcessesByName(a[1])) { try { p.Kill(); p.WaitForExit(3000); } catch { } }
                foreach (string c in new[] { "Cache", "Code Cache", "GPUCache" })
                {
                    string cd = Path.Combine(dir, c);
                    try { if (Directory.Exists(cd)) Directory.Delete(cd, true); } catch { }
                }
            }
            return found;
        }
    }

    // Строка результата диагностики.
    class DiagRow : PaintedControl
    {
        readonly DiagItem item;
        bool hoverFix;
        public event EventHandler FixClicked;

        public DiagRow(DiagItem it)
        {
            item = it;
            BackColor = Theme.Surface;
        }

        Rectangle FixRect
        {
            get
            {
                if (item.Fix == null) return Rectangle.Empty;
                int w = TextRenderer.MeasureText(item.FixText, Theme.Small).Width + Theme.P(16);
                return new Rectangle(Width - Theme.P(12) - w, (Height - Theme.P(28)) / 2, w, Theme.P(28));
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            bool h = FixRect.Contains(e.Location);
            if (h != hoverFix) { hoverFix = h; Cursor = h ? Cursors.Hand : Cursors.Default; Invalidate(); }
            base.OnMouseMove(e);
        }
        protected override void OnMouseLeave(EventArgs e) { hoverFix = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (FixRect.Contains(e.Location) && FixClicked != null) FixClicked(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            float S = Theme.S;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Surface);

            Color c = item.Level == 0 ? Theme.Accent : item.Level == 1 ? Theme.Warn : Theme.Danger;
            float d = 18 * S, cx = 14 * S + d / 2, cy = Height / 2f;
            using (var b = new SolidBrush(Color.FromArgb(Theme.IsLight ? 40 : 50, c))) g.FillEllipse(b, cx - d / 2, cy - d / 2, d, d);
            using (var pen = new Pen(c, 1.8f * S) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round })
            {
                float r = 4 * S;
                if (item.Level == 0) g.DrawLines(pen, new[] { new PointF(cx - r, cy), new PointF(cx - r / 3, cy + r * 0.7f), new PointF(cx + r, cy - r * 0.6f) });
                else if (item.Level == 1) { g.DrawLine(pen, cx, cy - r, cx, cy + r * 0.2f); g.DrawLine(pen, cx, cy + r, cx, cy + r + 0.1f); }
                else { g.DrawLine(pen, cx - r * 0.7f, cy - r * 0.7f, cx + r * 0.7f, cy + r * 0.7f); g.DrawLine(pen, cx + r * 0.7f, cy - r * 0.7f, cx - r * 0.7f, cy + r * 0.7f); }
            }

            var fr = FixRect;
            int textRight = (fr.IsEmpty ? Width - Theme.P(12) : fr.X - Theme.P(8));
            int x = Theme.P(44);
            var flags = TextFormatFlags.Left | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis;
            if (item.Detail == "")
                TextRenderer.DrawText(g, item.Title, Theme.Body, new Rectangle(x, 0, textRight - x, Height), Theme.Text, Theme.Surface, flags | TextFormatFlags.VerticalCenter);
            else
            {
                TextRenderer.DrawText(g, item.Title, Theme.Body, new Rectangle(x, Theme.P(8), textRight - x, Theme.P(20)), Theme.Text, Theme.Surface, flags);
                TextRenderer.DrawText(g, item.Detail, Theme.Caption, new Rectangle(x, Theme.P(28), textRight - x, Theme.P(16)), Theme.Muted, Theme.Surface, flags);
            }

            if (!fr.IsEmpty)
            {
                Color bg = hoverFix ? Theme.Accent : Theme.AccentDeep;
                Theme.FillRound(g, bg, fr, fr.Height / 2f);
                TextRenderer.DrawText(g, item.FixText, Theme.Small, fr, hoverFix ? (Theme.IsLight ? Color.White : Theme.Bg) : Theme.Accent, bg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
        }
    }

    class DiagForm : Form
    {
        readonly Zapret z;
        readonly Panel list;
        readonly Label summary;
        readonly FlatLink rerun, discord;
        bool running;

        public DiagForm(Zapret zapret, Icon icon)
        {
            z = zapret;
            Func<float, int> P = Theme.P;
            Text = L.T("Диагностика", "Diagnostics");
            Icon = icon;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            ForeColor = Theme.Text;
            Font = Theme.Body;
            ClientSize = new Size(P(440), P(600));

            var title = new Label { Text = L.T("Диагностика", "Diagnostics"), Font = Theme.Title, AutoSize = true, Location = new Point(P(20), P(16)), BackColor = Theme.Bg };
            summary = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, Bounds = new Rectangle(P(20), P(46), P(400), P(20)), BackColor = Theme.Bg };
            list = new Panel { Bounds = new Rectangle(P(20), P(74), P(400), P(472)), AutoScroll = true, BackColor = Theme.Bg };
            rerun = new FlatLink(L.T("Проверить снова", "Run again")) { Location = new Point(P(20), P(562)) };
            rerun.Click += delegate { Start(); };
            discord = new FlatLink(L.T("Очистить кэш Discord", "Clear Discord cache"));
            discord.Click += delegate { ClearDiscord(); };
            Controls.AddRange(new Control[] { title, summary, list, rerun, discord });
            discord.Location = new Point(ClientSize.Width - P(20) - discord.PreferredSize.Width, P(562));
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            MainForm.ApplyTitleBar(Handle);
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Start(); }

        public void Start()
        {
            if (running) return;
            running = true;
            summary.ForeColor = Theme.Muted;
            summary.Text = L.T("Проверяю…", "Checking…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                List<DiagItem> items;
                try { items = Diag.Run(z); }
                catch (Exception ex) { items = new List<DiagItem> { new DiagItem { Level = 2, Title = ex.Message } }; }
                if (IsDisposed) return;
                BeginInvoke((MethodInvoker)delegate { Fill(items); running = false; });
            });
        }

        void Fill(List<DiagItem> items)
        {
            list.SuspendLayout();
            foreach (Control c in list.Controls.Cast<Control>().ToList()) c.Dispose();
            list.Controls.Clear();
            int y = 0, w = list.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Theme.P(2);
            foreach (var it in items)
            {
                var row = new DiagRow(it) { Bounds = new Rectangle(0, y, w, Theme.P(it.Detail == "" ? 40 : 52)) };
                var item = it;
                row.FixClicked += delegate
                {
                    try { item.Fix(); } catch (Exception ex) { summary.ForeColor = Theme.Danger; summary.Text = ex.Message; return; }
                    Start();
                };
                list.Controls.Add(row);
                y += row.Height + Theme.P(4);
            }
            list.ResumeLayout();
            int bad = items.Count(i => i.Level == 2), warn = items.Count(i => i.Level == 1);
            if (bad + warn == 0) { summary.ForeColor = Theme.Accent; summary.Text = L.T("Всё в порядке", "Everything looks good"); }
            else
            {
                summary.ForeColor = bad > 0 ? Theme.Danger : Theme.Warn;
                summary.Text = L.T("Проблем: " + bad + ", предупреждений: " + warn, "Problems: " + bad + ", warnings: " + warn);
            }
        }

        void ClearDiscord()
        {
            if (Ask.Show(this, L.T("Discord будет закрыт, его кэш удалён. Продолжить?", "Discord will be closed and its cache removed. Continue?"),
                Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int n = Diag.ClearDiscordCache();
            summary.ForeColor = n > 0 ? Theme.Accent : Theme.Warn;
            summary.Text = n > 0 ? L.T("Кэш Discord очищен", "Discord cache cleared") : L.T("Discord не найден", "Discord not found");
        }
    }
}
