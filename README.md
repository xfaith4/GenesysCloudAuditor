# Genesys Cloud Auditor

This repository contains a .NET 8 Windows desktop auditor for Genesys Cloud configuration and operational data.

Audience: platform administrators, support engineers, and developers maintaining audit workflows.

Use this documentation when you need to configure credentials, run audits interactively or on a schedule, interpret findings, or package releases.

## Quick Start

1. Configure OAuth credentials and region in `src/GenesysExtensionAudit.App/appsettings.json` (or environment variables).
2. Build:

```powershell
dotnet restore
dotnet build -c Release
```

3. Run the desktop app:

```powershell
dotnet run --project src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj
```

4. Optional: run the headless runner:

```powershell
dotnet run --project src\GenesysExtensionAudit.Runner\GenesysExtensionAudit.Runner.csproj -- --dry-run
```

## Repository Layout

```text
GenesysCloudAuditor/
|-- src/
|   |-- GenesysExtensionAudit.App/            # WPF UI + scheduling UI
|   |-- GenesysExtensionAudit.Runner/         # Headless runner for scheduled tasks
|   |-- GenesysExtensionAudit.Core/           # Contracts + domain models
|   |-- GenesysExtensionAudit.Domain/         # Audit engine
|   `-- GenesysExtensionAudit.Infrastructure/ # API clients, orchestration, export, logging
|-- tests/
|   `-- GenesysExtensionAudit.Infrastructure.Tests/
|-- docs/
|   `-- reference and operational guides
|-- QA.md
`-- NOTES.md
```

## Documentation Map

| Document | Purpose |
| --- | --- |
| [QA.md](QA.md) | QA strategy, acceptance gates, and execution model |
| [NOTES.md](NOTES.md) | Verification backlog and documentation debt tracker |
| [docs/setup-and-operations.md](docs/setup-and-operations.md) | Operator runbook for setup and execution |
| [docs/oauth-and-api-resilience.md](docs/oauth-and-api-resilience.md) | Authentication and token lifecycle design |
| [docs/application-architecture.md](docs/application-architecture.md) | Application architecture and layering |
| [docs/data-model-and-audit-algorithms.md](docs/data-model-and-audit-algorithms.md) | Data model and audit logic |
| [docs/extension-normalization-policy.md](docs/extension-normalization-policy.md) | Extension normalization policy |
| [docs/detailed-qa-matrix.md](docs/detailed-qa-matrix.md) | Detailed end-to-end QA matrix |
| [docs/release-packaging-and-signing.md](docs/release-packaging-and-signing.md) | Release packaging and signing |

## Core Capabilities

- Extension consistency checks (profiles vs assigned extensions)
- Group, queue, flow, DID, and inactive-user audits
- Optional audit logs, operational events, and outbound events
- Excel report generation (`.xlsx`)
- Windows scheduled task integration through the desktop app
- Optional SharePoint upload in runner mode

## Configuration Summary

Primary settings live in:

- `src/GenesysExtensionAudit.App/appsettings.json`
- `src/GenesysExtensionAudit.Runner/appsettings.json`

Key sections:

- `Genesys` (region, paging, throttling)
- `GenesysOAuth` (client credentials)
- `Audit` (enabled audit paths and thresholds)
- `Export` (output directory and file prefix)
- `SharePoint` (runner upload target)
- `Scheduling` (desktop task registration behavior)

Do not commit credentials.

## Outputs

- Interactive app prompts to save a workbook after completion.
- Runner writes workbook to configured output directory.
- Workbook contains summary plus per-audit worksheets (only for selected audit paths).

## Troubleshooting Entry Points

- Setup and permissions: [setup guide](docs/setup-and-operations.md)
- OAuth and retry behavior: [OAuth guide](docs/oauth-and-api-resilience.md)
- Test coverage and failure triage: [QA.md](QA.md)

## Status Notes

Some historical documents in `docs/` originated from scaffold/planning output and have been normalized. Open verification items are tracked in [NOTES.md](NOTES.md).

