using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Zapret GUI")]
[assembly: AssemblyProduct("Zapret GUI by ryxIbtw")]
[assembly: AssemblyCompany("ryxIbtw")]
[assembly: AssemblyCopyright("© ryxIbtw")]
[assembly: AssemblyDescription("Интерфейс для zapret-discord-youtube by Flowseal. GUI by ryxIbtw")]
[assembly: AssemblyVersion("1.5.0.0")]
[assembly: AssemblyFileVersion("1.5.0.0")]

namespace ZapretGui
{
    static class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            L.Init();
            string rootArg = null, snap = null;
#if TESTHOOKS
            string dump = null, dumpOut = null;
#endif
            bool hidden = false;
            int waitPid = 0;
            for (int i = 0; i < args.Length; i++)
            {
                if (args[i] == "--tray") hidden = true;
                else if (args[i] == "--root" && i + 1 < args.Length) rootArg = args[++i];
                else if (args[i] == "--wait-pid" && i + 1 < args.Length) int.TryParse(args[++i], out waitPid);
#if TESTHOOKS
                else if (args[i] == "--snap" && i + 1 < args.Length) snap = args[++i];
                else if (args[i] == "--dump" && i + 2 < args.Length) { dump = args[++i]; dumpOut = args[++i]; }
#endif
            }

            // после самообновления: дождаться, пока старая копия закроется, и убрать её файл
            if (waitPid > 0)
            {
                try { System.Diagnostics.Process.GetProcessById(waitPid).WaitForExit(15000); } catch { }
            }
            SelfUpdate.CleanupOld();

            bool first;
            using (var mutex = new Mutex(true, "ZapretGUI_SingleInstance", out first))
            {
                if (!first && snap == null)
                {
                    PostMessage((IntPtr)0xFFFF, MainForm.WM_SHOWME, IntPtr.Zero, IntPtr.Zero);
                    return 0;
                }

                string root = FindRoot(rootArg);
                if (root == null) return 1;
                var z = new Zapret(root);

#if TESTHOOKS
                if (dump != null)
                {
                    z.LoadGame();
                    File.WriteAllText(dumpOut, z.BuildArgs(dump));
                    return 0;
                }
#endif

                Application.Run(new MainForm(z, snap, hidden));
            }
            return 0;
        }

        static string FindRoot(string arg)
        {
            string exeDir = Path.GetDirectoryName(Application.ExecutablePath);
            foreach (string c in new[] { arg, exeDir, Prefs.Get("Root", null) })
            {
                if (Zapret.IsValidRoot(c)) { Prefs.Set("Root", Path.GetFullPath(c)); return Path.GetFullPath(c); }
            }

            MessageBox.Show(L.T("Не найдена папка zapret-discord-youtube.\n\nПоложите ZapretGUI.exe рядом с service.bat " +
                "или укажите папку вручную в следующем окне.",
                "zapret-discord-youtube folder not found.\n\nPut ZapretGUI.exe next to service.bat or pick the folder in the next window."),
                "Zapret", MessageBoxButtons.OK, MessageBoxIcon.Information);
            while (true)
            {
                using (var d = new FolderBrowserDialog { Description = L.T("Папка zapret-discord-youtube (где лежит service.bat)", "zapret-discord-youtube folder (with service.bat)") })
                {
                    if (d.ShowDialog() != DialogResult.OK) return null;
                    if (Zapret.IsValidRoot(d.SelectedPath)) { Prefs.Set("Root", d.SelectedPath); return d.SelectedPath; }
                    MessageBox.Show(L.T("В этой папке нет service.bat и папки bin.", "This folder has no service.bat and bin folder."), "Zapret", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr w, IntPtr l);
    }
}
