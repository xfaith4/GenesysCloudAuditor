# Release Packaging and Signing

This guide defines how to produce distributable Windows artifacts.

Audience: release engineers.

Use this document for CI release jobs, local release rehearsal, and signing governance.

## Release Artifacts

| Artifact | Use case | Status |
| --- | --- | --- |
| ZIP (self-contained) | Portable internal distribution | Primary supported path |
| MSIX | Managed enterprise install/update path | Optional, requires packaging project and signing |

## Versioning Standard

Use SemVer for release identity (`MAJOR.MINOR.PATCH`).

Mapping guidance:

- Tag: `vX.Y.Z`
- Assembly/File version: `X.Y.Z.0`
- Informational version: `X.Y.Z+<commit>`
- MSIX package version: `X.Y.Z.<revision>`

## Build Prerequisites

- Windows build agent
- .NET SDK 8.x
- Optional for MSIX: Windows SDK packaging toolchain
- Optional for signing: access to signing certificate/material

## ZIP Build Procedure

```powershell
$Version = "1.0.0"

# Build and publish self-contained
 dotnet restore
 dotnet publish .\src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  /p:Version=$Version `
  /p:FileVersion="$Version.0" `
  /p:AssemblyVersion="$Version.0"

# Create artifact zip
$PublishDir = ".\src\GenesysExtensionAudit.App\bin\Release\net8.0-windows\win-x64\publish"
$OutDir = ".\artifacts\zip"
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
Compress-Archive -Path (Join-Path $PublishDir "*") -DestinationPath (Join-Path $OutDir "GenesysExtensionAudit_$Version_win-x64.zip") -Force
```

## Runner Build (Optional)

If distributing headless scheduling support, also publish runner outputs and include them in release assets.

## Signing Guidance

- MSIX must be signed to install.
- ZIP path should at least include checksums; executable signing is strongly recommended.

Example checksum generation:

```powershell
New-Item -ItemType Directory -Force -Path .\artifacts\checksums | Out-Null
Get-FileHash .\artifacts\zip\*.zip -Algorithm SHA256 |
  ForEach-Object { "$($_.Hash)  $($_.Path | Split-Path -Leaf)" } |
  Out-File -Encoding ascii .\artifacts\checksums\SHA256SUMS.txt
```

## Release Checklist

1. Tests and QA gates pass.
2. Build artifacts generated and verified on clean machine.
3. Secrets are not embedded in appsettings or output folders.
4. Checksums published with artifacts.
5. Release notes document major changes and known limitations.

## Verification Required

MSIX build instructions depend on whether a packaging project exists in this repository. Confirm that project before enabling MSIX in CI.

## Related Documents

- [QA strategy](../QA.md)
- [setup and operations guide](setup-and-operations.md)

