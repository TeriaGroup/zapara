param([Parameter(Mandatory=$true)][string]$EnvironmentHelper, [switch]$RegistrationDisabled,
    [ValidateSet('online','offline')][string]$Logout, [switch]$MinimumKeyboard, [switch]$AccountAccessibility)
$ErrorActionPreference = 'Stop'
if ($AccountAccessibility -and ($Logout -or $MinimumKeyboard -or $RegistrationDisabled)) { throw 'Cannot combine accessibility modes' }
if ($RegistrationDisabled -and $Logout) { throw 'cannot combine logout and registration-disabled' }
if ($MinimumKeyboard -and $Logout -ne 'online') { throw 'MinimumKeyboard requires Logout online' }
. (Join-Path $PSScriptRoot 'AccountUi.Logout.ps1')
$repo = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
if (!(Test-Path -LiteralPath $repo)) { throw 'Workspace missing' }
. $EnvironmentHelper
$desktopDotnet = (Get-Command dotnet.exe).Source
$owner = [Guid]::NewGuid().ToString('N')
$schema = 'acc_ui_' + $owner
$output = Join-Path $repo ('.superpowers/sdd/2026-09-08-platform-rest/evidence/windows-account-ui/' + $owner)
$data = Join-Path ([IO.Path]::GetTempPath()) ('opencode/zapara-w1-' + $owner)
$mutex = [Threading.Mutex]::new($false, 'Local\ZaparaServerBuildGate')
$held = $false; $server = $null; $driver = $null; $created = $false
$old = @{}
$keys = @('Accounts__Enabled','Accounts__Schema','ConnectionStrings__Accounts','ConnectionStrings__AccountsMigration','ASPNETCORE_ENVIRONMENT','ASPNETCORE_URLS','Timetable__Schema','Logging__LogLevel__Microsoft.AspNetCore.Routing.EndpointMiddleware')
foreach ($key in $keys) { $old[$key] = [Environment]::GetEnvironmentVariable($key) }
try {
    $held = $mutex.WaitOne(180000)
    if (!$held) { throw 'Build gate timeout' }
    [IO.Directory]::CreateDirectory($output) | Out-Null
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 5193)
    $listener.Start(); $listener.Stop()
    & psql.exe -X -w -v ON_ERROR_STOP=1 -c "CREATE SCHEMA $schema" | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Schema creation failed' }
    $created = $true
    $env:Accounts__Enabled='true'; $env:Accounts__Schema=$schema
    $env:ConnectionStrings__Accounts=$env:ZAPARA_TEST_POSTGRES
    $env:ConnectionStrings__AccountsMigration=$env:ZAPARA_TEST_POSTGRES
    $env:ASPNETCORE_ENVIRONMENT='Testing'
    if ($RegistrationDisabled) { $env:ASPNETCORE_ENVIRONMENT='Staging' }
    [Environment]::SetEnvironmentVariable('Logging__LogLevel__Microsoft.AspNetCore.Routing.EndpointMiddleware','Information')
    $env:ASPNETCORE_URLS='http://127.0.0.1:5193'
    $env:Timetable__Schema=$schema + '_tt'
    & $env:DOTNET8 (Join-Path $repo 'src/Zapara.AdminCli/bin/Debug/net8.0/Zapara.AdminCli.dll') accounts db-migrate
    if ($LASTEXITCODE -ne 0) { throw 'Account migration failed' }
    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName=$env:DOTNET8
    $info.Arguments='"' + (Join-Path $repo 'src/Zapara.Server/bin/Debug/net8.0/Zapara.Server.dll') + '"'
    $info.UseShellExecute=$false
    $info.RedirectStandardOutput=$true; $info.RedirectStandardError=$true
    $server=[Diagnostics.Process]::Start($info)
    [IO.File]::WriteAllText((Join-Path $output 'ownership.txt'), "schema=$schema`ndata=$data`nserverPid=$($server.Id)`nserverStart=$($server.StartTime.ToString('o'))`n")
    $stdout=$server.StandardOutput.ReadToEndAsync(); $stderr=$server.StandardError.ReadToEndAsync()
    $ready=$false
    for ($i=0; $i -lt 40; $i++) {
        if ($server.HasExited) { throw 'Owned Kestrel exited before readiness' }
        try { $r=Invoke-WebRequest 'http://127.0.0.1:5193/health/live' -UseBasicParsing -TimeoutSec 1; $ready=$r.StatusCode -eq 200 } catch {}
        if ($ready) { break }; Start-Sleep -Milliseconds 250
    }
    if (!$ready) { throw 'Owned Kestrel unavailable' }
    $capability=Invoke-RestMethod 'http://127.0.0.1:5193/api/v1/auth/capabilities' -TimeoutSec 5
    if ($capability.password -ne $true -or $capability.registration -ne (!$RegistrationDisabled)) { throw 'Unexpected real server capabilities' }
    [IO.File]::WriteAllText((Join-Path $output 'capabilities.json'), ($capability | ConvertTo-Json))
    $desktopRoot = [IO.Path]::GetDirectoryName($desktopDotnet)
    $driverInfo=[Diagnostics.ProcessStartInfo]::new()
    $driverInfo.FileName=$desktopDotnet
    $driverInfo.UseShellExecute=$false
    $driverInfo.RedirectStandardOutput=$true; $driverInfo.RedirectStandardError=$true
    $driverInfo.Environment['DOTNET_ROOT'] = $desktopRoot
    $driverInfo.Environment['DOTNET_ROOT_X64'] = $desktopRoot
    $driverInfo.Arguments='"' + (Join-Path $repo 'src/Vograph.Desktop.UiVerify/bin/Debug/net8.0-windows/Vograph.Desktop.UiVerify.dll') + '" --exe "' + (Join-Path $repo 'src/Vograph.Desktop/bin/Debug/net8.0/Vograph.exe') + '" --out "' + $output + '" --data "' + $data + '" --account-api http://127.0.0.1:5193 --timeout 20'
    if ($RegistrationDisabled) { $driverInfo.Arguments += ' --registration-disabled' }
    if ($Logout) { $driverInfo.Arguments += ' --logout ' + $Logout }
    if ($MinimumKeyboard) { $driverInfo.Arguments += ' --minimum-keyboard' }
    if ($AccountAccessibility) { $driverInfo.Arguments += ' --account-accessibility' }
    $driver=[Diagnostics.Process]::Start($driverInfo)
    $driverOut=$driver.StandardOutput.ReadToEndAsync(); $driverErr=$driver.StandardError.ReadToEndAsync()
    if ($Logout) { Wait-AccountLogoutCheckpoints -Output $output -Schema $schema -Mode $Logout -Server $server -Driver $driver }
    if (!$driver.WaitForExit(150000)) { throw "Owned GUI driver timeout; checkpoint: $output" }
    [IO.File]::WriteAllText((Join-Path $output 'driver.txt'), $driverOut.Result + $driverErr.Result)
    $result=$driver.ExitCode
    if (!$server.HasExited) { $server.Kill(); [void]$server.WaitForExit(5000) }
    # Keep only bounded route counters, never raw request/exception logs or credentials.
    $serverLog=$stdout.Result + $stderr.Result
    $wire=@{ registrationDisabled=[bool]$RegistrationDisabled; environment=$env:ASPNETCORE_ENVIRONMENT }
    foreach ($route in @('GET /api/v1/auth/capabilities','POST /api/v1/auth/login','POST /api/v1/auth/register','POST /api/v1/auth/logout')) {
        $wire[$route]=[regex]::Matches($serverLog, [regex]::Escape("Executing endpoint 'HTTP: $route'")).Count
    }
    [IO.File]::WriteAllText((Join-Path $output 'wire-counts.json'), ($wire | ConvertTo-Json))
    if ($wire['GET /api/v1/auth/capabilities'] -lt 2 -or $wire['POST /api/v1/auth/login'] -lt 1) { throw 'Endpoint instrumentation did not observe API and GUI requests' }
    if ($RegistrationDisabled -and $wire['POST /api/v1/auth/register'] -ne 0) { throw 'Unexpected registration request' }
    if (!$RegistrationDisabled -and !$Logout -and !$AccountAccessibility -and $wire['POST /api/v1/auth/register'] -ne 2) { throw 'Default registration positive control missing' }
    if ($Logout) {
        $expectedLogout = 0; if ($Logout -eq 'online') { $expectedLogout = 1 }
        if ($wire['POST /api/v1/auth/register'] -ne 1 -or $wire['POST /api/v1/auth/login'] -ne 1 -or $wire['POST /api/v1/auth/logout'] -ne $expectedLogout) { throw 'Ordinary logout endpoint counts mismatch' }
    }
    if ($result -ne 0) { throw "Account UI verification failed: $result; evidence: $output" }
    "ACCOUNT_UI_PASS=$output"
} finally {
    if ($driver) {
        $children=Get-CimInstance Win32_Process -Filter "ParentProcessId=$($driver.Id)"
        foreach ($child in $children) {
            if ($child.ExecutablePath -eq (Join-Path $repo 'src/Vograph.Desktop/bin/Debug/net8.0/Vograph.exe').Replace('/','\') -and $child.CreationDate -ge $driver.StartTime) {
                $owned=Get-Process -Id $child.ProcessId -ErrorAction SilentlyContinue
                if ($owned) { [void]$owned.CloseMainWindow(); if (!$owned.WaitForExit(5000)) { $owned.Kill(); [void]$owned.WaitForExit(5000) } }
            }
        }
        if (!$driver.HasExited) { $driver.Kill(); [void]$driver.WaitForExit(5000) }
        [IO.File]::WriteAllText((Join-Path $output 'driver.txt'), $driverOut.Result + $driverErr.Result)
        $driver.Dispose()
    }
    if ($server) { if (!$server.HasExited) { $server.Kill(); [void]$server.WaitForExit(5000) }; $server.Dispose() }
    if ($created) {
        & psql.exe -X -w -v ON_ERROR_STOP=1 -c "SET client_min_messages=warning; DROP SCHEMA $schema CASCADE" | Out-Null
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Owned schema cleanup failed' }
    }
    if ((Test-Path -LiteralPath (Join-Path $data '.uiverify'))) {
        $credentials=Join-Path $data 'credentials'
        if (Test-Path -LiteralPath $credentials) { [IO.Directory]::Delete($credentials, $true) }
        [IO.Directory]::Delete($data, $true)
    }
    if ($env:PGPASSFILE) { [IO.File]::Delete($env:PGPASSFILE) }
    foreach ($key in $keys) { [Environment]::SetEnvironmentVariable($key,$old[$key]) }
    if ($held) { $mutex.ReleaseMutex() }; $mutex.Dispose()
}
