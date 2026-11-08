// Редактор пользовательских списков (lists\*-user.txt): добавить, удалить, найти.
// Комментарии и заглушки (domain.example.abc, 203.0.113.113/32) сохраняются как были — service.bat не любит пустые файлы.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace ZapretGui
{
    class ListEditorForm : Form
    {
        readonly string path;
        readonly bool ipList;
        readonly List<string> keep = new List<string>();     // комментарии и заглушки
        readonly List<string> items = new List<string>();
        readonly TextField input, search;
        readonly ListBox box;
        readonly Label count, error;
        bool dirty;

        public bool Saved { get; private set; }
        public bool OpenInNotepad { get; private set; }

        public ListEditorForm(string file, string title, string description, bool ip, Icon icon)
        {
            path = file;
            ipList = ip;
            Func<float, int> P = Theme.P;
            Dialogs.Setup(this, title, icon, P(440), P(600));
            LoadFile();

            var head = new Label { Text = title, Font = Theme.Title, AutoSize = true, Location = new Point(P(20), P(16)), BackColor = Theme.Bg };
            var desc = new Label { Text = description, Font = Theme.Small, ForeColor = Theme.Muted, AutoSize = false, AutoEllipsis = true,
                Bounds = new Rectangle(P(20), P(46), P(400), P(20)), BackColor = Theme.Bg };

            input = new TextField(false) { Bounds = new Rectangle(P(20), P(78), P(296), P(38)) };
            Dialogs.Cue(input.Box, ip ? L.T("IP или подсеть, например 1.2.3.0/24", "IP or subnet, e.g. 1.2.3.0/24")
                                      : L.T("домен, например example.com", "domain, e.g. example.com"));
            input.Box.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; AddFromInput(); } };
            var add = new AccentButton(L.T("Добавить", "Add")) { Bounds = new Rectangle(P(324), P(78), P(96), P(38)) };
            add.Click += delegate { AddFromInput(); };
            error = new Label { Font = Theme.Caption, ForeColor = Theme.Danger, AutoSize = false, AutoEllipsis = true,
                Bounds = new Rectangle(P(22), P(120), P(398), P(18)), BackColor = Theme.Bg };

            search = new TextField(false) { Bounds = new Rectangle(P(20), P(142), P(400), P(34)) };
            Dialogs.Cue(search.Box, L.T("Поиск…", "Search…"));
            search.Box.TextChanged += delegate { Refill(); };

            var card = new Card { Bounds = new Rectangle(P(20), P(186), P(400), P(316)) };
            box = new ListBox
            {
                Bounds = new Rectangle(P(6), P(8), P(388), P(300)),
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Surface,
                ForeColor = Theme.Text,
                Font = Theme.Body,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = P(28),
                IntegralHeight = false,
                SelectionMode = SelectionMode.MultiExtended,
            };
            Dialogs.DarkScroll(box);
            box.DrawItem += DrawRow;
            box.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Delete) RemoveSelected();
                else if (e.KeyCode == Keys.A && e.Control) for (int i = 0; i < box.Items.Count; i++) box.SetSelected(i, true);
            };
            box.SelectedIndexChanged += delegate { UpdateCount(); };
            card.Controls.Add(box);

            var remove = new FlatLink(L.T("Удалить выбранные", "Remove selected")) { Location = new Point(P(20), P(512)) };
            remove.Click += delegate { RemoveSelected(); };
            count = new Label { Font = Theme.Small, ForeColor = Theme.Dim, AutoSize = false, TextAlign = ContentAlignment.TopRight,
                Bounds = new Rectangle(P(200), P(512), P(220), P(20)), BackColor = Theme.Bg };

            var notepad = new FlatLink(L.T("Открыть в Блокноте", "Open in Notepad")) { Location = new Point(P(20), P(560)) };
            notepad.Click += delegate { OpenInNotepad = true; Close(); };
            var save = new AccentButton(L.T("Сохранить", "Save")) { Bounds = new Rectangle(P(280), P(548), P(140), P(38)) };
            save.Click += delegate { Save(); };

            Controls.AddRange(new Control[] { head, desc, input, add, error, search, card, remove, count, notepad, save });
            Refill();
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); input.Box.Focus(); }

        // ---------- данные ----------

        static bool IsPlaceholder(string s)
        {
            return s.Equals(Zapret.DomainPlaceholder, StringComparison.OrdinalIgnoreCase) || s == Zapret.IpsetPlaceholder;
        }

        void LoadFile()
        {
            if (!File.Exists(path)) return;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string raw in File.ReadAllLines(path, Encoding.UTF8))
            {
                string t = raw.Trim();
                if (t == "") continue;
                if (t.StartsWith("#") || IsPlaceholder(t)) { keep.Add(t); continue; }
                if (seen.Add(t)) items.Add(t);
            }
        }

        void Save()
        {
            if (WriteFile()) DialogResult = DialogResult.OK;   // модальное окно закроется само
        }

        bool WriteFile()
        {
            var lines = new List<string>(keep);
            lines.AddRange(items);
            // файл не должен остаться пустым: если записей нет, оставляем заглушку
            if (!lines.Any(l => !l.StartsWith("#"))) lines.Add(ipList ? Zapret.IpsetPlaceholder : Zapret.DomainPlaceholder);
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                File.WriteAllText(path, string.Join("\r\n", lines) + "\r\n", new UTF8Encoding(false));
            }
            catch (Exception ex) { error.ForeColor = Theme.Danger; error.Text = ex.Message; return false; }
            dirty = false;
            Saved = true;
            return true;
        }

        // Приводит ввод к виду, который понимает zapret: без http://, пути и порта; кириллические домены — в punycode.
        string Normalize(string s, out string problem)
        {
            problem = null;
            s = s.Trim().Trim('"', '\'');
            if (ipList)
            {
                string ipPart = s, prefix = null;
                int slash = s.IndexOf('/');
                if (slash >= 0) { ipPart = s.Substring(0, slash); prefix = s.Substring(slash + 1); }
                IPAddress addr;
                int bits;
                int max = ipPart.Contains(":") ? 128 : 32;
                if (!IPAddress.TryParse(ipPart, out addr) || (!ipPart.Contains(":") && ipPart.Count(c => c == '.') != 3) ||
                    (prefix != null && (!int.TryParse(prefix, out bits) || bits < 0 || bits > max)))
                { problem = s; return null; }
                return addr.ToString() + (prefix != null ? "/" + prefix : "");
            }
            s = s.ToLowerInvariant();
            s = Regex.Replace(s, @"^[a-z]+://", "");
            int cut = s.IndexOfAny(new[] { '/', '?', '#' });
            if (cut >= 0) s = s.Substring(0, cut);
            int at = s.LastIndexOf('@');
            if (at >= 0) s = s.Substring(at + 1);
            int colon = s.IndexOf(':');
            if (colon >= 0) s = s.Substring(0, colon);
            s = s.Trim('.');
            if (s.StartsWith("*.")) s = s.Substring(2);
            try { s = new IdnMapping().GetAscii(s); } catch { problem = s; return null; }
            if (!Regex.IsMatch(s, @"^([a-z0-9_]([a-z0-9_-]*[a-z0-9_])?\.)+[a-z0-9-]{2,}$")) { problem = s; return null; }
            return s;
        }

        void AddFromInput()
        {
            var bad = new List<string>();
            int added = 0, dup = 0;
            foreach (string part in Regex.Split(input.Box.Text, @"[\s,;]+"))
            {
                if (part.Trim() == "") continue;
                string problem;
                string v = Normalize(part, out problem);
                if (v == null) { bad.Add(problem); continue; }
                if (items.Any(i => i.Equals(v, StringComparison.OrdinalIgnoreCase))) { dup++; continue; }
                items.Add(v);
                added++;
            }
            if (added > 0) { dirty = true; input.Box.Text = ""; search.Box.Text = ""; }
            Refill();
            if (added > 0) { box.ClearSelected(); box.SelectedIndex = box.Items.Count - 1; }
            if (bad.Count > 0) { error.ForeColor = Theme.Danger; error.Text = L.T("Не похоже на ", "Doesn't look like ") + (ipList ? "IP" : L.T("домен", "a domain")) + ": " + string.Join(", ", bad); }
            else if (dup > 0) { error.ForeColor = Theme.Warn; error.Text = L.T("Уже есть в списке", "Already in the list"); }
            else error.Text = "";
        }

        void RemoveSelected()
        {
            var sel = box.SelectedItems.Cast<string>().ToList();
            if (sel.Count == 0) return;
            foreach (string s in sel) items.Remove(s);
            dirty = true;
            Refill();
        }

        void Refill()
        {
            string q = search.Box.Text.Trim();
            box.BeginUpdate();
            box.Items.Clear();
            foreach (string s in items)
                if (q == "" || s.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0) box.Items.Add(s);
            box.EndUpdate();
            UpdateCount();
        }

        void UpdateCount()
        {
            string t = L.T("Записей: ", "Entries: ") + items.Count;
            if (box.Items.Count != items.Count) t += L.T(", найдено: ", ", found: ") + box.Items.Count;
            if (box.SelectedItems.Count > 1) t += L.T(", выбрано: ", ", selected: ") + box.SelectedItems.Count;
            if (dirty) t += L.T("  ·  не сохранено", "  ·  unsaved");
            count.Text = t;
        }

        void DrawRow(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0) return;
            bool sel = (e.State & DrawItemState.Selected) != 0;
            Color bg = sel ? Theme.AccentDeep : Theme.Surface;
            using (var b = new SolidBrush(Theme.Surface)) e.Graphics.FillRectangle(b, e.Bounds);
            if (sel)
            {
                e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                Theme.FillRound(e.Graphics, bg, new RectangleF(e.Bounds.X, e.Bounds.Y + 1, e.Bounds.Width - 2, e.Bounds.Height - 2), 8 * Theme.S);
            }
            var r = new Rectangle(e.Bounds.X + Theme.P(10), e.Bounds.Y, e.Bounds.Width - Theme.P(14), e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, (string)box.Items[e.Index], Theme.Body, r, sel ? Theme.Accent : Theme.Text, bg,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (dirty && !Saved)
            {
                var r = Ask.Show(this, L.T("Сохранить изменения в списке?", "Save changes to the list?"), Text,
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (r == DialogResult.Cancel) { e.Cancel = true; OpenInNotepad = false; return; }
                if (r == DialogResult.Yes && !WriteFile()) { e.Cancel = true; OpenInNotepad = false; return; }
            }
            base.OnFormClosing(e);
        }
    }
}
