# Genesys Cloud Auditor — Product Roadmap

This roadmap defines the evolution of **Genesys Cloud Auditor** from a configuration audit tool into a **correlation-driven health, integrity, and escalation workbench** for Genesys Cloud organizations.

The application’s purpose is to:

- detect tenant misconfigurations, inconsistencies, and hygiene issues
- identify probable platform synchronization or integrity defects
- correlate related data across multiple Genesys Cloud API domains
- produce actionable remediation guidance for internal change management teams
- generate evidence-rich escalation packets for **Genesys Care** when findings appear platform-side rather than customer-side

This roadmap builds on the current implemented checks for extension integrity, DID mismatches, queue/group hygiene, stale flows, inactive-user signals, user telephony integrity, queue serviceability, IVR flow dependency, site topology integrity, prompt hygiene, change adjacency, flapping detection, hot-spot ranking, and exported audit/operational/outbound/Care evidence outputs.

---

## Product Vision

Genesys Cloud Auditor should answer five increasingly valuable questions:

1. **What is wrong right now?**
2. **What APIs disagree about the same object or relationship?**
3. **What changed recently that may explain the issue?**
4. **Who should act on this finding, and what should they do next?**
5. **Does this look like a tenant-side misconfiguration or a probable Genesys platform problem?**

The long-term goal is not just to export findings, but to provide a structured evidence chain that makes operational triage and support escalation dramatically faster and more reliable.

---

## Guiding Principles

- **Correlation over isolated checks**
  The highest-value findings come from comparing multiple authoritative API surfaces that should describe the same reality.

- **Actionability over raw output**
  Every major finding should include severity, confidence, blast radius, recommended owner, and next step.

- **Evidence over suspicion**
  Probable platform defects should only be flagged when the application can demonstrate contradictory or unstable state across trusted APIs.

- **Repeatability over one-time snapshots**
  The app should preserve normalized historical snapshots so teams can distinguish chronic issues from transient anomalies.

- **Operator clarity over mystery meat**
  Findings should explain why they matter in plain language and provide object relationships, not just IDs and raw fields.

---

## Current Implemented Capabilities

The current product already provides an effective foundation for the roadmap:

| Capability                                     | Current State |
| ---------------------------------------------- | ------------- |
| Extension consistency checks                   | Implemented   |
| DID mismatch checks                            | Implemented   |
| Empty / duplicate queue checks                 | Implemented   |
| Empty / small group checks                     | Implemented   |
| Stale flow checks                              | Implemented   |
| Inactive / stale token user checks             | Implemented   |
| Missing location checks                        | Implemented   |
| Stale license usage audit                     | Implemented   |
| License over-provisioning audit               | Implemented   |
| Role / group overlap audit                    | Implemented   |
| User telephony integrity audit                 | Implemented (partial Phase 1.2 scope) |
| Queue serviceability audit                     | Implemented (partial Phase 1.3 scope) |
| IVR flow dependency audit                      | Implemented (partial Phase 1.4 scope) |
| Site / edge / trunk topology audit             | Implemented (partial Phase 1.5 scope) |
| Edge performance / distribution audit          | Implemented (Phase 1.5 extension) |
| Architect prompt hygiene audit                 | Implemented   |
| Change adjacency correlation                   | Implemented   |
| Flapping / instability detection               | Implemented   |
| Cross-domain hot spot ranking                  | Implemented   |
| Audit log export                               | Implemented   |
| Operational event export                       | Implemented   |
| Outbound event export                          | Implemented   |
| Excel workbook export                          | Implemented   |
| Genesys Care workbook summary                  | Implemented   |
| Genesys Care evidence JSON export              | Implemented   |
| Support-readiness scoring                      | Implemented (single-run, correlation-based) |
| Desktop and scheduled/headless execution model | Implemented   |

---

## Roadmap Structure

Future work is grouped into five major capability layers:

1. **Configuration Integrity**
2. **Cross-Endpoint Correlation**
3. **Operational and Temporal Intelligence**
4. **Actionability and Escalation**
5. **Reporting, History, and UX**

---

# Phase 1 — Correlation Foundation

