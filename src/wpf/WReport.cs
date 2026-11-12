// Окно «Сообщить о проблеме»: описание + сведения о системе и журнал → новая issue на GitHub.
// Текст виден целиком до отправки; путь к папке пользователя заменяется на %USERPROFILE%.
using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace ZapretGui
{
    class ReportW : WinBase
    {
        public const string IssuesUrl = "https://github.com/ryxIbtw/ZapretGUI/issues/new";
        const int JournalLines = 60;
        const int MaxUrl = 7000;   // длиннее ссылку браузер или GitHub может не принять

        readonly TextBox what, details;
        readonly TextBlock note;

        public ReportW(Window owner, Func<string> header) : base(L.T("Сообщить о проблеме", "Report a problem"), owner)
        {
            var sp = new StackPanel { Width = 520, Margin = new Thickness(20, 16, 20, 16) };
            sp.Children.Add(W.T(Title, W.Title, W.Text, true));
            var lead = W.T(L.T("Откроется страница новой issue на GitHub с этим текстом. Отправляете вы сами, там же можно всё поправить.",
                "A new GitHub issue page opens with this text. You send it yourself and can edit it there."), W.Small, W.Muted);
            lead.TextWrapping = TextWrapping.Wrap; lead.Margin = new Thickness(0, 4, 0, 12);
            sp.Children.Add(lead);

            sp.Children.Add(W.T(L.T("Что случилось?", "What happened?"), W.Body, W.Text));
            what = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, Height = 90, Margin = new Thickness(0, 6, 0, 12),
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Tag = L.T("например: после обновления не открывается YouTube, пробовал стратегии ALT и ALT2", "e.g. YouTube stopped working after the update, tried ALT and ALT2") };
            sp.Children.Add(what);

            sp.Children.Add(W.T(L.T("Что будет приложено", "What will be attached"), W.Body, W.Text));
            details = new TextBox { IsReadOnly = true, TextWrapping = TextWrapping.Wrap, AcceptsReturn = true, Height = 200, FontFamily = W.MonoFont, FontSize = 11.5,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Margin = new Thickness(0, 6, 0, 0) };
            details.Text = Hide(header() + "\r\n\r\n" + L.T("Журнал (последние записи):", "Log (latest entries):") + "\r\n" +
                string.Join("\r\n", Journal.Read().Skip(Math.Max(0, Journal.Read().Count - JournalLines))));
            sp.Children.Add(details);
            var priv = W.T(L.T("Паролей и личных файлов здесь нет. Путь к папке пользователя заменён на %USERPROFILE%.",
                "No passwords or personal files. Your user folder path is replaced with %USERPROFILE%."), W.Caption, W.Muted);
            priv.TextWrapping = TextWrapping.Wrap; priv.Margin = new Thickness(0, 6, 0, 0);
            sp.Children.Add(priv);

            var bar = new Grid { Margin = new Thickness(0, 14, 0, 0) };
            note = W.T("", W.Small, W.Accent);
            note.TextWrapping = TextWrapping.Wrap; note.VerticalAlignment = VerticalAlignment.Center; note.Margin = new Thickness(0, 0, 190, 0);
            bar.Children.Add(note);
            var go = new Pill(L.T("Открыть GitHub", "Open GitHub")) { MinWidth = 170, HorizontalAlignment = HorizontalAlignment.Right };
            go.Click += delegate { Send(); };
            bar.Children.Add(go);
            sp.Children.Add(bar);
            Root.Child = sp;
            Loaded += delegate { what.Focus(); };
        }

        // Путь к папке пользователя и имя пользователя не уходят в issue.
        static string Hide(string s)
        {
            try
            {
                string prof = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                if (!string.IsNullOrEmpty(prof)) s = s.Replace(prof, "%USERPROFILE%");
                string user = Environment.UserName;
                if (!string.IsNullOrEmpty(user) && user.Length >= 3)
                    s = System.Text.RegularExpressions.Regex.Replace(s, @"\\" + System.Text.RegularExpressions.Regex.Escape(user) + @"(?=\\|\b)", @"\%USERNAME%",
                        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }
            catch { }
            return s;
        }

        void Send()
        {
            string desc = what.Text.Trim();
            string firstLine = desc.Split('\n')[0].Trim();
            string title = firstLine.Length == 0 ? L.T("Проблема", "Problem") : firstLine.Length > 80 ? firstLine.Substring(0, 80) + "…" : firstLine;
            string full = (desc.Length > 0 ? desc + "\n\n" : "") + "<details><summary>" + L.T("Сведения о системе и журнал", "System info and log") + "</summary>\n\n```\n" +
                details.Text.Replace("\r\n", "\n") + "\n```\n</details>\n";
            try { Clipboard.SetText(full); } catch { }
            string url = IssuesUrl + "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(full);
            bool fits = url.Length <= MaxUrl;
            if (!fits)
            {
                string shortBody = (desc.Length > 0 ? desc + "\n\n" : "") + L.T("<!-- Сведения о системе и журнал скопированы: вставьте их сюда (Ctrl+V) вместо этой строки -->",
                    "<!-- System info and log were copied: paste them here (Ctrl+V) instead of this line -->");
                url = IssuesUrl + "?title=" + Uri.EscapeDataString(title) + "&body=" + Uri.EscapeDataString(shortBody);
            }
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
            note.Foreground = W.Accent;
            note.Text = fits ? L.T("Страница открыта в браузере. Текст ещё и в буфере обмена.", "The page is open in your browser. The text is also in the clipboard.")
                             : L.T("Страница открыта. Сведения в буфере обмена — вставьте их в issue (Ctrl+V).", "The page is open. The details are in the clipboard — paste them into the issue (Ctrl+V).");
            Journal.Add(L.T("Открыта страница «Сообщить о проблеме» на GitHub", "Opened the GitHub “Report a problem” page"));
        }
    }
}
