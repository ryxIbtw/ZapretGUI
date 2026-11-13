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

# ---------- описание релиза ----------
"Разное"
$un = (T 'SelfUpdate').GetMethod('JsonUnescape', $BF).Invoke($null, @('a\nb \"c\" Ж'))
Check 'разбор JSON-строки' ($un -eq "a`nb `"c`" Ж")

""
"Итог: прошло $script:pass, не прошло $script:fail"
Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
if ($script:fail -gt 0) { exit 1 } else { exit 0 }
