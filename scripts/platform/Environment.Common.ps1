Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$script:FixtureImage = 'postgres@sha256:cf78e76683b9ca8c5733cbbdce6c9262b45b6767934dd0a95e671f9a0fc20685'
function Assert-NoReparse([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Root guard: reparse point' }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
function Assert-Root([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path).TrimEnd('\')
    $base = [IO.Path]::GetFullPath((Join-Path ([IO.Path]::GetTempPath()) 'opencode')).TrimEnd('\')
    if ([IO.Path]::GetDirectoryName($full) -ine $base -or
        [IO.Path]::GetFileName($full) -cnotmatch '^zapara-platform-[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$') { throw 'Root guard: expected unique GUID directory under Temp/opencode' }
    Assert-NoReparse $full
    return $full
}
function Assert-Private([string]$Path, [bool]$Directory) {
    Assert-NoReparse $Path
    $sections = [Security.AccessControl.AccessControlSections]::Access -bor [Security.AccessControl.AccessControlSections]::Owner
    if ($Directory) { $acl = [IO.Directory]::GetAccessControl($Path, $sections) }
    else { $acl = [IO.File]::GetAccessControl($Path, $sections) }
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    if ($acl.GetOwner([Security.Principal.SecurityIdentifier]).Value -ne $sid.Value -or
        ($Directory -and -not $acl.AreAccessRulesProtected) -or $rules.Count -ne 1 -or
        $rules[0].IdentityReference.Value -ne $sid.Value -or
        $rules[0].AccessControlType -ne 'Allow' -or $rules[0].FileSystemRights -ne 'FullControl' -or
        ($Directory -and $rules[0].InheritanceFlags -ne 'ContainerInherit, ObjectInherit') -or
        (-not $Directory -and -not $rules[0].IsInherited)) { throw 'Private ACL guard failed' }
}
function Protect-Root([string]$Path) {
    $sid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = New-Object Security.AccessControl.DirectorySecurity
    $acl.SetAccessRuleProtection($true, $false)
    $rule = New-Object Security.AccessControl.FileSystemAccessRule($sid, 'FullControl', 'ContainerInherit, ObjectInherit', 'None', 'Allow')
    $acl.AddAccessRule($rule)
    [IO.Directory]::SetAccessControl($Path, $acl)
    Assert-Private $Path $true
    $probe = Join-Path $Path 'inheritance-probe'
    [IO.File]::WriteAllText($probe, '')
    try { Assert-Private $probe $false } finally { [IO.File]::Delete($probe) }
}
function Docker([string[]]$Arguments) {
    $old = $ErrorActionPreference; $ErrorActionPreference = 'Continue'
    try { $result = & docker.exe @Arguments 2>&1; $code = $LASTEXITCODE }
    finally { $ErrorActionPreference = $old }
    if ($code -ne 0) { throw "Docker command failed ($($Arguments[0])); exit=$code; raw output suppressed" }
    return ($result | Out-String).Trim()
}
function Assert-Labels($Labels, [string]$Owner) {
    if ($Labels.project -cne 'zapara' -or $Labels.purpose -cne 'test' -or $Labels.owner -cne $Owner) { throw 'Docker ownership labels mismatch' }
}
function Assert-Resources($Manifest) {
    $volume = Docker @('volume','inspect','--format','{{json .}}',$Manifest.volume) | ConvertFrom-Json
    if ($volume.Name -cne $Manifest.volume) { throw 'Volume identity mismatch' }
    Assert-Labels $volume.Labels $Manifest.owner
    if ($Manifest.containerId) {
        $identity = Docker @('inspect','--type','container','--format','{{.Id}}|{{.Name}}|{{.Config.Image}}',$Manifest.containerId)
        if ($identity -cne "$($Manifest.containerId)|/$($Manifest.containerName)|$($Manifest.image)") { throw 'Container identity mismatch' }
        $labels = Docker @('inspect','--type','container','--format','{{json .Config.Labels}}',$Manifest.containerId) | ConvertFrom-Json
        Assert-Labels $labels $Manifest.owner
    }
}
