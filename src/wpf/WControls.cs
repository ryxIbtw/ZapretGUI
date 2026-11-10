// Элементы интерфейса на WPF. Анимации — через WPF (видеокарта), поэтому они плавные.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace ZapretGui
{
    static class Fx
    {
        public static readonly IEasingFunction Out = new CubicEase { EasingMode = EasingMode.EaseOut };
        public static readonly IEasingFunction InOut = new SineEase { EasingMode = EasingMode.EaseInOut };

        // Плавно меняет числовое свойство (прозрачность, сдвиг, масштаб…).
        public static void To(IAnimatable target, DependencyProperty prop, double to, double ms, IEasingFunction ease = null)
        {
            var a = new DoubleAnimation(to, TimeSpan.FromMilliseconds(ms)) { EasingFunction = ease ?? Out };
            target.BeginAnimation(prop, a);
        }

        public static void Set(IAnimatable target, DependencyProperty prop, double value)
        {
            target.BeginAnimation(prop, null);
            ((DependencyObject)target).SetValue(prop, value);
        }
    }

    // Значок из Icons (те же SVG-пути, что в старой версии) нужного размера и цвета.
    class Ico : Viewbox
    {
        readonly Path path;
        public Ico(string name, double size, Brush brush)
        {
            Width = size; Height = size;
            path = new Path
            {
                Data = Geometry.Parse(Icons.PathData(name) ?? "M0 0"),
                StrokeThickness = 2, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            };
            var box = new Canvas { Width = 24, Height = 24 };
            box.Children.Add(path);
            Child = box;
            Brush = brush;
            if (Icons.IsFilled(name)) path.Fill = brush;
            filled = Icons.IsFilled(name);
            IsHitTestVisible = false;
        }
        readonly bool filled;
        public Brush Brush
        {
            get { return path.Stroke; }
            set { path.Stroke = value; if (filled) path.Fill = value; }
        }
        public void SetIcon(string name) { path.Data = Geometry.Parse(Icons.PathData(name) ?? "M0 0"); }
    }

    // Элемент, который можно нажать (общая логика наведения и щелчка).
    class Clickable : Border
    {
        public event EventHandler Click;
        protected bool Hot;
        bool down;
        public Clickable()
        {
            Cursor = Cursors.Hand;
            Background = Brushes.Transparent;
            MouseEnter += delegate { Hot = true; OnHot(true); };
            MouseLeave += delegate { Hot = false; down = false; OnHot(false); OnPress(false); };
            MouseLeftButtonDown += (s, e) => { down = true; OnPress(true); e.Handled = true; };
            MouseLeftButtonUp += (s, e) =>
            {
                bool was = down; down = false; OnPress(false);
                if (was && Hot) { e.Handled = true; var h = Click; if (h != null) h(this, EventArgs.Empty); }
            };
        }
        protected virtual void OnHot(bool on) { }
        protected virtual void OnPress(bool on) { }
        public void RaiseClick() { var h = Click; if (h != null) h(this, EventArgs.Empty); }
    }

    // Ссылка в подвале: приглушённый текст, при наведении — яркий.
    class FlatLink : Clickable
    {
        readonly TextBlock tb;
        public FlatLink(string text, double size = W.Small)
        {
            tb = W.T(text, size, W.Muted);
            Child = tb;
            Padding = new Thickness(1, 2, 1, 2);
        }
        public string Text { get { return tb.Text; } set { tb.Text = value; } }
        protected override void OnHot(bool on) { tb.Foreground = on ? W.Text : W.Muted; }
    }

    // Кнопка-«таблетка»: акцентная или спокойная (Secondary).
    class Pill : Clickable
    {
        readonly Border tint;
        readonly TextBlock tb;
        public Pill(string text, bool secondary = false, double height = 36)
        {
            Height = height;
            CornerRadius = new CornerRadius(height / 2);
            Background = secondary ? W.Knob : W.Accent;
            Padding = new Thickness(18, 0, 18, 0);
            tb = W.T(text, secondary ? W.Body : W.BodyStrong, secondary ? W.Text : W.OnAccent, !secondary);
            tb.HorizontalAlignment = HorizontalAlignment.Center; tb.VerticalAlignment = VerticalAlignment.Center;
            tint = new Border { Background = W.HoverTint, Opacity = 0, CornerRadius = CornerRadius, Margin = new Thickness(-18, 0, -18, 0), IsHitTestVisible = false };
            var g = new Grid();
            g.Children.Add(tint); g.Children.Add(tb);
            Child = g;
            RenderTransformOrigin = new Point(0.5, 0.5);
            RenderTransform = new ScaleTransform(1, 1);
        }
        public string Text { get { return tb.Text; } set { tb.Text = value; } }
        protected override void OnHot(bool on) { Fx.To(tint, OpacityProperty, on ? 0.08 : 0, 120); }
        protected override void OnPress(bool on)
        {
            var st = (ScaleTransform)RenderTransform;
            Fx.To(st, ScaleTransform.ScaleXProperty, on ? 0.97 : 1, 90); Fx.To(st, ScaleTransform.ScaleYProperty, on ? 0.97 : 1, 90);
        }
    }

    // Круглая кнопка-значок в шапке (шестерёнка, назад, тема).
    class IconButton : Clickable
    {
        readonly Ellipse hover;
        readonly Ico icon;
        public IconButton(string name, double size = 30)
        {
            Width = size; Height = size;
            hover = new Ellipse { Fill = W.SurfaceHover, Opacity = 0 };
            icon = new Ico(name, size * 0.6, W.Muted) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var g = new Grid();
            g.Children.Add(hover); g.Children.Add(icon);
            Child = g;
        }
        public void SetIcon(string name) { icon.SetIcon(name); }
        protected override void OnHot(bool on) { Fx.To(hover, OpacityProperty, on ? 1 : 0, 120); icon.Brush = on ? W.Text : W.Muted; }
    }

    // Шапка: «zapret» + плашка «by Flowseal», ниже плашка «GUI by ryxIbtw».
    class BrandHeader : StackPanel
    {
        public const string ZapretAuthor = "Flowseal", Author = "ryxIbtw";
        public BrandHeader()
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            var name = W.T("zapret", W.Title, W.Text, true);
            name.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(name);
            var p1 = PillOf("by ", ZapretAuthor); p1.Margin = new Thickness(10, 2, 0, 0); p1.VerticalAlignment = VerticalAlignment.Center;
            row.Children.Add(p1);
            Children.Add(row);
            var p2 = PillOf("GUI by ", Author); p2.Margin = new Thickness(1, 4, 0, 0); p2.HorizontalAlignment = HorizontalAlignment.Left;
            Children.Add(p2);
        }
        static Border PillOf(string prefix, string nick)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(W.T(prefix, W.Caption, W.Muted, true));
            sp.Children.Add(W.T(nick, W.Caption, W.Accent, true));
            return new Border
            {
                Background = W.AccentDeep, BorderBrush = W.AccentLine, BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10), Padding = new Thickness(10, 2, 10, 3), Child = sp,
            };
        }
    }

    // Подложка-карточка для строк настроек.
    class CardBox : Border
    {
        public readonly StackPanel Rows = new StackPanel();
        public CardBox()
        {
            Background = W.Surface;
            CornerRadius = new CornerRadius(14);
            Padding = new Thickness(4);
            Child = Rows;
        }
        public void Add(SettingRowW row)
        {
            row.Divider = Rows.Children.Count > 0;
            Rows.Children.Add(row);
        }
    }

    // Переключатель вкл/выкл с плавным ходом.
    class ToggleW : Grid
    {
        readonly Border on;
        readonly Ellipse knobOff, knobOn;
        readonly TranslateTransform move = new TranslateTransform();
        bool value;
        public ToggleW()
        {
            Width = 38; Height = 22;
            Children.Add(new Border { Background = W.Knob, CornerRadius = new CornerRadius(11) });
            on = new Border { Background = W.Accent, CornerRadius = new CornerRadius(11), Opacity = 0 };
            Children.Add(on);
            var knob = new Grid { Width = 16, Height = 16, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(3, 0, 0, 0), RenderTransform = move };
            knobOff = new Ellipse { Fill = W.KnobOff };
            knobOn = new Ellipse { Fill = Brushes.White, Opacity = 0 };
            knob.Children.Add(knobOff); knob.Children.Add(knobOn);
            Children.Add(knob);
            IsHitTestVisible = false;
        }
        public bool Value
        {
            get { return value; }
            set { Set(value, IsLoaded); }
        }
        public void Set(bool v, bool animate)
        {
            value = v;
            double ms = animate ? 180 : 0;
            if (ms == 0)
            {
                Fx.Set(on, OpacityProperty, v ? 1 : 0); Fx.Set(knobOn, OpacityProperty, v ? 1 : 0); Fx.Set(move, TranslateTransform.XProperty, v ? 16 : 0);
                return;
            }
            Fx.To(on, OpacityProperty, v ? 1 : 0, ms); Fx.To(knobOn, OpacityProperty, v ? 1 : 0, ms);
            Fx.To(move, TranslateTransform.XProperty, v ? 16 : 0, ms);
        }
    }

    // Строка настройки: значок, название, подпись; справа переключатель или значение со стрелкой.
    class SettingRowW : Clickable
    {
        readonly Border hover;
        readonly Rectangle divider;
        readonly TextBlock title, hint, value;
        readonly ToggleW toggle;
        readonly Ico chevron;
        public readonly bool IsSwitch;

        public SettingRowW(string titleText, bool isSwitch, string icon = null, string hintText = "", bool nav = false, double height = 52)
        {
            IsSwitch = isSwitch;
            Height = height;
            var g = new Grid();
            hover = new Border { Background = W.SurfaceHover, CornerRadius = new CornerRadius(10), Opacity = 0 };
            g.Children.Add(hover);
            divider = new Rectangle { Height = 1, Fill = W.Line, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14, 0, 14, 0), Visibility = Visibility.Collapsed };
            g.Children.Add(divider);

            var cols = new Grid { Margin = new Thickness(14, 0, 14, 0) };
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            cols.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            if (icon != null)
            {
                var ic = new Ico(icon, 18, W.Muted) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                cols.Children.Add(ic);
            }
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            title = W.T(titleText, W.Body, W.Text);
            hint = W.T(hintText ?? "", W.Caption, W.Muted);
            hint.Margin = new Thickness(0, 1, 0, 0);
            texts.Children.Add(title); texts.Children.Add(hint);
            Grid.SetColumn(texts, 1);
            cols.Children.Add(texts);

            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            if (isSwitch) { toggle = new ToggleW(); right.Children.Add(toggle); }
            else
            {
                value = W.T("", W.Body, W.Muted);
                value.VerticalAlignment = VerticalAlignment.Center;
                value.MaxWidth = 170;
                chevron = new Ico(nav ? "chevronRight" : "chevronDown", 14, W.Muted) { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 1, 0, 0) };
                right.Children.Add(value); right.Children.Add(chevron);
            }
            Grid.SetColumn(right, 2);
            cols.Children.Add(right);
            g.Children.Add(cols);
            Child = g;
            Hint = hintText;
        }

        public bool Divider { set { divider.Visibility = value ? Visibility.Visible : Visibility.Collapsed; } }
        public string Hint
        {
            get { return hint.Text; }
            set { hint.Text = value ?? ""; hint.Visibility = string.IsNullOrEmpty(value) ? Visibility.Collapsed : Visibility.Visible; }
        }
        public string Title { get { return title.Text; } set { title.Text = value; } }
        public string Value { get { return value == null ? "" : this.value.Text; } set { if (this.value != null) this.value.Text = value ?? ""; } }
        public bool Checked
        {
            get { return toggle != null && toggle.Value; }
            set { if (toggle != null && toggle.Value != value) toggle.Value = value; }
        }
        public void SetChecked(bool v, bool animate) { if (toggle != null) toggle.Set(v, animate); }

        protected override void OnHot(bool on)
        {
            Fx.To(hover, OpacityProperty, on ? 1 : 0, 140);
            Fx.To(divider, OpacityProperty, on ? 0 : 1, 140);
        }
    }

    // Карточка выбора стратегии: имя + стрелки ‹ ›; колесо мыши листает.
    class StrategyPickerW : Border
    {
        public event EventHandler Prev, Next, Open;
        readonly TextBlock name;
        readonly Border hover;
        public StrategyPickerW()
        {
            Background = W.Surface;
            CornerRadius = new CornerRadius(14);
            Height = 62;
            var g = new Grid();
            hover = new Border { Background = W.SurfaceHover, CornerRadius = new CornerRadius(14), Opacity = 0 };
            g.Children.Add(hover);
            var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 96, 0), Cursor = Cursors.Hand, Background = Brushes.Transparent };
            texts.Children.Add(W.T(L.T("Стратегия", "Strategy"), W.Caption, W.Muted));
            name = W.T("", W.BodyStrong, W.Text, true);
            name.Margin = new Thickness(0, 2, 0, 0);
            texts.Children.Add(name);
            g.Children.Add(texts);
            var area = new Border { Background = Brushes.Transparent, Cursor = Cursors.Hand };
            area.MouseLeftButtonUp += (s, e) => { Raise(Open); };
            g.Children.Insert(1, area);
            var arrows = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            arrows.Children.Add(Arrow("chevronLeft", () => Raise(Prev)));
            var nx = Arrow("chevronRight", () => Raise(Next)); nx.Margin = new Thickness(6, 0, 0, 0);
            arrows.Children.Add(nx);
            g.Children.Add(arrows);
            Child = g;
            MouseEnter += delegate { Fx.To(hover, OpacityProperty, 1, 140); };
            MouseLeave += delegate { Fx.To(hover, OpacityProperty, 0, 140); };
            MouseWheel += (s, e) => { Raise(e.Delta > 0 ? Prev : Next); e.Handled = true; };
            texts.MouseLeftButtonUp += (s, e) => { Raise(Open); };
        }
        void Raise(EventHandler h) { if (h != null) h(this, EventArgs.Empty); }
        static Clickable Arrow(string icon, Action act)
        {
            var e = new Ellipse { Fill = W.Knob };
            var c = new Clickable { Width = 34, Height = 34, CornerRadius = new CornerRadius(17) };
            var g = new Grid();
            g.Children.Add(e);
            g.Children.Add(new Ico(icon, 16, W.Text) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            c.Child = g;
            c.MouseEnter += delegate { e.Fill = W.Line; };
            c.MouseLeave += delegate { e.Fill = W.Knob; };
            c.Click += delegate { act(); };
            return c;
        }
        public void SetValue(string v) { name.Text = string.IsNullOrEmpty(v) ? L.T("нет .bat файлов", "no .bat files") : v; }
        public FrameworkElement Anchor { get { return this; } }
    }

    // Плашка сервиса на главном экране: значок, название и «3/3 · 124 мс»; цвет — состояние.
    class ServiceCardW : Clickable
    {
        readonly Border tintFill, tintLine, tile, hover;
        readonly Ico icon;
        readonly TextBlock meta;
        public ServiceCardW(string name, string iconName)
        {
            CornerRadius = new CornerRadius(14);
            Background = W.Surface;
            Height = 50;
            var g = new Grid();
            tintFill = new Border { CornerRadius = new CornerRadius(14), Opacity = 0 };
            tintLine = new Border { CornerRadius = new CornerRadius(14), BorderThickness = new Thickness(1), Opacity = 0 };
            hover = new Border { CornerRadius = new CornerRadius(14), Background = W.HoverTint, Opacity = 0 };
            g.Children.Add(tintFill); g.Children.Add(hover); g.Children.Add(tintLine);
            var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center };
            var tileG = new Grid { Width = 30, Height = 30 };
            tile = new Border { CornerRadius = new CornerRadius(9), Opacity = 0.17 };
            icon = new Ico(iconName, 16, W.Muted) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            tileG.Children.Add(tile); tileG.Children.Add(icon);
            row.Children.Add(tileG);
            var texts = new StackPanel { Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            texts.Children.Add(W.T(name, W.Body, W.Text));
            meta = W.T("", W.Caption, W.Muted);
            texts.Children.Add(meta);
            row.Children.Add(texts);
            g.Children.Add(row);
            Child = g;
            Set(-1, false, "");
        }

        Brush lastColor; double lastFill = -1;

        // v: доля открывшихся адресов 0..1, -1 — неизвестно.
        public void Set(float v, bool checking, string metaText)
        {
            bool known = !checking && v >= 0;
            Brush c = !known ? W.Muted : v >= 0.99f ? W.Accent : v > 0.01f ? W.Warn : W.Danger;
            double fill = known ? (Theme.IsLight ? 0.07 : 0.09) : 0;
            // анимацию запускаем только если состояние поменялось (Set вызывается каждые 2 секунды)
            if (c != lastColor || fill != lastFill)
            {
                tintFill.Background = c; tintLine.BorderBrush = c; tile.Background = c; icon.Brush = c;
                Fx.To(tintFill, OpacityProperty, fill, 250);
                Fx.To(tintLine, OpacityProperty, known ? 0.25 : 0, 250);
                lastColor = c; lastFill = fill;
            }
            string m = checking ? L.T("проверка…", "checking…") : v < 0 ? L.T("нажмите, чтобы проверить", "click to check") : metaText;
            if (meta.Text != m) meta.Text = m;
            meta.Foreground = known ? W.Muted : W.Dim;
        }
        protected override void OnHot(bool on) { Fx.To(hover, OpacityProperty, on ? 0.04 : 0, 140); }
    }
}
