# Готовит манифест winget (Windows Package Manager) для выпущенной версии.
# Запуск после выпуска релиза:  powershell -ExecutionPolicy Bypass -File winget\make-manifest.ps1 -Version 2.5.0
# Скачивает ZapretGUI.exe из релиза, считает SHA256 и кладёт три файла манифеста в winget\manifests\r\ryxIbtw\ZapretGUI\<версия>.
# Отправка в каталог winget (pull request в microsoft/winget-pkgs) — отдельный шаг, вручную.
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
$id = 'ryxIbtw.ZapretGUI'
$url = "https://github.com/ryxIbtw/ZapretGUI/releases/download/v$Version/ZapretGUI.exe"
$tmp = Join-Path ([IO.Path]::GetTempPath()) "zapretgui-winget-$Version.exe"
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor 3072
(New-Object Net.WebClient).DownloadFile($url, $tmp)
$sha = (Get-FileHash -Algorithm SHA256 $tmp).Hash.ToUpperInvariant()
Remove-Item $tmp -Force
$dir = Join-Path $PSScriptRoot "manifests\r\ryxIbtw\ZapretGUI\$Version"
New-Item -ItemType Directory -Force $dir | Out-Null
$utf8 = New-Object Text.UTF8Encoding $false

[IO.File]::WriteAllText("$dir\$id.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.version.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
DefaultLocale: ru-RU
ManifestType: version
ManifestVersion: 1.6.0
"@, $utf8)

[IO.File]::WriteAllText("$dir\$id.installer.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.installer.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
InstallerType: portable
Commands:
- zapretgui
Installers:
- Architecture: x64
  InstallerUrl: $url
  InstallerSha256: $sha
- Architecture: x86
  InstallerUrl: $url
  InstallerSha256: $sha
ManifestType: installer
ManifestVersion: 1.6.0
"@, $utf8)

[IO.File]::WriteAllText("$dir\$id.locale.ru-RU.yaml", @"
# yaml-language-server: `$schema=https://aka.ms/winget-manifest.defaultLocale.1.6.0.schema.json
PackageIdentifier: $id
PackageVersion: $Version
PackageLocale: ru-RU
Publisher: ryxIbtw
PublisherUrl: https://github.com/ryxIbtw
PackageName: Zapret GUI
PackageUrl: https://github.com/ryxIbtw/ZapretGUI
License: MIT
LicenseUrl: https://github.com/ryxIbtw/ZapretGUI/blob/main/LICENSE
ShortDescription: Простой интерфейс для zapret-discord-youtube от Flowseal — обход блокировок YouTube и Discord одной кнопкой
Description: |-
  Неофициальный интерфейс для zapret-discord-youtube (Flowseal). Включает обход одной кнопкой, подбирает стратегию,
  следит, открываются ли YouTube и Discord, ведёт статистику и сообщает о сбоях. Положите программу в папку zapret
  или дайте ей скачать zapret при первом запуске.
Tags:
- zapret
- youtube
- discord
- dpi
ReleaseNotesUrl: https://github.com/ryxIbtw/ZapretGUI/releases/tag/v$Version
ManifestType: defaultLocale
ManifestVersion: 1.6.0
"@, $utf8)

"Готово: $dir"
"SHA256: $sha"
"Проверить манифест:  winget validate --manifest `"$dir`""
