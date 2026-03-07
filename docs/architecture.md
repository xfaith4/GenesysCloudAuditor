# Architecture

## Overview

GenesysCloudAuditor is a WPF desktop application built on .NET 8 that audits Genesys Cloud tenants for configuration anomalies, telephony synchronization issues, and data quality problems. It uses the **Model-View-ViewModel (MVVM)** pattern with Microsoft's Generic Host for dependency injection and lifecycle management.

---

## Solution Structure

```
GenesysExtensionAudit.sln
│
├─ src/
│  ├─ GenesysExtensionAudit.App          (WPF UI, net8.0-windows)
│  ├─ GenesysExtensionAudit.Core         (Domain models and interfaces)
│  ├─ GenesysExtensionAudit.Infrastructure  (HTTP, OAuth, API clients, export)
│  ├─ GenesysExtensionAudit.Domain       (Audit engine and orchestration)
│  └─ GenesysExtensionAudit.Runner       (Headless CLI runner for scheduled tasks)
│
└─ tests/
   └─ GenesysExtensionAudit.Infrastructure.Tests
```

**Dependency graph:**

```
App  ──► Core
App  ──► Infrastructure
Infrastructure ──► Core
Runner ──► Infrastructure ──► Core
Tests  ──► Core + Infrastructure
```

---

## Layer Responsibilities

### App (WPF UI)

| Component | Responsibility |
|---|---|
| `App.xaml` / `Bootstrapper.cs` | Builds the Generic Host, registers all services, starts the application |
| `MainWindow.xaml` | Shell window with TabControl navigation |
| `Views/RunAuditView.xaml` | Start/Cancel controls, live progress, paginated results tabs |
| `Views/ScheduleAuditView.xaml` | Schedule creation UI |
| `ViewModels/MainViewModel` | Shell navigation state |
| `ViewModels/AuditRunViewModel` | Audit lifecycle: run controls, progress updates, results binding, export |

### Core (Domain Contracts)

| Component | Responsibility |
|---|---|
| `IAuditRunner` | Contract for running an audit asynchronously with progress and cancellation |
| `AuditOptions` | Runtime parameters (region, page size, include inactive, selected audit paths) |
| `AuditProgress` | Progress notifications (phase, percent, message) |
| `AuditResult` | Completed audit output (findings, summary, run metadata) |
| `UserProfileExtensionRecord` | Domain model: user + their profile extension data |
| `AssignedExtensionRecord` | Domain model: telephony assignment record |
| `AuditFindings` | Aggregate findings container with all finding types |
| `ExtensionNormalizationOptions` | Configuration for the normalization pipeline |
| `IExtensionNormalizer` | Contract for normalizing raw extension strings |
| `IAuditAnalyzer` | Contract for cross-referencing users and assignments |
| `IPaginator<T>` / `PagedResult<T>` | Pagination abstractions |

### Infrastructure

| Component | Responsibility |
|---|---|
| `TokenProvider` | OAuth Client Credentials flow with cached token refresh |
| `OAuthBearerHandler` | Attaches the Bearer token to every outbound HTTP request |
| `RateLimitHandler` | Token-bucket throttle to respect `MaxRequestsPerSecond` |
| `HttpLoggingHandler` | Request/response telemetry (sanitizes secrets) |
| `GenesysUsersClient` | Paginated `GET /api/v2/users` |
| `GenesysExtensionsClient` | Paginated `GET /api/v2/telephony/providers/edges/extensions` |
| `Paginator` | Sequential multi-page fetching with retry and backoff |
| `AuditRunner` | Orchestrates data fetch → normalize → analyze → result |
| `ExtensionNormalizer` | Normalization pipeline implementation |
| `AuditAnalyzer` | Cross-reference logic producing structured findings |
| `CsvReportWriter` | Excel-friendly UTF-8 BOM CSV export |
| `ExportService` | Multi-sheet workbook export coordinator |

### Domain (Audit Engine)

The domain layer contains the pure business logic for audit orchestration and analysis. It has no dependency on WPF or HTTP infrastructure, enabling unit testing in isolation.

---

## WPF MVVM Pattern

The application follows a strict MVVM pattern:

- **Views** contain only XAML layout and minimal code-behind. All behavior is driven by ViewModel bindings.
- **ViewModels** use `INotifyPropertyChanged` (via `CommunityToolkit.Mvvm`) and `ICommand` (async relay commands) to expose state and actions.
- **Models** are plain records and DTOs; they are never directly referenced by Views.

### Async UI and Cancellation

Long-running audit operations execute on background threads. Progress is marshalled to the UI thread via `IProgress<AuditProgress>`. Every audit run is associated with a `CancellationTokenSource` so users can cancel at any point.

```
UI Thread                  Background Thread
─────────                  ─────────────────
Click "Start"
  → AuditRunViewModel
     → Create CTS
     → Task.Run(AuditRunner.RunAsync(ct))
                             ← IProgress<AuditProgress>
     ← Update ProgressPercent, StatusMessage
     ← Render Results on completion
Click "Cancel"
  → CTS.Cancel()
```

---

## Dependency Injection

The Generic Host (`Microsoft.Extensions.Hosting`) wires all services at startup:

```csharp
Host.CreateDefaultBuilder()
    .ConfigureServices(services =>
    {
        services.AddHttpClient("GenesysApi")
                .AddHttpMessageHandler<OAuthBearerHandler>()
                .AddHttpMessageHandler<RateLimitHandler>()
                .AddHttpMessageHandler<HttpLoggingHandler>();

        services.AddSingleton<ITokenProvider, TokenProvider>();
        services.AddSingleton<IGenesysUsersClient, GenesysUsersClient>();
        services.AddSingleton<IGenesysExtensionsClient, GenesysExtensionsClient>();
        services.AddSingleton<IAuditRunner, AuditRunner>();
        services.AddTransient<AuditRunViewModel>();
        // ...
    });
```

`appsettings.json` binds into strongly-typed options objects (`GenesysRegionOptions`, `ExtensionNormalizationOptions`, `OAuthOptions`) via `IOptions<T>`.

---

## HTTP Resilience Pipeline

Each outbound API call passes through the following handler chain:

```
Application → OAuthBearerHandler → RateLimitHandler → HttpLoggingHandler → HttpClient
```

| Handler | Behavior |
|---|---|
| `OAuthBearerHandler` | Fetches/caches OAuth token; retries once on 401 after force-refresh |
| `RateLimitHandler` | Token-bucket throttle (configurable `MaxRequestsPerSecond`) |
| `HttpLoggingHandler` | Logs request/response with secret redaction |

In addition, `Paginator` implements retry with exponential backoff for 429 and 5xx responses, honoring the `Retry-After` response header.

---

## Scheduled Headless Runs

The `GenesysExtensionAudit.Runner` project exposes a CLI entry point consumed by Windows Scheduled Tasks:

```
GenesysExtensionAudit.Runner.exe --schedule-profile "<profile-path>"
```

The GUI writes a schedule profile JSON to disk and registers the task under `\GenesysExtensionAudit\` using the Windows Task Scheduler API. The runner loads the profile, runs the audit, writes the export, and exits — no UI required.

---

## See Also

- [Authentication](authentication.md) — OAuth configuration and token management
- [Deployment](deployment.md) — Build, packaging, and CI/CD pipeline
- [Audit Checks](audit-checks.md) — All audit types, data model, and normalization
