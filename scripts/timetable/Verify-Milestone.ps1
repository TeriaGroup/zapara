<#
.SYNOPSIS
Runs local fixture-only timetable milestone QA after all application writers join.
.DESCRIPTION
PowerShell 5.1. Caller configures ZAPARA_TEST_POSTGRES (Npgsql format), authorized
PGSERVICE/PGPASSFILE for the same PostgreSQL 16 test DB, psql/curl.exe/dotnet on PATH.
Only explicit Host=127.0.0.1 or ::1, Port=56432, Database=zapara_test are accepted.
DOTNET8 may name a private dotnet.exe with ASP.NET 8; otherwise PATH dotnet must
have it. Caller bootstraps DOTNET_ROOT_X64 for testhost. No installs or Docker work.
Retains synthetic HTTP/SQL/TRX evidence under TEMP; raw application/native output
is suppressed (may contain secrets). Native exit receipts remain. Never --fetch.
Existing lockfiles govern restore determinism; Desktop without a lock is restored
normally, without dependency upgrades. Does not assert reproducibility of unlocked
Desktop transitive versions. Own schema/process only; never drops a database.
.EXAMPLE
& .\scripts\timetable\Verify-Milestone.ps1
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
# These guards precede filesystem writes, processes, SDK calls and DB connections.
if ([string]::IsNullOrWhiteSpace($env:ZAPARA_TEST_POSTGRES) -or
    [string]::IsNullOrWhiteSpace($env:PGSERVICE)) {
    [Console]::Error.WriteLine('Required local DB environment/service missing. No resources created.')
    exit 1
}
try {
    Add-Type -AssemblyName System.Data
    $connection = New-Object System.Data.Common.DbConnectionStringBuilder
    # Explicit accessors bypass PowerShell's IDictionary adapter (which otherwise
    # treats ConnectionString as a dictionary entry rather than the CLR property).
    $connection.set_ConnectionString($env:ZAPARA_TEST_POSTGRES)
    # Fail closed on aliases/unknown routing options rather than approximate Npgsql.
    $allowed = @('host','port','database','username','user id','password','pooling',
        'timeout','command timeout','ssl mode','include error detail','log parameters',
        'persist security info','maximum pool size','minimum pool size')
    foreach ($key in $connection.Keys) {
        if ($allowed -notcontains $key.ToLowerInvariant()) { throw 'Unsupported key' }
    }
    if (@('127.0.0.1','::1') -notcontains [string]$connection.get_Item('Host') -or
        [string]$connection.get_Item('Port') -ne '56432' -or
        [string]$connection.get_Item('Database') -cne 'zapara_test') { throw 'Unsafe target' }
} catch {
    [Console]::Error.WriteLine('Local test DB target cannot be verified. No resources created.')
    exit 1
}
$connection.Clear()
$out = $null; $owned = $false; $gateHeld = $false; $gate = $null
$script:apiProcess = $null; $script:startNumber = 0; $script:stage = 'prerequisites'
$saved = @{}
function Check([bool]$ok, [string]$message) { if (-not $ok) { throw $message } }
function Native([string]$name, [string]$exe, [string[]]$argv, [int]$expected = 0,
                [switch]$KeepOutput) {
    $script:stage = $name
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $text = & $exe @argv 2>&1; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    Set-Content -Encoding UTF8 (Join-Path $out "$name.exit") $code
    # Only controlled psql SELECT output or curl status is retained by callers.
    if ($KeepOutput -and $code -eq $expected) {
        Set-Content -Encoding UTF8 (Join-Path $out "$name.log") ($text | Out-String)
    } else { Set-Content -Encoding UTF8 (Join-Path $out "$name.log") 'Raw output suppressed; see exit receipt/TRX.' }
    Check ($code -eq $expected) 'Native exit mismatch'
}
function Sql([string]$name, [string]$query) {
    Native $name $psql @('-X','-w','-q','-t','-A','-v','ON_ERROR_STOP=1','-c',$query) -KeepOutput
    return (Get-Content -Encoding UTF8 (Join-Path $out "$name.log") -Raw).Trim()
}
function Free-Port {
    $listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 5187)
    try { $listener.Start() } finally { $listener.Stop() }
}
function Stop-Api {
    if ($null -eq $script:apiProcess) { return }
    $p = $script:apiProcess
    if (-not $p.HasExited) { $p.Kill() }
    Check ($p.WaitForExit(10000)) 'Owned API failed to stop'
    Add-Content -Encoding UTF8 (Join-Path $out 'pids.txt') "stopped=$($p.Id) exit=$($p.ExitCode)"
    $p.Dispose(); $script:apiProcess = $null
}
function Start-Api {
    Free-Port
    $script:startNumber++; $tag = "host-$script:startNumber"
    $info = New-Object Diagnostics.ProcessStartInfo
    $info.FileName = $runtime
    $info.Arguments = '"' + (Join-Path $out 'server\Zapara.Server.dll') + '" --urls http://127.0.0.1:5187'
    $info.WorkingDirectory = $root; $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true; $info.RedirectStandardError = $true
    $script:apiProcess = New-Object Diagnostics.Process
    $script:apiProcess.StartInfo = $info
    Check ($script:apiProcess.Start()) 'Cannot launch owned host'
    Add-Content -Encoding UTF8 (Join-Path $out 'pids.txt') "started=$($script:apiProcess.Id) tag=$tag"
    # Drain both streams asynchronously; never persist raw app exceptions/config.
    $script:hostOut = $script:apiProcess.StandardOutput.ReadToEndAsync()
    $script:hostErr = $script:apiProcess.StandardError.ReadToEndAsync()
    for ($i = 0; $i -lt 30; $i++) {
        Check (-not $script:apiProcess.HasExited) 'Owned host exited'
        $status = & $curl -s --noproxy '*' --max-time 1 -o NUL -w '%{http_code}' 'http://127.0.0.1:5187/health/live'
        $code = $LASTEXITCODE
        Add-Content -Encoding UTF8 (Join-Path $out "$tag.poll") "attempt=$i exit=$code status=$status"
        if ($code -eq 0 -and $status -eq '200') {
            Check (-not $script:apiProcess.HasExited) 'Owned host exited'; return
        }
        Start-Sleep -Milliseconds 200
    }
    throw 'Bounded startup failed'
}
function Http([string]$name, [string]$path, [int]$status, [string]$method = 'GET') {
    Check ($null -ne $script:apiProcess -and -not $script:apiProcess.HasExited) 'Owned API not running'
    Native $name $curl @('-sS','--noproxy','*','--max-time','5','-X',$method,
        '-D',(Join-Path $out "$name.headers"),'-o',(Join-Path $out "$name.body"),
        '-w','%{http_code}',"http://127.0.0.1:5187$path") -KeepOutput
    Check ((Get-Content -Encoding UTF8 (Join-Path $out "$name.log") -Raw).Trim() -eq [string]$status) 'HTTP status mismatch'
    if ($method -eq 'POST' -and $status -eq 404) { return }
    $body = Get-Content -Encoding UTF8 (Join-Path $out "$name.body") -Raw
    Check (-not [string]::IsNullOrWhiteSpace($body)) 'Empty JSON response'
    return ($body | ConvertFrom-Json)
}
function Inspect([string]$name, [int]$snapshots, [int]$attempts, [int]$groups,
                 [string]$current = '', [string]$statuses = '') {
    Native $name $psql @('-X','-w','-v','ON_ERROR_STOP=1','-v',"schema=$schema",'-f',(Join-Path $PSScriptRoot 'Inspect.sql')) -KeepOutput
    $actual = Sql "$name-assert" @"
SELECT (SELECT count(*) FROM $schema.snapshots),
 (SELECT count(*) FROM $schema.refresh_attempts),
 (SELECT count(*) FROM $schema.state WHERE singleton),
 COALESCE((SELECT current_snapshot_id::text FROM $schema.state WHERE singleton), ''),
 COALESCE((SELECT jsonb_array_length(payload->'groups') FROM $schema.snapshots
 WHERE snapshot_id=(SELECT current_snapshot_id FROM $schema.state WHERE singleton)),0),
 COALESCE((SELECT string_agg(status || ':' || COALESCE(error_code,''), ',' ORDER BY sequence)
 FROM $schema.refresh_attempts),'');
"@
    Check ($actual -ceq "$snapshots|$attempts|1|$current|$groups|$statuses") 'SQL state assertion failed'
}
function PayloadHash([string]$name, [string]$id) {
    return (Sql $name "SELECT md5(payload::text) || '|' || source_sha256 || '|' || md5(original_xml) FROM $schema.snapshots WHERE snapshot_id='$id';")
}
$exitCode = 0
try {
    $sdk = (Get-Command dotnet -CommandType Application).Source
    $runtime = $sdk
    if ($env:DOTNET8) { $runtime = (Get-Command $env:DOTNET8 -CommandType Application).Source }
    $psql = (Get-Command psql -CommandType Application).Source
    $curl = (Get-Command curl.exe -CommandType Application).Source
    $root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
    $gate = New-Object Threading.Mutex($false, 'Local\ZaparaServerBuildGate')
    try { $gateHeld = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $gateHeld = $true }
    Check $gateHeld 'Build gate busy; run after parent join'
    $runtimes = & $runtime --list-runtimes 2>$null
    Check ($LASTEXITCODE -eq 0 -and ($runtimes -match '^Microsoft.AspNetCore.App 8\.')) 'ASP.NET 8 runtime required'
    Free-Port
    $out = Join-Path $env:TEMP ('zapara-tt-m1-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $out | Out-Null
    $schema = 'tt_m1_' + [guid]::NewGuid().ToString('N')
    Check ($schema -cmatch '\Att_m1_[0-9a-f]{32}\z') 'Unsafe schema'
    foreach ($key in @('ConnectionStrings__Timetable','Timetable__Schema','ASPNETCORE_URLS','PGCONNECT_TIMEOUT')) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    }
    $env:ConnectionStrings__Timetable = $env:ZAPARA_TEST_POSTGRES
    $env:Timetable__Schema = $schema; $env:ASPNETCORE_URLS = 'http://127.0.0.1:5187'
    $env:PGCONNECT_TIMEOUT = '5'
    # psql client variables verify the service's actual endpoint. inet_server_addr
    # is NOT loopback inside the authorized Docker port mapping.
    Native 'service-endpoint' $psql @('-X','-w','-v','ON_ERROR_STOP=1','-c','\echo :HOST :PORT :DBNAME') -KeepOutput
    $endpoint = (Get-Content -Encoding UTF8 (Join-Path $out 'service-endpoint.log') -Raw).Trim()
    Check ($endpoint -cmatch '\A(127\.0\.0\.1|::1) 56432 zapara_test\z') 'Service endpoint not approved'
    Check ((Sql 'service-target' "SELECT current_database()='zapara_test' AND current_setting('server_version_num')::int BETWEEN 160000 AND 169999;") -eq 't') 'Service DB/version not approved'
    $beforeTests = Sql 'test-schemas-before' "SELECT nspname FROM pg_namespace WHERE nspname LIKE 'tt_test_%' ORDER BY nspname;"
    Set-Content -Encoding UTF8 (Join-Path $out 'schema-requested.txt') $schema
    Native 'schema-create' $psql @('-X','-w','-v','ON_ERROR_STOP=1','-c',"CREATE SCHEMA $schema;")
    $owned = $true
    Set-Content -Encoding UTF8 (Join-Path $out 'schema-owned.txt') $schema
    foreach ($project in @('Zapara.Server','Zapara.Ingest','Zapara.Server.Tests','Vograph.Timetable.Tests','Vograph.Desktop.Tests','Vograph.Desktop')) {
        $csproj = Join-Path $root "src\$project\$project.csproj"
        $argsRestore = @('restore',$csproj)
        if (Test-Path (Join-Path (Split-Path $csproj) 'packages.lock.json')) { $argsRestore += '--locked-mode' }
        else { Check ($project -like 'Vograph.Desktop*') 'Required lockfile missing' }
        Native "$project-restore" $sdk $argsRestore
    }
    foreach ($pair in @(@('Zapara.Server','server'),@('Zapara.Ingest','ingest'))) {
        Native "publish-$($pair[1])" $sdk @('publish',(Join-Path $root "src\$($pair[0])\$($pair[0]).csproj"),'-c','Debug','--no-restore','-o',(Join-Path $out $pair[1]))
        $deps = Get-Content -Encoding UTF8 (Join-Path $out "$($pair[1])\$($pair[0]).deps.json") -Raw | ConvertFrom-Json
        $forbidden = @($deps.libraries.PSObject.Properties.Name | Where-Object { $_ -match '(?i)Vograph\.Core|sqlite' })
        Check ($forbidden.Count -eq 0) 'Server/CLI dependency graph contains Core or SQLite'
    }
    Native 'desktop-build' $sdk @('build',(Join-Path $root 'src\Vograph.Desktop\Vograph.Desktop.csproj'),'--no-restore')
    foreach ($project in @('Zapara.Server.Tests','Vograph.Timetable.Tests','Vograph.Desktop.Tests')) {
        Native "$project-tests" $sdk @('test',(Join-Path $root "src\$project\$project.csproj"),'--no-restore','--results-directory',$out,'--logger',"trx;LogFileName=$project.trx")
        [xml]$trx = Get-Content -Encoding UTF8 (Join-Path $out "$project.trx") -Raw
        $c = $trx.TestRun.ResultSummary.Counters
        Check ([int]$c.total -gt 0 -and [int]$c.executed -eq [int]$c.total -and [int]$c.passed -eq [int]$c.total -and [int]$c.failed -eq 0 -and [int]$c.notExecuted -eq 0) 'Suite failed/skipped/empty'
    }
    Check ((Sql 'test-schemas-after' "SELECT nspname FROM pg_namespace WHERE nspname LIKE 'tt_test_%' ORDER BY nspname;") -ceq $beforeTests) 'Test schema leak; do not delete unowned schemas'
    $cli = Join-Path $out 'ingest\Zapara.Ingest.dll'
    $fixtures = Join-Path $root 'src\Zapara.Server.Tests\Fixtures'
    Native 'init' $runtime @($cli,'db-init')
    # Only psql created this random schema; successful CLI init + SQL tables proves counterpart DB.
    Inspect 'sql-empty' 0 0 0
    Start-Api
    $null = Http 'live' '/health/live' 200
    $n = 0
    foreach ($path in @('/health/ready','/api/v1/status','/api/v1/groups','/api/v1/groups/3313/timetable')) {
        $n++; $e = Http "empty-$n" $path 503
        Check ($e.code -eq 'snapshot_unavailable') 'Wrong empty code'
    }
    Native 'ingest-a' $runtime @($cli,'ingest','--file',(Join-Path $fixtures 'valid-a.xml'))
    $a = Http 'groups-a' '/api/v1/groups' 200
    $aid = [guid]::Parse($a.meta.snapshotId).ToString()
    Check (@($a.groups).Count -eq 2 -and (($a.groups.id | Sort-Object) -join ',') -eq '3313,9999' -and -not $a.meta.stale) 'A groups/freshness mismatch'
    Check ($a.period.start -eq '2026-09-01' -and $a.period.timeZone -eq 'Europe/Moscow' -and $a.meta.sourceKind -eq 'file' -and $null -eq $a.meta.sourceModifiedAt -and $null -eq $a.meta.sourceUrl) 'Period/provenance mismatch'
    $lesson = Http 'lesson-a' '/api/v1/groups/3313/timetable' 200
    # Unicode via XML avoids PowerShell 5.1 BOM-dependent source decoding.
    [xml]$fixtureA = Get-Content -Encoding UTF8 (Join-Path $fixtures 'valid-a.xml') -Raw
    $subjectA = [string]$fixtureA.Timetable.Group[0].Days.Day.GroupLessons.Lesson.Discipline
    Check (@($lesson.lessons).Count -eq 1) 'Lesson count mismatch'
    $l = $lesson.lessons[0]
    Check ($l.subjectRaw -ceq $subjectA -and $l.subjectNormalized -ceq $subjectA.ToLowerInvariant() -and $l.timeStart -eq '09:00' -and $l.timeEnd -eq '10:35' -and $l.classroomRaw -eq '493;' -and $l.roomRaw -eq '493' -and $l.dayOfWeek -eq 1) 'Lesson wire mismatch'
    Check ($a.meta.sourceSha256 -ceq (Get-FileHash (Join-Path $fixtures 'valid-a.xml') -Algorithm SHA256).Hash.ToLowerInvariant()) 'Source byte hash mismatch'
    $empty = Http 'empty-group-a' '/api/v1/groups/9999/timetable' 200
    Check (@($empty.lessons).Count -eq 0 -and $empty.group.lessonCount -eq 0) 'Known empty group mismatch'
    $pin = Http 'pin-current-a' "/api/v1/groups?snapshotId=$aid" 200
    Check ($pin.meta.snapshotId -eq $aid -and -not $pin.meta.stale) 'Current pin stale'
    $null = Http 'ready-a' '/health/ready' 200
    Inspect 'sql-a' 1 1 2 $aid 'success:'
    $hashA = PayloadHash 'payload-a' $aid
    Native 'ingest-invalid' $runtime @($cli,'ingest','--file',(Join-Path $fixtures 'invalid.xml')) 2
    $bad = Http 'groups-invalid' '/api/v1/groups' 200
    Check ($bad.meta.snapshotId -eq $aid -and $bad.meta.stale -and @($bad.groups).Count -eq 2 -and $bad.refresh.lastFailureCode -eq 'snapshot_malformed' -and $bad.refresh.lastAttemptStatus -eq 'failed') 'Last-good/failure mismatch'
    Check ((PayloadHash 'payload-invalid' $aid) -ceq $hashA) 'Last-good payload/bytes changed'
    $null = Http 'ready-stale' '/health/ready' 200
    Inspect 'sql-invalid' 1 2 2 $aid 'success:,failed:snapshot_malformed'
    Native 'ingest-b' $runtime @($cli,'ingest','--file',(Join-Path $fixtures 'valid-b.xml'))
    $b = Http 'groups-b' '/api/v1/groups' 200
    $bid = [guid]::Parse($b.meta.snapshotId).ToString()
    Check ($bid -ne $aid -and @($b.groups).Count -eq 1 -and $b.groups[0].id -eq '3313' -and -not $b.meta.stale) 'B publication mismatch'
    $bl = Http 'lesson-b' '/api/v1/groups/3313/timetable' 200
    [xml]$fixtureB = Get-Content -Encoding UTF8 (Join-Path $fixtures 'valid-b.xml') -Raw
    Check (@($bl.lessons).Count -eq 1 -and $bl.lessons[0].subjectRaw -ceq [string]$fixtureB.Timetable.Group.Days.Day.GroupLessons.Lesson.Discipline) 'B subject mismatch'
    $missing = Http 'missing-b' '/api/v1/groups/9999/timetable' 404
    Check ($missing.code -eq 'group_not_found') 'Wrong removed-group code'
    $history = Http 'history-a' "/api/v1/groups/9999/timetable?snapshotId=$aid" 200
    Check ($history.meta.snapshotId -eq $aid -and $history.meta.stale -and @($history.lessons).Count -eq 0) 'Historical A missing'
    Inspect 'sql-b' 2 3 1 $bid 'success:,failed:snapshot_malformed,success:'
    Stop-Api; Start-Api
    $restart = Http 'restart' '/api/v1/status' 200
    Check ($restart.meta.snapshotId -eq $bid -and -not $restart.meta.stale) 'Restart lost B'
    $null = Http 'no-ingest' '/api/v1/ingest' 404 'POST'
    $null = Http 'no-refresh' '/api/v1/refresh' 404 'POST'
    Inspect 'sql-restart' 2 3 1 $bid 'success:,failed:snapshot_malformed,success:'
    Set-Content -Encoding UTF8 (Join-Path $out 'scenarios-passed.txt') 'Local fixture surface passed; live upstream not tested.'
} catch {
    $exitCode = 1
    [Console]::Error.WriteLine("Verification failed at stage: $script:stage. Raw exception suppressed.")
} finally {
    try { Stop-Api } catch { $exitCode = 1; [Console]::Error.WriteLine('Owned host cleanup failed; see PID receipt.') }
    try {
        if ($owned) {
            Check ($schema -cmatch '\Att_m1_[0-9a-f]{32}\z' -and (Get-Content -Encoding UTF8 (Join-Path $out 'schema-owned.txt') -Raw).Trim() -ceq $schema) 'Ownership receipt mismatch'
            Native 'schema-drop' $psql @('-X','-w','-v','ON_ERROR_STOP=1','-v',"schema=$schema",'-f',(Join-Path $PSScriptRoot 'Teardown.sql'))
            Check ((Sql 'schema-absent' "SELECT count(*) FROM pg_namespace WHERE nspname='$schema';") -eq '0') 'Schema remains'
            Set-Content -Encoding UTF8 (Join-Path $out 'teardown-receipt.txt') "dropped own schema=$schema; database/container untouched"
        }
    } catch { $exitCode = 1; [Console]::Error.WriteLine('Owned schema cleanup failed; see ownership receipt.') }
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
    if ($gateHeld) { $gate.ReleaseMutex() }
    if ($null -ne $gate) { $gate.Dispose() }
    if ($null -ne $out) { Write-Output "Evidence retained: $out" }
}
exit $exitCode
