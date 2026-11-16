// Когда что-то сломалось: уведомления из трея, красная точка на значке, автовосстановление,
// проверка после включения, тихая диагностика при запуске, сторож для службы и строка-предупреждение на панели.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using WF = System.Windows.Forms;

namespace ZapretGui
{
    partial class MainWindow
    {
        static bool AutoFixOn { get { return Prefs.Get("AutoFix", "0") == "1"; } set { Prefs.Set("AutoFix", value ? "1" : "0"); } }
        static bool NotifyOn { get { return Prefs.Get("NotifyBroken", "1") == "1"; } set { Prefs.Set("NotifyBroken", value ? "1" : "0"); } }

        bool justStarted;          // только что включили обход — проверить, открываются ли сайты
        bool pendingPickResults;   // подбор закончился, пока окно было в трее
        bool notifiedBroken;       // уже сообщали из трея, что сломалось
        int badStreak;             // сколько проверок подряд не открывается
        readonly List<string> autoTried = new List<string>();
        DateTime autoGaveUp = DateTime.MinValue;

        // ---------- значок и подсказка в трее ----------

        bool NetProblem { get { return running && netUp && netYt >= 0 && (netYt < 0.99f || netDc < 0.99f); } }

        System.Drawing.Icon TrayIcon()
        {
            if (busy || picking) return iconBusy;
            if (!running) return iconOff;
            return NetProblem && iconBad != null ? iconBad : iconOn;
        }

        // Подсказка значка: состояние, стратегия и сайты (у Windows предел — 63 символа).
        string TrayTip()
        {
            string s;
            if (picking) s = L.T("Zapret — идёт подбор", "Zapret — testing");
            else if (!running) s = L.T("Zapret — выключено", "Zapret — off");
            else
            {
                string st = NetProblem ? L.T("сбой", "problem") : L.T("работает", "working");
                s = "Zapret — " + st + "\n" + (activeStrategy ?? selected);
                if (netYt >= 0) s += "\nYouTube " + (int)Math.Round(netYt * 3) + "/3 · Discord " + (int)Math.Round(netDc * 3) + "/3";
            }
            return s.Length > 63 ? s.Substring(0, 62) + "…" : s;
        }

        // ---------- после каждой проверки сайтов ----------

        void OnNetResult(bool ok, bool net)
        {
            if (!running || picking || busy) { badStreak = 0; justStarted = false; return; }
            string bad = BrokenNames();

            // только что включили, а сайты не открылись — сразу предложить подбор
            if (justStarted)
            {
                justStarted = false;
                if (!ok && net)
                    ShowBanner(bad + L.T(" не открывается после включения. Подобрать стратегию?", " doesn't open after turning on. Find a strategy?"),
                        L.T("Подобрать", "Find"), AutoPick, W.Warn, 2);
            }

            if (ok)
            {
                if (notifiedBroken && NotifyOn)
                    Balloon(L.T("Обход снова работает", "Bypass works again"), L.T("YouTube и Discord открываются", "YouTube and Discord open"), WF.ToolTipIcon.Info);
                if (autoTried.Count > 0)
                    Journal.Add(L.T("Автовосстановление: заработало на стратегии ", "Auto-recovery: works with ") + selected);
                notifiedBroken = false; badStreak = 0; autoTried.Clear();
                HideBanner(2);
                return;
            }
            if (!net) { badStreak = 0; return; }   // нет интернета — стратегия не виновата

            badStreak++;
            if (!notifiedBroken && NotifyOn && badStreak >= 2)   // две проверки подряд — не случайность
            {
                notifiedBroken = true;
                Balloon(bad + L.T(" не открывается", " doesn't open"),
                    AutoFixOn ? L.T("Пробую другие стратегии…", "Trying other strategies…") : L.T("Откройте программу и нажмите «Подобрать стратегию»", "Open the app and click “Find a strategy”"),
                    WF.ToolTipIcon.Warning);
            }
            TryAutoFix();
        }

        string BrokenNames()
        {
            var n = new List<string>();
            if (netYt < 0.99f) n.Add("YouTube");
            if (netDc < 0.99f) n.Add("Discord");
            return n.Count == 0 ? "YouTube" : string.Join(L.T(" и ", " and "), n);
        }

        // Автовосстановление: 5 минут не работает — по очереди пробовать другие стратегии (сначала избранные).
        void TryAutoFix()
        {
            if (!AutoFixOn || svc && !autostart) return;
            if ((DateTime.Now - autoGaveUp).TotalMinutes < 60) return;
            int need = autoTried.Count == 0 ? 5 : 2;   // первой — 5 минут, каждой следующей — 2
            if (badStreak < need) return;
            if (autoTried.Count == 0) autoTried.Add(selected);
            var order = Favorites.Count > 0 ? Favorites.Where(strategies.Contains).Concat(strategies.Where(s => !Favorites.Contains(s))).ToList() : strategies;
            string next = order.FirstOrDefault(s => !autoTried.Contains(s));
            if (next == null)
            {
                autoGaveUp = DateTime.Now;
                Journal.Add(L.T("Автовосстановление: ни одна стратегия не помогла, следующая попытка через час", "Auto-recovery: no strategy helped, next try in an hour"));
                Balloon(L.T("Автовосстановление не помогло", "Auto-recovery didn't help"), L.T("Ни одна стратегия не заработала. Попробуйте диагностику.", "No strategy worked. Try diagnostics."), WF.ToolTipIcon.Error);
                string back = autoTried[0];
                autoTried.Clear(); badStreak = 0;
                if (back != selected) SelectStrategy(back);
                return;
            }
            autoTried.Add(next);
            badStreak = 0;
            Journal.Add(L.T("Автовосстановление: пробую ", "Auto-recovery: trying ") + next);
            SelectStrategy(next);
        }

