<#
.SYNOPSIS
Removes only the exact owned fixture and private run directory. Preserves ToolRoot.
.EXAMPLE
& ./scripts/platform/Remove-TestEnvironment.ps1 -Root $privateRunRoot -Owner $ownerUuid
#>
[CmdletBinding()]
param([Parameter(Mandatory = $true)][string]$Root, [Parameter(Mandatory = $true)][string]$Owner)
. (Join-Path $PSScriptRoot 'Environment.Common.ps1')
$Root = Assert-Root $Root
$manifestPath = Join-Path $Root 'ownership.json'
Assert-NoReparse $manifestPath
$m = [IO.File]::ReadAllText($manifestPath) | ConvertFrom-Json
if ($Owner -cne $m.owner) { throw 'Owner mismatch: nothing removed' }
if ($Owner -cnotmatch '^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$' -or
    [IO.Path]::GetFileName($Root) -cne "zapara-platform-$Owner" -or
    $m.containerName -cne ('zapara-platform-' + $Owner.Replace('-', '').Substring(0,12)) -or
    $m.volume -cne "$($m.containerName)-data" -or
    ($m.containerId -and $m.containerId -cnotmatch '^[0-9a-f]{64}$') -or
    $m.image -cne $script:FixtureImage -or $m.port -ne 56432) { throw 'Manifest identity guard failed' }
Assert-Private $Root $true
Assert-Private $manifestPath $false
if ([IO.Path]::GetFullPath($m.ToolRoot).StartsWith($Root, [StringComparison]::OrdinalIgnoreCase)) { throw 'ToolRoot preservation guard failed' }
# Enumerate without following links before any Docker removal or recursive deletion.
$pending = New-Object 'Collections.Generic.Queue[string]'
$pending.Enqueue($Root)
while ($pending.Count) {
    foreach ($child in [IO.Directory]::EnumerateFileSystemEntries($pending.Dequeue())) {
        Assert-NoReparse $child
        if ([IO.Directory]::Exists($child)) { $pending.Enqueue($child) }
    }
}
Assert-Resources $m
if ($m.containerId) {
    $null = Docker @('stop','--time','10',$m.containerId)
    $null = Docker @('rm',$m.containerId)
    $m.containerId = ''
    [IO.File]::WriteAllText($manifestPath, ($m | ConvertTo-Json), (New-Object Text.UTF8Encoding($false)))
}
$null = Docker @('volume','rm',$m.volume)
[IO.Directory]::Delete($Root, $true)
Write-Output "Removed owned fixture: owner=$Owner volume=$($m.volume); ToolRoot retained"
