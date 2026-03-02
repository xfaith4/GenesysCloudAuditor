# Genesys Extension Audit — WPF Desktop App (.NET 8)

Audits **Genesys Cloud** extension data by cross-referencing user profile *Work Phone extension* fields against the Edge telephony assignment list. Surfaces duplicates, orphaned profile values, and unassigned extensions in a navigable WPF UI with CSV export.

---

## Architecture

```text
┌────────────────────────────────────────────────────────────────────┐
│  GenesysExtensionAudit.App  (WPF, net8.0-windows)                 │
│  ├─ App.xaml / Bootstrapper.cs  (DI wiring, host lifecycle)       │
│  ├─ MainWindow.xaml             (shell: TabControl navigation)     │
│  ├─ Views/RunAuditView.xaml     (Start/Cancel, progress, results)  │
│  └─ ViewModels/                                                    │
│     ├─ MainViewModel            (shell, navigation)                │
│     └─ AuditRunViewModel        (run controls, progress, errors)   │
├────────────────────────────────────────────────────────────────────┤
│  GenesysExtensionAudit.Core  (class library, net8.0)              │
│  ├─ Application/                                                   │
│  │  ├─ IAuditRunner / AuditOptions / AuditProgress / AuditResult  │
│  ├─ Domain/Services/                                               │
│  │  ├─ ExtensionNormalization   (normalize/validate ext strings)   │
│  │  ├─ IExtensionNormalizer / ExtensionNormalizer                  │
│  │  ├─ IAuditAnalyzer / AuditAnalyzer  (cross-reference logic)    │
│  ├─ Domain/Models/                                                 │
│  │  └─ UserProfileExtensionRecord, AssignedExtensionRecord,        │
│  │     AuditFindings (DuplicateProfileExtension, etc.)            │
│  └─ Domain/Paging/  IPaginator, PagedResult<T>                    │
├────────────────────────────────────────────────────────────────────┤
│  GenesysExtensionAudit.Infrastructure  (class library, net8.0)    │
│  ├─ Application/AuditRunner    (orchestrates fetch + analyze)      │
│  ├─ Domain/Services/           ExtensionNormalizer, AuditAnalyzer  │
│  ├─ Http/                                                          │
│  │  ├─ GenesysRegionOptions    (Genesys:Region, PageSize, etc.)   │
│  │  ├─ ITokenProvider / TokenProvider   (OAuth client-creds)      │
│  │  ├─ OAuthBearerHandler      (attaches Bearer token)            │
│  │  ├─ HttpLoggingHandler      (request/response telemetry)       │
│  │  └─ RateLimitHandler        (token-bucket throttle)            │
│  ├─ Genesys/Clients/           IGenesysUsersClient (+ impl)        │
│  │                             IGenesysExtensionsClient (+ impl)   │
│  ├─ Genesys/Pagination/        Paginator (sequential page fetch)   │
│  ├─ Genesys/Dtos/              UserDto, ExtensionDto, page wrappers │
│  ├─ Logging/                   Serilog config, redaction utils     │
│  └─ Reporting/ExportService    (CSV export, Excel-friendly)        │
└────────────────────────────────────────────────────────────────────┘

Genesys Cloud API endpoints consumed
  Users:       GET /api/v2/users?pageSize={n}&pageNumber={p}[&state=active]
  Extensions:  GET /api/v2/telephony/providers/edges/extensions?pageSize={n}&pageNumber={p}
```

**Project dependency graph:**

```text
App  ──► Core
App  ──► Infrastructure
Infrastructure ──► Core
(Tests reference Core + Infrastructure)
```

---

## Implementation Status