This phase delivers the biggest leap in real-world usefulness.

## 1.1 Canonical correlation engine

Build a reusable engine that allows audit modules to define:

- the primary object under inspection
- related API entities to collect
- normalization rules
- expected relationships
- contradiction / anomaly rules
- severity and confidence rules
- exportable evidence fragments

### Outcome

A common framework for checks that compare multiple APIs instead of one API at a time.

---

## 1.2 User–station–extension–DID–site integrity audit

Correlate:

- users
- user profiles
- assigned extensions
- DID assignments
- stations
- sites
- locations
- telephony ownership relationships

### Planned checks

| Check                             | Priority | Description                                                                               | Status |
| --------------------------------- | -------- | ----------------------------------------------------------------------------------------- | ------ |
| User telephony completeness       | High     | User appears active and telephony-enabled but lacks a coherent station/extension/DID path | **Implemented** |
| DID ownership mismatch            | High     | A DID is associated to a user profile but assigned elsewhere in telephony                 | Partial |
| Station–user assignment conflict  | High     | A station references a user, but user/profile/telephony state disagrees                   | **Implemented** |
| User–site telephony contradiction | Medium   | User location/site and telephony resource site do not align                               | Planned |
| Ghost telephony assignment        | High     | Telephony asset references a deleted, inactive, or otherwise invalid user/resource        | Planned |
| Multiple ownership contradiction  | High     | Same telephony asset appears attributable to more than one active identity                | Planned |

Currently implemented in `AuditOrchestrator`: profile extension with no station, station with no profile extension, and DID-to-profile ownership mismatch checks. Remaining Phase 1.2 work is the deeper site-aware and multi-owner correlation.

### Why this matters

This is one of the clearest areas where tenant misconfiguration and platform sync issues blur together. Cross-endpoint comparison is essential.

---

## 1.3 Queue serviceability audit

Correlate:

- queues
- queue memberships
- user active state
- skills
- languages
- divisions
- wrap-up code dependencies
- routing configuration

### Planned checks

| Check                                      | Priority | Description                                                                                   | Status |
| ------------------------------------------ | -------- | --------------------------------------------------------------------------------------------- | ------ |
| Queue with non-serviceable membership      | High     | Queue has members on paper, but no realistically serviceable agents after filters are applied | **Implemented** |
| Queue skill mismatch                       | High     | Queue requires skills/languages that none of its members possess                              | Planned |
| Queue membership drift                     | Medium   | Queue membership exists, but many members are inactive, unlicensed, or otherwise non-usable   | Partial |
| Duplicate semantic queues                  | Medium   | Queues are near-duplicates by name, membership, and routing intent                            | Partial |
| Queue with incomplete routing dependencies | High     | Queue is missing downstream config required to actually process interactions                  | Planned |

The current implementation covers member-state viability with bounded queue-member sampling and explicit oversized-queue warnings. Remaining work is the deeper routing dependency and skill/language correlation layer.

### Why this matters

Many tenants look healthy in isolated admin screens while being operationally hollow.

---

## 1.4 Flow dependency audit

Correlate:

- Architect flows
- flow publish status
- inbound numbers / IVRs
- schedules / schedule groups
- queues referenced by flows
- downstream routing destinations

### Planned checks

| Check                          | Priority | Description                                                                         | Status |
| ------------------------------ | -------- | ----------------------------------------------------------------------------------- | ------ |
| IVR–flow binding stale         | High     | Number points to a flow that is deleted, stale, unpublished, or errored             | **Implemented** |
| Dead route dependency          | High     | Flow references queue/schedule/destination that no longer exists                    | Planned |
| Flow dependency drift          | Medium   | Flow is published, but dependent objects have changed materially since last publish | Planned |
| Critical entry-point fragility | Medium   | Important numbers depend on brittle or stale routing chains                         | Planned |

The current `IvrFlowBindingFinding` path covers missing open-hours bindings, missing schedule groups, deleted flows, draft flows, and stale published flows. Dependency traversal past the IVR/flow boundary remains future work.

### Why this matters

This catches silent customer-facing failures before they produce obvious outage reports.

---

## 1.5 Site–edge–trunk topology integrity

