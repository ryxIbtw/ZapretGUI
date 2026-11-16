// Сайты: «Проверить сайт», свои плашки сервисов на панели, добавление сайта из буфера обмена, автообновление IPSet.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ZapretGui
{
    partial class MainWindow
    {
        // ---------- свои плашки сервисов (до 3) ----------

        public class CustomService { public string Name, Url; }

        static List<CustomService> Services
        {
            get
            {
                var res = new List<CustomService>();
                foreach (string part in Prefs.Get("Services", "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    var p = part.Split('|');
                    if (p.Length == 2 && p[1].StartsWith("http")) res.Add(new CustomService { Name = p[0], Url = p[1] });
                }
                return res.Take(3).ToList();
            }
            set { Prefs.Set("Services", string.Join(";", value.Select(s => s.Name.Replace("|", " ").Replace(";", " ") + "|" + s.Url))); }
        }

        readonly Dictionary<string, long> serviceMs = new Dictionary<string, long>();
        readonly List<ServiceCardW> serviceCards = new List<ServiceCardW>();
        bool servicesChecked;

        int ServiceRowHeight { get { return simple || Services.Count == 0 ? 0 : 60; } }

        FrameworkElement BuildServiceCards()
        {
            serviceCards.Clear();
            var list = Services;
            if (list.Count == 0) return null;
            var g = new System.Windows.Controls.Primitives.UniformGrid { Columns = list.Count, Margin = new Thickness(-5, 5, -5, 0) };
            foreach (var s in list)
            {
                var c = new ServiceCardW(s.Name, "globe") { Margin = new Thickness(5), Tag = s.Url };
                c.Click += delegate { CheckNet(); };
                c.ToolTip = s.Url;
                serviceCards.Add(c);
                g.Children.Add(c);
            }
            return g;
        }

        // Проверка своих сервисов (вызывается из фоновой проверки сайтов).
        void ProbeServices()
        {
            // все сразу, а не по очереди: три неоткрывающихся сервиса не должны задерживать проверку на 15 секунд
            var list = Services;
            var threads = new List<Thread>();
            foreach (var s in list)
            {
                string url = s.Url;
                var t = new Thread(() =>
                {
                    long ms = NetCheck.ProbeMs(url, 5000);
                    lock (serviceMs) serviceMs[url] = ms;
                }) { IsBackground = true };
                threads.Add(t);
                t.Start();
            }
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(7000);
            foreach (var t in threads)
            {
                int left = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
                if (left > 0) t.Join(left);
            }
            servicesChecked = true;
        }

        void UpdateServiceCards()
        {
            foreach (var c in serviceCards)
            {
                string url = (string)c.Tag;
                long ms;
                bool known;
                lock (serviceMs) known = serviceMs.TryGetValue(url, out ms);
                if (!known || !servicesChecked) { c.Set(-1, netChecking, ""); continue; }
                c.Set(ms >= 0 ? 1 : 0, netChecking, ms >= 0 ? ms + L.T(" мс", " ms") : L.T("не открывается", "unreachable"));
            }
        }

        void ShowServicesMenu()
        {
            var m = WMenu.Create();
            var list = Services;
            foreach (var s in list)
            {
                var sv = s;
                WMenu.Add(m, L.T("Убрать «", "Remove “") + sv.Name + L.T("»", "”") + "   ·  " + sv.Url, false, () =>
                {
                    Services = Services.Where(x => x.Url != sv.Url).ToList();
                    RebuildLayout();
                });
            }
            if (list.Count > 0) WMenu.Sep(m);
            var add = WMenu.Add(m, list.Count >= 3 ? L.T("Можно не больше трёх", "Up to three") : L.T("Добавить сервис…", "Add a service…"), false, AddService);
            add.IsEnabled = list.Count < 3;
            WMenu.ShowBelowRight(m, rowServices);
        }

        void AddService()
        {
            string v = InputW.Show(this, L.T("Свой сервис на главном экране", "Own service on the main screen"), L.T("Адрес, например twitch.tv", "Address, e.g. twitch.tv"), "");
            if (string.IsNullOrWhiteSpace(v)) return;
            string url = v.Trim().StartsWith("http", StringComparison.OrdinalIgnoreCase) ? v.Trim() : "https://" + v.Trim();
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || !u.Host.Contains(".")) { Toast(L.T("Это не похоже на адрес сайта", "This doesn't look like a site address"), true); return; }
            string host = u.Host.StartsWith("www.") ? u.Host.Substring(4) : u.Host;
            string name = host.Split('.')[0];
            name = CultureInfo.CurrentCulture.TextInfo.ToTitleCase(name);
            var list = Services;
            if (list.Any(x => x.Url == u.GetLeftPart(UriPartial.Authority))) return;
            list.Add(new CustomService { Name = name, Url = u.GetLeftPart(UriPartial.Authority) });
            Services = list;
            Journal.Add(L.T("Добавлен свой сервис на панель: ", "Own service added to the panel: ") + name);
            RebuildLayout();
            CheckNetSoon(500);
        }

        // ---------- добавить сайт из буфера обмена в «Мои домены» ----------

        void AddFromClipboard()
        {
            string text = "";
            try { text = Clipboard.GetText(); } catch { }
            if (string.IsNullOrWhiteSpace(text)) { Toast(L.T("В буфере обмена нет текста", "The clipboard has no text"), true); return; }
            AddToMyDomains(text);
        }

        // Добавляет сайты в lists\list-general-user.txt и перезапускает обход. Возвращает число добавленных.
        internal int AddToMyDomains(string text)
        {
            try
            {
                z.EnsureUserLists();
                string path = Path.Combine(z.Lists, "list-general-user.txt");
                var keep = new List<string>(); var items = new List<string>();
                ListFile.Load(path, keep, items);
                int added, dup; List<string> bad;
                ListFile.AddFrom(text, false, items, out added, out dup, out bad);
                if (added == 0)
                {
                    Toast(dup > 0 ? L.T("Этот сайт уже есть в «Моих доменах»", "This site is already in My domains")
                                  : L.T("В буфере нет адреса сайта", "No site address in the clipboard"), dup == 0);
                    return 0;
                }
                ListFile.Write(path, keep, items, false);
                Journal.Add(L.T("Добавлено в «Мои домены»: ", "Added to My domains: ") + string.Join(", ", items.Skip(items.Count - added)));
                Toast(L.T("Добавлено в «Мои домены»: ", "Added to My domains: ") + added + (Active ? L.T(" · обход перезапускается", " · restarting the bypass") : ""));
                RestartIfActive();
                return added;
            }
            catch (Exception ex) { Toast(ex.Message, true); return 0; }
        }

        void CheckSite() { new CheckSiteW(this).ShowDialog(); }

        // ---------- автообновление списка IPSet раз в неделю ----------

        static bool IpsetAuto { get { return Prefs.Get("IpsetAuto", "0") == "1"; } set { Prefs.Set("IpsetAuto", value ? "1" : "0"); } }
        DateTime ipsetCheckedAt = DateTime.MinValue;

        void MaybeAutoUpdateIpset()
        {
            if (!IpsetAuto || busy || (DateTime.Now - ipsetCheckedAt).TotalMinutes < 60) return;
            ipsetCheckedAt = DateTime.Now;
            DateTime last;
            if (DateTime.TryParse(Prefs.Get("IpsetUpdated", ""), CultureInfo.InvariantCulture, DateTimeStyles.None, out last) && (DateTime.Now - last).TotalDays < 7) return;
            Prefs.Set("IpsetUpdated", DateTime.Now.ToString("s", CultureInfo.InvariantCulture));
            RunOp(L.T("Обновление списка IPSet…", "Updating the IPSet list…"), false, z.UpdateIpset,
                () => Journal.Add(L.T("Список IPSet обновлён автоматически (раз в неделю)", "IPSet list updated automatically (weekly)")));
        }
    }

    // ================= Проверить сайт =================
    class CheckSiteW : WinBase
    {
        readonly MainWindow owner;
        readonly TextBox input;
        readonly TextBlock result;
        readonly Pill addBtn;
        string lastHost;

        public CheckSiteW(MainWindow o) : base(L.T("Проверить сайт", "Check a site"), o)
        {
            owner = o;
            var sp = new StackPanel { Width = 420, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            var d = W.T(L.T("Открывается ли сайт прямо сейчас, с текущей стратегией. Если нет — его можно добавить в «Мои домены», и он тоже пойдёт через обход.",
                "Does the site open right now with the current strategy? If not, add it to My domains so it also goes through the bypass."), W.Small, W.Muted);
            d.TextWrapping = TextWrapping.Wrap; d.Margin = new Thickness(0, 4, 0, 12);
            sp.Children.Add(d);
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input = new TextBox { Tag = L.T("сайт, например rutracker.org", "site, e.g. example.com") };
            input.KeyDown += (s, e) => { if (e.Key == Key.Enter) { e.Handled = true; Check(); } };
            row.Children.Add(input);
            var go = new Pill(L.T("Проверить", "Check"), false, 36) { Margin = new Thickness(8, 0, 0, 0) };
            go.Click += delegate { Check(); };
            Grid.SetColumn(go, 1); row.Children.Add(go);
            sp.Children.Add(row);
            result = W.T("", W.Body, W.Text); result.TextWrapping = TextWrapping.Wrap; result.Margin = new Thickness(2, 12, 0, 0);
            sp.Children.Add(result);
            addBtn = new Pill(L.T("Добавить в «Мои домены»", "Add to My domains"), true) { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 12, 0, 0), Visibility = Visibility.Collapsed };
            addBtn.Click += delegate
            {
                if (lastHost == null) return;
                if (owner.AddToMyDomains(lastHost) > 0)
                {
                    addBtn.Visibility = Visibility.Collapsed;
                    result.Foreground = W.Muted;
                    result.Text = L.T("Добавлено. Через 10 секунд проверю ещё раз…", "Added. Checking again in 10 seconds…");
                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
                    t.Tick += delegate { t.Stop(); if (IsVisible) Check(); };
                    t.Start();
                }
            };
            sp.Children.Add(addBtn);
            Root.Child = sp;
            Loaded += delegate { input.Focus(); };
        }

        void Check()
        {
            string s = input.Text.Trim();
            if (s == "") return;
            string url = s.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? s : "https://" + s;
            Uri u;
            if (!Uri.TryCreate(url, UriKind.Absolute, out u) || !u.Host.Contains("."))
            { result.Foreground = W.Danger; result.Text = L.T("Это не похоже на адрес сайта", "This doesn't look like a site address"); return; }
            lastHost = u.Host;
            result.Foreground = W.Muted; result.Text = L.T("Проверяю ", "Checking ") + u.Host + "…";
            addBtn.Visibility = Visibility.Collapsed;
            ThreadPool.QueueUserWorkItem(delegate
            {
                long ms = NetCheck.ProbeMs(u.GetLeftPart(UriPartial.Path), 8000);
                Dispatcher.BeginInvoke((Action)delegate
                {
                    if (ms >= 0) { result.Foreground = W.Accent; result.Text = u.Host + L.T(" открывается · ", " opens · ") + ms + L.T(" мс", " ms"); }
                    else
                    {
                        result.Foreground = W.Danger;
                        result.Text = u.Host + L.T(" не открывается. Если сайт заблокирован, добавьте его в «Мои домены».", " doesn't open. If it is blocked, add it to My domains.");
                        addBtn.Visibility = Visibility.Visible;
                    }
                });
            });
        }
    }
}
