// Плавные переходы между страницами: двигаются картинки страниц в одном слое, а не живые элементы.
using System;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ZapretGui
{
    // Точный системный таймер на время анимации (обычный шаг Windows — около 15 мс, кадры идут неровно).
    static class HiResTimer
    {
        static int users;
        public static void Begin() { if (users++ == 0) try { timeBeginPeriod(1); } catch { } }
        public static void End() { if (users > 0 && --users == 0) try { timeEndPeriod(1); } catch { } }

        [DllImport("winmm.dll")] static extern uint timeBeginPeriod(uint ms);
        [DllImport("winmm.dll")] static extern uint timeEndPeriod(uint ms);
    }

    // Слой поверх страниц, в котором старая страница уезжает, а новая въезжает.
    class SlideCover : Control
    {
        const double Duration = 260;   // мс
        readonly Bitmap from;
        Bitmap to;
        readonly int dir;
        readonly Timer tm = new Timer { Interval = 8 };
        readonly Stopwatch clock = new Stopwatch();
        float e;            // 0…1 — насколько продвинулся сдвиг (с замедлением в конце)
        bool started, finished;
        public event EventHandler Done;

        public SlideCover(Bitmap before, int direction)
        {
            from = before; dir = direction;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.Opaque, true);
            tm.Tick += delegate
            {
                double p = Math.Min(1.0, clock.Elapsed.TotalMilliseconds / Duration);   // по реальному времени, а не по кадрам
                e = (float)(1 - Math.Pow(1 - p, 3));
                Invalidate();
                Update();
                if (p >= 1) Finish();
            };
        }

        // Картинка элемента со всем содержимым.
        public static Bitmap Grab(Control c)
        {
            if (c.Width <= 0 || c.Height <= 0) return null;
            var b = new Bitmap(c.Width, c.Height);
            c.DrawToBitmap(b, new Rectangle(0, 0, c.Width, c.Height));
            return b;
        }

        // То, что сейчас видно на экране (быстрее, чем перерисовывать) — только если окно впереди и ничем не закрыто.
        public static Bitmap GrabScreen(Control c)
        {
            var form = c.FindForm();
            if (form == null || Form.ActiveForm != form || !c.Visible || c.Width <= 0 || c.Height <= 0) return Grab(c);
            var b = new Bitmap(c.Width, c.Height);
            using (var g = Graphics.FromImage(b)) g.CopyFromScreen(c.PointToScreen(Point.Empty), Point.Empty, c.Size);
            return b;
        }

        public void Start(Bitmap after)
        {
            to = after;
            started = true;
            HiResTimer.Begin();
            clock.Start();
            tm.Start();
        }

        public void Finish()
        {
            if (finished) return;
            finished = true;
            tm.Stop();
            if (started) HiResTimer.End();
            if (Parent != null) Parent.Controls.Remove(this);
            var h = Done;
            Dispose();
            if (h != null) h(this, EventArgs.Empty);
        }

        protected override void OnPaint(PaintEventArgs pe)
        {
            var g = pe.Graphics;
            using (var b = new SolidBrush(Theme.Bg)) g.FillRectangle(b, ClientRectangle);
            int W = Width;
            if (to != null) g.DrawImageUnscaled(to, (int)Math.Round(dir * W * (1 - e)), 0);
            if (from != null) g.DrawImageUnscaled(from, (int)Math.Round(-dir * W * e), 0);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (started && !finished) { finished = true; HiResTimer.End(); }   // убрали вместе с окном посреди сдвига
                tm.Dispose();
                if (from != null) from.Dispose();
                if (to != null) to.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