| Component | Status | Notes |
| --- | --- | --- |
| `AuditEngine` (cross-reference logic) | ✅ Complete | `Domain/Services/AuditEngine.cs` — all 6 finding types |
| `ExportService` (CSV export) | ✅ Complete | All 7 CSV files, BOM, proper quoting |
| `PagingOrchestrator` | ✅ Complete | Bounded concurrency, single-flight, TTL cache, retries |
| `GenesysCloudApiClient` | ✅ Complete | Retry/backoff, 401 refresh, 429 Retry-After |
| `GenesysUsersClient` / `GenesysExtensionsClient` | ✅ Complete | Paged fetch with state filter |
| `Logging.cs` (Serilog + correlation) | ✅ Complete | Rolling file, header redaction |
| `AuditRunViewModel` | ✅ Complete | Async commands, progress, cancellation |
| `MainViewModel` / `NavigationService` | ✅ Complete | TabControl shell |
| `ResultsViews.xaml` | ✅ Complete | Expander/DataGrid hierarchy per finding type |
| `GenesysExtensionAudit.Core.csproj` | ✅ Created | Domain + Application layer |
| `GenesysExtensionAudit.Infrastructure.csproj` | ✅ Created | Infrastructure layer |
| `GenesysExtensionAudit.Infrastructure.Tests.csproj` | ✅ Created | xUnit integration tests |
| `IAuditRunner` + `AuditRunner` | ✅ Created | Wires fetch → normalize → analyze |
| `ExtensionNormalization` types | ✅ Created | Status enum + normalization result |
| `IExtensionNormalizer` / `ExtensionNormalizer` | ✅ Created | Digits-only normalization |
| `IAuditAnalyzer` / `AuditAnalyzer` | ✅ Created | Delegates to AuditEngine |
| `IPaginator` / `Paginator` | ✅ Created | Sequential page loop |
| `GenesysRegionOptions` | ✅ Created | Binds `Genesys:*` config section |
| `ITokenProvider` / `TokenProvider` | ✅ Created | OAuth client-credentials (stub) |
| `OAuthBearerHandler` | ✅ Created | Bearer token injection |
| `HttpLoggingHandler` | ✅ Created | Timing + status logging |
| `RateLimitHandler` | ✅ Created | Token-bucket rate limiter |
| `RunAuditView.xaml` | ✅ Created | Inputs, progress bar, results tabs |
| `Directory.Build.props` | ✅ Created | Shared C# language settings |
| `.gitignore` | ✅ Created | Standard .NET ignores |
| **OAuth `TokenProvider` (full impl)** | ⚠️ Stub | Needs real `client_credentials` HTTP call |
| **DTO → Domain mapping in AuditRunner** | ⚠️ Stub | `UserDto → UserProfileExtensionRecord` mapping needed |
| **`AuditAnalyzer` (full impl)** | ⚠️ Stub | Currently delegates to `AuditEngine`; wire mapping |
| **Results display in RunAuditView** | ⚠️ Partial | View exists; ViewModel needs `AuditReport` binding |
| **Export button / folder picker** | ⚠️ Missing | `ExportService` ready; UI trigger not wired |
| **Serilog wired in Bootstrapper** | ⚠️ Missing | `Logging.ConfigureSerilog()` not called in `App.xaml.cs` |
| **`Retry-After` header parsing in client** | ⚠️ Partial | Parses body int; true header parsing needs exception enrichment |

---

## Table of Contents

