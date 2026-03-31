# Genesys Cloud Auditor

Genesys Cloud Auditor is a Windows desktop and headless audit application for evaluating a **Genesys Cloud** organization for:

- tenant misconfigurations
- cross-endpoint inconsistencies
- stale or orphaned routing dependencies
- telephony ownership contradictions
- data hygiene problems
- operational patterns that may indicate a **probable platform-side issue**

The application collects and correlates data from multiple Genesys Cloud API domains, presents findings in a desktop UI, and exports structured audit results for investigation, change management, and support escalation.

---

## Why This Exists

Genesys Cloud environments often contain issues that are difficult to prove from a single admin screen or a single API response.

A queue may appear healthy but have no realistically serviceable agents.
A user profile may claim an extension while telephony assigns it elsewhere.
A number may still point to a flow that is stale, deleted, or operationally fragile.
An object may look valid in one API surface and contradictory in another.

Genesys Cloud Auditor is designed to detect those situations by comparing multiple authoritative sources and producing **evidence-backed findings** rather than isolated raw data dumps.

---

## Product Goals

Genesys Cloud Auditor is built to answer five practical questions:

1. **What is wrong right now?**
2. **Which APIs disagree about the same object or relationship?**
3. **What changed recently that may explain the issue?**
4. **Who should act on this finding, and what should they do next?**
5. **Does this appear to be a tenant-side misconfiguration or a probable Genesys platform issue?**

---

## Current Capabilities

The current application already provides a strong audit foundation.

### Implemented audit checks

| Check                                    | Sheet                    | Severity |
| ---------------------------------------- | ------------------------ | -------- |
| Duplicate profile extensions             | `Ext_Duplicates_Profile` | Critical |
| Extension ownership mismatch             | `Ext_Ownership_Mismatch` | Critical |
| Assignment vs profile extension mismatch | `Ext_Assign_vs_Profile`  | Warning  |
| Invalid extension values                 | `Invalid_Extensions`     | Warning  |
| Empty or single-member groups            | `Empty_Groups`           | Warning  |
| Empty or duplicate queues                | `Empty_Queues`           | Warning  |
| Stale / unpublished Architect flows      | `Stale_Flows`            | Warning  |
| Stale token users                        | `Stale_Tokens`           | Warning  |
| Users missing location                   | `Users_No_Location`      | Warning  |
| DID mismatches                           | `DID_Mismatches`         | Warning  |
| Stale license usage                      | `Stale_Licenses`         | Warning  |
| License over-provisioning                | `License_Over_Provisioning` | Warning |
| Role / group overlap                     | `Role_Group_Overlap`     | Warning  |
| User telephony integrity                 | `User_Telephony_Integrity` | High   |
| Queue serviceability                     | `Queue_Serviceability`   | High     |
| IVR flow dependency                      | `IVR_Flow_Bindings`      | Critical |
| Site / edge / trunk topology integrity   | `Site_Topology`          | Critical |
| Prompt hygiene                           | `Prompt_Hygiene`         | Warning  |
| Change adjacency correlation             | `Change_Adjacency`       | Info     |
| Flapping / instability detection         | `Flapping_Detection`     | Info     |
| Cross-domain hot spot ranking            | `Hot_Spots`              | Info     |
| Genesys Care escalation summary          | `Care_Case_Summary`      | Triage   |
| Audit log export                         | `Audit_Logs`             | Info     |
| Operational event export                 | `Operational_Events`     | Info     |
| Outbound event export                    | `Outbound_Events`        | Info     |
| Care evidence JSON packet                | `.care-evidence.json`    | Triage   |
| Care evidence HTML summary               | `.care-summary.html`     | Triage   |
| Elastic bulk export                      | Elastic index            | Triage   |

### Current delivery model

- Windows desktop application for interactive auditing
- Headless runner for scheduled execution
- Multi-sheet Excel workbook export
- Parallel machine-readable Care evidence JSON export
- Parallel human-readable Care evidence HTML export
- Optional ElasticSearch bulk export for findings and run summaries
- Optional scheduled task integration
- Optional SharePoint upload in runner mode
- Optional GitHub upload in runner mode

---

## What Makes This Tool Different

The long-term direction of the project is not merely “more checks.”

It is to become a **correlation-driven investigation workbench** that can:

- compare multiple API domains that should describe the same state
- identify contradictions across user, telephony, queue, routing, flow, and topology data
- preserve historical baselines
- classify findings by severity, confidence, and likely owner
- generate escalation-ready evidence for **Genesys Care** when a finding appears platform-side

This is the core product direction described in [ROADMAP.md](ROADMAP.md).

---

## Roadmap Direction

The roadmap is organized around five capability layers:

1. **Configuration Integrity**
2. **Cross-Endpoint Correlation**
3. **Operational and Temporal Intelligence**
4. **Actionability and Escalation**
5. **Reporting, History, and UX**

Key roadmap themes include:

- user–station–extension–DID–site integrity correlation
- queue serviceability analysis
- flow dependency and dead-route detection
- edge / site / trunk topology integrity checks
- change-to-symptom timeline correlation
- flapping / instability detection
- support-readiness scoring and Genesys Care evidence export
- historical drift analysis

See [ROADMAP.md](ROADMAP.md) for the full feature plan.

---

## Audience

This project is intended for:

- Genesys Cloud platform administrators
- cloud telephony and routing engineers
- support engineers and escalation teams
- internal change management teams
- developers building tenant health and audit workflows

---

## Solution Structure

```text
GenesysCloudAuditor/
|-- src/
|   |-- GenesysExtensionAudit.App/            # WPF desktop UI
|   |-- GenesysExtensionAudit.Runner/         # Headless runner for scheduled execution
|   |-- GenesysExtensionAudit.Core/           # Contracts and shared models
|   |-- GenesysExtensionAudit.Domain/         # Audit rules and domain logic
|   `-- GenesysExtensionAudit.Infrastructure/ # API clients, orchestration, export, logging
|-- tests/
|   `-- GenesysExtensionAudit.Infrastructure.Tests/
|-- docs/
|   `-- supporting architecture and operator documentation
|-- ROADMAP.md
|-- QA.md
`-- NOTES.md

## Getting Started

For setup, configuration, local execution, and runner usage, see:

- [QuickStart.md](QuickStart.md)
- [ROADMAP.md](ROADMAP.md)
- [docs/](docs/)
- [QA.md](QA.md)

