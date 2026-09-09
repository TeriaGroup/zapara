<#
.SYNOPSIS
  Android verify: emulator, Github/RuStore JVM + connected tests, adb pull frames, report.md.
.PARAMETER Avd
  AVD name used only when no emulator is already online. Default zapara-api34.
.PARAMETER Out
  Artifact directory. Default %TEMP%\zapara-android\<yyyyMMdd-HHmmss>.
.PARAMETER Flavor
  Product flavor: github or rustore. Default github.
.PARAMETER KeepEmulator
  Keep an emulator this script started. A foreign emulator is never killed.
.OUTPUTS
  Exit 0 all tests passed; 1 test failures; 2 emulator/build failure.
#>
param(
    [string]$Avd = "zapara-api34",
    [string]$Out = (Join-Path $env:TEMP ("zapara-android\" + (Get-Date -Format "yyyyMMdd-HHmmss"))),
    [string]$Flavor = "github",
    [switch]$KeepEmulator
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$package = "ru.zapara.app"
$notifyPermission = "android.permission.POST_NOTIFICATIONS"
$scriptsDir = $PSScriptRoot
$androidRoot = Split-Path -Parent $scriptsDir
$repoRoot = Split-Path -Parent $androidRoot
$flavorNorm = $Flavor.Trim().ToLowerInvariant()
if ($flavorNorm -ne "github" -and $flavorNorm -ne "rustore") {
    throw "Flavor must be github or rustore, got: $Flavor"
}
$capFlavor = $flavorNorm.Substring(0, 1).ToUpperInvariant() + $flavorNorm.Substring(1)
$gradlew = Join-Path $androidRoot "gradlew.bat"
$report = $null
$adb = $null
$serial = $null
$started = $null
$gate = $null
$locked = $false
$watchProcess = $null
$watchStop = $null
$savedAnimator = $null
$animatorChanged = $false
$savedEnv = @{}
$exitCode = 2
$jvmOk = $false
$connectedOk = $false
$motionOffOk = $false
$buildFailed = $false
$jvmRows = @()
$connectedRows = @()
$motionOffRows = @()
$packageLogErrors = @()
$jvmLogText = ""
$frames = @()

function Write-Log([string]$Message) {
    $line = "{0} {1}" -f (Get-Date -Format "s"), $Message
    Write-Host $line
    if ($script:Out) {
        Add-Content -LiteralPath (Join-Path $script:Out "run.log") -Value $line -Encoding UTF8
    }
}

function Add-Report([string]$Message) {
    Add-Content -LiteralPath $script:report -Value $Message -Encoding UTF8
}

function Resolve-SdkDir {
    $props = Join-Path $androidRoot "local.properties"
    if (Test-Path -LiteralPath $props) {
        foreach ($line in Get-Content -LiteralPath $props -Encoding UTF8) {
            if ($line -match '^\s*sdk\.dir\s*=\s*(.+)\s*$') {
                $raw = $Matches[1].Trim()
                $raw = $raw -replace '\\:', ':'
                $raw = $raw -replace '\\\\', '\'
                if (Test-Path -LiteralPath $raw) { return [IO.Path]::GetFullPath($raw) }
            }
        }
    }
    foreach ($c in @($env:ANDROID_HOME, $env:ANDROID_SDK_ROOT, "C:\Android\sdk", (Join-Path $env:LOCALAPPDATA "Android\Sdk"))) {
        if (-not [string]::IsNullOrWhiteSpace($c) -and (Test-Path -LiteralPath $c)) {
            return [IO.Path]::GetFullPath($c)
        }
    }
    throw "Android SDK not found (local.properties sdk.dir / ANDROID_HOME / C:\\Android\\sdk)"
}

function Resolve-JdkHome {
    $candidates = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($env:ZAPARA_JAVA_HOME)) { [void]$candidates.Add($env:ZAPARA_JAVA_HOME) }
    [void]$candidates.Add("C:\Android\jdk17")
    $opencode = Join-Path $env:TEMP "opencode"
    if (Test-Path -LiteralPath $opencode) {
        Get-ChildItem -LiteralPath $opencode -Directory -Filter "zapara-android-tools-*" -ErrorAction SilentlyContinue |
            Sort-Object Name -Descending |
            ForEach-Object {
                $jdkRoot = Join-Path $_.FullName "jdk"
                if (Test-Path -LiteralPath $jdkRoot) {
                    Get-ChildItem -LiteralPath $jdkRoot -Directory -ErrorAction SilentlyContinue |
                        ForEach-Object { [void]$candidates.Add($_.FullName) }
                }
            }
    }
    foreach ($c in $candidates) {
        if ([string]::IsNullOrWhiteSpace($c)) { continue }
        $java = Join-Path $c "bin\java.exe"
        if (Test-Path -LiteralPath $java) { return [IO.Path]::GetFullPath($c) }
    }
    throw "JDK 17 not found. Set ZAPARA_JAVA_HOME or install to C:\\Android\\jdk17."
}

function Invoke-Adb {
    param(
        [Parameter(Mandatory = $true)][string[]]$Args,
        [switch]$ThrowOnError
    )
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $text = & $script:adb @Args 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
    if ($ThrowOnError -and $code -ne 0) {
        throw "adb $($Args -join ' ') exit $code"
    }
    return @{ Code = $code; Text = @($text | ForEach-Object { "$_" }) }
}

function Get-EmulatorSerials {
    $result = Invoke-Adb -Args @("devices")
    $serials = New-Object System.Collections.Generic.List[string]
    foreach ($line in $result.Text) {
        if ($line -match '^(emulator-\d+)\s+device(\s|$)') {
            [void]$serials.Add($Matches[1])
        }
    }
    return @($serials)
}

function Wait-BootCompleted([string]$Serial, [int]$Tries = 90) {
    $boot = ""
    for ($i = 0; $i -lt $Tries; $i++) {
        $r = Invoke-Adb -Args @("-s", $Serial, "shell", "getprop", "sys.boot_completed")
        $boot = (($r.Text | Out-String).Trim())
        if ($boot -eq "1") { return $true }
        Start-Sleep -Seconds 2
    }
    return $false
}

function Invoke-Gradle {
    param(
        [Parameter(Mandatory = $true)][string[]]$Tasks,
        [Parameter(Mandatory = $true)][string]$LogFile
    )
    $quoted = foreach ($t in $Tasks) {
        if ($t -match '[\s"]') { '"' + ($t -replace '"', '\"') + '"' } else { $t }
    }
    $taskLine = [string]::Join(" ", $quoted)
    $cmd = "cd /d `"$androidRoot`" && `"$gradlew`" `"-Dorg.gradle.java.home=$jdkHome`" --no-daemon --console=plain $taskLine"
    Write-Log "gradle $taskLine"
    $old = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        cmd.exe /c "$cmd > `"$LogFile`" 2>&1"
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
    Write-Log "gradle exit $code  log=$LogFile"
    return $code
}

function Get-JUnitClassRows {
    param(
        [string]$Dir,
        [datetime]$After = [datetime]::MinValue
    )
    $map = @{}
    if (-not (Test-Path -LiteralPath $Dir)) { return @() }
    $files = @(Get-ChildItem -LiteralPath $Dir -Filter *.xml -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTime -ge $After })
    foreach ($file in $files) {
        try {
            [xml]$doc = Get-Content -LiteralPath $file.FullName -Encoding UTF8
        } catch {
            Write-Log "skip xml $($file.FullName): $($_.Exception.Message)"
            continue
        }
        $root = $doc.DocumentElement
        if ($null -eq $root) { continue }
        $suites = @()
        if ($root.LocalName -eq "testsuites") {
            $suites = @($root.ChildNodes | Where-Object { $_.LocalName -eq "testsuite" })
        } elseif ($root.LocalName -eq "testsuite") {
            $suites = @($root)
        } else {
            continue
        }
        foreach ($suite in $suites) {
            if ($null -eq $suite) { continue }
            $cases = @($suite.ChildNodes | Where-Object { $_.LocalName -eq "testcase" })
            if ($cases.Count -eq 0) {
                $name = [string]$suite.GetAttribute("name")
                if ([string]::IsNullOrWhiteSpace($name)) { continue }
                if (-not $map.ContainsKey($name)) {
                    $map[$name] = [pscustomobject]@{ Class = $name; Tests = 0; Failures = 0; Errors = 0; Skipped = 0; Seconds = 0.0 }
                }
                $row = $map[$name]
                $row.Tests += [int](0 + $suite.GetAttribute("tests"))
                $row.Failures += [int](0 + $suite.GetAttribute("failures"))
                $errAttr = $suite.GetAttribute("errors")
                if (-not [string]::IsNullOrWhiteSpace($errAttr)) { $row.Errors += [int]$errAttr }
                $timeAttr = $suite.GetAttribute("time")
                if (-not [string]::IsNullOrWhiteSpace($timeAttr)) { $row.Seconds += [double]$timeAttr }
                continue
            }
            foreach ($case in $cases) {
                $cls = [string]$case.GetAttribute("classname")
                if ([string]::IsNullOrWhiteSpace($cls)) { $cls = [string]$suite.GetAttribute("name") }
                if (-not $map.ContainsKey($cls)) {
                    $map[$cls] = [pscustomobject]@{ Class = $cls; Tests = 0; Failures = 0; Errors = 0; Skipped = 0; Seconds = 0.0 }
                }
                $row = $map[$cls]
                $row.Tests += 1
                $timeAttr = $case.GetAttribute("time")
                if (-not [string]::IsNullOrWhiteSpace($timeAttr)) { $row.Seconds += [double]$timeAttr }
                foreach ($child in @($case.ChildNodes)) {
                    if ($child.LocalName -eq "failure") { $row.Failures += 1 }
                    elseif ($child.LocalName -eq "error") { $row.Errors += 1 }
                    elseif ($child.LocalName -eq "skipped") { $row.Skipped += 1 }
                }
            }
        }
    }
    return @($map.Values | Sort-Object Class)
}

function Format-ClassTable($Rows) {
    $lines = New-Object System.Collections.Generic.List[string]
    [void]$lines.Add("| Класс | Тестов | Падений | Ошибок | Секунд |")
    [void]$lines.Add("|---|---:|---:|---:|---:|")
    if (-not $Rows -or @($Rows).Count -eq 0) {
        [void]$lines.Add("| - | 0 | 0 | 0 | 0 |")
        return $lines
    }
    foreach ($r in @($Rows)) {
        $short = $r.Class
        if ($short.StartsWith("$package.")) { $short = $short.Substring($package.Length + 1) }
        $sec = "{0:N1}" -f $r.Seconds
        $mark = ""
        if (($r.Failures + $r.Errors) -gt 0) { $mark = " FAIL" }
        [void]$lines.Add("| ``$short``$mark | $($r.Tests) | $($r.Failures) | $($r.Errors) | $sec |")
    }
    $t = ($Rows | Measure-Object Tests -Sum).Sum
    $f = ($Rows | Measure-Object Failures -Sum).Sum
    $e = ($Rows | Measure-Object Errors -Sum).Sum
    $s = ($Rows | Measure-Object Seconds -Sum).Sum
    [void]$lines.Add("| **итого** | **$t** | **$f** | **$e** | **{0:N1}** |" -f $s)
    return $lines
}

function Copy-Tree([string]$Src, [string]$Dest) {
    if (-not (Test-Path -LiteralPath $Src)) { return }
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    Copy-Item -LiteralPath $Src -Destination $Dest -Recurse -Force -ErrorAction SilentlyContinue
}

function Pull-Pngs {
    param([string[]]$Remotes, [string]$Dest)
    New-Item -ItemType Directory -Force -Path $Dest | Out-Null
    $pulled = 0
    foreach ($remote in $Remotes) {
        $tmp = Join-Path $script:Out ("pull-" + [Guid]::NewGuid().ToString("n"))
        New-Item -ItemType Directory -Force -Path $tmp | Out-Null
        $r = Invoke-Adb -Args @("-s", $script:serial, "pull", $remote, $tmp)
        $pngs = @(Get-ChildItem -LiteralPath $tmp -Recurse -Filter *.png -File -ErrorAction SilentlyContinue)
        foreach ($png in $pngs) {
            Copy-Item -LiteralPath $png.FullName -Destination (Join-Path $Dest $png.Name) -Force
            $pulled += 1
        }
        Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
        if ($r.Code -eq 0) {
            Write-Log "pull $remote -> $($pngs.Count) png"
        }
    }
    return $pulled
}

function Get-FrameRemotes {
    return @(
        "/sdcard/Android/data/$package/files/frames",
        "/storage/emulated/0/Android/data/$package/files/frames",
        "/sdcard/Android/data/$package/files/shell-diagnostic",
        "/data/local/tmp/zapara-shell-task2",
        "/data/local/tmp/zapara-focus-direction"
    )
}

function Start-FrameWatch([string]$Dest) {
    $script:watchStop = Join-Path $script:Out "stop-frame-watch"
    if (Test-Path -LiteralPath $script:watchStop) { Remove-Item -LiteralPath $script:watchStop -Force }
    $watchPs1 = Join-Path $script:Out "frame-watch.ps1"
    $watchBody = @"
param([string]`$Adb,[string]`$Serial,[string]`$Dest,[string]`$StopFile)
`$ErrorActionPreference = 'Continue'
New-Item -ItemType Directory -Force -Path `$Dest | Out-Null
`$remotes = @(
  '/sdcard/Android/data/$package/files/frames',
  '/storage/emulated/0/Android/data/$package/files/frames',
  '/sdcard/Android/data/$package/files/shell-diagnostic',
  '/data/local/tmp/zapara-shell-task2',
  '/data/local/tmp/zapara-focus-direction'
)
while (-not (Test-Path -LiteralPath `$StopFile)) {
  foreach (`$remote in `$remotes) {
    `$tmp = Join-Path `$Dest '.watch-tmp'
    if (Test-Path -LiteralPath `$tmp) { Remove-Item -LiteralPath `$tmp -Recurse -Force -ErrorAction SilentlyContinue }
    New-Item -ItemType Directory -Force -Path `$tmp | Out-Null
    & `$Adb -s `$Serial pull `$remote `$tmp 1>`$null 2>`$null
    Get-ChildItem -LiteralPath `$tmp -Recurse -Filter *.png -File -ErrorAction SilentlyContinue | ForEach-Object {
      Copy-Item -LiteralPath `$_.FullName -Destination (Join-Path `$Dest `$_.Name) -Force
    }
    Remove-Item -LiteralPath `$tmp -Recurse -Force -ErrorAction SilentlyContinue
  }
  Start-Sleep -Seconds 6
}
"@
    Set-Content -LiteralPath $watchPs1 -Value $watchBody -Encoding UTF8
    $script:watchProcess = Start-Process -FilePath "powershell.exe" -WindowStyle Hidden -PassThru -ArgumentList @(
        "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass",
        "-File", $watchPs1,
        "-Adb", $script:adb,
        "-Serial", $script:serial,
        "-Dest", $Dest,
        "-StopFile", $script:watchStop
    )
    Write-Log "frame-watch pid $($script:watchProcess.Id)"
}

function Stop-FrameWatch {
    if ($script:watchStop) {
        Set-Content -LiteralPath $script:watchStop -Value "stop" -Encoding ASCII
    }
    if ($script:watchProcess -and -not $script:watchProcess.HasExited) {
        Start-Sleep -Seconds 2
        if (-not $script:watchProcess.HasExited) {
            try { Stop-Process -Id $script:watchProcess.Id -Force -ErrorAction SilentlyContinue } catch { }
        }
    }
    $script:watchProcess = $null
}

function Grant-Notify {
    Invoke-Adb -Args @("-s", $script:serial, "shell", "pm", "grant", $package, $notifyPermission) | Out-Null
}

function Save-AnimatorScale {
    $r = Invoke-Adb -Args @("-s", $script:serial, "shell", "settings", "get", "global", "animator_duration_scale")
    $script:savedAnimator = (($r.Text | Out-String).Trim())
    Write-Log "animator_duration_scale was '$($script:savedAnimator)'"
}

function Set-AnimatorScale([string]$Value) {
    Invoke-Adb -Args @("-s", $script:serial, "shell", "settings", "put", "global", "animator_duration_scale", $Value) | Out-Null
    $script:animatorChanged = $true
    Write-Log "animator_duration_scale -> $Value"
}

function Restore-AnimatorScale {
    if (-not $script:animatorChanged) { return }
    $prev = $script:savedAnimator
    if ([string]::IsNullOrWhiteSpace($prev) -or $prev -eq "null") {
        Invoke-Adb -Args @("-s", $script:serial, "shell", "settings", "put", "global", "animator_duration_scale", "1") | Out-Null
        Write-Log "animator_duration_scale restored to 1"
    } else {
        Invoke-Adb -Args @("-s", $script:serial, "shell", "settings", "put", "global", "animator_duration_scale", $prev) | Out-Null
        Write-Log "animator_duration_scale restored to $prev"
    }
    $script:animatorChanged = $false
}

function Write-ParityCopy([string]$FramesDir, [string[]]$KnownTests) {
    $template = Join-Path $scriptsDir "parity-template.md"
    $dest = Join-Path $script:Out "parity-android.md"
    if (-not (Test-Path -LiteralPath $template)) {
        Write-Log "parity-template.md missing"
        return
    }
    $pngs = @()
    if (Test-Path -LiteralPath $FramesDir) {
        $pngs = @(Get-ChildItem -LiteralPath $FramesDir -Filter *.png -File -ErrorAction SilentlyContinue | ForEach-Object { $_.Name })
    }
    function Pick([string[]]$Names) {
        foreach ($n in $Names) {
            if ($pngs -contains $n) { return $n }
        }
        return ""
    }
    function PickTest([string[]]$Needles) {
        foreach ($t in $KnownTests) {
            foreach ($n in $Needles) {
                if ($t -like "*$n*") { return $t }
            }
        }
        return ""
    }
    $rows = @(
        @{ What = 'Пейджер дней и полоска дат вместо компактного переключателя и стрелок'; Frame = (Pick @('schedule-day-dark.png', 'schedule-day-light.png')); Test = (PickTest @('ScheduleSectionTest', 'SmartStartTest')) }
        @{ What = 'Полные имена дней в подписи под полоской и в заголовках Недели'; Frame = (Pick @('shell-week-dark.png', 'week-odd-dark.png', 'week-odd-light.png', 'schedule-day-dark.png')); Test = (PickTest @('WeekComposerTest', 'ShellNavigationTest')) }
        @{ What = 'Редактор домашки (текст, срок N-я пара) -> HomeworkEditorSheet'; Frame = (Pick @('homework-editor-dark.png', 'homework-list-dark.png')); Test = (PickTest @('HomeworkEditorStateTest', 'HomeworkSectionTest')) }
        @{ What = 'Далекие домашки видимы -> группа Дальше'; Frame = (Pick @('homework-list-dark.png', 'homework-list-light.png')); Test = (PickTest @('HomeworkGroupsTest', 'HomeworkSectionTest')) }
        @{ What = 'Фильтр четности у преподавателя -> сегмент Обе / Нечет / Чет'; Frame = (Pick @('teacher-details-dark.png', 'shell-teachers-dark.png')); Test = (PickTest @('TeacherDetailsComposerTest', 'MapTeacherTest')) }
        @{ What = 'Весь справочник преподавателей -> Только мои выключается'; Frame = (Pick @('teachers-list-dark.png', 'shell-teachers-dark.png')); Test = (PickTest @('MapTeacherTest', 'TeacherDetailsComposerTest')) }
        @{ What = 'Корпус -> этаж на карте -> сегмент корпуса и чипы этажей'; Frame = (Pick @('maps-plan-dark.png', 'maps-highlight-light.png', 'shell-maps-dark.png')); Test = (PickTest @('MapsComposerTest', 'MapTeacherTest')) }
        @{ What = 'Диагностика проверки обновлений -> строка статуса и лог в карточке Обновления'; Frame = (Pick @('settings-dark.png', 'settings-light.png')); Test = (PickTest @('UpdateViewModelTest', 'SettingsSectionTest')) }
        @{ What = 'Тест уведомления -> кнопка'; Frame = (Pick @('settings-dark.png')); Test = (PickTest @('SettingsLogicTest', 'SettingsSectionTest')) }
        @{ What = 'Дружелюбный текст на 403 / лимите GitHub'; Frame = (Pick @('settings-dark.png')); Test = (PickTest @('UpdateViewModelTest')) }
        @{ What = 'Авто-перепроверка при открытии настроек -> раз за сессию'; Frame = (Pick @('settings-dark.png')); Test = (PickTest @('UpdateViewModelTest', 'SettingsLogicTest')) }
        @{ What = 'Светлая тема'; Frame = (Pick @('settings-light.png', 'schedule-day-light.png', 'theme-native-light.png', 'homework-list-light.png')); Test = (PickTest @('ThemeNativeCaptureTest', 'TokensParityTest')) }
        @{ What = 'Раздел Домашка'; Frame = (Pick @('homework-list-dark.png', 'shell-homework-dark.png', 'homework-empty-dark.png')); Test = (PickTest @('HomeworkSectionTest', 'ShellNavigationTest')) }
        @{ What = 'Раздел Карты с подсветкой и контекстом'; Frame = (Pick @('maps-highlight-light.png', 'maps-highlight-dark.png', 'shell-maps-dark.png')); Test = (PickTest @('MapsComposerTest', 'HighlightGeometryTest', 'MapTeacherTest')) }
        @{ What = 'Каскад / переходы / reduce motion'; Frame = (Pick @('motion-appear-mid-dark.png', 'motion-off-dark.png')); Test = (PickTest @('MotionTest', 'MotionOffTest', 'MotionGuardTest')) }
        @{ What = 'Пустой день с подсказкой о следующей паре'; Frame = (Pick @('schedule-empty-light.png', 'schedule-empty-dark.png')); Test = (PickTest @('ScheduleSectionTest', 'ScheduleComposerTest')) }
        @{ What = 'Чип устаревания расписания'; Frame = (Pick @('settings-dark.png', 'shell-schedule-dark.png')); Test = (PickTest @('SettingsLogicTest', 'SettingsSectionTest')) }
        @{ What = 'О приложении'; Frame = (Pick @('settings-about-dark.png', 'settings-dark.png')); Test = (PickTest @('SettingsSectionTest')) }
    )
    $open = 0
    $md = New-Object System.Collections.Generic.List[string]
    [void]$md.Add("# Паритет Android 2.0 (§Д11)")
    [void]$md.Add("")
    [void]$md.Add("Скопировано из ``android/scripts/parity-template.md`` и заполнено по кадрам/тестам прогона.")
    [void]$md.Add("")
    [void]$md.Add("| Что должно остаться | Кадр | Тест |")
    [void]$md.Add("|---|---|---|")
    foreach ($row in $rows) {
        $frame = $row.Frame
        $test = $row.Test
        if ([string]::IsNullOrWhiteSpace($frame) -and [string]::IsNullOrWhiteSpace($test)) { $open += 1 }
        [void]$md.Add("| $($row.What) | $frame | $test |")
    }
    [void]$md.Add("")
    [void]$md.Add("Открытых строк: $open")
    Set-Content -LiteralPath $dest -Value $md -Encoding UTF8
}

try {
    New-Item -ItemType Directory -Force -Path $Out | Out-Null
    $script:Out = [IO.Path]::GetFullPath($Out)
    $script:report = Join-Path $script:Out "report.md"
    $framesDir = Join-Path $script:Out "frames"
    New-Item -ItemType Directory -Force -Path $framesDir | Out-Null
    Set-Content -LiteralPath $script:report -Value ("# Android verify - " + (Get-Date -Format "s")) -Encoding UTF8
    Write-Log "out=$($script:Out)"

    if (-not (Test-Path -LiteralPath $gradlew)) { throw "gradlew.bat not found: $gradlew" }

    $sdkDir = Resolve-SdkDir
    $jdkHome = Resolve-JdkHome
    $script:adb = Join-Path $sdkDir "platform-tools\adb.exe"
    $emulatorExe = Join-Path $sdkDir "emulator\emulator.exe"
    if (-not (Test-Path -LiteralPath $script:adb)) { throw "adb not found: $($script:adb)" }

    foreach ($key in @("JAVA_HOME", "ANDROID_HOME", "ANDROID_SDK_ROOT", "ANDROID_SERIAL", "PATH")) {
        $savedEnv[$key] = [Environment]::GetEnvironmentVariable($key, "Process")
    }
    $env:JAVA_HOME = $jdkHome
    $env:ANDROID_HOME = $sdkDir
    $env:ANDROID_SDK_ROOT = $sdkDir
    $env:PATH = "$jdkHome\bin;$sdkDir\platform-tools;$sdkDir\emulator;" + $env:PATH

    $javaExe = Join-Path $jdkHome "bin\java.exe"
    $javaLog = Join-Path $script:Out "java.version"
    cmd.exe /c "`"$javaExe`" -version > `"$javaLog`" 2>&1" | Out-Null
    $javaVer = ""
    if (Test-Path -LiteralPath $javaLog) { $javaVer = Get-Content -LiteralPath $javaLog -Raw }
    Write-Log ("java " + (($javaVer -replace "`r", "").Trim()))
    Add-Report ""
    Add-Report "## Окружение"
    Add-Report ""
    Add-Report "- Flavor: ``$flavorNorm``"
    Add-Report "- SDK: ``$sdkDir``"
    Add-Report "- JDK: ``$jdkHome``"
    Add-Report "- java: ``$((($javaVer -split "`n")[0]).Trim())``"
    Add-Report "- android: ``$androidRoot``"

    $before = @(Get-EmulatorSerials)
    if ($before.Count -gt 0) {
        if ($before -contains "emulator-5554") { $script:serial = "emulator-5554" }
        else { $script:serial = $before[0] }
        $msg = "Уже запущен эмулятор: $($before -join ', ') - использую $($script:serial), не останавливаю."
        Write-Log $msg
        Add-Report "- $msg"
        $script:started = $null
        Invoke-Adb -Args @("-s", $script:serial, "wait-for-device") | Out-Null
        if (-not (Wait-BootCompleted -Serial $script:serial -Tries 30)) {
            throw "Эмулятор $script:serial не ответил boot_completed"
        }
    } else {
        if (-not (Test-Path -LiteralPath $emulatorExe)) { throw "emulator.exe not found: $emulatorExe" }
        $oldEap = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        try { $avds = & $emulatorExe -list-avds 2>&1 } finally { $ErrorActionPreference = $oldEap }
        Write-Log ("avds: " + (($avds | Out-String).Trim() -replace "`r", " "))
        $avdOk = @($avds | Where-Object { "$_".Trim() -eq $Avd }).Count -gt 0
        if (-not $avdOk) {
            throw "AVD '$Avd' нет. Доступны: $(($avds | Out-String).Trim())"
        }
        Write-Log "starting emulator -avd $Avd"
        $script:started = Start-Process -FilePath $emulatorExe -PassThru -ArgumentList @(
            "-avd", $Avd, "-no-boot-anim", "-no-audio", "-no-snapshot"
        )
        Invoke-Adb -Args @("wait-for-device") -ThrowOnError | Out-Null
        $bootOk = Wait-BootCompleted -Serial "emulator-5554" -Tries 90
        $online = @(Get-EmulatorSerials)
        if ($online.Count -gt 0) { $script:serial = $online[0] }
        if (-not $bootOk -or [string]::IsNullOrWhiteSpace($script:serial)) {
            if ($script:started) {
                try { Stop-Process -Id $script:started.Id -Force -ErrorAction SilentlyContinue } catch { }
            }
            throw "Эмулятор не загрузился за 180 с"
        }
        Add-Report "- Запущен эмулятор ``$($script:serial)`` (pid $($script:started.Id), avd $Avd)"
        Write-Log "started $($script:serial) pid $($script:started.Id)"
    }

    $env:ANDROID_SERIAL = $script:serial
    $bootSdk = ((Invoke-Adb -Args @("-s", $script:serial, "shell", "getprop", "ro.build.version.sdk")).Text | Out-String).Trim()
    $bootModel = ((Invoke-Adb -Args @("-s", $script:serial, "shell", "getprop", "ro.product.model")).Text | Out-String).Trim()
    Add-Report "- serial: ``$($script:serial)``  model: ``$bootModel``  sdk: ``$bootSdk``"
    Add-Report "- owned: $(if ($script:started) { 'yes pid ' + $script:started.Id } else { 'no (foreign, not killed)' })"

    Save-AnimatorScale
    Invoke-Adb -Args @("-s", $script:serial, "logcat", "-c") | Out-Null

    $gate = New-Object System.Threading.Mutex($false, "Local\ZaparaAndroidBuildGate")
    Write-Log "waiting Android build mutex"
    $locked = $gate.WaitOne(600000)
    if (-not $locked) {
        throw "Не удалось взять mutex Local\ZaparaAndroidBuildGate за 10 мин"
    }
    Write-Log "mutex acquired"

    $jvmTask = ":app:test${capFlavor}DebugUnitTest"
    $connectedTask = ":app:connected${capFlavor}DebugAndroidTest"
    $jvmMark = (Get-Date).AddSeconds(-2)
    $jvmCode = Invoke-Gradle -Tasks @($jvmTask) -LogFile (Join-Path $script:Out "jvm.log")
    $jvmXmlSrc = Join-Path $androidRoot "app\build\test-results\test${capFlavor}DebugUnitTest"
    $jvmXmlDest = Join-Path $script:Out "jvm-results"
    Copy-Tree $jvmXmlSrc $jvmXmlDest
    Copy-Tree (Join-Path $androidRoot "app\build\reports\tests\test${capFlavor}DebugUnitTest") (Join-Path $script:Out "jvm-html")
    # UP-TO-DATE tests keep old XML timestamps; only ignore stale XML when Gradle failed.
    if ($jvmCode -eq 0) {
        $jvmRows = @(Get-JUnitClassRows -Dir $jvmXmlDest)
    } else {
        $jvmRows = @(Get-JUnitClassRows -Dir $jvmXmlDest -After $jvmMark)
    }
    $jvmOk = ($jvmCode -eq 0)
    $jvmLogText = ""
    if (Test-Path -LiteralPath (Join-Path $script:Out "jvm.log")) {
        $jvmLogText = Get-Content -LiteralPath (Join-Path $script:Out "jvm.log") -Raw -ErrorAction SilentlyContinue
    }
    $jvmTestCount = 0
    if ($jvmRows.Count -gt 0) { $jvmTestCount = ($jvmRows | Measure-Object Tests -Sum).Sum }
    if ($jvmCode -ne 0 -and $jvmTestCount -eq 0) { $buildFailed = $true }
    Write-Log "jvm ok=$jvmOk classes=$($jvmRows.Count) tests=$jvmTestCount buildFailed=$buildFailed"

    if (-not $buildFailed) {
        $installTask = ":app:install${capFlavor}Debug"
        $installCode = Invoke-Gradle -Tasks @($installTask) -LogFile (Join-Path $script:Out "install.log")
        if ($installCode -ne 0) {
            $buildFailed = $true
            Write-Log "install failed"
        }
    }

    if (-not $buildFailed) {
        Grant-Notify
        Invoke-Adb -Args @("-s", $script:serial, "shell", "rm", "-rf", "/sdcard/Android/data/$package/files/frames") | Out-Null

        Start-FrameWatch -Dest $framesDir
        $connectedMark = (Get-Date).AddSeconds(-2)
        $connectedCode = Invoke-Gradle -Tasks @(
            "-Pandroid.experimental.testOptions.uninstallAfterTest=false",
            $connectedTask
        ) -LogFile (Join-Path $script:Out "connected.log")
        $connectedOk = ($connectedCode -eq 0)
        $connectedXmlSrc = Join-Path $androidRoot "app\build\outputs\androidTest-results\connected\debug\flavors\$flavorNorm"
        $connectedXmlDest = Join-Path $script:Out "connected-results"
        Copy-Tree $connectedXmlSrc $connectedXmlDest
        Copy-Tree (Join-Path $androidRoot "app\build\reports\androidTests") (Join-Path $script:Out "connected-html")
        if ($connectedCode -eq 0) {
            $connectedRows = @(Get-JUnitClassRows -Dir $connectedXmlDest)
        } else {
            $connectedRows = @(Get-JUnitClassRows -Dir $connectedXmlDest -After $connectedMark)
        }
        [void](Pull-Pngs -Remotes (Get-FrameRemotes) -Dest $framesDir)
        Grant-Notify
        Write-Log "connected ok=$connectedOk classes=$($connectedRows.Count)"

        Set-AnimatorScale "0"
        try {
            $motionMark = (Get-Date).AddSeconds(-2)
            $motionCode = Invoke-Gradle -Tasks @(
                "-Pandroid.experimental.testOptions.uninstallAfterTest=false",
                "-Pandroid.testInstrumentationRunnerArguments.class=$package.MotionOffTest",
                $connectedTask
            ) -LogFile (Join-Path $script:Out "motion-off.log")
            $motionOffOk = ($motionCode -eq 0)
            $motionXmlDest = Join-Path $script:Out "motion-off-results"
            Copy-Tree $connectedXmlSrc $motionXmlDest
            if ($motionCode -eq 0) {
                $motionOffRows = @(Get-JUnitClassRows -Dir $motionXmlDest)
            } else {
                $motionOffRows = @(Get-JUnitClassRows -Dir $motionXmlDest -After $motionMark)
            }
            [void](Pull-Pngs -Remotes (Get-FrameRemotes) -Dest $framesDir)
            Write-Log "motion-off ok=$motionOffOk classes=$($motionOffRows.Count)"
        } finally {
            Restore-AnimatorScale
        }

        Stop-FrameWatch
        [void](Pull-Pngs -Remotes (Get-FrameRemotes) -Dest $framesDir)
    } else {
        Write-Log "skip connected: build failed"
    }

    $logcatRaw = Join-Path $script:Out "logcat-errors.log"
    $errDump = Invoke-Adb -Args @("-s", $script:serial, "logcat", "-d", "*:E")
    Set-Content -LiteralPath $logcatRaw -Value $errDump.Text -Encoding UTF8
    $packageLogErrors = @($errDump.Text | Where-Object { $_ -match [regex]::Escape($package) })
    Set-Content -LiteralPath (Join-Path $script:Out "logcat-package-errors.log") -Value $packageLogErrors -Encoding UTF8

    $frames = @(Get-ChildItem -LiteralPath $framesDir -Filter *.png -File -ErrorAction SilentlyContinue | Sort-Object Name)
    $jvmFail = 0; $connFail = 0; $offFail = 0
    if ($jvmRows.Count -gt 0) { $jvmFail = ($jvmRows | Measure-Object Failures -Sum).Sum + ($jvmRows | Measure-Object Errors -Sum).Sum }
    if ($connectedRows.Count -gt 0) { $connFail = ($connectedRows | Measure-Object Failures -Sum).Sum + ($connectedRows | Measure-Object Errors -Sum).Sum }
    if ($motionOffRows.Count -gt 0) { $offFail = ($motionOffRows | Measure-Object Failures -Sum).Sum + ($motionOffRows | Measure-Object Errors -Sum).Sum }
    $testsOk = $jvmOk -and $connectedOk -and $motionOffOk -and ($jvmFail + $connFail + $offFail) -eq 0

    $knownTests = New-Object System.Collections.Generic.List[string]
    $allRows = @()
    if ($jvmRows) { $allRows += @($jvmRows) }
    if ($connectedRows) { $allRows += @($connectedRows) }
    if ($motionOffRows) { $allRows += @($motionOffRows) }
    foreach ($row in $allRows) {
        if ($null -eq $row) { continue }
        [void]$knownTests.Add([string]$row.Class)
    }
    Write-ParityCopy -FramesDir $framesDir -KnownTests @($knownTests)

    Add-Report ""
    Add-Report "## Итог"
    Add-Report ""
    if ($buildFailed) { $testsOk = $false }
    $status = if ($buildFailed) { "BUILD FAIL" } elseif ($testsOk) { "PASS" } else { "FAIL" }
    Add-Report ("Тесты: **{0}** (JVM {1}, connected {2}, MotionOff scale=0 {3}); падений JVM {4}, connected {5}, motion-off {6}; кадров {7}; ошибок пакета в logcat {8}." -f `
        $status, $(if ($jvmOk) { "PASS" } else { "FAIL" }), $(if ($connectedOk) { "PASS" } else { "FAIL" }), $(if ($motionOffOk) { "PASS" } else { "FAIL" }), `
        $jvmFail, $connFail, $offFail, $frames.Count, $packageLogErrors.Count)
    if ($buildFailed -and -not [string]::IsNullOrWhiteSpace($jvmLogText)) {
        Add-Report ""
        Add-Report "## Сборка"
        Add-Report ""
        Add-Report "Сборка не собралась (код 2). Фрагмент ``jvm.log``:"
        Add-Report ""
        Add-Report '```'
        $errLines = @($jvmLogText -split "`r?`n" | Where-Object { $_ -match '^(e:|> Task :.*FAILED|FAILURE:|Execution failed)' })
        if ($errLines.Count -eq 0) { $errLines = @($jvmLogText -split "`r?`n" | Select-Object -Last 20) }
        $errLines | Select-Object -First 30 | ForEach-Object { Add-Report $_ }
        Add-Report '```'
    }
    Add-Report ""
    Add-Report "## JVM ``$jvmTask``"
    Add-Report ""
    Format-ClassTable $jvmRows | ForEach-Object { Add-Report $_ }
    Add-Report ""
    Add-Report "## Connected ``$connectedTask``"
    Add-Report ""
    Format-ClassTable $connectedRows | ForEach-Object { Add-Report $_ }
    Add-Report ""
    Add-Report "## MotionOffTest (animator_duration_scale=0)"
    Add-Report ""
    Format-ClassTable $motionOffRows | ForEach-Object { Add-Report $_ }
    Add-Report ""
    Add-Report "## Кадры"
    Add-Report ""
    if ($frames.Count -eq 0) {
        Add-Report "_кадров нет - Gradle мог снять пакет до pull; смотри frame-watch в этом каталоге._"
    } else {
        foreach ($f in $frames) {
            $kb = [Math]::Round($f.Length / 1KB)
            Add-Report "- ``$($f.Name)`` ($kb KB)"
        }
    }
    Add-Report ""
    Add-Report "## logcat ``*:E`` пакета ``$package``"
    Add-Report ""
    Add-Report ("Строк: **{0}**. Полный дамп: ``logcat-errors.log``, фильтр: ``logcat-package-errors.log``." -f $packageLogErrors.Count)
    if ($packageLogErrors.Count -gt 0) {
        Add-Report ""
        Add-Report '```'
        $packageLogErrors | Select-Object -First 40 | ForEach-Object { Add-Report $_ }
        if ($packageLogErrors.Count -gt 40) { Add-Report ("... ещё {0}" -f ($packageLogErrors.Count - 40)) }
        Add-Report '```'
    }
    Add-Report ""
    Add-Report "## Паритет"
    Add-Report ""
    Add-Report "Шаблон: ``android/scripts/parity-template.md``. Заполненная копия: ``parity-android.md``."
    Add-Report ""
    Add-Report "report: ``$($script:report)``"
    Write-Host "report: $($script:report)"

    if ($buildFailed) { $exitCode = 2 }
    elseif ($testsOk) { $exitCode = 0 }
    else { $exitCode = 1 }
    Write-Log "exit $exitCode"
}
catch {
    $msg = $_.Exception.Message
    Write-Log "ERROR $msg"
    if ($script:report) {
        Add-Report ""
        Add-Report "## Ошибка"
        Add-Report ""
        Add-Report "``$msg``"
    }
    $exitCode = 2
}
finally {
    try { Stop-FrameWatch } catch { }
    try { Restore-AnimatorScale } catch { }
    if ($locked -and $gate) {
        try { $gate.ReleaseMutex() } catch { }
    }
    if ($gate) { $gate.Dispose() }
    foreach ($key in $savedEnv.Keys) {
        [Environment]::SetEnvironmentVariable($key, $savedEnv[$key], "Process")
    }
    if ($script:started -and -not $KeepEmulator) {
        Write-Log "stopping owned emulator $($script:serial) pid $($script:started.Id)"
        if ($script:adb -and $script:serial) {
            Invoke-Adb -Args @("-s", $script:serial, "emu", "kill") | Out-Null
        }
        Start-Sleep -Seconds 3
        if ($script:started -and -not $script:started.HasExited) {
            try { Stop-Process -Id $script:started.Id -Force -ErrorAction SilentlyContinue } catch { }
        }
    } elseif ($script:serial) {
        Write-Log "leaving emulator $($script:serial) running"
    }
}

[Environment]::Exit($exitCode)
