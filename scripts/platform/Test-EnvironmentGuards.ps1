param([Parameter(Mandatory = $true)][string]$ToolRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$new = Join-Path $PSScriptRoot 'New-TestEnvironment.ps1'
$remove = Join-Path $PSScriptRoot 'Remove-TestEnvironment.ps1'
function Reject([string]$Name, [scriptblock]$Action, [string]$Expected) {
    try { & $Action } catch {
        if ($_.Exception.Message -notlike "*$Expected*") { throw }
        Write-Output "PASS $Name : $Expected"
        return
    }
    throw "Guard did not reject: $Name"
}
foreach ($file in @($PSCommandPath, $new, $remove, (Join-Path $PSScriptRoot 'Environment.Common.ps1'))) {
    if (-not (Test-Path -LiteralPath $file)) { throw 'RED: lifecycle implementation missing' }
    $tokens = $null; $errors = $null
    $null = [Management.Automation.Language.Parser]::ParseFile($file, [ref]$tokens, [ref]$errors)
    if ($errors.Count) { throw "Parser errors: $file" }
}
Write-Output 'PASS parser: zero errors'
$base = Join-Path ([IO.Path]::GetTempPath()) 'opencode'
$root = Join-Path $base ('zapara-platform-' + [guid]::NewGuid().ToString())
Reject 'missing ToolRoot' { & $new -ToolRoot (Join-Path $base 'missing-tools') -Root $root } 'ToolRoot prerequisite'
Reject 'unsafe Root' { & $new -ToolRoot $ToolRoot -Root $base } 'Root guard'
$target = Join-Path $base ('zapara-platform-' + [guid]::NewGuid().ToString())
$null = [IO.Directory]::CreateDirectory($target)
try {
    & cmd.exe /c mklink /J $root $target > $null
    if ($LASTEXITCODE -ne 0) { throw 'Cannot create owned junction guard fixture' }
    Reject 'reparse Root' { & $new -ToolRoot $ToolRoot -Root $root } 'Root guard: reparse point'
} finally {
    if ([IO.Directory]::Exists($root)) { [IO.Directory]::Delete($root) }
    [IO.Directory]::Delete($target)
}
$listener = New-Object Net.Sockets.TcpListener([Net.IPAddress]::Loopback, 56432)
try {
    $listener.Start()
    Reject 'busy port' { & $new -ToolRoot $ToolRoot -Root $root } 'Port guard'
} finally { $listener.Stop() }
$null = [IO.Directory]::CreateDirectory($root)
try {
    $manifest = @{ owner = [guid]::NewGuid().ToString(); containerId = ('a' * 64); containerName = 'zapara-platform-aaaaaaaaaaaa'; volume = 'zapara-platform-aaaaaaaaaaaa-data'; image = 'invalid'; port = 56432; ToolRoot = $ToolRoot }
    [IO.File]::WriteAllText((Join-Path $root 'ownership.json'), ($manifest | ConvertTo-Json))
    Reject 'owner mismatch synthetic manifest' { & $remove -Root $root -Owner ([guid]::NewGuid().ToString()) } 'Owner mismatch'
} finally { [IO.Directory]::Delete($root, $true) }
Write-Output 'PASS guards: no Docker resources requested'
