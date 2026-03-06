# Deployment

## Build Requirements

| Requirement | Version |
|---|---|
| Windows | 10 or 11 (64-bit) |
| .NET SDK | 8.x |
| Visual Studio | 2022 17.8+ (optional; `dotnet` CLI sufficient) |

---

## Building from Source

```powershell
# Restore dependencies
dotnet restore

# Debug build
dotnet build

# Release build
dotnet build -c Release

# Run the WPF application (requires Windows)
dotnet run --project src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj

# Run tests
dotnet test tests\GenesysExtensionAudit.Infrastructure.Tests\
```

---

## Self-Contained Publish (Portable ZIP)

To create a portable, self-contained distribution that does not require .NET to be installed on the target machine:

```powershell
dotnet publish .\src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -o .\artifacts\publish\win-x64
```

Then zip the output:

```powershell
Compress-Archive -Path .\artifacts\publish\win-x64\* `
  -DestinationPath .\artifacts\GenesysExtensionAudit-win-x64-v1.0.0.zip
```

---

## Versioning

The project uses **Semantic Versioning** (`MAJOR.MINOR.PATCH`):

| Part | When to increment |
|---|---|
| `MAJOR` | Breaking changes to audit logic, export schema, or configuration |
| `MINOR` | New audit checks, new export sheets, new configuration options |
| `PATCH` | Bug fixes, performance improvements, documentation updates |

Version is controlled in `Directory.Build.props`:

```xml
<PropertyGroup>
  <Version>1.0.0</Version>
  <AssemblyVersion>1.0.0.0</AssemblyVersion>
  <FileVersion>1.0.0.0</FileVersion>
</PropertyGroup>
```

---

## CI/CD Pipeline

### Workflow: `release-zip.yml`

**Triggers:**
- Tag push matching `v*` (e.g., `v1.2.3`)
- Manual `workflow_dispatch` with a tag name input

**Steps:**
1. Checkout repository
2. Set up .NET 8
3. Resolve release metadata (tag name, release title)
4. `dotnet publish` self-contained win-x64
5. Create ZIP archive: `GenesysExtensionAudit-win-x64-<tag>.zip`
6. Upload to GitHub Release via `softprops/action-gh-release@v2`

**Outputs:** A GitHub Release asset attached to the matching tag.

### Creating a Release

```bash
git tag v1.2.3
git push origin v1.2.3
```

This triggers the workflow automatically. The release notes are auto-generated from commits since the previous tag.

For a manual release without a tag push, use the **Actions → Build And Release Zip → Run workflow** button and provide the tag name.

---

## MSIX Packaging (Optional)

For MSIX installer packages (Windows App Store / enterprise sideloading):

1. Add a **Windows Application Packaging Project** to the solution targeting the WPF app.
2. Configure `Package.appxmanifest` with package identity, publisher, and capabilities.
3. Code-sign the MSIX with a trusted certificate (required for distribution outside the Store):
   ```powershell
   signtool sign /fd SHA256 /a /f certificate.pfx /p "password" MyApp.msix
   ```
4. Distribute the `.msix` file (or `.msixbundle` for multi-arch).

MSIX auto-update can be configured with an **App Installer** (`.appinstaller`) file pointing to a hosted location.

> The current CI pipeline produces a ZIP (no installer). MSIX packaging is an optional enhancement for enterprise distribution.

---

## Configuration in Deployed Environments

Credentials must be injected at deploy time — never baked into the published output.

### Option 1 — Environment variables (recommended for production)

```powershell
[System.Environment]::SetEnvironmentVariable("GenesysOAuth__ClientId",     "YOUR_ID",     "Machine")
[System.Environment]::SetEnvironmentVariable("GenesysOAuth__ClientSecret", "YOUR_SECRET", "Machine")
[System.Environment]::SetEnvironmentVariable("Genesys__Region",            "mypurecloud.com", "Machine")
```

### Option 2 — Sidecar `appsettings.Production.json`

Place an `appsettings.Production.json` alongside the executable. .NET will merge it over the base `appsettings.json`:

```json
{
  "Genesys": { "Region": "usw2.pure.cloud" },
  "GenesysOAuth": {
    "ClientId": "YOUR_ID",
    "ClientSecret": "YOUR_SECRET"
  }
}
```

Set `DOTNET_ENVIRONMENT=Production` to ensure the correct file is loaded.

---

## Scheduled Task Deployment

When deploying for automated scheduled runs:

1. Deploy the ZIP to a persistent directory (e.g., `C:\Tools\GenesysAudit\`).
2. Set credentials as machine-level environment variables (see above).
3. Register a Windows Scheduled Task pointing to `GenesysExtensionAudit.Runner.exe` with the desired schedule profile:
   ```
   GenesysExtensionAudit.Runner.exe --schedule-profile "C:\Tools\GenesysAudit\profile.json"
   ```
4. Configure the task to run under a service account with access to the output directory.

Alternatively, use the **Schedule Audits** tab in the GUI to register tasks interactively.

---

## See Also

- [Architecture](architecture.md) — Project structure and solution layout
- [Authentication](authentication.md) — Credential configuration options
