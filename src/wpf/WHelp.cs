// Справка: короткие ответы на частые вопросы, ссылки на обсуждения и issue.
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ZapretGui
{
    class HelpW : WinBase
    {
        public const string DiscussionsUrl = "https://github.com/ryxIbtw/ZapretGUI/discussions";

        public HelpW(Window owner) : base(L.T("Справка", "Help"), owner)
        {
            var sp = new StackPanel { Width = 520, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            var lead = W.T(L.T("Нажмите на вопрос, чтобы увидеть ответ.", "Click a question to see the answer."), W.Small, W.Muted);
            lead.Margin = new Thickness(0, 4, 0, 10);
            sp.Children.Add(lead);
            var list = new StackPanel();
            string[][] faq = {
                new[] { L.T("Обход включён, но YouTube не открывается", "The bypass is on, but YouTube doesn't open"),
                        L.T("Нажмите «Подобрать стратегию» — программа проверит все стратегии и покажет лучшую. Если не помогло, откройте «Диагностику»: часто мешают VPN, прокси, AdGuard или другой обход.",
                            "Click “Find a strategy” — the app tests all strategies and shows the best. If that doesn't help, open Diagnostics: VPNs, proxies, AdGuard or another bypass often interfere.") },
                new[] { L.T("Что такое стратегия?", "What is a strategy?"),
                        L.T("Набор настроек zapret (файл .bat в папке zapret). Разные провайдеры блокируют по-разному, поэтому у каждого работает своя стратегия.",
                            "A set of zapret settings (a .bat file in the zapret folder). Providers block differently, so each needs its own strategy.") },
                new[] { L.T("Работает YouTube, но не Discord (или наоборот)", "YouTube works but Discord doesn't (or vice versa)"),
                        L.T("Попробуйте другую стратегию стрелками. Для голоса в Discord и игр включите «Игровой фильтр». Если Discord завис на подключении — «Диагностика» → «Очистить кэш Discord».",
                            "Try another strategy with the arrows. For Discord voice and games turn on the Game filter. If Discord is stuck connecting — Diagnostics → Clear Discord cache.") },
                new[] { L.T("Нужен ли другой сайт кроме YouTube и Discord?", "I need another site besides YouTube and Discord"),
                        L.T("«Списки» → «Проверить сайт». Если он не открывается, добавьте его в «Мои домены» — он тоже пойдёт через обход.",
                            "Lists → Check a site. If it doesn't open, add it to My domains so it also goes through the bypass.") },
                new[] { L.T("Что такое IPSet и игровой фильтр?", "What are IPSet and the game filter?"),
                        L.T("IPSet — обход по IP-адресам из списка, а не только по названиям сайтов. Игровой фильтр — обход ещё и для портов игр. Наведите на «?» рядом с ними, чтобы увидеть подробности.",
                            "IPSet — bypass by IP addresses from a list. The game filter also covers game ports. Hover the “?” next to them for details.") },
                new[] { L.T("Как сделать, чтобы обход включался сам?", "How do I make the bypass start by itself?"),
                        L.T("Включите «Обход с Windows» — zapret станет службой и будет работать даже без программы. Чтобы программа сама открывалась в трее — «Программа с Windows».",
                            "Turn on “Bypass with Windows” — zapret becomes a service and runs even without the app. To have the app open in the tray — “App with Windows”.") },
                new[] { L.T("Безопасно ли это?", "Is it safe?"),
                        L.T("Программа ничего не отправляет о вас. Она только запускает zapret от Flowseal и проверяет, открываются ли сайты. Код открыт на GitHub.",
                            "The app sends nothing about you. It only runs zapret by Flowseal and checks whether sites open. The code is open on GitHub.") },
                new[] { L.T("Антивирус ругается на winws.exe или WinDivert", "My antivirus complains about winws.exe or WinDivert"),
                        L.T("Это частая ложная тревога: WinDivert перехватывает трафик, как и вирусы. Добавьте папку zapret в исключения антивируса.",
                            "A common false alarm: WinDivert intercepts traffic, like malware does. Add the zapret folder to the antivirus exclusions.") },
                new[] { L.T("Как удалить всё?", "How do I remove everything?"),
                        L.T("Настройки → «Удалить всё». Служба, задачи и настройки ZapretGUI удалятся, папка zapret останется — её можно удалить вручную.",
                            "Settings → Remove everything. The service, tasks and ZapretGUI settings are removed; delete the zapret folder by hand if you want.") },
            };
            foreach (var qa in faq)
            {
                var answer = W.T(qa[1], W.Small, W.Muted);
                answer.TextWrapping = TextWrapping.Wrap; answer.TextTrimming = TextTrimming.None; answer.Visibility = Visibility.Collapsed; answer.Margin = new Thickness(0, 6, 0, 2);
                var st = new StackPanel();
                var qt = W.T(qa[0], W.Body, W.Text); qt.TextWrapping = TextWrapping.Wrap; qt.TextTrimming = TextTrimming.None;
                st.Children.Add(qt); st.Children.Add(answer);
                var card = new Clickable { Background = W.Surface, CornerRadius = new CornerRadius(12), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 0, 0, 6), Child = st };
                card.Click += delegate { answer.Visibility = answer.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible; };
                list.Children.Add(card);
            }
            sp.Children.Add(new ScrollViewer { Content = list, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var bar = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 0) };
            var disc = new FlatLink(L.T("Обсуждения на GitHub", "Discussions on GitHub")) { Margin = new Thickness(0, 0, 16, 0) };
            disc.Click += delegate { Open(DiscussionsUrl); };
            var issue = new FlatLink(L.T("Сообщить о проблеме", "Report a problem"));
            issue.Click += delegate
            {
                var mw = owner as MainWindow;   // сведения о системе берутся у главного окна
                Func<string> header = () => mw != null ? mw.JournalHeaderPublic() : "";
                Close();
                new ReportW(owner, header).ShowDialog();
            };
            bar.Children.Add(disc); bar.Children.Add(issue);
            sp.Children.Add(bar);
            Root.Child = sp;
        }

        static void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } }
    }
}
