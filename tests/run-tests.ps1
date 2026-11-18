# Автотесты логики ZapretGUI: подбор, статистика, списки. Ничего не меняют в системе и настройках программы.
# Запуск: powershell -ExecutionPolicy Bypass -File tests\run-tests.ps1
param([string]$Exe = "")
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$tmp = Join-Path ([IO.Path]::GetTempPath()) ("zapretgui-tests-" + [Guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force $tmp | Out-Null
if (-not $Exe) {
    $Exe = Join-Path $tmp 'test.exe'
    & powershell -ExecutionPolicy Bypass -File (Join-Path $root 'build.ps1') -NoAdmin -Out $Exe | Out-Null
    if (-not (Test-Path $Exe)) { "Сборка не удалась"; exit 2 }
}
$asm = [Reflection.Assembly]::LoadFrom($Exe)
$BF = [Reflection.BindingFlags]'Public,NonPublic,Static'
function T($n) { $asm.GetType("ZapretGui.$n") }
$script:pass = 0; $script:fail = 0
function Check($name, $cond) {
    if ($cond) { $script:pass++; "  ok    $name" } else { $script:fail++; "  FAIL  $name" }
}

# ---------- подбор ----------
"Подбор"
$PR = T 'PickResult'
function NewPick($name, $ok, $ping, $speed) { $r = [Activator]::CreateInstance($PR); $r.Name = $name; $r.Mode = 'std'; $r.Ok = $ok; $r.Total = 36; $r.PingOk = $ping; $r.SpeedKBps = $speed; $r }
$lt = [System.Collections.Generic.List``1].MakeGenericType($PR)
$list = [Activator]::CreateInstance($lt)
$list.Add((NewPick 'slow' 36 16 300)); $list.Add((NewPick 'fast' 36 16 2400)); $list.Add((NewPick 'worse' 35 16 9000)); $list.Add((NewPick 'fewping' 36 15 9000))
$ranked = $PR.GetMethod('Rank', $BF).Invoke($null, [object[]]@(,$list))
Check 'при равенстве первой идёт самая быстрая' ($ranked[0].Name -eq 'fast' -and $ranked[1].Name -eq 'slow')
Check 'больше пингов важнее скорости' ($ranked[2].Name -eq 'fewping')
Check 'меньше успешных — ниже всех' ($ranked[3].Name -eq 'worse')
Check '«наравне» определяется без учёта скорости' ($ranked[1].SameScore($ranked[0]) -and -not $ranked[2].SameScore($ranked[0]))

# ---------- статистика ----------
"Статистика"
$S = T 'Stats'; $SR = T 'StatRec'
$rl = [System.Collections.Generic.List``1].MakeGenericType($SR)
$recs = [Activator]::CreateInstance($rl)
$day = [DateTime]::Today.AddDays(-1)
for ($i = 0; $i -lt 60; $i++) {
    $r = [Activator]::CreateInstance($SR); $r.T = $day.AddHours(10).AddMinutes($i); $r.On = $true; $r.Strategy = 'A'
    $r.YtMax = 3; $r.DcMax = 3; $r.Dc = 3; $r.Yt = $(if ($i -ge 30 -and $i -lt 40) { 0 } else { 3 }); $r.YtMs = 100; $r.DcMs = 80
    $recs.Add($r)
}
$from = $day; $to = $day.AddDays(1)
$up = $S.GetMethod('UpPct', $BF).Invoke($null, @($recs, 0, $from, $to))
Check ("YouTube работал ~83% (получилось {0:N1})" -f $up) ($up -gt 80 -and $up -lt 86)
$out = $S.GetMethod('Outages', $BF).Invoke($null, @($recs, 0, $from, $to))
Check "один сбой YouTube (получилось $out)" ($out -eq 1)
$segs = $S.GetMethod('Segments', $BF).Invoke($null, @($recs, 0, $from, $to))
Check "три отрезка: работает, сбой, работает (получилось $($segs.Count))" ($segs.Count -eq 3 -and $segs[1].Kind -eq 5)
$gap = [Activator]::CreateInstance($SR); $gap.T = $day.AddHours(12); $gap.On = $true; $gap.YtMax = 3; $gap.Yt = 3; $gap.DcMax = 3; $gap.Dc = 3
$recs.Add($gap)
$segs2 = $S.GetMethod('Segments', $BF).Invoke($null, @($recs, 0, $from, $to))
Check 'пропуск больше 150 с — новый отрезок' ($segs2.Count -eq 4)

# дочитывание файла
$f = Join-Path $tmp 'stats.tsv'
$S.GetField('PathOverride', $BF).SetValue($null, $f)
$S.GetField('Enabled', $BF).SetValue($null, $true)
function AddRec($min) { $r = [Activator]::CreateInstance($SR); $r.T = [DateTime]::Now.AddMinutes(-$min); $r.On = $true; $r.Strategy = 'B'; $r.YtMax = 3; $r.Yt = 3; $r.DcMax = 3; $r.Dc = 3; $S.GetMethod('Add', $BF).Invoke($null, @($r)) }
AddRec 3; AddRec 2
$c1 = $S.GetMethod('LoadCached', $BF).Invoke($null, @([DateTime]::Today.AddDays(-1)))
AddRec 1
$c2 = $S.GetMethod('LoadCached', $BF).Invoke($null, @([DateTime]::Today.AddDays(-1)))
Check "дочитывает новые строки (было $($c1.Count), стало $($c2.Count))" ($c1.Count -eq 2 -and $c2.Count -eq 3)
$S.GetField('PathOverride', $BF).SetValue($null, $null)

# ---------- списки ----------
"Списки"
$LF = T 'ListFile'
function Norm($s, $ip) { $p = $null; $args2 = [object[]]@($s, $ip, $p); $v = $LF.GetMethod('Normalize', $BF).Invoke($null, $args2); @($v, $args2[2]) }
$n = Norm 'https://Www.Example.COM/path?x=1' $false
Check "ссылка превращается в домен (получилось $($n[0]))" ($n[0] -eq 'www.example.com' -or $n[0] -eq 'example.com')
$n = Norm 'пример.рф' $false
Check "кириллица в punycode (получилось $($n[0]))" ($n[0] -like 'xn--*')
$n = Norm '1.2.3.0/24' $true
Check 'подсеть принимается' ($n[0] -eq '1.2.3.0/24')
$n = Norm 'не домен вовсе' $false
Check 'мусор не принимается' ($n[0] -eq $null -or $n[0] -eq '')

# ---------- статистика: новые отчёты ----------
"Статистика (подробно)"
$ol = $S.GetMethod('OutageList', $BF).Invoke($null, @($recs, 0, $from, $to))
Check "список сбоев: один, ~10 минут (получилось $($ol.Count))" ($ol.Count -eq 1 -and [int]($ol[0].To - $ol[0].From).TotalMinutes -ge 9 -and $ol[0].Strategy -eq 'A')
$ps = $S.GetMethod('PingSeries', $BF).Invoke($null, @($recs, $from, $to, 24))
Check "пинг за 10-й час ~90 мс (получилось $([int]$ps[10]))" ($ps[10] -gt 80 -and $ps[10] -lt 100 -and $ps[3] -eq -1)
$fh = $S.GetMethod('FailByHour', $BF).Invoke($null, @($recs, $from, $to))
Check ("перебои в 10 часов ~17% (получилось {0:N2})" -f $fh[10]) ($fh[10] -gt 0.1 -and $fh[10] -lt 0.25 -and $fh[5] -eq -1)
$csv = [string](Join-Path $tmp 'out.csv')
$S.GetMethod('ExportCsv', $BF).Invoke($null, @($recs, $csv))
$lines = Get-Content $csv
Check "CSV: заголовок и все строки (строк $($lines.Count))" ($lines.Count -eq $recs.Count + 1 -and $lines[0] -like 'time;*')

# ---------- причины ошибок winws ----------
"Ошибки winws"
$Z = T 'Zapret'
$ex = $Z.GetMethod('ExplainWinws', $BF)
$t1 = $ex.Invoke($null, @(5, 'error opening filter: Access is denied'))
Check 'нет прав администратора' ($t1 -match 'администратор|administrator')
$t2 = $ex.Invoke($null, @(577, 'driver signature'))
Check 'драйвер заблокирован' ($t2 -match 'WinDivert')
$t3 = $ex.Invoke($null, @(1, 'unknown option --foo'))
Check 'неизвестный параметр стратегии' ($t3 -match 'параметр|option')
Check 'в тексте есть сообщение winws' ($t3 -match 'unknown option --foo')

# ---------- переносной режим (файл рядом с тестовой сборкой) ----------
"Настройки"
$P = T 'Prefs'
$iniPath = $P.GetProperty('IniPath', $BF).GetValue($null)
Check "файл настроек рядом с exe ($iniPath)" ($iniPath -like "$tmp*" -or (Split-Path $iniPath) -eq (Split-Path $Exe))
if ((Split-Path $iniPath) -eq (Split-Path $Exe) -and -not (Test-Path $iniPath)) {
    [IO.File]::WriteAllText($iniPath, "# test`r`nLang=en`r`nNote:general=лучшая вечером`r`n", (New-Object Text.UTF8Encoding $false))
    Check 'переносной режим виден' ($P.GetProperty('Portable', $BF).GetValue($null))
    Check 'чтение из ini' ($P.GetMethod('Get', $BF).Invoke($null, @('Lang', 'ru')) -eq 'en' -and $P.GetMethod('Get', $BF).Invoke($null, @('Note:general', '')) -eq 'лучшая вечером')
    $P.GetMethod('Set', $BF).Invoke($null, @('Theme', 'dark'))
    Check 'запись в ini, а не в реестр' ((Get-Content $iniPath -Encoding UTF8) -contains 'Theme=dark')
    Remove-Item $iniPath -Force
} else { "  пропущено: тестовая сборка лежит не во временной папке" }

# ---------- резервная копия: новые настройки (в переносном режиме — всё во временной папке) ----------
"Резервная копия"
if ((Split-Path $iniPath) -eq (Split-Path $Exe)) {
    [IO.File]::WriteAllText($iniPath, "Favorites=general|general (ALT)`r`nNote:general=лучшая вечером`r`nTiles=auto,game`r`nStatsKeep=7`r`n", (New-Object Text.UTF8Encoding $false))
    $P.GetField('ini', $BF).SetValue($null, $null)   # программа держит ini в памяти — перечитать после замены файла
    $root = Join-Path $tmp 'zapret'; New-Item -ItemType Directory -Force (Join-Path $root 'lists'), (Join-Path $root 'bin'), (Join-Path $root 'utils') | Out-Null
    [IO.File]::WriteAllText((Join-Path $root 'lists\list-general-user.txt'), "example.com`r`n")
    $zz = [Activator]::CreateInstance((T 'Zapret'), @([string]$root))
    $SB = T 'SettingsBackup'
    $zip = [string](Join-Path $tmp 'backup.zip')
    $SB.GetMethod('Export', $BF).Invoke($null, @($zz, $zip))
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $arc = [IO.Compression.ZipFile]::OpenRead($zip)
    $man = (New-Object IO.StreamReader($arc.GetEntry('zapretgui-settings.txt').Open())).ReadToEnd(); $arc.Dispose()
    Check 'в копии есть избранное, заметка и плитки' ($man -match 'pref\.Favorites=general' -and $man -match 'pref\.Note:general=лучшая вечером' -and $man -match 'pref\.Tiles=auto,game')
    [IO.File]::WriteAllText($iniPath, "Lang=ru`r`n", (New-Object Text.UTF8Encoding $false))
    $P.GetField('ini', $BF).SetValue($null, $null)
    $SB.GetMethod('Import', $BF).Invoke($null, @($zz, $zip)) | Out-Null
    Check 'после загрузки заметка и избранное вернулись' ($P.GetMethod('Get', $BF).Invoke($null, @('Note:general', '')) -eq 'лучшая вечером' -and $P.GetMethod('Get', $BF).Invoke($null, @('Favorites', '')) -eq 'general|general (ALT)')
    Check 'журнал и статистика в переносном режиме — рядом с программой' ($P.GetProperty('DataDir', $BF).GetValue($null) -like "$tmp*")
    Remove-Item $iniPath -Force
    $P.GetField('ini', $BF).SetValue($null, $null)
}

# ---------- статистика: свои сервисы, карта, сравнение стратегий ----------
"Статистика (новое)"
foreach ($r in $recs) { $r.Extra = 'Twitch=' + $(if ($r.T.Minute -lt 6) { -1 } else { 120 }) }
$sup = $S.GetMethod('ServiceUpPct', $BF).Invoke($null, @($recs, 'Twitch', $from, $to))
Check ("свой сервис: ~90% (получилось {0:N1})" -f $sup) ($sup -gt 85 -and $sup -lt 95)
$names = $S.GetMethod('ServiceNames', $BF).Invoke($null, @(,$recs))
Check 'имена своих сервисов из записей' ($names.Count -eq 1 -and $names[0] -eq 'Twitch')
$heat = $S.GetMethod('FailByWeekdayHour', $BF).Invoke($null, @($recs, $from, $to))
$wd = ([int]$day.DayOfWeek + 6) % 7
Check ("карта: перебои в {0}-й день в 10 часов (получилось {1:N2})" -f $wd, $heat[$wd, 10]) ($heat[$wd, 10] -gt 0.1 -and $heat[$wd, 3] -eq -1)
$per = $S.GetMethod('UpByDayPerStrategy', $BF).Invoke($null, @($recs, $from, 1))
Check ("сравнение стратегий: A ~83% в этот день (получилось {0:N1})" -f $per['A'][0]) ($per.ContainsKey('A') -and $per['A'][0] -gt 80 -and $per['A'][0] -lt 86)

# ---------- переезд в папку zapret: чужой файл не удаляется ----------
"Переезд программы"
$other = [string](Join-Path $tmp 'ZapretGUI-other.exe')
[IO.File]::WriteAllBytes($other, [byte[]](1..64))
$asm.GetType('ZapretGui.SelfMove').GetMethod('RemoveOldCopy', $BF).Invoke($null, @($other))
Check 'другой файл с тем же именем остаётся на месте' (Test-Path $other)
$notExe = [string](Join-Path $tmp 'notes.txt'); 'x' | Set-Content $notExe
$asm.GetType('ZapretGui.SelfMove').GetMethod('RemoveOldCopy', $BF).Invoke($null, @($notExe))
Check 'не-exe файл не трогается' (Test-Path $notExe)

# ---------- DNS (только чтение) ----------
$dns = (T 'Diag').GetMethod('DnsCheck', $BF).Invoke($null, @())
Check "проверка DNS отвечает: $($dns.Title)" ($dns -ne $null -and $dns.Title -ne '')

# ---------- описание релиза ----------
"Разное"
$J = T 'Json'
$o = $J.GetMethod('Obj', $BF).Invoke($null, @('{"tag_name":"v9.9.9","body":"a\nb \"c\" Ж","assets":[{"name":"ZapretGUI.exe","browser_download_url":"https://x/ZapretGUI.exe"},{"name":"other.zip"}]}'))
Check 'разбор JSON: строки' ($J.GetMethod('Str', $BF).Invoke($null, @($o, 'tag_name')) -eq 'v9.9.9' -and $J.GetMethod('Str', $BF).Invoke($null, @($o, 'body')) -eq "a`nb `"c`" Ж")
$arr = $J.GetMethod('Arr', $BF).Invoke($null, @($o, 'assets'))
Check "разбор JSON: списки (получилось $($arr.Count))" ($arr.Count -eq 2 -and $arr[0]['name'] -eq 'ZapretGUI.exe')
$cur = (T 'SelfUpdate').GetProperty('Current', $BF).GetValue($null)
$notes = (T 'SelfUpdate').GetMethod('BuiltInNotes', $BF).Invoke($null, @($cur))
Check "встроенный список изменений для версии $cur" ($notes -ne $null -and $notes.Length -gt 50)

""
"Итог: прошло $script:pass, не прошло $script:fail"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
