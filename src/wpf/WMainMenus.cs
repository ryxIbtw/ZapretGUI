// Меню главного окна: стратегии, игровой фильтр, IPSet, тема, язык, fake-файлы, «Ещё», списки.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using WF = System.Windows.Forms;

namespace ZapretGui
{
    partial class MainWindow
    {
        // ================= Меню =================

        internal System.Windows.Controls.ContextMenu ShowStrategyMenu(FrameworkElement anchor, bool above)
        {
            if (busy) return null;
            strategies = z.Strategies();
            var m = WMenu.Create();
            var fav = Favorites;
            foreach (string s in strategies)
            {
                string name = s;
                string note = Note(name);
                WMenu.Add(m, (fav.Contains(name) ? "★ " : "") + name + (note != "" ? "   ·  " + note : ""), name == selected, () => SelectStrategy(name));
            }
            WMenu.Sep(m);
            string cur = selected;
            WMenu.Add(m, fav.Contains(cur) ? L.T("Убрать «" + cur + "» из избранных", "Remove “" + cur + "” from favorites")
                                           : L.T("★ Добавить «" + cur + "» в избранные", "★ Add “" + cur + "” to favorites"), false, () => { ToggleFavorite(cur); UpdateUi(); });
            WMenu.Add(m, L.T("Сравнить «" + cur + "» с другой…", "Compare “" + cur + "” with another…"), false, () => AutoPick(new List<string> { cur }));
            WMenu.Add(m, L.T("Все стратегии…", "All strategies…"), false, ShowStrategies);
            m.MaxHeight = 420;   // длиннее — с прокруткой
            m.Opened += delegate
            {
                // сразу показать выбранную стратегию, даже если она внизу списка
                foreach (var it in m.Items)
                {
                    var mi = it as System.Windows.Controls.MenuItem;
                    if (mi != null && mi.IsChecked) { mi.BringIntoView(); break; }
                }
            };
            if (above) WMenu.ShowAbove(m, anchor); else WMenu.ShowBelow(m, anchor);
            return m;
        }

        void ShowGameMenu()
        {
            if (busy) return;
            var m = WMenu.Create();
            foreach (string mode in new[] { "disabled", "all", "tcp", "udp" })
            {
                string md = mode;
                WMenu.Add(m, GameLabel(md), z.GameMode == md, () =>
                {
                    if (z.GameMode == md) return;
                    z.GameMode = md; z.SaveGame();
                    Journal.Add(L.T("Игровой фильтр: ", "Game filter: ") + GameLabel(md));
                    UpdateUi();
                    RestartIfActive();
                });
            }
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Порты: TCP ", "Ports: TCP ") + z.GameTcp + ", UDP " + z.GameUdp + "…", false, EditGamePorts);
            WMenu.ShowBelowRight(m, rowGame);
        }

        void EditGamePorts()
        {
            string tcp = InputW.Show(this, L.T("Порты TCP для игрового фильтра", "TCP ports for the game filter"),
                L.T("Например: 1024-65535 или 1024-1934,1936-65535", "E.g. 1024-65535 or 1024-1934,1936-65535"), z.GameTcp);
            if (tcp == null) return;
            string udp = InputW.Show(this, L.T("Порты UDP для игрового фильтра", "UDP ports for the game filter"), L.T("Например: 1024-65535", "E.g. 1024-65535"), z.GameUdp);
            if (udp == null) return;
            tcp = tcp.Replace(" ", ""); udp = udp.Replace(" ", "");
            if (!Zapret.ValidRange(tcp) || !Zapret.ValidRange(udp)) { Toast(L.T("Неверный формат портов", "Invalid port format"), true); return; }
            z.GameTcp = tcp; z.GameUdp = udp; z.SaveGame();
            Journal.Add(L.T("Порты игрового фильтра: TCP ", "Game filter ports: TCP ") + tcp + ", UDP " + udp);
            Toast(L.T("Порты сохранены", "Ports saved"));
            if (z.GameMode != "disabled") RestartIfActive();
        }

        void ShowIpsetMenu()
        {
            if (busy) return;
            var m = WMenu.Create();
            string cur = z.IpsetMode();
            var items = new[] {
                new[] { "loaded", L.T("По списку (ipset-all)", "List (ipset-all)") },
                new[] { "none", L.T("Выкл", "Off") },
                new[] { "any", L.T("Все IP-адреса", "Any IP address") } };
            foreach (var it in items)
            {
                string md = it[0];
                WMenu.Add(m, it[1], cur == md, () =>
                {
                    try { z.SetIpset(md); } catch (Exception ex) { Toast(ex.Message, true); return; }
                    Journal.Add("IPSet: " + IpsetLabel(md));
                    UpdateUi();
                    RestartIfActive();
                });
            }
            WMenu.ShowBelowRight(m, rowIpset);
        }

