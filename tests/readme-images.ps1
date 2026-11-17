# Картинки для README и сайта: docs\banner.png, docs\screenshots.png, docs\screenshots-2.png.
# Запуск:  powershell -ExecutionPolicy Bypass -File tests\readme-images.ps1 -Exe <тестовая сборка> -Root <папка zapret>
# Тестовая сборка:  powershell -ExecutionPolicy Bypass -File build.ps1 -NoAdmin -Out <test.exe>
# Данные на снимках — выдуманные (статистика и сайты), обход и настройки программы не трогаются.
param([Parameter(Mandatory = $true)][string]$Exe, [Parameter(Mandatory = $true)][string]$Root)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("zapretgui-images-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $tmp | Out-Null

function Shot($name, $page, $theme, $extra) {
    $env:ZAPRETGUI_SNAP_ON = '1'; $env:ZAPRETGUI_SNAP_DPI = '2'
    $env:ZAPRETGUI_SNAP_SIMPLE = $null; $env:ZAPRETGUI_SNAP_RANGE = $null
    if ($extra) { foreach ($k in $extra.Keys) { Set-Item "env:$k" $extra[$k] } }
    $out = Join-Path $tmp $name
    & powershell -STA -ExecutionPolicy Bypass -File (Join-Path $PSScriptRoot 'snap.ps1') -exe $Exe -root $Root -out $out -page $page -theme $theme | Out-Null
    if (-not (Test-Path $out)) { throw "Снимок не получился: $name" }
    $out
}
$dark = Shot 'dark.png' main dark $null
$light = Shot 'light.png' main light $null
$simple = Shot 'simple.png' main dark @{ ZAPRETGUI_SNAP_SIMPLE = '1' }
$stats = Shot 'stats.png' stats dark @{ ZAPRETGUI_SNAP_RANGE = '1' }

$compose = @'
param($dark, $light, $simple, $stats, $docs)
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase, System.Xaml
function Img($p) { $b = New-Object System.Windows.Media.Imaging.BitmapImage; $b.BeginInit(); $b.CacheOption = 'OnLoad'; $b.UriSource = New-Object Uri($p); $b.EndInit(); $b.Freeze(); $b }
function Brush($h) { $c = [System.Windows.Media.ColorConverter]::ConvertFromString($h); $s = New-Object System.Windows.Media.SolidColorBrush $c; $s.Freeze(); $s }
function Win($bmp, $x, $y, $w, $h, $border) {
    $b = New-Object System.Windows.Controls.Border; $b.Width = $w; $b.Height = $h; $b.CornerRadius = 14
    $ib = New-Object System.Windows.Media.ImageBrush $bmp; $ib.Stretch = 'Fill'; $b.Background = $ib
    $b.BorderBrush = Brush $border; $b.BorderThickness = 1
    $fx = New-Object System.Windows.Media.Effects.DropShadowEffect; $fx.BlurRadius = 40; $fx.ShadowDepth = 12; $fx.Opacity = 0.45; $fx.Direction = 270; $b.Effect = $fx
    [System.Windows.Media.RenderOptions]::SetBitmapScalingMode($b, 'HighQuality')
    [System.Windows.Controls.Canvas]::SetLeft($b, $x); [System.Windows.Controls.Canvas]::SetTop($b, $y); $b
}
function Txt($t, $size, $hex, $weight, $x, $y, $w) {
    $tb = New-Object System.Windows.Controls.TextBlock; $tb.Text = $t; $tb.FontSize = $size; $tb.Foreground = Brush $hex; $tb.FontWeight = $weight
    $tb.FontFamily = New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Display, Segoe UI'
    if ($w) { $tb.Width = $w; $tb.TextAlignment = 'Center' }
    [System.Windows.Controls.Canvas]::SetLeft($tb, $x); [System.Windows.Controls.Canvas]::SetTop($tb, $y); $tb
}
function Chip($text, $x, $y) {
    $b = New-Object System.Windows.Controls.Border; $b.CornerRadius = 9; $b.Padding = New-Object System.Windows.Thickness(13, 6, 13, 7)
    $b.Background = Brush '#1B1C20'; $b.BorderBrush = Brush '#2C2D33'; $b.BorderThickness = 1
    $t = New-Object System.Windows.Controls.TextBlock; $t.Text = $text; $t.FontSize = 15; $t.Foreground = Brush '#ECECEE'
    $t.FontFamily = New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Text, Segoe UI'; $b.Child = $t
    [System.Windows.Controls.Canvas]::SetLeft($b, $x); [System.Windows.Controls.Canvas]::SetTop($b, $y)
    $b.Measure((New-Object System.Windows.Size([double]::PositiveInfinity, [double]::PositiveInfinity))); $b
}
function Save($c, $w, $h, $scale, $path) {
    $c.Width = $w; $c.Height = $h; $c.Measure((New-Object System.Windows.Size($w, $h))); $c.Arrange((New-Object System.Windows.Rect(0, 0, $w, $h))); $c.UpdateLayout()
    $rtb = [System.Windows.Media.Imaging.RenderTargetBitmap]::new([int]($w * $scale), [int]($h * $scale), 96 * $scale, 96 * $scale, [System.Windows.Media.PixelFormats]::Pbgra32); $rtb.Render($c)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder; $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create([System.Windows.Media.Imaging.BitmapSource]$rtb))
    $fs = [IO.File]::Create($path); $enc.Save($fs); $fs.Close()
}
$d = Img $dark; $l = Img $light; $s = Img $simple; $st = Img $stats
$W1 = $d.PixelWidth / 2; $H1 = $d.PixelHeight / 2

