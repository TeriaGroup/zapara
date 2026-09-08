[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Verify-Milestone.ps1'
if (-not (Test-Path -LiteralPath $scriptPath)) { throw 'RED: account verification implementation missing' }
$saved = $env:ZAPARA_TEST_POSTGRES
try {
    foreach ($value in @('', 'Host=remote.invalid;Port=56432;Database=zapara_test')) {
        $env:ZAPARA_TEST_POSTGRES = $value
        $result = & powershell.exe -NoProfile -ExecutionPolicy Bypass -File $scriptPath 2>&1
        if ($LASTEXITCODE -ne 1 -or ($result -join '') -notmatch 'No resources created') {
            throw 'Account verifier guard failed'
        }
    }
    'PASS: absent/wrong DB rejected before resource creation'
    $source = [IO.File]::ReadAllText($scriptPath)
    if ($source -notmatch 'WaitOne\(180000\)') { throw 'RED: account build gate must be bounded to 180 seconds' }
    . (Join-Path $PSScriptRoot 'Verification.Common.ps1')
    $listener = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, 5190)
    $listener.Start()
    try {
        $rejected = $false
        try { Start-Api } catch { $rejected = $_.Exception.Message -eq 'Account proof port 5190 unavailable' }
        if (-not $rejected) { throw 'RED: busy account proof port must fail before starting a process' }
    } finally { $listener.Stop() }
    'PASS: build gate bound and busy port rejected before process creation'
} finally { $env:ZAPARA_TEST_POSTGRES = $saved }
