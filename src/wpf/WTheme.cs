// Тема для WPF: общие кисти цветов (перекрашиваются на месте при смене темы — без пересборки окон),
// шрифты и стили стандартных элементов (меню, подсказки, поля ввода, полосы прокрутки).
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using D = System.Drawing;

namespace ZapretGui
{
    static class W
    {
        // --- кисти: одни и те же объекты на весь интерфейс ---
        public static readonly SolidColorBrush Bg = B(), Surface = B(), SurfaceHover = B(), Line = B(), Knob = B(), Text = B(), Muted = B(),
            Dim = B(), Accent = B(), AccentDeep = B(), Warn = B(), Danger = B(), OnAccent = B(), KnobOff = B(), HoverTint = B(), AccentLine = B();
        static SolidColorBrush B() { return new SolidColorBrush(Colors.Black); }

        public static event Action Changed;   // тема поменялась (для градиентов, которые не кисти)

        public static Color C(D.Color c) { return Color.FromArgb(c.A, c.R, c.G, c.B); }
        public static Color A(Color c, double alpha) { return Color.FromArgb((byte)Math.Round(255 * Math.Max(0, Math.Min(1, alpha))), c.R, c.G, c.B); }
        public static Color Mix(Color a, Color b, double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return Color.FromArgb((byte)(a.A + (b.A - a.A) * t), (byte)(a.R + (b.R - a.R) * t), (byte)(a.G + (b.G - a.G) * t), (byte)(a.B + (b.B - a.B) * t));
        }

        // Переносит текущие цвета Theme в кисти (вызывать после Theme.Apply/ApplyMode).
        public static void Sync()
        {
            Bg.Color = C(Theme.Bg); Surface.Color = C(Theme.Surface); SurfaceHover.Color = C(Theme.SurfaceHover); Line.Color = C(Theme.Line);
            Knob.Color = C(Theme.Knob); Text.Color = C(Theme.Text); Muted.Color = C(Theme.Muted); Dim.Color = C(Theme.Dim);
            Accent.Color = C(Theme.Accent); AccentDeep.Color = C(Theme.AccentDeep); Warn.Color = C(Theme.Warn); Danger.Color = C(Theme.Danger);
            OnAccent.Color = Theme.IsLight ? Colors.White : C(Theme.Bg);
            KnobOff.Color = C(Theme.KnobDotOff);
            HoverTint.Color = C(Theme.HoverTint);
            AccentLine.Color = A(C(Theme.Accent), 0.28);
            PushResources();
            var h = Changed; if (h != null) h();
        }

        // Кисти, положенные в ресурсы приложения, WPF «замораживает» — поэтому туда кладутся копии,
        // а при смене темы они заменяются новыми (стили с DynamicResource подхватывают замену сразу).
        static ResourceDictionary appRes;
        static Dictionary<string, SolidColorBrush> Named()
        {
            return new Dictionary<string, SolidColorBrush>
            {
                { "Bg", Bg }, { "Surface", Surface }, { "SurfaceHover", SurfaceHover }, { "Line", Line }, { "Knob", Knob }, { "Text", Text },
                { "Muted", Muted }, { "Dim", Dim }, { "Accent", Accent }, { "AccentDeep", AccentDeep }, { "Warn", Warn }, { "Danger", Danger },
                { "OnAccent", OnAccent }, { "AccentLine", AccentLine },
            };
        }
        static void PushResources()
        {
            if (appRes == null) return;
            foreach (var kv in Named()) appRes[kv.Key] = new SolidColorBrush(kv.Value.Color);
        }

        // --- шрифты (размеры в точках WPF: 1 pt WinForms = 4/3 px) ---
        public static readonly FontFamily TextFont = new FontFamily("Segoe UI Variable Text, Segoe UI");
        public static readonly FontFamily DisplayFont = new FontFamily("Segoe UI Variable Display, Segoe UI");
        public static readonly FontFamily SmallFont = new FontFamily("Segoe UI Variable Small, Segoe UI");
        public static readonly FontFamily MonoFont = new FontFamily("Consolas");
        public const double Body = 13.3, BodyStrong = 14.6, Small = 12, Caption = 11.3, Title = 18.6, Status = 20;

        public static TextBlock T(string text, double size, Brush color, bool strong = false, FontFamily family = null)
        {
            return new TextBlock
            {
                Text = text, FontSize = size, Foreground = color,
                FontFamily = family ?? (size >= Title ? DisplayFont : size <= Caption ? SmallFont : TextFont),
                FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
            };
        }

        // Регистрирует кисти и стили в ресурсах приложения.
        public static void Install(Application app)
        {
            var res = app.Resources;
            appRes = res;
            PushResources();
            res["TextFont"] = TextFont;
            res.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(Styles));
        }

