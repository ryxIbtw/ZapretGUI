# Сборка ZapretGUI.exe встроенным в Windows компилятором C# (.NET Framework 4.x).
# Запуск:  powershell -ExecutionPolicy Bypass -File build.ps1
param([string]$Out = "$PSScriptRoot\ZapretGUI.exe", [switch]$NoAdmin)

$ErrorActionPreference = 'Stop'
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "$env:WINDIR\Microsoft.NET\Framework\v4.0.30319\csc.exe" }
$src = "$PSScriptRoot\src"

# Иконка приложения (круг с символом питания), PNG внутри ICO
Add-Type -AssemblyName System.Drawing
$ico = "$src\app.ico"
$sizes = 16, 32, 48, 256
$pngs = foreach ($s in $sizes) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(27, 28, 32))), 0.5, 0.5, $s - 1.5, $s - 1.5)
    $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(52, 211, 153)), ([Math]::Max(1.6, $s * 0.1))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $c = $s / 2; $r = $s * 0.24
    $g.DrawArc($pen, $c - $r, $c - $r + $s * 0.03, $r * 2, $r * 2, -55, 290)
    $g.DrawLine($pen, $c, $s * 0.2, $c, $s * 0.48)
    $g.Dispose()
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    , $ms.ToArray()
}
$fs = [System.IO.File]::Create($ico); $w = New-Object System.IO.BinaryWriter $fs
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $b = if ($s -ge 256) { 0 } else { $s }
    $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $w.Write($p) }
$w.Close()

$cscArgs = @('/nologo', '/target:winexe', '/optimize+', '/codepage:65001', "/out:$Out", "/win32icon:$ico",
    '/r:System.dll', '/r:System.Drawing.dll', '/r:System.Windows.Forms.dll', '/r:System.ServiceProcess.dll', '/r:System.Core.dll',
    '/r:System.IO.Compression.dll', '/r:System.IO.Compression.FileSystem.dll')
if (-not $NoAdmin) { $cscArgs += "/win32manifest:$src\app.manifest" }
$cscArgs += (Get-ChildItem "$src\*.cs").FullName

& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw "Сборка завершилась с ошибкой" }
Write-Host "Готово: $Out"

