// Кнопка питания на WPF: свет за кнопкой, волна при включении, кольцо прогресса подбора, вращение во время операций,
// подписи состояния под кругом. Всё анимируется средствами WPF.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ZapretGui
{
    class PowerHero : Grid
    {
        public event EventHandler Click;
        readonly double size;
        readonly Grid circle;
        readonly Ellipse glow, ripple, baseFill, onFill, hoverFill, track, spinner;
        readonly Path progressArc;
        readonly Ico glyphOff, glyphOn;
        readonly Grid glowHost;
        readonly ScaleTransform press = new ScaleTransform(1, 1), rippleScale = new ScaleTransform(1, 1), glowScale = new ScaleTransform(1, 1);
        readonly RotateTransform spin = new RotateTransform(0);
        readonly TextBlock title, subtitle;
        readonly RadialGradientBrush glowBrush;
        bool on, busy, hot, down, compact;
        bool initialized;   // первое SetOn — всегда без анимации
        bool breathing;     // идёт «дыхание» света
        double progress = -1;

        // Прогресс подбора с плавным догоном (анимируемое свойство).
        public static readonly DependencyProperty ShownProperty = DependencyProperty.Register("Shown", typeof(double), typeof(PowerHero),
            new PropertyMetadata(0.0, (d, e) => ((PowerHero)d).DrawArc()));
        public double Shown { get { return (double)GetValue(ShownProperty); } set { SetValue(ShownProperty, value); } }

        // compact — без подписей и пятна света (для мини-панели у трея).
        public PowerHero(double circleSize, bool compactMode = false)
        {
            size = circleSize;
            compact = compactMode;
            double ring = size + 14;

            // свет за кнопкой
            glowBrush = new RadialGradientBrush();
            glowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 0));
            glowBrush.GradientStops.Add(new GradientStop(Colors.Transparent, 1));
            glowHost = new Grid { Opacity = 0, IsHitTestVisible = false };
            glow = new Ellipse { Width = size * 2.6, Height = size * 1.32, Fill = glowBrush, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = glowScale };
            glowHost.Children.Add(glow);

            // область должна вмещать пятно света целиком, иначе WPF обрежет его по краям
            var stage = new Grid { Width = compact ? ring + 40 : size * 2.6 + 8, Height = compact ? ring : size + 38, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top };
            if (!compact) stage.Children.Add(glowHost);

            ripple = new Ellipse { Width = size, Height = size, Stroke = W.Accent, StrokeThickness = 2.5, Opacity = 0, IsHitTestVisible = false,
                RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = rippleScale };
            stage.Children.Add(ripple);

            track = new Ellipse { Width = ring, Height = ring, Stroke = W.Line, StrokeThickness = 4, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            stage.Children.Add(track);
            progressArc = new Path { Stroke = W.Accent, StrokeThickness = 4, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = ring, Height = ring, Visibility = Visibility.Collapsed, IsHitTestVisible = false };
            stage.Children.Add(progressArc);

            circle = new Grid { Width = size, Height = size, Cursor = Cursors.Hand, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = press };
            baseFill = new Ellipse { Fill = W.Surface, Stroke = W.Line, StrokeThickness = 2 };
            onFill = new Ellipse { Fill = W.AccentDeep, Stroke = W.Accent, StrokeThickness = 2, Opacity = 0 };
            hoverFill = new Ellipse { Fill = W.HoverTint, Opacity = 0 };
            double gs = size * 0.38;
            glyphOff = new Ico("power", gs, W.Muted) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            glyphOn = new Ico("power", gs, W.Accent) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Opacity = 0 };
            spinner = new Ellipse { Margin = new Thickness(6), Stroke = W.Accent, StrokeThickness = 3, Visibility = Visibility.Collapsed,
                StrokeDashCap = PenLineCap.Round, RenderTransformOrigin = new Point(0.5, 0.5), RenderTransform = spin, IsHitTestVisible = false };
            circle.Children.Add(baseFill); circle.Children.Add(onFill); circle.Children.Add(hoverFill);
            circle.Children.Add(glyphOff); circle.Children.Add(glyphOn); circle.Children.Add(spinner);
            stage.Children.Add(circle);

            // с клавиатуры: Tab до кнопки, Enter или пробел — нажать
            circle.Focusable = true;
            circle.FocusVisualStyle = null;
            circle.GotKeyboardFocus += delegate { if (InputManager.Current.MostRecentInputDevice is KeyboardDevice) { baseFill.Stroke = W.Accent; baseFill.StrokeThickness = 3; } };
            circle.LostKeyboardFocus += delegate { baseFill.Stroke = W.Line; baseFill.StrokeThickness = 2; };
            circle.KeyDown += (s, e) =>
            {
                if (e.Key != Key.Enter && e.Key != Key.Space) return;
                e.Handled = true;
                var h = Click; if (h != null) h(this, EventArgs.Empty);
            };
            circle.MouseEnter += delegate { hot = true; UpdateHover(); };
            circle.MouseLeave += delegate { hot = false; down = false; UpdateHover(); Press(false); };
            circle.MouseLeftButtonDown += (s, e) => { down = true; Press(true); e.Handled = true; };
            circle.MouseLeftButtonUp += (s, e) =>
            {
                bool was = down; down = false; Press(false);
                if (was && hot) { e.Handled = true; var h = Click; if (h != null) h(this, EventArgs.Empty); }
            };

            var col = new StackPanel();
            col.Children.Add(stage);
            title = W.T("", W.Status, W.Text, true);
            title.HorizontalAlignment = HorizontalAlignment.Center;
            subtitle = W.T("", W.Small, W.Muted);
            subtitle.HorizontalAlignment = HorizontalAlignment.Center;
            subtitle.Margin = new Thickness(20, 1, 20, 0);
            if (!compact) { col.Children.Add(title); col.Children.Add(subtitle); }
            Children.Add(col);

            W.Changed += UpdateGlowColors;
            UpdateGlowColors();
            Unloaded += delegate { W.Changed -= UpdateGlowColors; };
            // окно спрятано в трей — «дыхание» не крутим, чтобы не тратить процессор
            IsVisibleChanged += delegate { UpdateBreath(); };
        }

        public TextBlock TitleBlock { get { return title; } }
        public TextBlock SubtitleBlock { get { return subtitle; } }

        void UpdateGlowColors()
        {
            Color a = W.Accent.Color;
            glowBrush.GradientStops[0].Color = W.A(a, Theme.IsLight ? 0.16 : 0.22);
            glowBrush.GradientStops[1].Color = W.A(a, 0);
        }

        void UpdateHover()
        {
            Fx.To(hoverFill, OpacityProperty, hot && !busy ? 0.04 : 0, 120);
            glyphOff.Brush = hot ? W.Text : W.Muted;
        }

        void Press(bool p)
        {
            Fx.To(press, ScaleTransform.ScaleXProperty, p ? 0.965 : 1, 90);
            Fx.To(press, ScaleTransform.ScaleYProperty, p ? 0.965 : 1, 90);
        }

        public void SetText(string t, Brush color, string sub)
        {
            title.Text = t; title.Foreground = color; subtitle.Text = sub;
        }

        // animate = false — сразу в нужном состоянии (при первом показе).
        public void SetOn(bool value, bool animate)
        {
            if (initialized && on == value) return;   // ничего не поменялось — ничего не трогаем
            if (!initialized || Fx.Off) animate = false;   // новая кнопка сразу в нужном состоянии, без волны; и при «Без анимаций»
            initialized = true;
            bool turnedOn = value && !on;
            on = value;
            double ms = animate ? 350 : 0;
            Apply(onFill, on ? 1 : 0, ms);
            Apply(glyphOn, on ? 1 : 0, ms);
            Apply(glowHost, on ? 1 : 0, animate ? 600 : 0);
            UpdateBreath();
            if (turnedOn && animate && !busy && !compact) Ripple();
        }

        static void Apply(UIElement e, double to, double ms)
        {
            if (ms <= 0) Fx.Set(e, OpacityProperty, to); else Fx.To(e, OpacityProperty, to, ms);
        }

        // мягкое «дыхание» света: только пока обход включён и кнопку видно
        void UpdateBreath()
        {
            bool want = on && !compact && IsVisible && !Fx.Off;
            if (want == breathing) return;
            breathing = want;
            if (want)
            {
                var a = new DoubleAnimation(0.96, 1.06, TimeSpan.FromSeconds(2.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Fx.InOut };
                glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
                glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
                glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0.8, 1, TimeSpan.FromSeconds(2.4)) { AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = Fx.InOut });
            }
            else
            {
                glowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
                glowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
                glow.BeginAnimation(OpacityProperty, null);
            }
        }

        // расходящееся кольцо в момент включения
        void Ripple()
        {
            double grow = (size / 2 + 40) / (size / 2);
            var d = TimeSpan.FromMilliseconds(650);
            rippleScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1.02, grow, d) { EasingFunction = Fx.Out });
            rippleScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(1.02, grow, d) { EasingFunction = Fx.Out });
            ripple.BeginAnimation(OpacityProperty, new DoubleAnimation(0.7, 0, d) { EasingFunction = Fx.Out });
        }

        public void SetBusy(bool value)
        {
            if (busy == value) return;
            busy = value;
            UpdateRings();
            UpdateHover();
        }

        // Доля проверенных стратегий 0…1; -1 — подбора нет.
        public void SetProgress(double p)
        {
            if (p == progress) return;
            bool start = progress < 0 && p >= 0;
            progress = p;
            if (p >= 0)
            {
                if (start) Fx.Set(this, ShownProperty, 0);
                BeginAnimation(ShownProperty, new DoubleAnimation(p, TimeSpan.FromMilliseconds(400)) { EasingFunction = Fx.Out });
            }
            UpdateRings();
        }

        void UpdateRings()
        {
            bool ringMode = busy && progress >= 0;
            track.Visibility = progressArc.Visibility = ringMode ? Visibility.Visible : Visibility.Collapsed;
            bool spinMode = busy && progress < 0;
            if (spinMode && spinner.Visibility != Visibility.Visible)
            {
                spinner.Visibility = Visibility.Visible;
                double c = Math.PI * (size - 12);
                spinner.StrokeDashArray = new DoubleCollection { c * 0.22 / 3, c / 3 };   // одна дуга ~80°
                spin.BeginAnimation(RotateTransform.AngleProperty, new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else if (!spinMode && spinner.Visibility == Visibility.Visible)
            {
                spinner.Visibility = Visibility.Collapsed;
                spin.BeginAnimation(RotateTransform.AngleProperty, null);
            }
            DrawArc();
        }

        void DrawArc()
        {
            double ring = size + 14, r = ring / 2 - 2, cx = ring / 2, cy = ring / 2;
            double p = Math.Max(0.004, Math.Min(0.9999, Shown));
            double ang = p * 2 * Math.PI;
            var start = new Point(cx, cy - r);
            var end = new Point(cx + r * Math.Sin(ang), cy - r * Math.Cos(ang));
            var fig = new PathFigure { StartPoint = start, IsClosed = false };
            fig.Segments.Add(new ArcSegment(end, new Size(r, r), 0, ang > Math.PI, SweepDirection.Clockwise, true));
            var geo = new PathGeometry();
            geo.Figures.Add(fig);
            progressArc.Data = geo;
        }
    }
}