        // Стили стандартных элементов в цветах темы (DynamicResource — кисти выше).
        const string Styles = @"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='ContextMenu'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='SnapsToDevicePixels' Value='True'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='FontFamily' Value='{DynamicResource TextFont}'/>
    <Setter Property='FontSize' Value='13.3'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ContextMenu'>
        <Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='9' Padding='4' Margin='0,0,6,6'>
          <Border.Effect><DropShadowEffect BlurRadius='14' ShadowDepth='3' Opacity='0.35'/></Border.Effect>
          <ScrollViewer VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' CanContentScroll='False'>
            <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
          </ScrollViewer>
        </Border>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style TargetType='MenuItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='MenuItem'>
        <Grid>
          <Border x:Name='bd' Background='Transparent' CornerRadius='6' Padding='10,6,16,6' MinWidth='150'>
            <DockPanel>
              <ContentPresenter x:Name='ic' ContentSource='Icon' DockPanel.Dock='Left' Width='14' Height='14' Margin='0,0,9,0' VerticalAlignment='Center'/>
              <TextBlock x:Name='chk' DockPanel.Dock='Left' Text='✓' Width='16' Foreground='{DynamicResource Accent}' VerticalAlignment='Center'/>
              <TextBlock x:Name='arrow' DockPanel.Dock='Right' Text='›' FontSize='15' Margin='14,-2,-6,0' Foreground='{DynamicResource Muted}' Visibility='Collapsed' VerticalAlignment='Center'/>
              <ContentPresenter ContentSource='Header' RecognizesAccessKey='False' VerticalAlignment='Center'/>
            </DockPanel>
          </Border>
          <Popup x:Name='PART_Popup' Placement='Right' HorizontalOffset='-4' VerticalOffset='-5' AllowsTransparency='True' Focusable='False' PopupAnimation='Fade'
                 IsOpen='{Binding IsSubmenuOpen, RelativeSource={RelativeSource TemplatedParent}}'>
            <Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='9' Padding='4' Margin='0,0,6,6'>
              <Border.Effect><DropShadowEffect BlurRadius='14' ShadowDepth='3' Opacity='0.35'/></Border.Effect>
              <ScrollViewer VerticalScrollBarVisibility='Auto' HorizontalScrollBarVisibility='Disabled' CanContentScroll='False' MaxHeight='420'>
                <StackPanel IsItemsHost='True' KeyboardNavigation.DirectionalNavigation='Cycle'/>
              </ScrollViewer>
            </Border>
          </Popup>
        </Grid>
        <ControlTemplate.Triggers>
          <Trigger Property='HasItems' Value='True'><Setter TargetName='arrow' Property='Visibility' Value='Visible'/></Trigger>
          <Trigger Property='Icon' Value='{x:Null}'><Setter TargetName='ic' Property='Visibility' Value='Collapsed'/></Trigger>
          <Trigger Property='IsChecked' Value='False'><Setter TargetName='chk' Property='Text' Value=''/></Trigger>
          <Trigger Property='IsHighlighted' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource SurfaceHover}'/></Trigger>
          <Trigger Property='IsEnabled' Value='False'><Setter Property='Foreground' Value='{DynamicResource Dim}'/></Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style TargetType='Separator'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='Separator'><Border Height='1' Margin='8,4' Background='{DynamicResource Line}'/></ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style TargetType='ToolTip'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='HasDropShadow' Value='True'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ToolTip'>
        <Border Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='7' Padding='11,8'>
          <ContentPresenter TextBlock.Foreground='{DynamicResource Text}' TextBlock.FontSize='12' TextBlock.FontFamily='{DynamicResource TextFont}'/>
        </Border>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style TargetType='TextBox'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='CaretBrush' Value='{DynamicResource Text}'/>
    <Setter Property='SelectionBrush' Value='{DynamicResource Accent}'/>
    <Setter Property='FontFamily' Value='{DynamicResource TextFont}'/>
    <Setter Property='FontSize' Value='13.3'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='TextBox'>
        <Border x:Name='bd' Background='{DynamicResource Surface}' BorderBrush='{DynamicResource Line}' BorderThickness='1' CornerRadius='10' Padding='10,7'>
          <Grid>
            <ScrollViewer x:Name='PART_ContentHost' Focusable='False' HorizontalScrollBarVisibility='Hidden' VerticalScrollBarVisibility='Auto'/>
            <TextBlock x:Name='cue' Text='{TemplateBinding Tag}' Foreground='{DynamicResource Dim}' IsHitTestVisible='False' Visibility='Collapsed' Margin='2,0,0,0'/>
          </Grid>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='bd' Property='BorderBrush' Value='{DynamicResource Accent}'/></Trigger>
          <MultiTrigger>
            <MultiTrigger.Conditions><Condition Property='Text' Value=''/><Condition Property='IsKeyboardFocused' Value='False'/></MultiTrigger.Conditions>
            <Setter TargetName='cue' Property='Visibility' Value='Visible'/>
          </MultiTrigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Width' Value='10'/>
    <Setter Property='MinWidth' Value='10'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ScrollBar'>
        <Track x:Name='PART_Track' IsDirectionReversed='True'>
          <Track.Thumb>
            <Thumb>
              <Thumb.Template>
                <ControlTemplate TargetType='Thumb'><Border Background='{DynamicResource Knob}' CornerRadius='3' Width='6' Margin='2,2'/></ControlTemplate>
              </Thumb.Template>
            </Thumb>
          </Track.Thumb>
        </Track>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
  <Style TargetType='ListBoxItem'>
    <Setter Property='OverridesDefaultStyle' Value='True'/>
    <Setter Property='Foreground' Value='{DynamicResource Text}'/>
    <Setter Property='Template'><Setter.Value>
      <ControlTemplate TargetType='ListBoxItem'>
        <Border x:Name='bd' Background='Transparent' CornerRadius='8' Padding='10,6' Margin='0,1'>
          <ContentPresenter/>
        </Border>
        <ControlTemplate.Triggers>
          <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='bd' Property='Background' Value='{DynamicResource SurfaceHover}'/></Trigger>
          <Trigger Property='IsSelected' Value='True'>
            <Setter TargetName='bd' Property='Background' Value='{DynamicResource AccentDeep}'/>
            <Setter Property='Foreground' Value='{DynamicResource Accent}'/>
          </Trigger>
        </ControlTemplate.Triggers>
      </ControlTemplate>
    </Setter.Value></Setter>
  </Style>
</ResourceDictionary>";
    }
}
