param(
    [string]$InstallerOutput = "",
    [switch]$ForceReinstall
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($InstallerOutput)) {
    $InstallerOutput = Join-Path $repoRoot "installer\output"
}

$certCer = Join-Path $repoRoot "installer\certs\XZip.Dev.cer"
$msix = Get-ChildItem -Path $InstallerOutput -Filter *.msix -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    throw "No .msix file found in $InstallerOutput. Build installer first."
}

#
# Import the signer certificate extracted from THIS exact MSIX package.
# This avoids subject-based mismatches when multiple CN=XZip certs exist.
#
$sig = Get-AuthenticodeSignature -FilePath $msix.FullName
if (-not $sig.SignerCertificate) {
    throw @"
MSIX is not signed: $($msix.FullName)

Rebuild installer with signing enabled:
  powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -SignPackage
"@
}

$tmpCer = Join-Path $env:TEMP ("xzip-msix-signer-" + $sig.SignerCertificate.Thumbprint + ".cer")
Export-Certificate -Cert $sig.SignerCertificate -FilePath $tmpCer -Force | Out-Null

Import-Certificate -FilePath $tmpCer -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
Import-Certificate -FilePath $tmpCer -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null

# When running elevated, trust in LocalMachine as well to avoid chain issues on some systems.
try {
    Import-Certificate -FilePath $tmpCer -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
    Import-Certificate -FilePath $tmpCer -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
}
catch {
    # Ignore if not running elevated; CurrentUser stores are already configured above.
}

$existing = Get-AppxPackage -Name "XZip" -ErrorAction SilentlyContinue
if ($existing -and $ForceReinstall) {
    Remove-AppxPackage -Package $existing.PackageFullName
}

Add-AppxPackage -Path $msix.FullName -ForceUpdateFromAnyVersion
Write-Host "Installed: $($msix.FullName)"
