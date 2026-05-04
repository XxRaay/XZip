param(
    [string]$Configuration = "Release",
    [string]$Platform = "x64",
    [string]$Version = "0.1.0.0",
    [string]$Publisher = "CN=XZip",
    [string]$CertificatePassword = "xzip-dev-password",
    [string]$BaseUri = "",
    [switch]$SignPackage,
    [switch]$SkipCertInstall
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$appProject = Join-Path $repoRoot "src\XZip.App\XZip.App.csproj"
$publishProfile = Join-Path $repoRoot "src\XZip.App\Properties\PublishProfiles\win-x64-msix.pubxml"
$installerDir = Join-Path $repoRoot "installer"
$outputDir = Join-Path $installerDir "output"
$certDir = Join-Path $installerDir "certs"
$templatePath = Join-Path $installerDir "XZip.appinstaller.template"
$appInstallerOut = Join-Path $outputDir "XZip.appinstaller"
$pfxPath = Join-Path $certDir "XZip.Dev.pfx"
$cerPath = Join-Path $certDir "XZip.Dev.cer"

if (!(Test-Path $appProject)) { throw "Project not found: $appProject" }
if (!(Test-Path $publishProfile)) { throw "Publish profile not found: $publishProfile" }
if (!(Test-Path $templatePath)) { throw "Template not found: $templatePath" }

New-Item -ItemType Directory -Path $outputDir -Force | Out-Null
New-Item -ItemType Directory -Path $certDir -Force | Out-Null

$signingMsbuildProps = @("/p:AppxPackageSigningEnabled=false")
if ($SignPackage) {
    $securePassword = ConvertTo-SecureString -String $CertificatePassword -AsPlainText -Force

    Write-Host "==> Ensuring code-signing certificate"
    $cert = Get-ChildItem Cert:\CurrentUser\My |
        Where-Object {
            $_.Subject -eq $Publisher -and
            $_.HasPrivateKey -and
            ($_.EnhancedKeyUsageList.ObjectId -contains "1.3.6.1.5.5.7.3.3")
        } |
        Sort-Object NotAfter -Descending |
        Select-Object -First 1

    if (-not $cert) {
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $Publisher `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy Exportable `
            -Provider "Microsoft Enhanced RSA and AES Cryptographic Provider" `
            -KeySpec Signature `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -FriendlyName "XZip Dev Certificate" `
            -NotAfter (Get-Date).AddYears(5)
    }

    try {
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    }
    catch {
        Write-Host "Existing certificate is not exportable, creating a new exportable one..."
        $cert = New-SelfSignedCertificate `
            -Type Custom `
            -Subject $Publisher `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -KeyExportPolicy Exportable `
            -Provider "Microsoft Enhanced RSA and AES Cryptographic Provider" `
            -KeySpec Signature `
            -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3") `
            -CertStoreLocation "Cert:\CurrentUser\My" `
            -FriendlyName "XZip Dev Certificate (Exportable)" `
            -NotAfter (Get-Date).AddYears(5)
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePassword | Out-Null
    }

    Export-Certificate -Cert $cert -FilePath $cerPath -Force | Out-Null

    if (-not $SkipCertInstall) {
        Write-Host "==> Trusting development certificate in CurrentUser\\TrustedPeople"
        $alreadyTrusted = Get-ChildItem Cert:\CurrentUser\TrustedPeople |
            Where-Object { $_.Thumbprint -eq $cert.Thumbprint } |
            Select-Object -First 1
        if (-not $alreadyTrusted) {
            Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\TrustedPeople" | Out-Null
        }

        $alreadyRootTrusted = Get-ChildItem Cert:\CurrentUser\Root |
            Where-Object { $_.Thumbprint -eq $cert.Thumbprint } |
            Select-Object -First 1
        if (-not $alreadyRootTrusted) {
            Import-Certificate -FilePath $cerPath -CertStoreLocation "Cert:\CurrentUser\Root" | Out-Null
        }
    }

    $signingMsbuildProps = @(
        "/p:AppxPackageSigningEnabled=true",
        "/p:PackageCertificateThumbprint=$($cert.Thumbprint.Trim())"
    )
}

Write-Host "==> Building MSIX package"
$msbuildCmd = Get-Command msbuild -ErrorAction SilentlyContinue
if ($msbuildCmd) {
    & $msbuildCmd.Source $appProject `
        /restore `
        /p:PublishProfile="win-x64-msix" `
        /p:Configuration=$Configuration `
        /p:Platform=$Platform `
        /p:AppxPackageVersion=$Version `
        /p:Publisher=$Publisher `
        /p:AppxPackageDir="$outputDir\" `
        $signingMsbuildProps
}
else {
    Write-Host "msbuild.exe not found in PATH, falling back to dotnet msbuild"
    dotnet msbuild $appProject `
        /restore `
        /p:PublishProfile="win-x64-msix" `
        /p:Configuration=$Configuration `
        /p:Platform=$Platform `
        /p:AppxPackageVersion=$Version `
        /p:Publisher=$Publisher `
        /p:AppxPackageDir="$outputDir\" `
        $signingMsbuildProps
}

if ($LASTEXITCODE -ne 0) {
    throw "MSIX build failed with exit code $LASTEXITCODE"
}

$msix = Get-ChildItem -Path $outputDir -Filter *.msix -Recurse |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if (-not $msix) {
    throw "MSIX package not found in $outputDir"
}

if ($BaseUri) {
    Write-Host "==> Generating .appinstaller"
    $appinstallerUri = ($BaseUri.TrimEnd('/') + "/XZip.appinstaller")
    $msixUri = ($BaseUri.TrimEnd('/') + "/" + $msix.Name)
    $content = Get-Content $templatePath -Raw
    $content = $content.Replace("{{APPINSTALLER_URI}}", $appinstallerUri)
    $content = $content.Replace("{{MSIX_URI}}", $msixUri)
    $content = $content.Replace("{{VERSION}}", $Version)
    Set-Content -Path $appInstallerOut -Value $content -Encoding UTF8
}

Write-Host ""
Write-Host "Done. Installer package created:"
Write-Host "  MSIX: $($msix.FullName)"
if ($BaseUri) {
    Write-Host "  AppInstaller: $appInstallerOut"
}
Write-Host ""
Write-Host "Install from repository root:"
Write-Host "  powershell -ExecutionPolicy Bypass -File .\scripts\install-msix.ps1"
Write-Host ""
Write-Host "Or from any directory:"
Write-Host "  powershell -ExecutionPolicy Bypass -File `"$repoRoot\scripts\install-msix.ps1`""
