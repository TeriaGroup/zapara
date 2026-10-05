<#
.SYNOPSIS
Local-only compiled account API acceptance: real PostgreSQL, AdminCli, Kestrel and curl.
.DESCRIPTION
Dot-source the operator's private environment helper first. No installs, restores,
containers, remote DBs or native tests. Each dotnet invocation holds the server gate.
Only a fresh acc_http_<guid> schema/process/private directory is owned. Request/header
files are user-ACL-private and deleted; only redacted receipts survive. PS 5.1.
#>
[CmdletBinding()]
param()
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
try {
    if ([string]::IsNullOrWhiteSpace($env:ZAPARA_TEST_POSTGRES) -or $env:PGSERVICE -ne 'zapara_test') { throw 'Missing fixture' }
    Add-Type -AssemblyName System.Data
    $connection = [System.Data.Common.DbConnectionStringBuilder]::new()
    $connection.set_ConnectionString($env:ZAPARA_TEST_POSTGRES)
    $allowed = @('host','port','database','username','password','pooling','timeout','command timeout','ssl mode',
        'include error detail','log parameters','persist security info','maximum pool size','minimum pool size')
    foreach ($key in $connection.Keys) { if ($allowed -notcontains $key.ToLowerInvariant()) { throw 'Unsupported routing' } }
    if ([string]$connection.get_Item('Host') -ne '127.0.0.1' -or [string]$connection.get_Item('Port') -ne '56432' -or
        [string]$connection.get_Item('Database') -cne 'zapara_test') { throw 'Unsafe target' }
    $connection.Clear()
} catch { [Console]::WriteLine('Local fixture target unverified. No resources created.'); exit 1 }
. (Join-Path $PSScriptRoot 'Verification.Common.ps1')
$script:receipts = [Collections.Generic.List[string]]::new()
$script:secrets = [Collections.Generic.List[string]]::new()
$script:secrets.Add($env:ZAPARA_TEST_POSTGRES)
$script:api = $null; $script:out = $null; $created = $false; $held = $false; $gate = $null
$schema = 'acc_http_' + [Guid]::NewGuid().ToString('N')
$saved = @{}; $failed = $false
try {
    $script:psql = (Get-Command psql.exe -ErrorAction Stop).Source
    $script:curl = (Get-Command curl.exe -ErrorAction Stop).Source
    $script:runtime = $env:DOTNET8
    Check (Test-Path -LiteralPath $script:runtime) 'DOTNET8 runtime missing'
    $parent = Join-Path ([IO.Path]::GetTempPath()) 'opencode'
    Check (Test-Path -LiteralPath $parent) 'Artifact parent missing'
    $ancestor = [IO.DirectoryInfo]::new($parent)
    while ($null -ne $ancestor) {
        Check (($ancestor.Attributes -band [IO.FileAttributes]::ReparsePoint) -eq 0) 'Reparse artifact ancestry'
        $ancestor = $ancestor.Parent
    }
    $script:out = Join-Path $parent ('account-http-proof-' + [Guid]::NewGuid().ToString('N'))
    [void][IO.Directory]::CreateDirectory($script:out)
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl.SetAccessRuleProtection($true, $false)
    $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new($sid, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
    [IO.Directory]::SetAccessControl($script:out, $acl)
    $actualAcl = [IO.Directory]::GetAccessControl($script:out)
    Check ($actualAcl.AreAccessRulesProtected -and $actualAcl.Access.Count -eq 1) 'Private ACL not applied'
    Receipt 'private root protected current-user-only ACL=true'
    $script:private = Join-Path $script:out 'private'
    [void][IO.Directory]::CreateDirectory($script:private)
    $gate = [Threading.Mutex]::new($false, 'Local\ZaparaServerBuildGate')
    try { $held = $gate.WaitOne(180000) } catch [Threading.AbandonedMutexException] { $held = $true }
    Check $held 'Build gate timeout'
    $root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
    $null = Native 'dotnet' @('publish',(Join-Path $root 'src\Zapara.Server\Zapara.Server.csproj'),'--no-restore','-c','Debug','-o',(Join-Path $script:out 'server'))
    Receipt 'dotnet publish Server --no-restore Debug exit=0'
    $null = Native 'dotnet' @('publish',(Join-Path $root 'src\Zapara.AdminCli\Zapara.AdminCli.csproj'),'--no-restore','-c','Debug','-o',(Join-Path $script:out 'cli'))
    Receipt 'dotnet publish AdminCli --no-restore Debug exit=0'
    foreach ($key in @('ConnectionStrings__Accounts','ConnectionStrings__AccountsMigration','Accounts__Schema','Accounts__Enabled','DOTNET_ENVIRONMENT','ASPNETCORE_ENVIRONMENT')) {
        $saved[$key] = [Environment]::GetEnvironmentVariable($key, 'Process')
    }
    $env:ConnectionStrings__Accounts = $env:ZAPARA_TEST_POSTGRES
    $env:ConnectionStrings__AccountsMigration = $env:ZAPARA_TEST_POSTGRES
    $env:Accounts__Schema = $schema; $env:Accounts__Enabled = 'true'
    $env:DOTNET_ENVIRONMENT = 'Development'; $env:ASPNETCORE_ENVIRONMENT = 'Development'
    Check ((Sql 'SELECT current_database()') -eq 'zapara_test') 'Database identity mismatch'
    $null = Sql "CREATE SCHEMA $schema"; $created = $true; Receipt "created schema=$schema"
    $null = Native $script:runtime @((Join-Path $script:out 'cli\Zapara.AdminCli.dll'),'accounts','db-migrate')
    Receipt 'compiled AdminCli accounts db-migrate exit=0'
    $password = 'Synthetic local password ' + [Guid]::NewGuid().ToString('N')
    $newPassword = 'Changed local password ' + [Guid]::NewGuid().ToString('N')
    $script:secrets.Add($password); $script:secrets.Add($newPassword)
    $script:secrets.Add('http.synthetic')
    Start-Api
    $null = Http 'GET' '/api/v1/account/me' 401 $null '' 'invalid_session'
    $user = Http 'POST' '/api/v1/auth/register' 201 @{ username='http.synthetic'; password=$password }
    Check ($null -eq $user.PSObject.Properties['accessToken']) 'Registration unexpectedly issued session'
    $first = Login $password
    $me = Http 'GET' '/api/v1/account/me' 200 $null $first.accessToken
    Check ($me.user.userId -eq $user.userId) 'Wrong authenticated actor'
    $rotated = Http 'POST' '/api/v1/auth/refresh' 200 @{refreshToken=$first.refreshToken}
    $script:secrets.Add($rotated.accessToken); $script:secrets.Add($rotated.refreshToken)
    $null = Http 'GET' '/api/v1/account/me' 401 $null $first.accessToken 'invalid_session'
    $null = Http 'GET' '/api/v1/account/me' 200 $null $rotated.accessToken
    Check ((Sql "SELECT count(*) FROM $schema.refresh_tokens WHERE consumed_at IS NOT NULL") -eq '1') 'Rotation SQL missing'
    Check ((Sql "SELECT count(*) FROM $schema.access_tokens WHERE octet_length(token_hash)=32") -eq '1') 'Token hashes SQL mismatch'
    $null = Http 'POST' '/api/v1/auth/refresh' 401 @{refreshToken=$first.refreshToken} '' 'invalid_session'
    $null = Http 'GET' '/api/v1/account/me' 401 $null $rotated.accessToken 'invalid_session'
    $null = Http 'POST' '/api/v1/auth/refresh' 401 @{refreshToken=$rotated.refreshToken} '' 'invalid_session'
    $live = Login $password; $second = Login $password
    $null = Http 'DELETE' "/api/v1/account/devices/$($second.familyId)" 204 $null $live.accessToken
    $null = Http 'DELETE' "/api/v1/account/devices/$($second.familyId)" 204 $null $live.accessToken
    $null = Http 'GET' '/api/v1/account/me' 401 $null $second.accessToken 'invalid_session'
    Stop-Api; Start-Api
    $null = Http 'GET' '/api/v1/account/me' 200 $null $live.accessToken
    $null = Http 'GET' '/api/v1/account/me' 401 $null $second.accessToken 'invalid_session'
    $null = Http 'POST' '/api/v1/account/password/change' 204 @{currentPassword=$password;newPassword=$newPassword} $live.accessToken
    $null = Http 'GET' '/api/v1/account/me' 401 $null $live.accessToken 'invalid_session'
    $null = Http 'POST' '/api/v1/auth/refresh' 401 @{refreshToken=$live.refreshToken} '' 'invalid_session'
    $fresh = Login $newPassword
    $null = Http 'POST' '/api/v1/auth/logout' 204 $null $fresh.accessToken
    $null = Http 'GET' '/api/v1/account/me' 401 $null $fresh.accessToken 'invalid_session'
    $null = Http 'GET' '/health/live' 200
    Check ((Sql "SELECT count(*) FROM $schema.account_security_events WHERE action='password_change'") -eq '1') 'Password audit absent'
    $rows = Sql "SELECT string_agg(row_to_json(t)::text,'') FROM (SELECT * FROM $schema.account_security_events) t"
    foreach ($secret in $script:secrets) { Check (-not $rows.Contains($secret)) 'Audit canary present' }
    Receipt 'SQL rotation/hash/audit assertions=true'
    Stop-Api
    Receipt 'ACCEPTANCE GREEN'
} catch {
    $failed = $true
    Receipt 'ACCEPTANCE FAILED (raw exception suppressed)'
} finally {
    try { Stop-Api } catch { $failed=$true; Receipt 'Owned process cleanup/log check failed' }
    if ($created) {
        try {
            $null = Sql "DROP SCHEMA $schema CASCADE"
            Check ((Sql "SELECT count(*) FROM pg_namespace WHERE nspname='$schema'") -eq '0') 'Schema remains'
            Receipt "dropped schema=$schema remaining=0"
        } catch { $failed=$true; Receipt "Owned schema cleanup failed: $schema" }
    }
    foreach ($key in $saved.Keys) { [Environment]::SetEnvironmentVariable($key, $saved[$key], 'Process') }
    if ($held) { $gate.ReleaseMutex() }; if ($null -ne $gate) { $gate.Dispose() }
    if ($null -ne $script:out) {
        if (Test-Path -LiteralPath (Join-Path $script:out 'private')) {
            [IO.Directory]::Delete((Join-Path $script:out 'private'), $true)
            Receipt 'owned request/header/response secret files deleted=true'
        }
        [IO.File]::WriteAllLines((Join-Path $script:out 'receipts.txt'), $script:receipts)
        [Console]::WriteLine("Redacted receipts: $script:out")
    }
}
if ($failed) { exit 1 }
'PASS: real account Kestrel/curl/SQL acceptance; owned process/schema/secret files cleaned'