Correlate:

- sites
- edges
- trunks
- stations
- telephony locations
- DID inventories where available

### Planned checks

| Check                          | Priority | Description                                                                | Status |
| ------------------------------ | -------- | -------------------------------------------------------------------------- | ------ |
| Trunk–edge assignment orphan   | Medium   | Edge references trunk/site state that no longer reconciles                 | **Implemented** |
| Site–edge mismatch             | High     | Site relationship differs across authoritative edge/site resources         | **Implemented** |
| Edge load distribution skew    | High     | Online edges within the same site carry materially imbalanced observed conversation load | **Implemented** |
| Secondary edge unexpected load | Medium   | Standby/secondary edges carry live traffic while primary edges remain online | **Implemented** |
| Station topology contradiction | Medium   | Station/site/location relationships appear internally inconsistent         | Planned |
| DID inventory orphan           | Medium   | DID ranges exist in inventory but do not reconcile to active service paths | Planned |

The current topology/performance layer detects orphaned edge-to-site bindings, offline edges, sites with no active edges, trunks hosted on offline edges, trunks out of service, trunks reporting down/unknown state, and per-edge operational load imbalance derived from matched operational events.

---

# Phase 2 — Operational and Time-Based Intelligence

This phase adds “what changed?” and “is this recurring?”—the good stuff.

## 2.1 Change-to-symptom correlation

Correlate current findings with:

- audit logs
- operational events
- outbound events
- relevant configuration changes
- flow publish timestamps

### Planned checks

| Check                    | Priority | Description                                                                   | Status |
| ------------------------ | -------- | ----------------------------------------------------------------------------- | ------ |
| Regression chain builder | High     | Build a timeline showing config changes followed by symptoms/findings         | Planned |
| Change adjacency marker  | High     | Surface recent changes that touched related objects within the finding window | **Implemented** |
| Suspect release window   | Medium   | Group findings by likely onset window after correlated changes                | Planned |

### Example output

“Queue membership changed at 14:03; routing anomalies began at 14:07; serviceability fell to zero by 14:11.”

---

`ChangeAdjacencyAnalyzer` is implemented and exported to the `Change_Adjacency` worksheet. It correlates audit-log changes to active findings by object ID/name within a configurable lookback window.

---

## 2.2 Flapping and instability detection

### Planned checks

| Check                | Priority | Description                                                                                     | Status |
| -------------------- | -------- | ----------------------------------------------------------------------------------------------- | ------ |
| Assignment flapping  | High     | Ownership or assignment repeatedly changes between states                                       | **Implemented** |
| Publish churn        | Medium   | Flows repeatedly republished or altered without stabilizing behavior                            | **Implemented** |
| Resource oscillation | Medium   | Site/edge/station/trunk relationships repeatedly move between valid and invalid interpretations | **Implemented** |

All three patterns are implemented as `FlappingDetectionAnalyzer` in `Domain/Services`.
Detection is purely audit-log driven (no additional API calls). Configurable window and
minimum-change threshold are exposed through `AuditRunOptions` (`FlappingDetectionWindowMinutes`,
`FlappingDetectionMinChanges`).

### Why this matters

Flapping is often a signal of automation conflict, admin collision, sync lag, or deeper platform instability.

---

## 2.3 Hot spot ranking

### Planned checks

| Check                      | Priority | Description                                                                        | Status |
| -------------------------- | -------- | ---------------------------------------------------------------------------------- | ------ |
| Chronic object ranking     | Medium   | Rank queues, users, sites, and telephony resources by repeated anomaly association | **Implemented** |
| Domain instability index   | Medium   | Score routing, telephony, identity, or outbound domains for recurring problems     | Partial (object-level; domain scoring planned) |
| Blast-radius concentration | Medium   | Identify small sets of objects involved in a disproportionate share of findings    | **Implemented** |

Object-level hot spot ranking is implemented as `HotSpotAnalyzer` in `Domain/Services`.
It aggregates all collected findings (including Phase 2.1 and 2.2 results), identifies objects
that appear across two or more distinct audit domains, and ranks them by total finding count.
Results are exported to a dedicated `Hot_Spots` worksheet. Configurable via
`HotSpotMinDistinctDomains` in `AuditRunOptions`.