        // ---------- тихая диагностика при запуске ----------

        void QuietDiagSoon()
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(8) };
            t.Tick += delegate
            {
                t.Stop();
                ThreadPool.QueueUserWorkItem(delegate
                {
                    List<DiagItem> items = null;
                    try { items = Diag.Run(z); } catch { }
                    if (items == null) return;
                    var bad = items.FirstOrDefault(i => i.Level >= 2);
                    if (bad == null) return;
                    Post(() =>
                    {
                        Journal.Add(L.T("Диагностика при запуске: ", "Startup diagnostics: ") + bad.Title + (bad.Detail != "" ? " — " + bad.Detail : ""));
                        ShowBanner(L.T("Найдена проблема: ", "Problem found: ") + bad.Title, L.T("Диагностика", "Diagnostics"), OpenDiag, W.Danger, 1);
                    });
                });
            };
            t.Start();
        }

        // ---------- сторож для службы ----------

        // Служба установлена, но не работает (например, её остановили) — сторож её перезапустит.
        void WatchServiceOnStart()
        {
            if (svc && !running && WatchdogOn)
            {
                Journal.Add(L.T("Служба zapret установлена, но не работает — сторож её запустит", "The zapret service is installed but not running — the watchdog will start it"));
                expectRunning = true;
            }
        }

        // ---------- строка-предупреждение на панели ----------
        // Чем меньше priority, тем важнее: 0 — обновление, 1 — проблема в системе, 2 — сайты не открываются.

        Border banner;
        TextBlock bannerText;
        FlatLink bannerAction;
        Action bannerDo;
        int bannerPriority = int.MaxValue;
        Grid rootGrid;

        const double BannerSpace = 52;   // высота строки (42) + отступ под ней (10)

        FrameworkElement BuildBanner()
        {
            banner = new Border { CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 0, 10, 0), Height = 42, Margin = new Thickness(0, 0, 0, 10), Visibility = Visibility.Collapsed, BorderThickness = new Thickness(1) };
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition());
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerText = W.T("", W.Small, W.Text); bannerText.VerticalAlignment = VerticalAlignment.Center;
            g.Children.Add(bannerText);
            bannerAction = new FlatLink("") { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            bannerAction.Click += delegate { var a = bannerDo; HideBanner(bannerPriority); if (a != null) a(); };
            Grid.SetColumn(bannerAction, 1); g.Children.Add(bannerAction);
            var close = new IconButton("x", 24) { Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            close.Click += delegate { HideBanner(bannerPriority); };
            Grid.SetColumn(close, 2); g.Children.Add(close);
            banner.Child = g;
            RenderBanner();
            return banner;
        }

        // Очередь предупреждений: показывается самое важное, остальные ждут (в скобках — сколько ещё).
        readonly SortedDictionary<int, Tuple<string, string, Action, Brush>> banners = new SortedDictionary<int, Tuple<string, string, Action, Brush>>();

        void ShowBanner(string text, string action, Action act, Brush color, int priority)
        {
            banners[priority] = Tuple.Create(text, action, act, color);
            RenderBanner();
        }

        void HideBanner(int priority)
        {
            if (banners.Remove(priority)) RenderBanner();
        }

        void RenderBanner()
        {
            if (banner == null) return;
            if (banners.Count == 0)
            {
                if (banner.Visibility == Visibility.Visible)
                {
                    banner.Visibility = Visibility.Collapsed;
                    if (rootGrid != null) rootGrid.Height = Ht;
                }
                bannerPriority = int.MaxValue;
                return;
            }
            var first = banners.First();
            var b = first.Value;
            bannerPriority = first.Key;
            bannerText.Text = b.Item1 + (banners.Count > 1 ? L.T("   (и ещё ", "   (+") + (banners.Count - 1) + ")" : "");
            bannerText.ToolTip = b.Item1;
            bannerAction.Text = b.Item2 ?? "";
            bannerAction.Visibility = b.Item2 == null ? Visibility.Collapsed : Visibility.Visible;
            bannerDo = b.Item3;
            var c = ((SolidColorBrush)b.Item4).Color;
            banner.Background = new SolidColorBrush(W.A(c, Theme.IsLight ? 0.10 : 0.12));
            banner.BorderBrush = new SolidColorBrush(W.A(c, 0.35));
            banner.Visibility = Visibility.Visible;
            if (rootGrid != null) rootGrid.Height = Ht + BannerSpace;
        }
    }
}
