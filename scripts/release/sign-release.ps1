#requires -Version 7.0
<#
.SYNOPSIS
    Writes SHA256SUMS for release assets and signs it (ECDSA P-256, SHA-256, DER signature).

.DESCRIPTION
    Upload SHA256SUMS and SHA256SUMS.sig to the GitHub release next to the assets.
    The Windows auto-updater refuses an archive that is not listed in SHA256SUMS and, once its embedded
    release key is set, a SHA256SUMS without a valid signature. See docs/RELEASE_SIGNING.md.

.EXAMPLE
    pwsh scripts/release/sign-release.ps1 -Assets out/ZAPARA_win-x64.zip, out/ZAPARA_android-debug.apk -PrivateKey D:\keys\zapara-release.key
#>
param(
    [Parameter(Mandatory)] [string[]] $Assets,
    # PEM private key (EC P-256). Without it only SHA256SUMS is written.
    [string] $PrivateKey = $env:ZAPARA_RELEASE_KEY,
    # Where SHA256SUMS and SHA256SUMS.sig go; defaults to the folder of the first asset.
    [string] $OutDir,
    # Optional public key PEM to check the fresh signature against (catches using the wrong key).
    [string] $PublicKey
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$files = $Assets | ForEach-Object { Get-Item -LiteralPath $_ }
if (-not $OutDir) { $OutDir = $files[0].DirectoryName }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$names = @{}
$lines = foreach ($f in $files) {
    if ($names.ContainsKey($f.Name)) { throw "Duplicate asset name: $($f.Name)" }
    $names[$f.Name] = $true
    $hash = (Get-FileHash -LiteralPath $f.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    "$hash  $($f.Name)"
}
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
    $ecdsa.ImportFromPem((Get-Content -Raw -LiteralPath $PrivateKey))
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
