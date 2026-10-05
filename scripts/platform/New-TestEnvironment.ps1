<#
.SYNOPSIS
Creates an owned, fixture-only PostgreSQL 16 Docker environment. No pulls/installs.
.DESCRIPTION
Windows PowerShell 5.1. ToolRoot is mandatory and contains dotnet/dotnet.exe and
pgsql/bin/psql.exe. Private helper/DPAPI/passfiles live outside Git in Temp/opencode.
Dot-source the emitted environment.ps1 separately in each worker shell. Never print
connection variables or passfiles. Docker administrators can inspect container env.
.EXAMPLE
& ./scripts/platform/New-TestEnvironment.ps1 -ToolRoot $existingPrivateTools
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$ToolRoot,
    [string]$Root = (Join-Path (Join-Path ([IO.Path]::GetTempPath()) 'opencode') ('zapara-platform-' + [guid]::NewGuid().ToString()))
)
. (Join-Path $PSScriptRoot 'Environment.Common.ps1')
$Root = Assert-Root $Root
if (-not (Test-Path -LiteralPath (Join-Path $ToolRoot 'dotnet/dotnet.exe') -PathType Leaf) -or
    -not (Test-Path -LiteralPath (Join-Path $ToolRoot 'pgsql/bin/psql.exe') -PathType Leaf)) { throw 'ToolRoot prerequisite: dotnet/psql missing' }
$ToolRoot = [IO.Path]::GetFullPath($ToolRoot).TrimEnd('\')
Assert-NoReparse $ToolRoot
if (Test-Path -LiteralPath $Root) { throw 'Root guard: refuses existing directory' }
if ($ToolRoot.StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) { throw 'ToolRoot preservation guard failed' }
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 56432)
try { $listener.Start() } catch { throw 'Port guard: 127.0.0.1:56432 unavailable' } finally { $listener.Stop() }
$digest = Docker @('image','inspect','--format','{{json .RepoDigests}}',$script:FixtureImage) | ConvertFrom-Json
if ($digest -cnotcontains $script:FixtureImage) { throw 'Pinned image prerequisite missing; no pull attempted' }
if ((Docker @('info','--format','{{.OSType}}')) -cne 'linux') { throw 'Linux Docker prerequisite missing' }
$owner = [IO.Path]::GetFileName($Root).Substring('zapara-platform-'.Length)
$name = 'zapara-platform-' + $owner.Replace('-', '').Substring(0,12)
$volume = "$name-data"
if ((Docker @('ps','-aq','--filter',"name=^/$name$")) -or
    (Docker @('volume','ls','-q','--filter',"name=^$volume$"))) { throw 'Resource name collision: no takeover' }
$gate = New-Object Threading.Mutex($false, 'Local\ZaparaPlatformFixture56432')
$held = $false
try { $held = $gate.WaitOne(0) } catch [Threading.AbandonedMutexException] { $held = $true }
if (-not $held) { $gate.Dispose(); throw 'Setup guard: another fixture setup owns port gate' }
$manifestPath = Join-Path $Root 'ownership.json'
$utf8 = New-Object Text.UTF8Encoding($false)
$oldPassword = $env:POSTGRES_PASSWORD
$createdVolume = $false; $createdId = ''; $stage = 'private ACL'; $secure = $null
try {
    if (Test-Path -LiteralPath $Root) { throw 'Root guard: refuses existing directory' }
    Assert-NoReparse $Root
    $null = [IO.Directory]::CreateDirectory($Root)
    Protect-Root $Root
    # Copy credential-free validation logic so workers do not depend on repo cwd.
    [IO.File]::Copy((Join-Path $PSScriptRoot 'Environment.Common.ps1'), (Join-Path $Root 'Environment.Common.ps1'))
    $bytes = New-Object byte[] 48
    $rng = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $rng.GetBytes($bytes) } finally { $rng.Dispose() }
    $password = [Convert]::ToBase64String($bytes)
    [Array]::Clear($bytes, 0, $bytes.Length)
    $secure = ConvertTo-SecureString $password -AsPlainText -Force
    [IO.File]::WriteAllText((Join-Path $Root 'password.dpapi'), (ConvertFrom-SecureString $secure), $utf8)
    Assert-Private (Join-Path $Root 'password.dpapi') $false
    $service = "[zapara_test]`nhost=127.0.0.1`nport=56432`ndbname=zapara_test`nuser=zapara_owner`nconnect_timeout=2`n"
    [IO.File]::WriteAllText((Join-Path $Root 'pg_service.conf'), $service, $utf8)
    $helper = @'
