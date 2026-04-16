# Run Audit UI and Workflow

This document defines the operator workflow in the desktop application.

Audience: operations users and QA engineers.

Use this guide when running audits manually, selecting audit paths, and exporting results.

## Workflow Overview

```text
Configure -> Select audit paths -> Start -> Monitor -> Review summary -> Export workbook
```

## Run Audit Tab

The `Run Audit` tab is the primary execution surface.

### Configuration fields

- `Page Size` (clamped to `1..500`)
- `Include Inactive Users`
- `Operational Lookback Days`

### Audit path selection

Available toggles:

- Extensions
- Groups
- Queues
- Flows
- Inactive Users
- DIDs
- Audit Logs
- Operational Event Logs
- Outbound Events

`Select All` updates all path flags at once.

### Audit Logs selector

When `Audit Logs` is enabled:

- A catalog entity selector is shown.
- `Refresh` reloads available entities.
- `(All Catalog Entities)` runs without a service-name filter.

## Execution States

| State | Expected behavior |
| --- | --- |
| Idle | Start enabled if at least one audit path is selected |
| Running | Start disabled, Cancel enabled, progress and status update |
| Cancelling | Status shows cancellation in progress |
| Complete | Report is retained for export |
| Failed | Error message shown and UI returns to idle |

## Export Behavior

After a successful run:

- The app generates a workbook in memory.
- A save dialog prompts for `.xlsx` destination.
- `Export Last Report...` re-exports the last completed report without re-running.

## Scheduling Integration

The `Schedule Audits` tab registers Windows scheduled tasks that run the headless runner with a schedule profile.

Task execution model:

```text
Windows Task Scheduler
`-- GenesysExtensionAudit.Runner.exe --schedule-profile <profile.json>
```

If runner auto-discovery fails, set `Scheduling:RunnerExecutablePath` in app settings.

## Operator Constraints

- At least one audit path must be selected.
- One-time schedule start time must be in the future.
- Weekly schedules require one or more weekdays.
- Credentials should be injected through secure configuration channels; do not embed secrets in exported files.

## Related Documents

- [setup and operations guide](setup-and-operations.md)
- [architecture guide](application-architecture.md)
- [QA matrix](detailed-qa-matrix.md)
