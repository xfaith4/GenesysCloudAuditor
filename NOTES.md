# Documentation Notes and Verification Queue

This file tracks documentation claims that need human verification or periodic review.

Audience: maintainers and release owners.

Use this file before publishing external docs or customer-facing release notes.

## Current Verification Items

| Item | Why verification is required | Source |
| --- | --- | --- |
| Exact Genesys permission labels in each tenant | Permission names vary by Genesys org and UI terminology | [setup guide](docs/setup-and-operations.md) |
| SharePoint Graph permission readiness (`Sites.ReadWrite.All`) | Depends on Azure app registration and tenant consent | [release guide](docs/release-packaging-and-signing.md) |
| Scheduler behavior under restricted local security policy | Windows Task Scheduler policies can block task creation or execution | [UI workflow guide](docs/run-audit-workflow.md) |
| Performance expectations for very large tenants | Tenant size and API limits vary; benchmark data is environment-specific | [QA matrix](docs/detailed-qa-matrix.md) |

## Documentation Debt

- Historical filenames in `docs/` are intentionally preserved for traceability but should be renamed in a future cleanup pass.
- Add screenshots for `Run Audit`, `Schedule Audits`, and report output once UI stabilizes.
- Add a contributor documentation standard (`docs/style-guide.md`) if the repository grows.

## Privacy Guardrails

- Documentation and tests must use synthetic, non-personal placeholders only.
- Use `example.invalid` for email examples and reserved `555`-range numbers for PSTN examples.
- Do not commit customer names, tenant-specific identifiers, PHI, PII, live API tokens, or real secrets.
- If a sample needs a human-readable actor, prefer role-based labels such as `Inactive User 01` or `Profile Owner 01`.

## Review Cadence

- Review this file before each tagged release.
- Close or refresh stale verification items every 90 days.

---

## Quick Reference

- **Build:** `dotnet build -c Release`
- **Test:** `dotnet test tests\GenesysExtensionAudit.Infrastructure.Tests\`
- **Run:** `dotnet run --project src\GenesysExtensionAudit.App\GenesysExtensionAudit.App.csproj`
- **Publish:** See [Deployment](docs/deployment.md)
- **Credentials:** Use .NET user secrets or environment variables — never commit to source control
