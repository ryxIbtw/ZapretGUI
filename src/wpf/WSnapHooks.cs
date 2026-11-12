// Только для тестовой сборки (build.ps1 -Wpf -NoAdmin): подготовка снимков окна.
#if TESTHOOKS
using System;
using System.Windows;

namespace ZapretGui
{
    partial class MainWindow
    {
        public Zapret Zap { get { return z; } }

        public void SnapPrepare()
        {
            if (Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_NET") == "ok")
            {
                netYt = 1; netDc = 1;
                lastNetMs = new long[] { 120, 98, 140, 88, 101, 95 };
                lastNet = DateTime.Now;
            }
            if (Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_NET") == "bad")
            {
                netYt = 0; netDc = 1;
                lastNetMs = new long[] { -1, -1, -1, 88, 101, 95 };
                lastNet = DateTime.Now;
            }
            string on = Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_ON");
            if (on != null) { running = on == "1"; activeStrategy = running ? selected : null; power.SetOn(running, false); }
            if (Environment.GetEnvironmentVariable("ZAPRETGUI_SNAP_PICK") == "1") { busy = true; picking = true; pickProgress = 0.62; busyText = "Подбор 12 / 18"; pickName = "general (FAKE TLS AUTO)"; }
            UpdateUi();
        }

        public Window SnapFlyout()
        {
            flyout = new FlyoutW(this);
            SyncFlyout();
            flyout.WindowStartupLocation = WindowStartupLocation.Manual;
            flyout.Left = -3000; flyout.Top = 0;
            flyout.Show();
            return flyout;
        }
    }
}
#endif
