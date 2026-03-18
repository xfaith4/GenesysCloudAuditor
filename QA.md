# QA Strategy

This document defines how to validate Genesys Cloud Auditor before release.

Audience: QA engineers and developers.

Use this guide when planning test execution, determining release gates, or adding automated coverage.

## Scope

The QA scope covers:

- Data retrieval correctness (pagination, include inactive behavior)
- Resilience (rate limiting, retries, cancellation)
- Report integrity (worksheet content and row mapping)
- UI behavior (command enablement, progress and error surfaces)
- Runner behavior (CLI args, local output, optional SharePoint upload)

Detailed matrix: [docs/detailed-qa-matrix.md](docs/detailed-qa-matrix.md)

## Quality Gates

A release candidate is acceptable only when all gates pass:

1. Functional correctness
   - All selected audit paths return expected findings on controlled fixtures.
2. Reliability
   - 429 and transient failures are retried within configured limits.
3. Safety
   - Cancellation leaves no corrupted output and UI returns to idle state.
4. Reporting
   - Generated workbook opens in Excel and worksheet counts match findings.
5. Regression
   - Infrastructure tests pass in CI and local smoke run is clean.

## Test Layers

| Layer | Purpose | Current expectation |
| --- | --- | --- |
| Unit tests | Deterministic logic validation | Normalization and service-level rules |
| Integration tests | API pagination and orchestration behavior | Mocked HTTP and paging validation |
| Manual UI tests | Responsiveness and operator workflow | Start/cancel/export/scheduling checks |
| Runner smoke tests | Headless execution sanity | CLI args and output validation |

## Required Scenarios Per Release

1. IncludeInactive off and on produce correct user query behavior.
2. Large page counts complete without deadlock or runaway memory.
3. 429 with and without `Retry-After` follows retry policy.
4. Cancellation works during fetch, retry delay, and analysis.
5. Workbook exports all selected sections and no unselected sections.
6. Scheduled profile execution path works with `--schedule-profile`.

## Evidence Template

Capture at minimum:

- Commit SHA tested
- Config profile used (sanitized)
- Test results summary (pass/fail by gate)
- Defects opened with severity and reproduction notes
- Workbook artifact from the run (sanitized)

## Defect Severity Guidance

| Severity | Definition |
| --- | --- |
| Critical | Incorrect findings or data loss/corruption |
| High | Major workflow blocked (cannot run/export/schedule) |
| Medium | Partial function degraded with workaround |
| Low | UX or cosmetic issue without audit impact |

## Related Documents

- [README.md](README.md)
- [docs/setup-and-operations.md](docs/setup-and-operations.md)
- [docs/detailed-qa-matrix.md](docs/detailed-qa-matrix.md)
