#requires -Version 7.0
<#
.SYNOPSIS
    Writes SHA256SUMS for release assets and signs it (ECDSA P-256, SHA-256, DER signature).
    The first line, "version: <Version>", ties the file (and its signature) to one release.

.DESCRIPTION
    Upload SHA256SUMS and SHA256SUMS.sig to the GitHub release next to the assets.
    The Windows auto-updater refuses an archive that is not listed in SHA256SUMS and, once its embedded
    release key is set, a SHA256SUMS without a valid signature. See docs/RELEASE_SIGNING.md.

.EXAMPLE
    pwsh scripts/release/sign-release.ps1 -Version 2.1.43 -Assets out/ZAPARA_win-x64.zip, out/ZAPARA_android-debug.apk -PrivateKey D:\keys\zapara-release.key
#>
param(
    # Release version without prefix, matching the tag (v2.1.43 / windows-v2.1.43 -> 2.1.43).
    [Parameter(Mandatory)] [string] $Version,
    [Parameter(Mandatory)] [string[]] $Assets,
    # PEM private key (EC P-256), preferably encrypted (BEGIN ENCRYPTED PRIVATE KEY): the passphrase is asked
    # interactively. Without a key only SHA256SUMS is written.
    [string] $PrivateKey = $env:ZAPARA_RELEASE_KEY,
    # Where SHA256SUMS and SHA256SUMS.sig go; defaults to the folder of the first asset.
    [string] $OutDir,
    # Optional public key PEM to check the fresh signature against (catches using the wrong key).
    [string] $PublicKey
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$Version = $Version.Trim() -replace '^(windows-)?[vV]', ''
if ($Version -notmatch '^\d+(\.\d+){1,3}$') { throw "Version must look like 2.1.43, got '$Version'." }

$files = @($Assets | ForEach-Object { Get-Item -LiteralPath $_ })
# The updater prefers ZAPARA_win-x64_<version>.zip and falls back to ZAPARA_win-x64.zip. Add the versioned copy
# next to the legacy archive (README links keep using the legacy name); upload both.
$versionedName = "ZAPARA_win-x64_$Version.zip"
$legacy = $files | Where-Object Name -eq 'ZAPARA_win-x64.zip' | Select-Object -First 1
if ($legacy -and -not ($files | Where-Object Name -eq $versionedName)) {
    $copy = Join-Path $legacy.DirectoryName $versionedName
    Copy-Item -LiteralPath $legacy.FullName -Destination $copy -Force
    $files += Get-Item -LiteralPath $copy
    Write-Host "Added $copy"
}
if (-not $OutDir) { $OutDir = $files[0].DirectoryName }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$names = @{}
$lines = @("version: $Version") + @(foreach ($f in $files) {
    if ($names.ContainsKey($f.Name)) { throw "Duplicate asset name: $($f.Name)" }
    $names[$f.Name] = $true
    $hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($f.Name)"
})
$sumsPath = Join-Path $OutDir 'SHA256SUMS'
$sums = [System.Text.Encoding]::UTF8.GetBytes((($lines -join "`n") + "`n"))
[System.IO.File]::WriteAllBytes($sumsPath, $sums)
Write-Host "Wrote $sumsPath"
$lines | ForEach-Object { Write-Host "  $_" }

if (-not $PrivateKey) {
    Write-Warning 'No private key given: SHA256SUMS.sig was not created.'
    return
}

$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
try {
    $pem = Get-Content -Raw -LiteralPath $PrivateKey
    if ($pem -match 'BEGIN ENCRYPTED PRIVATE KEY') {
        $secure = Read-Host -AsSecureString 'Release key passphrase'
        $plain = [System.Net.NetworkCredential]::new('', $secure).Password
        try { $ecdsa.ImportFromEncryptedPem($pem, $plain) } finally { $plain = $null }
    } else {
        Write-Warning 'The release key is not encrypted. See docs/RELEASE_SIGNING.md.'
        $ecdsa.ImportFromPem($pem)
    }
    if ($ecdsa.KeySize -ne 256) { throw 'The release key must be an EC P-256 key.' }
    $sig = $ecdsa.SignData($sums, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
        [System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
} finally {
    $ecdsa.Dispose()
}
$sigPath = Join-Path $OutDir 'SHA256SUMS.sig'
[System.IO.File]::WriteAllBytes($sigPath, $sig)
Write-Host "Wrote $sigPath"

if ($PublicKey) {
    $pub = [System.Security.Cryptography.ECDsa]::Create()
    try {
        $pub.ImportFromPem((Get-Content -Raw -LiteralPath $PublicKey))
        $ok = $pub.VerifyData($sums, $sig, [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.DSASignatureFormat]::Rfc3279DerSequence)
    } finally {
        $pub.Dispose()
    }
    if (-not $ok) { throw 'The signature does not verify with the given public key.' }
    Write-Host 'Signature verified with the public key.'
}