- [Prerequisites](#prerequisites)
- [Genesys Cloud OAuth setup](#genesys-cloud-oauth-setup)
- [Required permissions (OAuth scopes)](#required-genesys-cloud-permissions-oauth-scopes)
- [Configuration](#configuration)
- [Build and run](#build-and-run)
- [Running an audit](#running-an-audit)
- [Interpreting the reports](#interpreting-the-reports)
- [Exporting results to CSV](#exporting-results-to-csv)
- [Troubleshooting](#troubleshooting)
- [Developer guide](#developer-guide)
- [Notes and limitations](#notes-and-limitations)

---

## Prerequisites

- Windows 10/11
- .NET SDK **8.x** (for building from source)
- A Genesys Cloud org with an OAuth client (Client Credentials type)

---

## Genesys Cloud OAuth setup

1. In Genesys Cloud Admin → Integrations → OAuth, create an OAuth client:
   - **Grant Type:** Client Credentials
2. Record the **Client ID** and **Client Secret**.
3. Assign the required permissions to the OAuth client's roles (see below).

> The app uses **Client Credentials** flow. No user login required.

---

## Required Genesys Cloud permissions (OAuth scopes)

| Permission | Endpoint |
| --- | --- |
| `user:view` (or read users) | `GET /api/v2/users` |
| `telephony:plugin:all` or read edge extensions | `GET /api/v2/telephony/providers/edges/extensions` |

Exact permission names depend on org setup and Genesys UI wording. If you receive **403 Forbidden**, verify role assignments on the OAuth client.

---

## Configuration

### appsettings.json

Located at `src/GenesysExtensionAudit.App/appsettings.json`:

```json
{
  "Genesys": {
    "Region": "mypurecloud.com",
    "PageSize": 100,
    "IncludeInactive": false,
    "MaxRequestsPerSecond": 3
  },
  "GenesysOAuth": {
    "ClientId": "",
    "ClientSecret": ""
  }
}
```

| Setting | Description |
| --- | --- |
| `Genesys:Region` | Your org's API domain — e.g. `mypurecloud.com`, `usw2.pure.cloud`, `euw2.pure.cloud` |
| `Genesys:PageSize` | Records per page (1–500). Genesys caps vary by endpoint; 100 is safe. |
| `Genesys:IncludeInactive` | `false` → `&state=active` filter applied. `true` → all users (including inactive). |
| `Genesys:MaxRequestsPerSecond` | Throttle to avoid 429 rate-limit errors. |
| `GenesysOAuth:ClientId` | OAuth client ID (or use user-secrets). |
| `GenesysOAuth:ClientSecret` | OAuth client secret (or use user-secrets). |

### Secrets for local development (user-secrets)

**Never commit credentials.** Use .NET user-secrets:

```powershell
cd src\GenesysExtensionAudit.App
dotnet user-secrets set "GenesysOAuth:ClientId"     "YOUR_CLIENT_ID"
dotnet user-secrets set "GenesysOAuth:ClientSecret" "YOUR_CLIENT_SECRET"
```

User-secrets are loaded automatically when `DOTNET_ENVIRONMENT=Development`.

### Environment variables (CI/packaging)

Override any config key using `__` as the section separator:

```powershell
setx GenesysOAuth__ClientId     "YOUR_CLIENT_ID"
setx GenesysOAuth__ClientSecret "YOUR_CLIENT_SECRET"
setx Genesys__Region            "mypurecloud.com"
```

---

## Build and run

```powershell
# From repo root
dotnet restore
dotnet build -c Release
dotnet run --project src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj
```

To run tests:

```powershell
dotnet test
```

---

## Running an audit

1. Launch the application.
2. Verify settings in the **Run Audit** tab:
   - **Region** is correct for your org
   - **IncludeInactive** is set as desired
   - **PageSize** is appropriate (100 is safe; increase for faster large-tenant runs)
3. Click **Start** to begin the audit.
4. Monitor:
   - **Status** bar (current phase, page counts)
   - **Progress** bar (0–100%)
5. Click **Cancel** to abort mid-run (safe — no partial export is written).

**What is fetched (in parallel where possible):**

| Data | Endpoint | State filter |
| --- | --- | --- |
| Users | `/api/v2/users?pageSize={n}&pageNumber={p}` | `&state=active` when `IncludeInactive=false` |
| Edge extensions | `/api/v2/telephony/providers/edges/extensions?pageSize={n}&pageNumber={p}` | None |

---

## Interpreting the reports

### Summary

Quick health check totals:

| Metric | Healthy baseline |
| --- | --- |
| `DuplicateProfileExtensions` | 0 — any > 0 means multiple users share an extension value |
| `ProfileExtensionsNotAssigned` | Low — extensions set on profiles that don't exist in telephony |
| `DuplicateAssignedExtensions` | 0 — same extension assigned more than once at telephony layer |
| `AssignedExtensionsMissingFromProfiles` | Informational — assigned extensions with no matching user profile |
| `InvalidProfileExtensions` | 0 — malformed/non-numeric extension values on user profiles |

### Duplicates By Profile (Work Phone Extension)

**What:** Multiple users have the same value in their Work Phone extension field.

**Why it matters:** Duplicate values cause call routing ambiguity, failed provisioning, and inaccurate reporting.

**How to fix:**

1. Identify which user legitimately owns the extension.
2. Clear or correct the extension field on the other users.
3. Re-run the audit to confirm zero findings.

### Extensions On Profiles But Not Assigned

**What:** A user's profile extension value has no corresponding entry in the Edge extension assignment list.

**Common causes:**

- Profile field manually edited after telephony deprovisioning
- Extension deleted/recycled in telephony but not cleared on the profile
- Org uses a telephony model that doesn't fully reflect in the Edge extensions endpoint

**How to fix:**

- If extension should exist: recreate it in Genesys telephony.
- If extension is stale: clear the user's Work Phone extension field.

### Other exported sections

| Section | Meaning |
| --- | --- |
| `DuplicateAssignedExtensions` | Same extension key appears on multiple telephony assignments |
| `AssignedExtensionsMissingFromProfiles` | Assigned extension has no corresponding user profile value (optional) |
| `InvalidProfileExtensions` | Non-numeric or whitespace-only values on user profiles |
| `InvalidAssignedExtensions` | Malformed extension values in telephony assignments |

---

## Exporting results to CSV

After a completed audit, click **Export** (or call `ExportService.ExportAll(report, options)` in code).

Output files (one per section, plus Summary):

| File | Columns |
| --- | --- |
| `{prefix}_Summary.csv` | Key, Value |
| `{prefix}_DuplicateProfileExtensions.csv` | ExtensionKey, UserName, UserId, State, ExtensionRaw |
| `{prefix}_ProfileExtensionsNotAssigned.csv` | ExtensionKey, UserName, UserId, State, ExtensionRaw |
| `{prefix}_DuplicateAssignedExtensions.csv` | ExtensionKey, AssignmentId, ExtensionRaw, TargetType, TargetId |
| `{prefix}_AssignedExtensionsMissingFromProfiles.csv` | ExtensionKey, AssignmentId, ExtensionRaw, TargetType, TargetId |
| `{prefix}_InvalidProfileExtensions.csv` | UserName, UserId, State, ExtensionRaw, Status, Notes |
| `{prefix}_InvalidAssignedExtensions.csv` | AssignmentId, ExtensionRaw, Status, Notes |

**Excel tips:**

- Files are written with **UTF-8 BOM** for seamless Excel opening.
- Fields containing commas, quotes, or newlines are RFC-4180 quoted.
- If columns appear shifted: use Data → From Text/CSV with delimiter = comma.

---

## Troubleshooting

### 401 Unauthorized / 403 Forbidden

| Check | Action |
| --- | --- |
| Client ID/Secret correct | Verify in appsettings or user-secrets (no trailing whitespace) |
| Region matches org | `Genesys:Region` must match your org's API domain |
| OAuth grant type | Must be **Client Credentials** |
| Role permissions | Ensure the OAuth client's roles include user-read and telephony-read |

The client will automatically retry a 401 once after forcing a token refresh.

### 429 Too Many Requests

- Lower `Genesys:MaxRequestsPerSecond` (try 1 or 2)
- The app respects `Retry-After` on 429 responses and retries with exponential backoff (max 6 attempts, cap 30s)
- Avoid running during peak admin/provisioning windows

### Audit is slow / large tenants

- Increase `Genesys:PageSize` (up to API cap, typically 200–500 for users)
- The `PagingOrchestrator` uses bounded-parallel page fetching; tune `MaxParallelRequests` in options
- Run during off-hours

### No results / missing users

- `IncludeInactive=false` excludes inactive users — set `true` to include them
- Users with blank Work Phone extension fields never appear in profile-based findings
- Verify the OAuth client has read access to all users (some orgs have division-scoped permissions)

### CSV opens incorrectly in Excel

- Use **Data → Get Data → From Text/CSV** (not double-click) if your locale uses semicolons
- Verify the exporter writes UTF-8 BOM (`IncludeUtf8Bom=true` in `ExportOptions`)
- Names/fields with embedded commas must be RFC-4180 quoted — open an issue with a sanitized sample if rows appear shifted

### TLS/Proxy/Firewall issues

- Verify outbound HTTPS to `api.{Region}` and `login.{Region}` from the workstation
- .NET respects system proxy settings (IE/WinHTTP proxy); no extra config needed in most environments
- Check firewall rules allow outbound 443

---

## Developer Guide

### Completing the stubs

The following areas require implementation before the app is fully functional:

#### 1. OAuth `TokenProvider` (client credentials flow)

`src/GenesysExtensionAudit.Infrastructure/Http/TokenProvider.cs` — replace the stub with a real HTTP call:

```
POST https://login.{Region}/oauth/token
Content-Type: application/x-www-form-urlencoded
Authorization: Basic base64(clientId:clientSecret)

grant_type=client_credentials
```

Cache the token until `expires_in - 60` seconds. Implement `ForceRefreshAsync` to clear cache.

#### 2. DTO → Domain mapping in `AuditRunner`

`src/GenesysExtensionAudit.Infrastructure/Application/AuditRunner.cs` — map fetched DTOs to domain records:

```csharp
// UserDto → UserProfileExtensionRecord
// ExtensionDto → AssignedExtensionRecord
// Then call: _analyzer.Analyze(userRecords, extensionRecords)
```

The `DtosExtensions.GetWorkPhoneExtension()` helper in `Infrastructure/Genesys/Dtos/` handles the `primaryContactInfo` extraction.

#### 3. Wire `AuditReport` into `RunAuditView` / ViewModel

`AuditRunViewModel` currently only tracks progress. After `RunAsync` completes, it should expose `AuditReport` (or `AuditFindings`) so `RunAuditView.xaml` can render results tabs.

#### 4. Export button in `RunAuditView`

Add a **Export…** button that calls `ExportService.ExportAll(report, new ExportOptions { OutputDirectory = ... })` after the audit completes.

#### 5. Wire Serilog in `Bootstrapper`

Call `Logging.ConfigureSerilog(hostBuilder)` before `.Build()` to enable rolling file logs. Add `Logging.cs` options to `appsettings.json`:

```json
"Logging": {
  "MinimumLevel": "Information",
  "EnableFile": true,
  "LogDirectory": "logs"
}
```

### Extension normalization rules

`ExtensionNormalization.Normalize()` applies this pipeline (configured via `ExtensionNormalizationOptions`):

1. **Trim** whitespace
2. **Strip non-digits** (keeps leading zeros by default)
3. **Validate** result is non-empty and within digit-length bounds
4. Returns `ExtensionNormalizationResult` with `IsOk`, `Normalized`, `Status`, `Notes`

Status values: `Ok`, `Empty`, `WhitespaceOnly`, `NonDigitOnly`, `TooShort`, `TooLong`

### Running tests

```powershell
dotnet test tests\GenesysExtensionAudit.Infrastructure.Tests\
```

The test suite covers:

- Users pagination with `state=active` / without
- Extensions pagination across multiple pages
- Mock handler that verifies exact call counts and URLs

See `QA.md` for the full end-to-end QA test matrix (pagination, rate-limit, cancellation, export, UI).

---

## Notes and limitations

- The audit uses the **Work Phone extension field** (`primaryContactInfo` with type=work + mediaType=PHONE) as the profile source of truth. Orgs that store extensions differently (custom fields, division-scoped schemas) may need adapter logic in `DtosExtensions.GetWorkPhoneExtension()`.
- Genesys tenant configurations vary significantly. Treat findings as audit aids; validate against your telephony provisioning model before bulk changes.
- The `AssignedExtensionsMissingFromProfiles` report is disabled by default (`ComputeAssignedButMissingFromProfiles = false`) as it generates high volume for orgs with many unassigned extension slots.
- `ResultsViews.xaml` uses `ItemsControl` + `DataGrid` per finding — for tenants with thousands of findings, enable UI virtualization or add paging in the view.

---

## License

Add your license here (MIT/Apache-2.0/etc.), or remove this section if not applicable.
