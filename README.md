# Genesys Cloud Auditor

A Windows desktop application (.NET 8 / WPF) that audits **Genesys Cloud** tenant configuration for telephony anomalies, platform synchronization bugs, and data quality issues. Findings are presented in a tabbed UI and exported to a multi-sheet Excel workbook.

---

## What It Audits

| Check | Sheet | Severity |
|---|---|---|
| Duplicate Work Phone extensions across user profiles | `Ext_Duplicates_Profile` | 🔴 Critical |
| Extension claimed by a profile but assigned to a different entity in telephony | `Ext_Ownership_Mismatch` | 🔴 Critical |
| Extensions present in only one of profile or telephony assignment | `Ext_Assign_vs_Profile` | 🟡 Warning |
| Malformed or non-numeric extension values | `Invalid_Extensions` | 🟡 Warning |
| Groups with zero or one member | `Empty_Groups` | 🟡 Warning |
| Queues with no agents or duplicate names | `Empty_Queues` | 🟡 Warning |
| Architect flows not republished within a configurable threshold | `Stale_Flows` | 🟡 Warning |
| Inactive or deactivated user accounts | `Inactive_Users` | 🟡 Warning |
| Unassigned, orphaned, or misassigned DIDs | `DID_Mismatches` | 🟡 Warning |
| Genesys Cloud audit log exports | `Audit_Logs` | ℹ️ Info |
| Operational and outbound event log exports | `Operational_Events` / `Outbound_Events` | ℹ️ Info |

---

## Quick Start

### Prerequisites

- Windows 10/11 (64-bit)
- .NET SDK 8.x
- A Genesys Cloud OAuth client (Client Credentials grant type) with user-read and telephony-read permissions

### 1. Configure credentials

```powershell
cd src\GenesysExtensionAudit.App
dotnet user-secrets set "GenesysOAuth:ClientId"     "YOUR_CLIENT_ID"
dotnet user-secrets set "GenesysOAuth:ClientSecret" "YOUR_CLIENT_SECRET"
```

Edit `src\GenesysExtensionAudit.App\appsettings.json` to set your org's region:

```json
{
  "Genesys": {
    "Region": "mypurecloud.com",
    "PageSize": 100,
    "IncludeInactive": false,
    "MaxRequestsPerSecond": 3
  }
}
```

### 2. Build and run

```powershell
dotnet restore
dotnet build -c Release
dotnet run --project src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj
```

### 3. Run an audit

1. Launch the application.
2. Verify settings in the **Run Audit** tab (region, inactive toggle, page size).
3. Click **Start**. Monitor progress in the status bar and progress indicator.
4. After completion, click **Export Last Report...** to save the `.xlsx` workbook.

---

## Documentation

| Document | Description |
|---|---|
| [docs/architecture.md](docs/architecture.md) | Solution structure, MVVM pattern, DI, HTTP pipeline |
| [docs/authentication.md](docs/authentication.md) | OAuth setup, token management, required permissions |
| [docs/audit-checks.md](docs/audit-checks.md) | All audit types, data model, normalization pipeline |
| [docs/deployment.md](docs/deployment.md) | Build, packaging, CI/CD, versioning |
| [docs/qa-test-plan.md](docs/qa-test-plan.md) | QA test matrix and acceptance criteria |
| [docs/examples/](docs/examples/README.md) | Example export output for each report sheet |
| [ROADMAP.md](ROADMAP.md) | Planned future audit checks |

---

## Troubleshooting

| Symptom | Action |
|---|---|
| **401 Unauthorized** | Verify Client ID/Secret; confirm `Genesys:Region` matches your org |
| **403 Forbidden** | Check that the OAuth client's roles include user-read and telephony-read |
| **429 Too Many Requests** | Lower `Genesys:MaxRequestsPerSecond`; the app retries automatically with `Retry-After` backoff |
| **Audit slow on large tenants** | Increase `Genesys:PageSize` (up to API cap, typically 200–500); run during off-hours |
| **Missing users** | Set `IncludeInactive=true` if inactive users should be included; verify OAuth client is not division-scoped |
| **TLS/proxy errors** | Verify outbound HTTPS to `api.{Region}` and `login.{Region}` on port 443 |

---

## Running Tests

```powershell
dotnet test tests\GenesysExtensionAudit.Infrastructure.Tests\
```

---

## Scheduling Headless Runs

Use the **Schedule Audits** tab to register Windows Scheduled Tasks that run the audit without the UI. The task invokes `GenesysExtensionAudit.Runner.exe` with a schedule profile JSON. See [docs/deployment.md](docs/deployment.md) for details.

---

## License

Add your license here (MIT / Apache-2.0 / etc.), or remove this section if not applicable.