---

## 2.4 Rule-driven best-practice sentinel layer

This phase formalizes the product as a **weekly best-practice sentinel** rather than a raw log exporter or generic observability tool.

The intent is to scan selected Genesys Cloud API domains on a recurring cadence, compare the tenant state against well-documented expected patterns, and emit **operator-ready signals** with minimal additional interpretation work.

### Sentinel principles

- Rules must be backed by documented Genesys Cloud guidance or explicitly declared internal operating standards
- Each signal must answer:
  - what best practice or expected state was checked
  - which API surfaces were used
  - what contradicted the expected state
  - whether the likely cause is tenant configuration, change activity, automation drift, or possible platform-side behavior
- Audit logs should be analyzed inside the app, not merely exported for later human interpretation
- Weekly scans should emphasize:
  - smoke that may become fire
  - drift from previously healthy baselines
  - suspicious admin or automation changes
  - platform behaviors that appear inconsistent with the documented model

### Planned scope of work

| Scope item                             | Priority | Description                                                                                           | Status |
| -------------------------------------- | -------- | ----------------------------------------------------------------------------------------------------- | ------ |
| Rule registry and metadata model       | High     | Define a common structure for source-backed best-practice rules, rule IDs, versions, owner, and APIs | Planned |
| Source/provenance tracking             | High     | Persist the documentation source or internal standard that justifies each rule                        | Planned |
| Sentinel worksheet and summary rollup  | High     | Add a triage-first export showing only interpreted best-practice signals, not raw event dumps         | Planned |
| Best-practice mapping hygiene hardening | High    | Normalize aliases, remove raw event/rollup enrichment noise, and map only actionable finding types   | Planned |
| Audit-log signaling engine             | High     | Convert raw audit logs into categorized signals such as risky change, unusual churn, role drift       | Planned |
| Admin role change detection            | High     | Flag privileged role grants/removals, division scope changes, and admin access changes                | Planned |
| Platform configuration change detector | High     | Detect significant queue, flow, IVR, site, edge, trunk, prompt, and telephony configuration changes  | Planned |
| CX as Code change-awareness            | Medium   | Distinguish likely managed change windows from ad hoc admin changes when patterns suggest automation  | Planned |
| Weekly drift sentinel                  | High     | Compare current findings and normalized state against prior weekly baselines                           | Planned |
| Best-practice exception model          | Medium   | Allow intentional deviations to be recorded so recurring approved exceptions do not create noise      | Planned |
| Signal severity and confidence model   | High     | Score signals by operational risk, blast radius, persistence, and alignment with documented guidance  | Planned |

### Initial sentinel domains

- edge / site topology expectations
- failover posture and unexpected secondary-edge behavior
- routing dependency hygiene
- audit-log change correlation
- historical drift against prior weekly scans
- privileged admin / role changes
- platform configuration changes in key monitored domains

### Audit-log automation goals

The audit-log path should evolve from "accessible raw events" to "interpreted operational signals."

Planned audit-log signal families:

- privileged role granted / removed
- division scope broadened or narrowed
- queue membership churn above normal threshold
- flow publish burst or repeated rollback / republish pattern
- site, edge, or trunk topology edits preceding telephony findings
- DID / extension ownership changes preceding user telephony contradictions
- suspicious volume of manual admin changes outside known change windows
- recurring drift after previous remediation, suggesting automation conflict or platform sync lag

### Expected outputs

- a dedicated sentinel-oriented worksheet for interpreted signals
- summary rollups that answer:
  - what changed this week
  - what now violates expected best practice
  - what is newly risky versus chronic
  - what likely needs admin review this week
- rule metadata in exports so operators can trace each signal back to its best-practice basis
- unmapped best-practice finding types limited to real policy coverage gaps, not raw event exports or derived rollups
- generic umbrella findings replaced with specific codes before they enter the best-practice mapping layer
- less dependence on manually reading raw audit logs except for deep investigation

### Why this matters

This keeps the auditor aligned to its original mission:

