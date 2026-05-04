# Installer (MSIX)

This folder stores build outputs for the XZip installer.

## Files

- `output/` - generated `.msix` and optional `.appinstaller`
- `certs/` - development signing certificates for sideloading
- `XZip.appinstaller.template` - template used by `scripts/build-installer.ps1`

## Build

From repository root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

Build signed installer (development certificate):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -SignPackage
```

The script will:

1. Build an MSIX package into `installer/output`
2. Optionally generate `XZip.appinstaller` when `-BaseUri` is provided
3. If `-SignPackage` is specified, create/reuse `CN=XZip` cert and export it to `installer/certs/XZip.Dev.pfx`

## Install locally

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-msix.ps1
```

The install script works on stock PowerShell by importing the dev certificate
and running `Add-AppxPackage -ForceUpdateFromAnyVersion`.