        void ShowLangMenu()
        {
            var m = WMenu.Create();
            WMenu.Add(m, "Русский", !L.En, () => SetLang(false));
            WMenu.Add(m, "English", L.En, () => SetLang(true));
            WMenu.ShowBelowRight(m, rowLang);
        }

        // Меню «Оформление»: тема, цвет акцента (с цветными кружками), масштаб.
        void ShowThemeMenu()
        {
            var m = WMenu.Create();
            string mode = ThemeMode;
            WMenu.Header(m, L.T("Тема", "Theme"));
            foreach (string md in Theme.Modes) { string x = md; WMenu.Add(m, ThemeLabel(x), mode == x, () => SetThemeMode(x)); }
            WMenu.Sep(m);
            WMenu.Header(m, L.T("Цвет", "Color"));
            foreach (string ac in Theme.Accents)
            {
                string x = ac;
                var it = WMenu.Add(m, Theme.AccentLabel(x), Theme.AccentName == x, () => SetAccent(x));
                if (x == "auto")
                {
                    // половина зелёная, половина синяя — «авто» = зелёный в тёмной теме, синий в светлой
                    var lg = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
                    lg.GradientStops.Add(new GradientStop(Color.FromRgb(52, 211, 153), 0.5));
                    lg.GradientStops.Add(new GradientStop(Color.FromRgb(37, 99, 235), 0.5));
                    it.Icon = new System.Windows.Shapes.Ellipse { Width = 12, Height = 12, Fill = lg };
                }
                else it.Icon = WMenu.Dot(W.C(Theme.AccentSample(x)));
            }
            WMenu.Sep(m);
            WMenu.Header(m, L.T("Масштаб", "Scale"));
            int cur = UiScalePref;
            foreach (int sc in Theme.Scales) { int x = sc; WMenu.Add(m, x + "%", cur == x, () => SetScale(x)); }
            WMenu.ShowBelowRight(m, rowTheme);
        }

        void SetThemeMode(string mode)
        {
            if (mode == ThemeMode) return;
            Prefs.Set("Theme", mode);
            Theme.ApplyMode(mode);
            Recolor();
        }

        void SetAccent(string name)
        {
            if (name == "custom")
            {
                // свой цвет — в окне в стиле программы
                var cw = new ColorW(this, Theme.CustomRgb);
                cw.ShowDialog();
                if (cw.Rgb < 0) return;
                Prefs.Set("AccentCustom", cw.Rgb.ToString("X6"));
                Prefs.Set("Accent", name);
                Theme.AccentName = name;
                Theme.Apply(Theme.IsLight);
                Recolor();
                return;
            }
            if (name == Theme.AccentName) return;
            Prefs.Set("Accent", name);
            Theme.AccentName = name;
            Theme.Apply(Theme.IsLight);
            Recolor();
        }

        void SetScale(int percent)
        {
            if (percent == UiScalePref) return;
            Prefs.Set("Scale", percent.ToString());
            Theme.SetScale(1f, percent / 100f);
            rootScale.ScaleX = rootScale.ScaleY = Theme.UiScale;
            UpdateUi();
        }

        void SetLang(bool en)
        {
            if (en == L.En) return;
            Prefs.Set("Lang", en ? "en" : "ru");
            L.En = en;
            BuildUi();   // все подписи заново
            UpdateUi();
        }

        void ShowFakesMenu()
        {
            if (busy) return;
            var fakes = z.FakeFiles();
            if (fakes.Count == 0) { Toast(L.T("В папке bin нет .bin файлов", "No .bin files in the bin folder"), true); return; }
            var m = WMenu.Create();
            AddFakeSection(m, "Discord UDP", Zapret.FakeDiscord, fakes);
            WMenu.Sep(m);
            AddFakeSection(m, L.T("Игровой фильтр UDP", "Game filter UDP"), Zapret.FakeGame, fakes);
            m.MaxHeight = 480;
            WMenu.ShowBelowRight(m, rowFakes);
        }

        void AddFakeSection(System.Windows.Controls.ContextMenu m, string title, string active, List<string> fakes)
        {
            WMenu.Header(m, title);
            string cur = null;
            try { cur = z.CurrentFake(active); } catch { }
            foreach (string f in fakes)
            {
                string name = f;
                WMenu.Add(m, name, name == cur, () =>
                {
                    try { z.SetFake(active, name); } catch (Exception ex) { Toast(ex.Message, true); return; }
                    Journal.Add(L.T("Fake-файл ", "Fake file ") + title + ": " + name);
                    Toast(title + ": " + name);
                    RestartIfActive();
                });
            }
        }

