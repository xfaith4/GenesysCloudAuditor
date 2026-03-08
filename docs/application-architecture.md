# Application Architecture

This document describes the implementation architecture for the desktop app and runner.

Audience: contributors making structural or cross-layer changes.

Use this guide before modifying DI registration, orchestrator flow, or API client boundaries.

## Architecture Overview

```text
Presentation (WPF App)
|-- Views
|-- ViewModels
`-- Scheduling UI

Application/Orchestration
`-- Audit orchestrator + progress model

Domain/Core
|-- Models and contracts
`-- Normalization and analysis services

Infrastructure
|-- Genesys API clients
|-- HTTP handlers (OAuth, logging, rate limit)
|-- Export and reporting services
`-- SharePoint upload integration

Runner
`-- Headless host using same orchestration and reporting services
```

## Runtime Composition

Both app and runner use `Microsoft.Extensions.Hosting` with DI and configuration binding.

Key service groups:

- API clients for users, extensions, groups, queues, flows, DIDs, logs/events
- token provider and HTTP middleware handlers
- audit orchestrator and analyzer services
- Excel report service

## UI Execution Model

`RunAuditViewModel` controls:

- option selection
- start/cancel commands
- progress/status updates
- post-run export actions

Execution requirements:

- long-running operations are asynchronous
- cancellation token is propagated end-to-end
- UI command enablement follows run state

## Runner Execution Model

Runner supports:

- standard config-driven run
- `--dry-run` to skip SharePoint upload
- `--schedule-profile <path>` to execute a scheduled profile

Runner always writes workbook output locally, then optionally uploads to SharePoint when configured.

## Design Constraints

- No secrets in logs.
- Keep HTTP retry/rate-limit behavior centralized in infrastructure.
- Keep view models free of direct API logic.
- Keep domain logic deterministic and testable.

## Related Documents

- [solution map](solution-and-project-map.md)
- [OAuth and resilience design](oauth-and-api-resilience.md)
- [data model and algorithms](data-model-and-audit-algorithms.md)