- weekly scans of key APIs
- fast detection of misconfiguration smoke before it becomes operational fire
- better separation of tenant-side issues from suspicious platform behavior
- less manual interpretation work for the monitoring team

---

# Phase 3 — Actionability and Escalation Intelligence

This phase makes the tool genuinely operational.

## 3.1 Recommended action model

Every major finding should emit:

- finding type
- severity
- confidence
- blast radius
- impacted objects
- suspected owner
- recommended next action
- probable cause category

### Action categories

| Category                 | Description                                                                      |
| ------------------------ | -------------------------------------------------------------------------------- |
| Local Configuration Fix  | Likely resolvable directly by tenant admin or engineering team                   |
| Change Review Required   | Recent changes suggest managed review or rollback may be needed                  |
| Monitor / Re-run         | Evidence is weak or transient; continue observation                              |
| Escalate to Genesys Care | Evidence suggests probable platform-side issue or unresolved contradictory state |

This model is now in active use across the newer correlation findings. `FindingSeverity` and `FindingCategory` are emitted for user telephony, queue serviceability, IVR flow dependency, site topology, prompt hygiene, change adjacency, flapping, and hot-spot outputs. `CareEvidencePacket` also carries confidence, blast radius, suspected owner, probable cause category, support readiness, and recommended action for escalation candidates.

---

## 3.2 Probable platform issue qualification

A finding should be classified as **Probable Platform Issue** only when criteria are met.

### Qualification rules

A finding is a candidate for support escalation when:

1. two or more authoritative APIs disagree
2. the contradiction persists across repeated collection or repeated runs
3. no recent admin change plausibly explains the issue
4. the inconsistency reflects an invalid or impossible state under the documented model
5. business impact is meaningful or plausibly emerging

### Planned checks

| Check                             | Priority | Description                                                                                        | Status |
| --------------------------------- | -------- | -------------------------------------------------------------------------------------------------- | ------ |
| Persistent contradiction detector | High     | Contradictory API state persists across collections/runs                                           | Planned |
| No-local-cause qualifier          | High     | Finding remains after excluding recent tenant changes as an explanation                            | Partial |
| Support-readiness scorer          | High     | Finding contains sufficient object IDs, timestamps, evidence, and impact context for case creation | **Implemented** |

The current support-readiness scorer is single-run and correlation-based. It uses finding severity/category, object identity completeness, API-surface count, recent change adjacency, hot-spot presence, and flapping signals to classify each candidate as `Ready`, `NeedsReview`, or `Monitor`. Historical persistence across runs remains part of the future Phase 4 dependency.

---

## 3.3 Genesys Care evidence packet export

Generate a structured escalation packet for support-worthy findings.

### Packet contents

- org / tenant identifier
- region
- generated timestamp
- affected object IDs and names
- API surfaces involved
- contradictory evidence summary
- related audit log / operational event timeline
- reproduction notes if available
- severity, confidence, and blast radius
- recommended case summary text
- supporting workbook sheet references

### Planned outputs

| Output               | Priority | Description                                                  | Status |
| -------------------- | -------- | ------------------------------------------------------------ | ------ |
| Support case summary | High     | Human-readable case narrative for Care ticket entry          | **Implemented** |
| Evidence JSON        | High     | Machine-readable evidence package for archival or automation | **Implemented** |
| Timeline appendix    | Medium   | Event and change history supporting the escalation           | Partial |

The runner now writes a `.care-evidence.json` artifact alongside the workbook, and the workbook includes a `Care_Case_Summary` sheet with support readiness, confidence, blast radius, suspected owner, probable cause, recent change context, and qualification notes. Current packet coverage includes IVR/flow dependency, user telephony integrity, queue serviceability, and site topology escalation candidates.

---

# Phase 4 — Historical Baselines and Drift Intelligence

This phase gives the application memory.

## 4.1 Snapshot persistence

Persist normalized snapshots for each audit run:

- normalized users
- normalized telephony ownership
- normalized queues and memberships
- normalized flow dependencies
- normalized topology relationships
- finding summaries

### Planned features

