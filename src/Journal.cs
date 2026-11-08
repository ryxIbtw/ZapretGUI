// Журнал событий: что и когда делала программа. Файл %APPDATA%\ZapretGUI\events.log, последние ~500 записей.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;

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
            get { return PathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ZapretGUI", "events.log"); }
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

    class JournalForm : Form
    {
        readonly TextField field;
        readonly Label summary;
        readonly Func<string> header;
        readonly Action<string> onAdded;

        public JournalForm(Icon icon, Func<string> headerText)
        {
            header = headerText;
            Func<float, int> P = Theme.P;
            Dialogs.Setup(this, L.T("Журнал событий", "Event log"), icon, P(560), P(560));

            var title = new Label { Text = L.T("Журнал событий", "Event log"), Font = Theme.Title, AutoSize = true, Location = new Point(P(20), P(16)), BackColor = Theme.Bg };
            summary = new Label { Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true,
                Bounds = new Rectangle(P(20), P(46), P(520), P(20)), BackColor = Theme.Bg };
            field = new TextField(true) { Bounds = new Rectangle(P(20), P(74), P(520), P(420)) };
            field.Box.ReadOnly = true;
            field.Box.BackColor = Theme.Surface;     // у ReadOnly поля Windows иначе делает серый фон
            field.Box.Font = Theme.Mono;
            field.Box.WordWrap = true;               // длинные строки переносятся, время в начале строки всегда видно

            var copy = new AccentButton(L.T("Копировать", "Copy")) { Bounds = new Rectangle(P(400), P(508), P(140), P(36)) };
            copy.Click += delegate
            {
                Dialogs.Copy(header() + "\r\n\r\n" + string.Join("\r\n", Journal.Read()));
                summary.ForeColor = Theme.Accent;
                summary.Text = L.T("Скопировано вместе со сведениями о системе — можно вставить в сообщение", "Copied with system info — paste it into a message");
            };
            var open = new FlatLink(L.T("Открыть файл", "Open file")) { Location = new Point(P(20), P(518)) };
            open.Click += delegate
            {
                try
                {
                    if (!File.Exists(Journal.FilePath)) Journal.Add(L.T("Журнал создан", "Log created"));
                    Process.Start(new ProcessStartInfo("notepad.exe", "\"" + Journal.FilePath + "\"") { UseShellExecute = true });
                }
                catch { }
            };
            Controls.AddRange(new Control[] { title, summary, field, copy, open });

            onAdded = line => { if (IsHandleCreated && !IsDisposed) try { BeginInvoke((MethodInvoker)Fill); } catch { } };
            Journal.Added += onAdded;
            FormClosed += delegate { Journal.Added -= onAdded; };
            Fill();
        }

        void Fill()
        {
            var lines = Journal.Read();
            field.Box.Text = lines.Count == 0 ? L.T("Пока записей нет.", "No entries yet.") : string.Join("\r\n", lines);
            field.Box.SelectionStart = field.Box.TextLength;
            field.Box.ScrollToCaret();
            summary.ForeColor = Theme.Muted;
            summary.Text = L.T("Записей: " + lines.Count + "  ·  хранятся последние " + Journal.Keep,
                               "Entries: " + lines.Count + "  ·  the last " + Journal.Keep + " are kept");
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            field.Box.SelectionStart = field.Box.TextLength;
            field.Box.ScrollToCaret();
        }
    }
}