# Dot-source in each worker shell. No schema is selected; consumer owns schemas.
& {
    param([string]$PrivateRoot)
    . (Join-Path $PrivateRoot 'Environment.Common.ps1')
    Assert-Private $PrivateRoot $true
    $secretPath = Join-Path $PrivateRoot 'password.dpapi'
    Assert-Private $secretPath $false
    $secure = ConvertTo-SecureString ([IO.File]::ReadAllText($secretPath).Trim())
    $pointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure)
    try {
        $password = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($pointer)
        $pass = Join-Path $PrivateRoot ("pgpass-$PID.conf")
        if (Test-Path -LiteralPath $pass) { Assert-Private $pass $false }
        else { [IO.File]::WriteAllText($pass, ''); Assert-Private $pass $false }
        [IO.File]::WriteAllText($pass, "127.0.0.1:56432:zapara_test:zapara_owner:$password`n", (New-Object Text.UTF8Encoding($false)))
        $env:ZAPARA_TEST_POSTGRES = "Host=127.0.0.1;Port=56432;Database=zapara_test;Username=zapara_owner;Password=$password;Include Error Detail=false"
        $env:PGPASSFILE = $pass
    } finally {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($pointer)
        $password = $null; $secure.Dispose()
    }
    $env:PGSERVICE = 'zapara_test'
    $env:PGSERVICEFILE = Join-Path $PrivateRoot 'pg_service.conf'
    $env:PGHOST = '127.0.0.1'; $env:PGHOSTADDR = '127.0.0.1'
    $env:PGPORT = '56432'; $env:PGDATABASE = 'zapara_test'; $env:PGUSER = 'zapara_owner'
    $env:PGPASSWORD = $null; $env:PGOPTIONS = $null; $env:PGCONNECT_TIMEOUT = '2'
    $tools = '__TOOLS__'
    $env:DOTNET_ROOT_X64 = Join-Path $tools 'dotnet'
    $env:DOTNET_ROOT = $env:DOTNET_ROOT_X64
    $env:DOTNET8 = Join-Path $env:DOTNET_ROOT_X64 'dotnet.exe'
    $psqlBin = Join-Path $tools 'pgsql/bin'
    if (($env:PATH -split ';') -notcontains $psqlBin) { $env:PATH = "$psqlBin;$env:PATH" }
} $PSScriptRoot
'@
    $helper = $helper.Replace('__TOOLS__', $ToolRoot.Replace("'", "''"))
    [IO.File]::WriteAllText((Join-Path $Root 'environment.ps1'), $helper, $utf8)
    $stage = 'volume create'
    $null = Docker @('volume','create','--label','project=zapara','--label','purpose=test','--label',"owner=$owner",$volume)
    $createdVolume = $true
    $m = [ordered]@{ containerId = ''; containerName = $name; volume = $volume; owner = $owner; image = $script:FixtureImage; port = 56432; ToolRoot = $ToolRoot }
    [IO.File]::WriteAllText($manifestPath, ($m | ConvertTo-Json), $utf8)
    $stage = 'container create'
    $env:POSTGRES_PASSWORD = $password
    $createdId = Docker @('create','--pull=never','--name',$name,'--label','project=zapara','--label','purpose=test','--label',"owner=$owner",'--restart=no','--publish','127.0.0.1:56432:5432','--mount',"type=volume,source=$volume,target=/var/lib/postgresql/data",'--env','POSTGRES_USER=zapara_owner','--env','POSTGRES_DB=zapara_test','--env','POSTGRES_PASSWORD','--env','POSTGRES_INITDB_ARGS=--auth-host=scram-sha-256 --auth-local=scram-sha-256',$script:FixtureImage)
    if ($createdId -cnotmatch '^[0-9a-f]{64}$') { throw 'Invalid created container ID' }
    $m.containerId = $createdId
    [IO.File]::WriteAllText($manifestPath, ($m | ConvertTo-Json), $utf8)
    Assert-Resources ([pscustomobject]$m)
    $stage = 'start'
    $null = Docker @('start',$createdId)
    $stage = 'ready SELECT 1'
    # Run helper/query in a child shell: setup never leaves credentials in caller env.
    $queryScript = @'
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'environment.ps1')
try {
    for ($i = 0; $i -lt 60; $i++) {
        $ErrorActionPreference = 'Continue'
        $answer = & psql.exe -X -w -t -A -v ON_ERROR_STOP=1 -c 'SELECT 1' 2>$null
        $code = $LASTEXITCODE
        $ErrorActionPreference = 'Stop'
        if ($code -eq 0 -and ($answer | Out-String).Trim() -eq '1') { exit 0 }
        Start-Sleep -Seconds 1
    }
    exit 1
} finally { if ($env:PGPASSFILE) { [IO.File]::Delete($env:PGPASSFILE) } }
'@
    $queryPath = Join-Path $Root 'ready.ps1'
    [IO.File]::WriteAllText($queryPath, $queryScript, $utf8)
    & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $queryPath *> $null
    if ($LASTEXITCODE -ne 0) { throw 'SELECT 1 readiness failed (bounded retries)' }
    Write-Output "Ready SELECT 1=1; container=$createdId; name=$name; volume=$volume; project=zapara purpose=test owner=$owner; 127.0.0.1:56432"
    Write-Output (Join-Path $Root 'environment.ps1')
} catch {
    # No guessed name cleanup after ambiguous native creation; retain exact receipts.
    Write-Warning "Setup failed at $stage; known containerId=$createdId volume=$volume owner=$owner. Raw error suppressed."
    if ($createdVolume -and (Test-Path -LiteralPath $manifestPath) -and $stage -ne 'container create') {
        & (Join-Path $PSScriptRoot 'Remove-TestEnvironment.ps1') -Root $Root -Owner $owner
    }
    throw "Setup failed at $stage; inspect safe ownership receipts before retry"
} finally {
    $env:POSTGRES_PASSWORD = $oldPassword
    $password = $null
    if ($secure) { $secure.Dispose() }
    if ($held) { $gate.ReleaseMutex() }
    $gate.Dispose()
}
