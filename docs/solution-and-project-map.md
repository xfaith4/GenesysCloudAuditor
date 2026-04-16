# Solution and Project Map

This document explains the current repository layout and project responsibilities.

Audience: contributors onboarding to the codebase.

Use this guide before changing architecture, dependencies, or project references.

## Current Solution Topology

```text
GenesysExtensionAudit_scaffold.sln
|-- src/GenesysExtensionAudit.App
|-- src/GenesysExtensionAudit.Runner
|-- src/GenesysExtensionAudit.Core
|-- src/GenesysExtensionAudit.Domain
|-- src/GenesysExtensionAudit.Infrastructure
`-- tests/GenesysExtensionAudit.Infrastructure.Tests
```

## Project Responsibilities

| Project | Responsibility |
| --- | --- |
| `GenesysExtensionAudit.App` | WPF UI, navigation, scheduling UX |
| `GenesysExtensionAudit.Runner` | Headless execution for scheduled/background runs |
| `GenesysExtensionAudit.Core` | Contracts, models, and domain-facing abstractions |
| `GenesysExtensionAudit.Domain` | Audit engine logic |
| `GenesysExtensionAudit.Infrastructure` | API clients, orchestration, exports, logging, configuration |
| `GenesysExtensionAudit.Infrastructure.Tests` | Integration-style tests for infrastructure behavior |

## Dependency Direction

```text
App -> Core
App -> Infrastructure
Runner -> Core
Runner -> Infrastructure
Infrastructure -> Core
Domain logic consumed by Infrastructure orchestrator
Tests -> Infrastructure (+ transitively Core/Domain)
```

## Migration Note

The current solution filename is `GenesysExtensionAudit_scaffold.sln`.

## Related Documents

- [architecture guide](application-architecture.md)
- [README](../README.md)