# скриншоты: тёмная и светлая тема
$c = New-Object System.Windows.Controls.Canvas; $c.Background = Brush '#1A1B1F'
$c.Children.Add((Win $d 96 70 $W1 $H1 '#2C2D33')) | Out-Null
$c.Children.Add((Win $l (96 + $W1 + 64) 70 $W1 $H1 '#D9DCE2')) | Out-Null
$c.Children.Add((Txt 'Тёмная тема' 18 '#8B8C94' 'Normal' 96 (70 + $H1 + 24) $W1)) | Out-Null
$c.Children.Add((Txt 'Светлая тема' 18 '#8B8C94' 'Normal' (96 + $W1 + 64) (70 + $H1 + 24) $W1)) | Out-Null
Save $c (96 * 2 + $W1 * 2 + 64) (70 + $H1 + 96) 2 (Join-Path $docs 'screenshots.png')

# простой режим и статистика
$ws = $s.PixelWidth / 2; $hs = $s.PixelHeight / 2; $wt = $st.PixelWidth / 2; $ht = [Math]::Min($st.PixelHeight / 2, 1100)
$H = [Math]::Max($hs, $ht)
$c2 = New-Object System.Windows.Controls.Canvas; $c2.Background = Brush '#1A1B1F'
$c2.Children.Add((Win $s 96 (70 + ($H - $hs) / 2) $ws $hs '#2C2D33')) | Out-Null
$crop = New-Object System.Windows.Media.Imaging.CroppedBitmap($st, (New-Object System.Windows.Int32Rect(0, 0, $st.PixelWidth, [int]($ht * 2))))
$c2.Children.Add((Win $crop (96 + $ws + 64) 70 $wt $ht '#2C2D33')) | Out-Null
$c2.Children.Add((Txt 'Простой режим' 18 '#8B8C94' 'Normal' 96 (70 + $H + 24) $ws)) | Out-Null
$c2.Children.Add((Txt 'Статистика' 18 '#8B8C94' 'Normal' (96 + $ws + 64) (70 + $H + 24) $wt)) | Out-Null
Save $c2 (96 * 2 + $ws + 64 + $wt) (70 + $H + 96) 2 (Join-Path $docs 'screenshots-2.png')

# баннер 1280×640
$b = New-Object System.Windows.Controls.Canvas; $b.Background = Brush '#111214'; $b.ClipToBounds = $true
$g = New-Object System.Windows.Shapes.Ellipse; $g.Width = 880; $g.Height = 640
$rg = New-Object System.Windows.Media.RadialGradientBrush
$rg.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromArgb(56, 52, 211, 153)), 0))
$rg.GradientStops.Add((New-Object System.Windows.Media.GradientStop ([System.Windows.Media.Color]::FromArgb(0, 52, 211, 153)), 1)); $g.Fill = $rg
[System.Windows.Controls.Canvas]::SetLeft($g, 540); [System.Windows.Controls.Canvas]::SetTop($g, -20); $b.Children.Add($g) | Out-Null
$k = 0.85
$b.Children.Add((Win $l 830 124 ($W1 * $k) ($H1 * $k) '#D9DCE2')) | Out-Null
$b.Children.Add((Win $d 726 70 ($W1 * $k) ($H1 * $k) '#2C2D33')) | Out-Null
$b.Children.Add((Txt 'Zapret GUI' 76 '#ECECEE' 'SemiBold' 84 188 $null)) | Out-Null
$sub = Txt "Понятный интерфейс для обхода блокировок`nYouTube и Discord — всё на одной панели" 24 '#8B8C94' 'Normal' 88 312 $null
$sub.FontFamily = New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Text, Segoe UI'; $b.Children.Add($sub) | Out-Null
$x = 88
foreach ($t in 'Автоподбор стратегии', 'Статистика', 'Уведомления о сбоях', 'Темы') { $ch = Chip $t $x 410; $b.Children.Add($ch) | Out-Null; $x += $ch.DesiredSize.Width + 10 }
$foot = New-Object System.Windows.Controls.TextBlock; $foot.FontSize = 17; $foot.FontFamily = New-Object System.Windows.Media.FontFamily 'Segoe UI Variable Text, Segoe UI'
foreach ($p in @(@('zapret by ', '#5C5D65', 'Normal'), @('Flowseal', '#34D399', 'SemiBold'), @('   ·   GUI by ', '#5C5D65', 'Normal'), @('ryxIbtw', '#34D399', 'SemiBold'))) {
    $run = New-Object System.Windows.Documents.Run $p[0]; $run.Foreground = Brush $p[1]; $run.FontWeight = $p[2]; $foot.Inlines.Add($run)
}
[System.Windows.Controls.Canvas]::SetLeft($foot, 88); [System.Windows.Controls.Canvas]::SetTop($foot, 482); $b.Children.Add($foot) | Out-Null
Save $b 1280 640 1 (Join-Path $docs 'banner.png')
"ok"
'@
$cs = Join-Path $tmp 'compose.ps1'
[IO.File]::WriteAllText($cs, $compose, (New-Object Text.UTF8Encoding $true))
& powershell -STA -ExecutionPolicy Bypass -File $cs -dark $dark -light $light -simple $simple -stats $stats -docs (Join-Path $repo 'docs')
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
"Готово: docs\banner.png, docs\screenshots.png, docs\screenshots-2.png"
