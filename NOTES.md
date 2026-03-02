This scaffold was generated from orchestrator artifacts. See /docs for additional design notes and patch snippets.

Here is a prompt for Codex to make it into a buildable .NET 8 WFT solution:

You are working in a repo generated from GenesysExtensionAudit_scaffold.zip.

GOAL
Turn this scaffold into a buildable .NET 8 WPF solution with clean layering and consistent namespaces.

REQUIRED OUTCOME
- A real Visual Studio solution: GenesysExtensionAudit.sln
- Projects:
  1) src/GenesysExtensionAudit.App (WPF net8.0-windows)
  2) src/GenesysExtensionAudit.Domain (class library)
  3) src/GenesysExtensionAudit.Infrastructure (class library)
  4) tests/GenesysExtensionAudit.Tests (xUnit)
- App references Domain + Infrastructure
- Infrastructure references Domain
- Tests reference Domain + Infrastructure

STRUCTURE WORK
- Move/keep AuditEngine.cs under Domain and set its namespace to GenesysExtensionAudit.Domain (or GenesysExtensionAudit.Domain.Services).
- Replace any namespace "GenesysCloudExtensionAudit" with "GenesysExtensionAudit.Domain" (or the correct layer) consistently.
- Ensure DTOs + API services live in Infrastructure with namespace GenesysExtensionAudit.Infrastructure.*.
- Ensure WPF ViewModels live in App with namespace GenesysExtensionAudit.App.ViewModels (or GenesysExtensionAudit.ViewModels, but be consistent).

DEPENDENCIES
- Add required NuGet packages where needed:
  - App: CommunityToolkit.Mvvm, Microsoft.Extensions.Hosting, Microsoft.Extensions.Http, Microsoft.Extensions.Configuration.Json, logging
  - Infrastructure: Serilog + Serilog.Sinks.File (and whatever your Logging.cs requires), plus Options/Http packages
  - Tests: xunit, xunit.runner.visualstudio, Microsoft.NET.Test.Sdk
- Make sure all package references match the code usage.

APP WIRING
- Implement DI bootstrapping in App.xaml.cs (HostBuilder)
- Register:
  - HttpClientFactory + typed client(s)
  - UsersService, ExtensionsService
  - PagingOrchestrator options
  - Logging (Serilog)
  - ViewModels
- Ensure long-running calls run async without freezing UI:
  - Use async commands
  - CancellationToken support
  - Progress reporting bound to UI

CONFIG
- appsettings.json should load and bind options objects (OAuth clientId/clientSecret, paging settings, retry/backoff settings).
- Support user-secrets for OAuth client secret.

BUILD QUALITY
- Fix all compile errors
- Add minimal missing types referenced by XAML bindings (e.g., record types for DataGrid rows) as needed.
- Ensure solution builds and tests run.

DELIVERABLES
- Commit-ready code changes only. No pseudocode.
- Provide a short “How to run” in README.md: configure secrets, build, run, export.