| Feature                          | Priority | Description                                             |
| -------------------------------- | -------- | ------------------------------------------------------- |
| Snapshot save/load               | High     | Save normalized state for comparison across runs        |
| Historical diff engine           | High     | Compare current state to previous baseline(s)           |
| Chronic anomaly ledger           | Medium   | Track findings that recur over time                     |
| Finding lifecycle classification | Medium   | Mark findings as New, Resolved, Recurrent, or Worsening |

---

## 4.2 Drift detection

### Planned checks

| Check                     | Priority | Description                                                           |
| ------------------------- | -------- | --------------------------------------------------------------------- |
| Membership drift          | Medium   | Queue/group/user relationships changed materially since last baseline |
| Telephony ownership drift | High     | Extensions, DIDs, stations, or site bindings changed unexpectedly     |
| Routing drift             | High     | Flow/queue/schedule bindings changed between runs                     |
| Security drift            | Medium   | Roles, OAuth clients, or privileged assignments changed materially    |

---

# Phase 5 — Reporting, UX, and Explainability

This phase turns the results into something people will actually use.

## 5.1 Executive summary and triage dashboard

### Planned outputs

| Output                          | Priority | Description                                                        |
| ------------------------------- | -------- | ------------------------------------------------------------------ |
| Executive summary sheet         | High     | Findings by severity, domain, probable cause, and owner            |
| “Open case recommended” counter | High     | Count and summarize support-worthy findings                        |
| Top impacted objects            | Medium   | Highlight objects with highest recurrence or blast radius          |
| Domain health scoring           | Medium   | Score telephony, routing, identity, outbound, and security posture |

---

## 5.2 Relationship and dependency views

### Planned outputs

| Output                       | Priority | Description                                             |
| ---------------------------- | -------- | ------------------------------------------------------- |
| Object dependency tree       | High     | Render finding-specific object relationship chains      |
| Finding evidence chain       | High     | Show which APIs and comparisons produced the conclusion |
| Why-this-matters explanation | High     | Plain-language explanation of risk or likely impact     |
| Recent change context        | Medium   | Inline summary of nearby relevant changes/events        |

### Example relationship chain

`User -> Extension -> DID -> Station -> Site -> Queue -> Flow`

or

`DID -> IVR -> Flow -> Queue -> Agents`

---

## 5.3 Workbook and export improvements

### Planned features

| Feature                       | Priority | Description                                                                |
| ----------------------------- | -------- | -------------------------------------------------------------------------- |
| Summary-first workbook layout | High     | Open directly to triage-oriented summary sheets                            |
| Cross-sheet linking           | Medium   | Link findings to supporting detail sheets                                  |
| JSON evidence export          | High     | Parallel machine-readable export for automation                            |
| HTML summary export           | Medium   | Human-friendly evidence summary suitable for sharing or ticket attachments |
| Rule metadata export          | Medium   | Include which audit rule/version produced each finding                     |

---

# Cross-Domain Audit Catalog

The following catalog groups future checks by domain rather than implementation phase.

## Identity / Access

| Planned Check                        | Priority | Description                                                                      |
| ------------------------------------ | -------- | -------------------------------------------------------------------------------- |
| Inactive users with privileged roles | High     | High-impact access remains assigned to stale or inactive accounts                |
| Roles assigned to invalid users      | Medium   | Roles reference deleted or invalid identities                                    |
| Over-privileged OAuth clients        | High     | OAuth clients hold permissions broader than intended                             |
| Division scope contradictions        | Medium   | User/role/division relationships produce misleading or inconsistent access state |
| Privileged asset ownership drift     | Medium   | Sensitive admin or telephony ownership changed unexpectedly                      |

---

## Routing / Queueing

| Planned Check                   | Priority | Description                                                             |
| ------------------------------- | -------- | ----------------------------------------------------------------------- |
| Queue with no routing viability | High     | Queue cannot effectively service work despite appearing configured      |
| Queue dependency gap            | High     | Queue relies on missing wrap-up/skill/language/routing configuration    |
| Queue duplication cluster       | Medium   | Multiple queues likely represent abandoned clones or overlapping intent |
| Routing-policy contradiction    | Medium   | Queue behavior implied by one resource is contradicted elsewhere        |

---