        // Меню «Ещё»: по группам, чтобы не было одного длинного списка.
        void ShowMoreMenu(FrameworkElement anchor)
        {
            var m = WMenu.Create();
            var lists = WMenu.Sub(m, L.T("Списки и сайты", "Lists and sites"));
            WMenu.AddTo(lists, L.T("Мои домены — тоже через обход", "My domains — also bypassed"), false, () => EditList("list-general-user.txt"));
            WMenu.AddTo(lists, L.T("Исключения — сайты без обхода", "Exclusions — sites left alone"), false, () => EditList("list-exclude-user.txt"));
            WMenu.AddTo(lists, L.T("IP-исключения — адреса без обхода", "IP exclusions — IPs left alone"), false, () => EditList("ipset-exclude-user.txt"));
            WMenu.AddTo(lists, L.T("Проверить сайт…", "Check a site…"), false, CheckSite);
            WMenu.AddTo(lists, L.T("Добавить сайт из буфера обмена", "Add a site from the clipboard"), false, AddFromClipboard);
            WMenu.AddTo(lists, L.T("Обновить список IPSet", "Update IPSet list"), false, UpdateIpset);

            var strat = WMenu.Sub(m, L.T("Стратегии", "Strategies"));
            WMenu.AddTo(strat, L.T("Все стратегии, избранные и заметки", "All strategies, favorites and notes"), false, ShowStrategies);
            WMenu.AddTo(strat, L.T("Результаты прошлого подбора", "Last strategy test results"), false, ShowLastPick);
            MoreStrategyItems(strat);

            WMenu.Add(m, L.T("Профили…", "Profiles…"), false, () => ShowProfilesMenu(anchor));
            WMenu.Add(m, Paused ? L.T("Пауза…", "Pause…") : L.T("Пауза обхода…", "Pause the bypass…"), false, () => ShowPauseMenu(anchor)).IsEnabled = Active || Paused;
            WMenu.Sep(m);
            WMenu.Add(m, L.T("Статистика", "Statistics"), false, ShowStats);
            WMenu.Add(m, L.T("Журнал событий", "Event log"), false, ShowJournal);
            WMenu.Add(m, L.T("Диагностика", "Diagnostics"), false, OpenDiag);
            WMenu.Sep(m);
            var zp = WMenu.Sub(m, "zapret");
            WMenu.AddTo(zp, L.T("Классическое меню (service.bat)", "Classic menu (service.bat)"), false, OpenServiceBat);
            WMenu.AddTo(zp, L.T("Открыть папку zapret", "Open zapret folder"), false, () => Shell("explorer.exe", "\"" + z.Root + "\""));
            WMenu.AddTo(zp, L.T("Страница zapret на GitHub", "zapret on GitHub"), false, () => Shell(AboutW.ZapretUrl, null));
            var help = WMenu.Sub(m, L.T("Помощь", "Help"));
            WMenu.AddTo(help, L.T("Справка: частые вопросы", "Help: common questions"), false, () => new HelpW(this).ShowDialog());
            WMenu.AddTo(help, L.T("Обсуждения на GitHub", "Discussions on GitHub"), false, () => Shell(HelpW.DiscussionsUrl, null));
            WMenu.AddTo(help, L.T("Сообщить о проблеме", "Report a problem"), false, ShowReport);
            WMenu.AddTo(help, L.T("Страница ZapretGUI на GitHub", "ZapretGUI on GitHub"), false, () => Shell(AboutW.RepoUrl, null));
            WMenu.AddTo(help, L.T("О программе", "About"), false, ShowAbout);
            WMenu.ShowAbove(m, anchor);
        }
        void EditList(string file)
        {
            try { z.EnsureUserLists(); } catch { }
            string path = Path.Combine(z.Lists, file);
            string title, desc;
            bool ip = file.StartsWith("ipset", StringComparison.OrdinalIgnoreCase);
            if (file == "list-general-user.txt")
            { title = L.T("Мои домены", "My domains"); desc = L.T("Эти сайты тоже пойдут через обход  ·  lists\\", "These sites also go through the bypass  ·  lists\\") + file; }
            else if (file == "list-exclude-user.txt")
            { title = L.T("Исключения", "Exclusions"); desc = L.T("Эти сайты обход не трогает  ·  lists\\", "The bypass leaves these sites alone  ·  lists\\") + file; }
            else
            { title = L.T("IP-исключения", "IP exclusions"); desc = L.T("Эти IP-адреса обход не трогает  ·  lists\\", "The bypass leaves these IPs alone  ·  lists\\") + file; }

            var w = new ListEditorW(this, path, title, desc, ip);
            w.ShowDialog();
            if (w.Saved) Journal.Add(L.T("Список изменён: ", "List changed: ") + file);
            if (w.OpenInNotepad)
            {
                Shell("notepad.exe", "\"" + path + "\"");
                Toast(L.T("После сохранения списка перезапустите обход", "Restart the bypass after saving the list"));
                return;
            }
            if (!w.Saved) return;
            if (Active && !busy && AskW.Show(this, L.T("Список сохранён. Перезапустить обход, чтобы изменения заработали?", "List saved. Restart the bypass to apply the changes?"),
                    title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes) Start();
            else Toast(L.T("Список сохранён", "List saved"));
        }
    }
}
