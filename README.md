# XZip

A modern, fast, beautiful archive manager for Windows.

Built with **.NET 8** and **WinUI 3** (Windows App SDK 1.7+), powered by
[SharpCompress](https://github.com/adamhathcock/sharpcompress) for ZIP / 7z / RAR / TAR / GZ / BZ2.

---

## Features

| Area              | Status   | Notes                                                                  |
| ----------------- | -------- | ---------------------------------------------------------------------- |
| ZIP               | done     | Read + write, parallel deflate via TPL Dataflow                        |
| ZIP encryption    | done     | Password-protected ZIP creation with optional AES-256                  |
| 7z                | done     | Read-only (SharpCompress limitation)                                   |
| RAR               | done     | Read-only (browse + extract)                                           |
| TAR / TGZ / TBZ2  | done     | Read + write                                                           |
| GZ / BZ2          | partial  | Detected; single-stream extract                                        |
| Mica / Acrylic    | done     | Mica on Windows 11, Acrylic fallback on Windows 10                     |
| Light / Dark / Sys| done     | Switchable in Settings, persisted                                      |
| Drag & drop in    | done     | Drop archive to open; drop files into Create page to add               |
| Drag & drop out   | done     | Drag selected archive entries into Explorer / other apps               |
| Preview pane      | removed  | Hidden by request; archive list opens without side preview panel        |
| Localization      | done     | RU / EN .resw files                                                    |
| Recent archives   | done     | Persisted between sessions                                             |
| Shell extension   | scaffold | C++/WRL DLL + Package.appxmanifest registrations for Win10 + Win11     |
| MSIX packaging    | manifest | Single-project MSIX manifest with FTAs and `xzip://` protocol          |
| CLI               | done     | `xzip-helper.exe extract|extract-here|extract-to|add|list|probe`        |

Archive creation intentionally does **not** include 7z or RAR. Supported create formats are:
`ZIP`, `TAR`, `TAR.GZ`, `TAR.BZ2`.

## Project layout

```
XZip/
├── src/
│   ├── XZip.Core/          # archive engine + abstractions, .NET 8 class lib
│   ├── XZip.App/           # WinUI 3 application
│   ├── XZip.Helper/        # headless xzip-helper.exe (used by shell extension)
│   └── XZip.ShellExt/      # C++/WRL IExplorerCommand DLL
├── tests/
│   └── XZip.Core.Tests/    # xUnit + FluentAssertions
├── assets/
├── docs/
└── .github/workflows/
```

## Architecture

```
┌────────────────┐      ┌──────────────────┐
│   XZip.App     │◄────►│   XZip.Core      │
│   (WinUI 3)    │      │ (Archive engine) │
└────────────────┘      └────────┬─────────┘
                                 │
┌────────────────┐               │   ┌──────────────────┐
│ XZip.ShellExt  │               ├──►│  SharpCompress   │
│ (C++/WRL DLL)  │               │   └──────────────────┘
└─────┬──────────┘               │   ┌──────────────────┐
      │                          └──►│  System.IO.Comp. │
      ▼                              └──────────────────┘
┌────────────────┐
│ xzip-helper.exe│
│  (CLI tool)    │
└────────────────┘
```

## Build

Prerequisites:

- Windows 10 1809 (build 17763) or newer
- **.NET 8 SDK** or newer with rollForward enabled (the projects target `net8.0`)
- Windows App SDK 1.7+ (pulled by NuGet)
- Visual Studio 2022 17.10+ with the **Windows application development** workload, or
  the equivalent MSBuild + Windows SDK 10.0.26100 + C++/v143 toolset

```powershell
# Engine + tests (any target)
dotnet build XZip.slnx -c Release
dotnet test  tests/XZip.Core.Tests -c Release

# WinUI 3 app (requires MSBuild)
msbuild src/XZip.App/XZip.App.csproj `
        -p:Configuration=Release -p:Platform=x64 -p:WindowsPackageType=None

# Shell extension DLL (C++ project)
msbuild src/XZip.ShellExt/XZip.ShellExt.vcxproj `
        -p:Configuration=Release -p:Platform=x64
```

## Installer (MSIX)

The project includes a ready-to-use installer pipeline based on MSIX in:

- `src/XZip.App/Properties/PublishProfiles/win-x64-msix.pubxml`
- `scripts/build-installer.ps1`
- `scripts/install-msix.ps1`
- `installer/XZip.appinstaller.template`

Build installer:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1
```

Build a signed installer (dev certificate):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 -SignPackage
```

Important: installation via `Add-AppxPackage` requires a signed `.msix`.
If you plan to install right away, use `-SignPackage`.

Install locally:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-msix.ps1
```

If installation fails with certificate trust errors:

1. Open **PowerShell as Administrator**.
2. Run:

```powershell
$repoRoot = (Resolve-Path ".").Path
$msix = Get-ChildItem -Path (Join-Path $repoRoot "installer\output") -Filter *.msix -Recurse |
  Sort-Object LastWriteTime -Descending |
  Select-Object -First 1 -ExpandProperty FullName
$msix
$sig = Get-AuthenticodeSignature $msix
$tmpCer = Join-Path $env:TEMP ("xzip-" + $sig.SignerCertificate.Thumbprint + ".cer")
Export-Certificate -Cert $sig.SignerCertificate -FilePath $tmpCer -Force | Out-Null
Import-Certificate -FilePath $tmpCer -CertStoreLocation "Cert:\LocalMachine\Root" | Out-Null
Import-Certificate -FilePath $tmpCer -CertStoreLocation "Cert:\LocalMachine\TrustedPeople" | Out-Null
Add-AppxPackage -Path $msix -ForceUpdateFromAnyVersion
```

Note: install script imports the dev certificate and runs a standard
`Add-AppxPackage -ForceUpdateFromAnyVersion` flow for maximum compatibility.
For production distribution, prefer `-SignPackage` with a trusted certificate.

Generate `.appinstaller` for hosted updates:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-installer.ps1 `
  -BaseUri "https://your-domain.example.com/xzip"
```

## CLI usage

```text
xzip-helper extract      <archive> <destination>
xzip-helper extract-here <archive>
xzip-helper extract-to   <archive>            (creates <name>/ next to archive)
xzip-helper add          <output.zip> <input...>
xzip-helper list         <archive>
xzip-helper probe        <archive>
```

## Roadmap

The plan is split into eight phases (see `docs/`):

| Phase | Topic                                            | Status |
| ----- | ------------------------------------------------ | ------ |
| 0     | Solution scaffold, packages, CI                  | ✅     |
| 1     | Core engine (ZIP) + tests                        | ✅     |
| 2     | WinUI 3 main window + Mica / Acrylic + nav       | ✅     |
| 3     | Create page + parallel TPL Dataflow pipeline     | ✅     |
| 4     | 7z / TAR / TGZ / TBZ2 providers                  | ✅     |
| 5     | Preview pane + drag & drop out                   | ✅     |
| 6     | Settings, themes, RU / EN resources              | ✅     |
| 7     | Shell extension scaffold + MSIX manifest         | ✅     |
| 8     | Icons, packaging, polish                         | 🟡     |

## License

MIT — see [LICENSE](LICENSE).