## Telephony / Numbering

| Planned Check                  | Priority | Description                                                                  |
| ------------------------------ | -------- | ---------------------------------------------------------------------------- |
| DID ownership mismatch         | High     | DID profile ownership disagrees with telephony assignment                    |
| Extension range violation      | Medium   | Extensions fall outside expected site/org numbering patterns                 |
| User-station-DID contradiction | High     | Cross-object telephony identity does not reconcile                           |
| Emergency service exposure     | High     | Telephony-enabled entity lacks coherent emergency-related assignment context |
| Inventory orphan detection     | Medium   | Number or asset inventory exists without valid downstream binding            |

---

## Architect / Flowing / Entry Points

| Planned Check                    | Priority | Description                                                          |
| -------------------------------- | -------- | -------------------------------------------------------------------- |
| Stale dependency after publish   | Medium   | Published flow depends on changed or missing downstream resources    |
| Number-to-flow fragility         | High     | Critical inbound numbers depend on stale or brittle routing          |
| Schedule reference contradiction | Medium   | Schedule or schedule group state no longer supports intended routing |
| Orphaned destination references  | High     | Flow references deleted queues, flows, or destinations               |

---

## Outbound

| Planned Check                      | Priority | Description                                                           |
| ---------------------------------- | -------- | --------------------------------------------------------------------- |
| Campaign with invalid dependency   | High     | Campaign depends on inactive or deleted object such as list/queue     |
| Outbound routing contradiction     | Medium   | Outbound configuration implies invalid downstream service path        |
| Outbound event hot spot            | Medium   | Specific campaigns or lists correlate disproportionately to anomalies |
| Change-linked outbound degradation | Medium   | Recent changes align with outbound issue onset                        |

---

## Data Quality / Hygiene

| Planned Check                 | Priority | Description                                                                 |
| ----------------------------- | -------- | --------------------------------------------------------------------------- |
| Duplicate semantic names      | Medium   | Similar names likely represent confusion or abandoned copies                |
| Missing user email / metadata | Low      | Weak identity hygiene that can affect reporting and administration          |
| Naming standard drift         | Low      | Objects diverge from expected naming patterns by domain/site/division       |
| Legacy orphan cluster         | Medium   | Objects appear to be remnants of prior migrations or decommissioned designs |

---
## 5.4 ElasticSearch export and operational indexing

Provide an optional post-processing export path that sends finalized audit findings, summaries, and evidence records to a configurable ElasticSearch index via API.

This capability is strictly an **output/integration feature** and must not replace or bypass the application’s primary data acquisition and correlation model.

### Planned features

| Feature                              | Priority | Description                                                                 |
| ------------------------------------ | -------- | --------------------------------------------------------------------------- |
| Elastic export toggle                | High     | Allow operators to enable or disable Elastic export per run                 |
| Configurable endpoint URI            | High     | Let the user define the ElasticSearch API endpoint within the UI            |
| Configurable target index            | High     | Let the user define the destination index name within the UI                |
| Environment-variable token loading   | High     | Read Elastic API token from environment variable, never store secret in UI  |
| Export payload shaping               | High     | Send normalized finding/evidence documents in a stable schema               |
| Bulk indexing mode                   | Medium   | Support efficient batch submission for large result sets                    |
| Export status and failure reporting  | High     | Surface success/failure counts, HTTP status, and response details in-app    |
| Retry-safe delivery behavior         | Medium   | Prevent duplicate or corrupted indexing during transient failures           |
| Rule/version metadata in documents   | Medium   | Include rule ID/version and run metadata for downstream filtering           |
| Optional run-summary document        | Medium   | Write one summary document per run in addition to per-finding documents     |

### Current implementation status

The current repo now includes the core Phase 5.4 export path:

- Shared `ElasticExport` configuration in appsettings and desktop user settings
- Per-run Elastic export toggles in the desktop run flow and scheduled profile flow
- Environment-variable token loading with no token persistence in UI settings
- NDJSON bulk indexing of normalized finding documents
- Optional run-summary document per audit run
- Export status reporting with document counts and operator-facing failure messages
- Infrastructure tests covering missing-token validation and bulk-payload shaping

