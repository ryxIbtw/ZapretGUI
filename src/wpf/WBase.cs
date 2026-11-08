// Основа окон WPF (цвета, тёмный заголовок, масштаб), окна-вопросы, окно ввода и меню.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace ZapretGui
{
    class WinBase : Window
    {
        public readonly Border Root = new Border();
        readonly ScaleTransform scale = new ScaleTransform(1, 1);

        public WinBase(string title, Window owner)
        {
            Title = title;
            Background = W.Bg;
            Foreground = W.Text;
            FontFamily = W.TextFont;
            FontSize = W.Body;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = owner == null;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Display);
            if (owner != null && owner.IsVisible) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; }
            else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            if (owner != null) Icon = owner.Icon;
            Root.LayoutTransform = scale;
            Root.Background = W.Bg;
            ApplyScale();
            Content = Root;
            SourceInitialized += delegate { TitleBar(this); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { e.Handled = true; Close(); } };
        }

        public void ApplyScale() { scale.ScaleX = scale.ScaleY = Theme.UiScale; }

        // Тёмный/светлый заголовок окна в цвет фона (Windows 10 2004+/Windows 11).
        public static void TitleBar(Window w)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                if (h == IntPtr.Zero) return;
                int dark = Theme.IsLight ? 0 : 1;
                DwmSetWindowAttribute(h, 20, ref dark, 4);
                int color = Theme.Bg.R | (Theme.Bg.G << 8) | (Theme.Bg.B << 16);
                DwmSetWindowAttribute(h, 35, ref color, 4);
                int text = Theme.Text.R | (Theme.Text.G << 8) | (Theme.Text.B << 16);
                DwmSetWindowAttribute(h, 36, ref text, 4);
            }
            catch { }
        }

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }

    // Вопросы и предупреждения в стиле программы. Те же кнопки и ответы, что у MessageBox.
    static class AskW
    {
        public static MessageBoxResult Show(Window owner, string text, string title, MessageBoxButton buttons, MessageBoxImage icon = MessageBoxImage.None)
        {
            var w = new WinBase(title, owner);
            MessageBoxResult result = buttons == MessageBoxButton.YesNo ? MessageBoxResult.No : buttons == MessageBoxButton.OK ? MessageBoxResult.OK : MessageBoxResult.Cancel;

            var grid = new Grid { Width = 420, Margin = new Thickness(24, 24, 24, 20) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            grid.RowDefinitions.Add(new RowDefinition());
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            Brush c = icon == MessageBoxImage.Warning ? W.Warn : icon == MessageBoxImage.Error ? W.Danger : W.Accent;
            string glyph = icon == MessageBoxImage.Warning ? "alert" : icon == MessageBoxImage.Error ? "x" : icon == MessageBoxImage.Question ? "help" : "info";
            var mark = new Grid { Width = 38, Height = 38, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 16, 0) };
            mark.Children.Add(new Ellipse { Fill = c, Opacity = Theme.IsLight ? 0.14 : 0.18 });
            mark.Children.Add(new Ico(glyph, 20, c) { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center });
            grid.Children.Add(mark);

            var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = W.Text, FontSize = W.Body, FontFamily = W.TextFont, Margin = new Thickness(0, 2, 0, 0) };
            Grid.SetColumn(tb, 1);
            grid.Children.Add(tb);

            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 22, 0, 0) };
            var defs = new List<Tuple<string, MessageBoxResult>>();
            switch (buttons)
            {
                case MessageBoxButton.YesNo: defs.Add(Tuple.Create(L.T("Нет", "No"), MessageBoxResult.No)); defs.Add(Tuple.Create(L.T("Да", "Yes"), MessageBoxResult.Yes)); break;
                case MessageBoxButton.YesNoCancel:
                    defs.Add(Tuple.Create(L.T("Отмена", "Cancel"), MessageBoxResult.Cancel)); defs.Add(Tuple.Create(L.T("Нет", "No"), MessageBoxResult.No));
                    defs.Add(Tuple.Create(L.T("Да", "Yes"), MessageBoxResult.Yes)); break;
                case MessageBoxButton.OKCancel: defs.Add(Tuple.Create(L.T("Отмена", "Cancel"), MessageBoxResult.Cancel)); defs.Add(Tuple.Create("OK", MessageBoxResult.OK)); break;
                default: defs.Add(Tuple.Create("OK", MessageBoxResult.OK)); break;
            }
            MessageBoxResult primary = defs[defs.Count - 1].Item2;
            for (int i = 0; i < defs.Count; i++)
            {
                var d = defs[i];
                var b = new Pill(d.Item1, i < defs.Count - 1) { MinWidth = 96, Margin = new Thickness(10, 0, 0, 0) };
                b.Click += delegate { result = d.Item2; w.Close(); };
                bar.Children.Add(b);
            }
            Grid.SetRow(bar, 1); Grid.SetColumnSpan(bar, 2);
            grid.Children.Add(bar);
            w.Root.Child = grid;
            w.KeyDown += (s, e) => { if (e.Key == Key.Enter) { result = primary; w.Close(); } };
            w.ShowDialog();
            return result;
        }
    }

    // Окно ввода одной строки.
    static class InputW
    {
        public static string Show(Window owner, string title, string hint, string value)
        {
            var w = new WinBase("Zapret", owner);
            string result = null;
            var sp = new StackPanel { Width = 340, Margin = new Thickness(22, 18, 22, 18) };
            sp.Children.Add(W.T(title, W.Body, W.Text));
            var h = W.T(hint, W.Caption, W.Muted); h.Margin = new Thickness(0, 3, 0, 10);
            sp.Children.Add(h);
            var tb = new TextBox { Text = value ?? "" };
            sp.Children.Add(tb);
            var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = new Pill(L.T("Отмена", "Cancel"), true) { MinWidth = 96 };
            var ok = new Pill(L.T("Сохранить", "Save")) { MinWidth = 96, Margin = new Thickness(10, 0, 0, 0) };
            cancel.Click += delegate { w.Close(); };
            ok.Click += delegate { result = tb.Text.Trim(); w.Close(); };
            bar.Children.Add(cancel); bar.Children.Add(ok);
            sp.Children.Add(bar);
            w.Root.Child = sp;
            tb.KeyDown += (s, e) => { if (e.Key == Key.Enter) { result = tb.Text.Trim(); w.Close(); } };
            w.Loaded += delegate { tb.Focus(); tb.SelectAll(); };
            w.ShowDialog();
            return result;
        }
    }

    // Меню в цветах темы.
    static class WMenu
    {
        public static ContextMenu Create()
        {
            var m = new ContextMenu();
            m.LayoutTransform = new ScaleTransform(Theme.UiScale, Theme.UiScale);
            return m;
        }

        public static MenuItem Add(ContextMenu m, string text, bool current, Action click)
        {
            var it = new MenuItem { Header = text, IsChecked = current };
            if (current) it.Foreground = W.Accent;
            if (click != null) it.Click += delegate { click(); };
            m.Items.Add(it);
            return it;
        }

        public static void Header(ContextMenu m, string text)
        {
            var it = new MenuItem { Header = text, IsEnabled = false };
            m.Items.Add(it);
        }

        public static void Sep(ContextMenu m) { m.Items.Add(new Separator()); }

        // Цветной кружок-образец для пункта меню.
        public static Ellipse Dot(Color c) { return new Ellipse { Width = 12, Height = 12, Fill = new SolidColorBrush(c) }; }

        public static void ShowBelow(ContextMenu m, FrameworkElement anchor)
        {
            m.PlacementTarget = anchor;
            m.Placement = PlacementMode.Bottom;
            m.HorizontalOffset = 0;
            m.VerticalOffset = 2;
            m.IsOpen = true;
        }

        public static void ShowAbove(ContextMenu m, FrameworkElement anchor)
        {
            m.PlacementTarget = anchor;
            m.Placement = PlacementMode.Top;
            m.VerticalOffset = -2;
            m.IsOpen = true;
        }

        // Под правым краем строки (для строк настроек), выравнивание по правому краю.
        public static void ShowBelowRight(ContextMenu m, FrameworkElement anchor)
        {
            m.PlacementTarget = anchor;
            m.Placement = PlacementMode.Custom;
            m.CustomPopupPlacementCallback = (popup, target, offset) => new[] { new CustomPopupPlacement(new Point(target.Width - popup.Width - 4, target.Height), PopupPrimaryAxis.Horizontal) };
            m.IsOpen = true;
        }
    }
}
