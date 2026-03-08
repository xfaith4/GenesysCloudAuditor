# Setup and Operations Guide

This runbook covers deployment prerequisites, configuration, execution, and report interpretation.

Audience: operators and administrators.

Use this guide when setting up a new environment or running production audits.

## 1. Prerequisites

- Windows 10/11
- .NET 8 SDK (source build scenarios)
- Genesys Cloud OAuth client credentials
- Access to required Genesys API permissions

## 2. Configuration

Primary config files:

- `src/GenesysExtensionAudit.App/appsettings.json`
- `src/GenesysExtensionAudit.Runner/appsettings.json`

Key sections:

- `Genesys`: region, page size, throttle
- `GenesysOAuth`: client ID and secret
- `Audit`: enabled paths and thresholds
- `Export`: output folder and file prefix
- `SharePoint`: optional upload target for runner

Security requirement:

- Keep `ClientSecret` out of source control.
- Prefer environment-variable injection for non-local environments.

## 3. OAuth and Permissions

The application uses client credentials.

At minimum, the configured principal must read:

- users
- extensions

Additional selected audit paths require corresponding read access.

If you receive `403`, validate role mapping in the target tenant.

## 4. Running Audits

### Desktop application

```powershell
dotnet run --project src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj
```

Flow:

1. Confirm configuration.
2. Select audit paths.
3. Start run and monitor progress.
4. Save generated workbook.

### Headless runner

```powershell
dotnet run --project src\GenesysExtensionAudit.Runner\GenesysExtensionAudit.Runner.csproj -- --dry-run
```

Optional scheduled profile:

```powershell
GenesysExtensionAudit.Runner.exe --schedule-profile <path-to-profile.json>
```

## 5. Interpreting Results

The workbook summary provides high-level counts by audit path.

Common result groups:

- extension profile duplicates
- profile extensions not in assignment pool
- duplicate assigned extensions
- groups and queues hygiene findings
- stale flow findings
- inactive user or missing location findings
- DID mismatches
- optional audit/operational/outbound event sections

Treat findings as operational evidence, then validate against tenant-specific design before bulk remediation.

## 6. Troubleshooting

| Symptom | Typical cause | Action |
| --- | --- | --- |
| `401` | wrong credentials or token failure | verify OAuth settings and region |
| `403` | missing permissions | update role/scope grants |
| `429` | rate limit | reduce request rate and retry |
| empty findings unexpectedly | audit path disabled or filter mismatch | verify selected paths and `IncludeInactive` |
| export failure | write permissions or path issue | use writable destination |

## 7. Related Documents

- [README](../README.md)
- [OAuth design](oauth-and-api-resilience.md)
- [QA matrix](detailed-qa-matrix.md)