Remaining gaps in this block are connection-test UX, stronger retry/idempotency semantics, and broader rule metadata/versioning depth.

### UI requirements

- Add UI fields for:
  - Elastic endpoint URI
  - target index name
  - environment variable name for token (default suggested value may be provided)
  - enable/disable export checkbox
- Do not expose the token value in the UI
- Validate endpoint and index values before submission
- Provide a test-connection / test-export action if feasible

### Security requirements

- The Elastic API token must be obtained from an environment variable
- The token must not be written to logs, exports, config files, or workbook outputs
- Error messages must redact authorization details
- The application should tolerate missing token state gracefully and emit a clear operator-facing validation message

### Data model expectations

At minimum, indexed documents should support:

- run ID
- generated timestamp
- org / tenant identifier
- region
- finding ID
- finding type
- severity
- confidence
- probable cause category
- recommended owner
- recommended next action
- impacted object IDs / names
- evidence summary
- support-escalation eligibility
- rule ID / rule version

### Why this matters

This enables centralized search, long-term retention, dashboarding, triage workflows, and correlation with external operational telemetry in Elastic-based environments.

---

# Documentation and Repository Hygiene Backlog

Pending documentation/repository cleanup items:

| Item | Priority | Status |
| --- | --- | --- |
| Rename scaffold-era solution filename (`GenesysExtensionAudit_scaffold.sln` -> `GenesysExtensionAudit.sln`) | Medium | Planned |
| Add/refresh product screenshots for `Run Audit`, `Schedule Audits`, and report output docs | Medium | Planned |
| Define and publish contributor documentation standards as repository contributor volume grows | Medium | Planned |

-----
# Rule Authoring and Extensibility

To make the platform durable, future audit checks should be definable through a common rule contract.

## Proposed rule contract attributes

- rule ID
- rule name
- domain
- required endpoints / collectors
- normalization dependencies
- join keys / correlation keys
- anomaly criteria
- severity default
- confidence model
- evidence template
- recommended owner
- suggested actions
- support-escalation eligible (true/false)

This enables:

- internal extensibility
- customer-specific rule packs
- controlled rule versioning
- regression-safe testing

---

# Testing and Validation Roadmap

## Planned quality gates

| Gate                      | Purpose                                                     |
| ------------------------- | ----------------------------------------------------------- |
| Collector contract tests  | Ensure endpoint ingestion remains stable                    |
| Correlation tests         | Validate cross-endpoint joins and contradiction logic       |
| Rule provenance tests     | Ensure each sentinel rule maps to a documented source or approved internal standard |
| Severity/confidence tests | Prevent noisy or inflated findings                          |
| Snapshot diff tests       | Ensure historical change tracking is deterministic          |
| Export validation tests   | Verify workbook/JSON/HTML output integrity                  |
| Care packet fixture tests | Ensure escalation bundles contain the expected evidence set |

---

# Suggested Delivery Sequence

## Near-term priority

1. Build the rule registry and source/provenance model for best-practice sentinel checks
2. Add automated audit-log signal interpretation for change, churn, and admin-risk events
3. Finish the remaining unimplemented Phase 1 correlation checks
4. Expand weekly drift signaling across configuration, role, and topology domains
5. Add timeline appendix generation for Care packets
6. Promote sentinel rollups into a true triage-first workbook layout

## Mid-term priority

1. Best-practice exception management
2. Suspect release window / regression chain builder
3. Domain health scoring
4. Cross-sheet evidence linking
5. CX as Code / managed-change awareness

## Long-term priority

1. Rule-pack extensibility
2. HTML evidence reports
3. Advanced trend analytics
4. Customer-tunable risk models
5. ElasticSearch export and downstream indexing

---

# Definition of Success

Genesys Cloud Auditor is successful when it can reliably produce findings that are:

- **clear enough for admins to fix**
- **credible enough for engineers to trust**
- **structured enough for automation to consume**
- **evidence-rich enough for Genesys Care escalation**
- **historically grounded enough to distinguish noise from chronic problems**

At that point, the application becomes more than an auditor:
it becomes a tenant integrity and platform anomaly investigation tool.
