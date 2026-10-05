<#
.SYNOPSIS
Local synthetic PostgreSQL backup/restore rehearsal against zapara_test only.
.DESCRIPTION
PowerShell 5.1. Dot-sources the private platform environment helper when present.
Accepts only Host=127.0.0.1, Port=56432, Database=zapara_test. Creates an owned
ops_rehearse_* schema, dumps it with pg_dump, drops the table, restores, asserts
the row, then drops the schema. Never prints connection strings or secrets.
Does not start/stop PostgreSQL or run docker compose. Exit 0 success, 2 unsafe
target, 5 restore/rehearsal failed.
.EXAMPLE
& .\scripts\ops\Backup-Restore-Rehearsal.ps1
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$helper = 'C:\Users\Dufa\AppData\Local\Temp\opencode\zapara-platform-b4cd526d-7c33-4b01-b477-3ee53b8c0450\environment.ps1'
if (Test-Path -LiteralPath $helper) {
    . $helper
}

function Test-SafeTarget {
    if ([string]::IsNullOrWhiteSpace($env:ZAPARA_TEST_POSTGRES)) { return $false }
    try {
        Add-Type -AssemblyName System.Data
        $connection = New-Object System.Data.Common.DbConnectionStringBuilder
        $connection.set_ConnectionString($env:ZAPARA_TEST_POSTGRES)
        $allowed = @('host','port','database','username','user id','password','pooling',
            'timeout','command timeout','ssl mode','include error detail','log parameters',
            'persist security info','maximum pool size','minimum pool size')
        foreach ($key in $connection.Keys) {
            if ($allowed -notcontains $key.ToLowerInvariant()) { return $false }
        }
        $hostName = [string]$connection.get_Item('Host')
        $port = [string]$connection.get_Item('Port')
        $database = [string]$connection.get_Item('Database')
        $connection.Clear()
        if ($hostName -cne '127.0.0.1' -or $port -cne '56432' -or $database -cne 'zapara_test') {
            return $false
        }
        return $true
    } catch {
        return $false
    }
}

if (-not (Test-SafeTarget)) {
    [Console]::Error.WriteLine('Unsafe or missing local test DB target. No resources created.')
    exit 2
}

function Invoke-Native([string]$exe, [string[]]$argv) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $null = & $exe @argv 2>&1
        return [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
}

function Invoke-Sql([string]$query) {
    $old = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $text = & $script:psql @('-X','-w','-q','-t','-A','-h','127.0.0.1','-p','56432','-d','zapara_test','-v','ON_ERROR_STOP=1','-c',$query) 2>&1
        $code = [int]$LASTEXITCODE
    } finally {
        $ErrorActionPreference = $old
    }
    if ($code -ne 0) { throw 'SQL failed' }
    return (($text | Out-String).Trim())
}

$schema = $null
$outDir = $null
$exitCode = 5
try {
    $script:psql = (Get-Command psql.exe -ErrorAction Stop).Source
    $script:pgDump = (Get-Command pg_dump.exe -ErrorAction Stop).Source

    $schema = 'ops_rehearse_' + [Guid]::NewGuid().ToString('N')
    if ($schema -cnotmatch '\Aops_rehearse_[0-9a-f]{32}\z') { throw 'Schema name guard failed' }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss'
    $parent = Join-Path ([IO.Path]::GetTempPath()) 'opencode'
    if (-not (Test-Path -LiteralPath $parent)) {
        [void][IO.Directory]::CreateDirectory($parent)
    }
    $outDir = Join-Path $parent ("zapara-ops-$stamp")
    [void][IO.Directory]::CreateDirectory($outDir)
    $backupPath = Join-Path $outDir 'backup.sql'

    $null = Invoke-Sql ("CREATE SCHEMA $schema")
    $null = Invoke-Sql ("CREATE TABLE $schema.dummy (id int PRIMARY KEY, note text NOT NULL)")
    $null = Invoke-Sql ("INSERT INTO $schema.dummy (id, note) VALUES (1, 'synthetic-rehearsal')")

    $dumpCode = Invoke-Native $script:pgDump @(
        '-h','127.0.0.1','-p','56432','-d','zapara_test','-w',
        '-n',$schema,'-f',$backupPath
    )
    if ($dumpCode -ne 0) { throw 'pg_dump failed' }
    if (-not (Test-Path -LiteralPath $backupPath)) { throw 'Dump missing' }
    $dumpInfo = Get-Item -LiteralPath $backupPath
    if ($dumpInfo.Length -le 0) { throw 'Dump empty' }

    $null = Invoke-Sql ("DROP TABLE $schema.dummy")

    # Schema remains; strip CREATE SCHEMA so restore only recreates the table/data.
    $raw = [IO.File]::ReadAllText($backupPath)
    $filtered = [regex]::Replace($raw, '(?m)^CREATE SCHEMA[^;]*;', '-- schema retained for table-level restore')
    $restorePath = Join-Path $outDir 'restore.sql'
    [IO.File]::WriteAllText($restorePath, $filtered, [Text.UTF8Encoding]::new($false))

    $restoreCode = Invoke-Native $script:psql @(
        '-X','-w','-q','-h','127.0.0.1','-p','56432','-d','zapara_test',
        '-v','ON_ERROR_STOP=1','-f',$restorePath
    )
    if ($restoreCode -ne 0) { throw 'Restore failed' }

    $count = Invoke-Sql ("SELECT count(*)::text FROM $schema.dummy WHERE id = 1 AND note = 'synthetic-rehearsal'")
    if ($count -cne '1') { throw 'Restore assertion failed' }

    $exitCode = 0
    Write-Output "OK schema=$schema dumpBytes=$($dumpInfo.Length) out=$outDir"
} catch {
    [Console]::Error.WriteLine('Backup/restore rehearsal failed.')
    $exitCode = 5
} finally {
    if ($null -ne $schema) {
        try { $null = Invoke-Sql ("DROP SCHEMA IF EXISTS $schema CASCADE") } catch { }
    }
    if ($null -ne $schema) {
        try {
            $left = Invoke-Sql ("SELECT count(*)::text FROM pg_namespace WHERE nspname = '$schema'")
            if ($left -cne '0') {
                [Console]::Error.WriteLine('Owned schema cleanup incomplete.')
                if ($exitCode -eq 0) { $exitCode = 5 }
            }
            $patternLeft = Invoke-Sql ("SELECT count(*)::text FROM pg_namespace WHERE nspname LIKE 'ops_rehearse\_%' ESCAPE '\'")
            if ($patternLeft -cne '0') {
                [Console]::Error.WriteLine('Leftover ops_rehearse_* schemas remain.')
                if ($exitCode -eq 0) { $exitCode = 5 }
            }
        } catch {
            if ($exitCode -eq 0) { $exitCode = 5 }
        }
    }
}

exit $exitCode
