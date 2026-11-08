// Окно «О программе»: версия, ссылки на zapret от Flowseal и на репозиторий GUI, лицензия, контрольная сумма exe.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ZapretGui
{
    class AboutForm : Form
    {
        public const string RepoUrl = "https://github.com/ryxIbtw/ZapretGUI";
        public const string ZapretUrl = "https://github.com/Flowseal/zapret-discord-youtube";
        public const string LicenseUrl = "https://github.com/ryxIbtw/ZapretGUI/blob/main/LICENSE";

        public AboutForm(Icon icon, string zapretVersion)
        {
            Func<float, int> P = Theme.P;
            int W = P(380);
            Dialogs.Setup(this, L.T("О программе", "About"), icon, W, P(498));

            int logo = P(64);
            var pic = new PictureBox { Image = Theme.Logo(logo, Theme.Accent, Theme.Lerp(Theme.Accent, Color.Black, 0.28f), Color.Empty),
                Bounds = new Rectangle((W - logo) / 2, P(22), logo, logo), BackColor = Theme.Bg };
            var name = new Label { Text = "Zapret GUI", Font = Theme.Status, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false,
                Bounds = new Rectangle(0, P(94), W, P(32)), BackColor = Theme.Bg };
            var ver = new Label { Text = L.T("Версия ", "Version ") + SelfUpdate.Current.ToString(3) + "  ·  GUI by " + BrandTitle.Author,
                Font = Theme.Small, ForeColor = Theme.Muted, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false,
                Bounds = new Rectangle(0, P(126), W, P(20)), BackColor = Theme.Bg };
            Controls.AddRange(new Control[] { pic, name, ver });

            var repo = new SettingRow(L.T("Репозиторий ZapretGUI", "ZapretGUI repository"), false) { Nav = true, Hint = "github.com/ryxIbtw/ZapretGUI" };
            var zapret = new SettingRow("zapret-discord-youtube", false) { Nav = true,
                Hint = "by " + BrandTitle.ZapretAuthor + (zapretVersion != "" ? L.T("  ·  установлена v", "  ·  installed v") + zapretVersion : "") };
            var lic = new SettingRow(L.T("Лицензия", "License"), false) { Nav = true, Hint = "MIT  ·  © 2026 " + BrandTitle.Author };
            repo.Click += delegate { Open(RepoUrl); };
            zapret.Click += delegate { Open(ZapretUrl); };
            lic.Click += delegate { Open(LicenseUrl); };
            var rows = new[] { repo, zapret, lic };
            var card = new Card { Bounds = new Rectangle(P(20), P(162), W - P(40), rows.Length * P(52) + P(8)) };
            for (int i = 0; i < rows.Length; i++)
            {
                rows[i].Bounds = new Rectangle(P(4), P(4) + i * P(52), card.Width - P(8), P(52));
                rows[i].Divider = i > 0;
                card.Controls.Add(rows[i]);
            }
            Controls.Add(card);

            var note = new Label { Text = L.T("Неофициальный интерфейс. Сам zapret-discord-youtube принадлежит его авторам.",
                                              "Unofficial GUI. zapret-discord-youtube itself belongs to its authors."),
                Font = Theme.Caption, ForeColor = Theme.Dim, TextAlign = ContentAlignment.TopCenter, AutoSize = false,
                Bounds = new Rectangle(P(20), P(336), W - P(40), P(34)), BackColor = Theme.Bg };
            Controls.Add(note);

            // контрольная сумма этого exe — чтобы сверить с файлом ZapretGUI.exe.sha256 в релизе
            string hash = SelfUpdate.FileSha256(Application.ExecutablePath) ?? "—";
            var hashTitle = new Label { Text = L.T("SHA256 этого файла", "SHA256 of this file"), Font = Theme.Caption, ForeColor = Theme.Muted,
                AutoSize = true, Location = new Point(P(22), P(378)), BackColor = Theme.Bg };
            var hashBox = new Card { Bounds = new Rectangle(P(20), P(398), W - P(40), P(48)) };
            var hashText = new Label { Text = hash.Length == 64 ? hash.Substring(0, 32) + "\n" + hash.Substring(32) : hash,
                Font = Theme.MonoSmall, ForeColor = Theme.Text, BackColor = Theme.Surface, AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter, Bounds = new Rectangle(P(8), P(4), hashBox.Width - P(16), P(40)) };
            hashBox.Controls.Add(hashText);
            var copy = new FlatLink(L.T("Копировать", "Copy"));
            copy.Click += delegate { Dialogs.Copy(hash); copy.Text = L.T("Скопировано", "Copied"); };
            Controls.AddRange(new Control[] { hashTitle, hashBox, copy });
            copy.Location = new Point(W - P(20) - copy.PreferredSize.Width, P(378));

            var close = new FlatLink(L.T("Закрыть", "Close"));
            close.Click += delegate { Close(); };
            Controls.Add(close);
            close.Location = new Point((W - close.PreferredSize.Width) / 2, P(464));
        }

        static void Open(string url)
        {
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { }
        }
    }
}
