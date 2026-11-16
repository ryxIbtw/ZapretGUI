# Снимок окна программы в PNG — для проверки вида и для картинок в README.
# Нужна тестовая сборка:  powershell -ExecutionPolicy Bypass -File build.ps1 -NoAdmin -Out <test.exe>
# Запуск (параметры — по именам):
#   powershell -STA -ExecutionPolicy Bypass -File tests\snap.ps1 -exe <test.exe> -root <папка zapret> -out <файл.png> -page main -theme dark
# Окно создаётся напрямую, поиск папки zapret не вызывается — настройки программы не меняются, обход не трогается.
# Страницы: main settings welcome pick picksetup about journal editor ask flyout stats report whatsnew guinews strategies help checksite targets diag
# Доп. переменные окружения: ZAPRETGUI_SNAP_ON=0/1, _NET=ok/bad, _SIMPLE=1, _TILES=auto,watch, _RANGE=0/1/2, _DPI=2, _BANNER=1, _PICK=1, _NOTES=<файл>
param($exe, $root, $out, $page = 'main', $theme = 'dark', $accent = 'auto', $scale = '', $lang = 'ru')
Add-Type -AssemblyName PresentationFramework, PresentationCore, WindowsBase
$env:ZAPRETGUI_SNAP_PAGE = $page; $env:ZAPRETGUI_SNAP_THEME = $theme; $env:ZAPRETGUI_SNAP_ACCENT = $accent; $env:ZAPRETGUI_SNAP_LANG = $lang
if (-not $env:ZAPRETGUI_SNAP_NET) { $env:ZAPRETGUI_SNAP_NET = 'ok' }
if ($scale) { $env:ZAPRETGUI_SNAP_SCALE = $scale } else { $env:ZAPRETGUI_SNAP_SCALE = $null }
try {
    $asm = [Reflection.Assembly]::LoadFrom($exe)
    $BF = [Reflection.BindingFlags]'Public,NonPublic,Static'
    function T($n) { $asm.GetType("ZapretGui.$n") }
    (T 'L').GetField('En', $BF).SetValue($null, ($lang -eq 'en'))
    $app = (New-Object System.Windows.Application).PSObject.BaseObject
    $app.ShutdownMode = 'OnExplicitShutdown'
    (T 'W').GetMethod('Install', $BF).Invoke($null, @($app))
    $z = [Activator]::CreateInstance((T 'Zapret'), @($root))
    $w = [Activator]::CreateInstance((T 'MainWindow'), [Reflection.BindingFlags]'Public,NonPublic,Instance', $null, @($z, $out, $false), $null)
    (T 'L').GetField('En', $BF).SetValue($null, ($lang -eq 'en'))
    $guard = New-Object System.Windows.Threading.DispatcherTimer
    $guard.Interval = [TimeSpan]::FromSeconds(25)
    $guard.add_Tick({ $app.Shutdown() })
    $guard.Start()
    (T 'Snap').GetMethod('Run', $BF).Invoke($null, @($w, $out))
    $app.Run() | Out-Null
} catch {
    "SNAP ERROR: " + $_.Exception.ToString()
    exit 1
}
