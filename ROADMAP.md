# Genesys Cloud Auditor — Product Roadmap

This roadmap defines the evolution of **Genesys Cloud Auditor** from a configuration audit tool into a **correlation-driven health, integrity, and escalation workbench** for Genesys Cloud organizations.

The application’s purpose is to:

- detect tenant misconfigurations, inconsistencies, and hygiene issues
- identify probable platform synchronization or integrity defects
- correlate related data across multiple Genesys Cloud API domains
- produce actionable remediation guidance for internal change management teams
- generate evidence-rich escalation packets for **Genesys Care** when findings appear platform-side rather than customer-side

This roadmap builds on the current implemented checks for extension integrity, DID mismatches, queue/group hygiene, stale flows, inactive-user signals, and exported audit/operational/outbound events.

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
| Audit log export                               | Implemented   |
| Operational event export                       | Implemented   |
| Outbound event export                          | Implemented   |
| Excel workbook export                          | Implemented   |
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

| Check                             | Priority | Description                                                                               |
| --------------------------------- | -------- | ----------------------------------------------------------------------------------------- |
| User telephony completeness       | High     | User appears active and telephony-enabled but lacks a coherent station/extension/DID path |
| DID ownership mismatch            | High     | A DID is associated to a user profile but assigned elsewhere in telephony                 |
| Station–user assignment conflict  | High     | A station references a user, but user/profile/telephony state disagrees                   |
| User–site telephony contradiction | Medium   | User location/site and telephony resource site do not align                               |
| Ghost telephony assignment        | High     | Telephony asset references a deleted, inactive, or otherwise invalid user/resource        |
| Multiple ownership contradiction  | High     | Same telephony asset appears attributable to more than one active identity                |

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

| Check                                      | Priority | Description                                                                                   |
| ------------------------------------------ | -------- | --------------------------------------------------------------------------------------------- |
| Queue with non-serviceable membership      | High     | Queue has members on paper, but no realistically serviceable agents after filters are applied |
| Queue skill mismatch                       | High     | Queue requires skills/languages that none of its members possess                              |
| Queue membership drift                     | Medium   | Queue membership exists, but many members are inactive, unlicensed, or otherwise non-usable   |
| Duplicate semantic queues                  | Medium   | Queues are near-duplicates by name, membership, and routing intent                            |
| Queue with incomplete routing dependencies | High     | Queue is missing downstream config required to actually process interactions                  |

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

| Check                          | Priority | Description                                                                         |
| ------------------------------ | -------- | ----------------------------------------------------------------------------------- |
| IVR–flow binding stale         | High     | Number points to a flow that is deleted, stale, unpublished, or errored             |
| Dead route dependency          | High     | Flow references queue/schedule/destination that no longer exists                    |
| Flow dependency drift          | Medium   | Flow is published, but dependent objects have changed materially since last publish |
| Critical entry-point fragility | Medium   | Important numbers depend on brittle or stale routing chains                         |

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

| Check                          | Priority | Description                                                                |
| ------------------------------ | -------- | -------------------------------------------------------------------------- |
| Trunk–edge assignment orphan   | Medium   | Edge references trunk/site state that no longer reconciles                 |
| Site–edge mismatch             | High     | Site relationship differs across authoritative edge/site resources         |
| Station topology contradiction | Medium   | Station/site/location relationships appear internally inconsistent         |
| DID inventory orphan           | Medium   | DID ranges exist in inventory but do not reconcile to active service paths |

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

| Check                    | Priority | Description                                                                   |
| ------------------------ | -------- | ----------------------------------------------------------------------------- |
| Regression chain builder | High     | Build a timeline showing config changes followed by symptoms/findings         |
| Change adjacency marker  | High     | Surface recent changes that touched related objects within the finding window |
| Suspect release window   | Medium   | Group findings by likely onset window after correlated changes                |

### Example output

“Queue membership changed at 14:03; routing anomalies began at 14:07; serviceability fell to zero by 14:11.”

---

## 2.2 Flapping and instability detection

### Planned checks

| Check                | Priority | Description                                                                                     |
| -------------------- | -------- | ----------------------------------------------------------------------------------------------- |
| Assignment flapping  | High     | Ownership or assignment repeatedly changes between states                                       |
| Publish churn        | Medium   | Flows repeatedly republished or altered without stabilizing behavior                            |
| Resource oscillation | Medium   | Site/edge/station/trunk relationships repeatedly move between valid and invalid interpretations |

### Why this matters

Flapping is often a signal of automation conflict, admin collision, sync lag, or deeper platform instability.

---

## 2.3 Hot spot ranking

### Planned checks

| Check                      | Priority | Description                                                                        |
| -------------------------- | -------- | ---------------------------------------------------------------------------------- |
| Chronic object ranking     | Medium   | Rank queues, users, sites, and telephony resources by repeated anomaly association |
| Domain instability index   | Medium   | Score routing, telephony, identity, or outbound domains for recurring problems     |
| Blast-radius concentration | Medium   | Identify small sets of objects involved in a disproportionate share of findings    |

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

| Check                             | Priority | Description                                                                                        |
| --------------------------------- | -------- | -------------------------------------------------------------------------------------------------- |
| Persistent contradiction detector | High     | Contradictory API state persists across collections/runs                                           |
| No-local-cause qualifier          | High     | Finding remains after excluding recent tenant changes as an explanation                            |
| Support-readiness scorer          | High     | Finding contains sufficient object IDs, timestamps, evidence, and impact context for case creation |

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

| Output               | Priority | Description                                                  |
| -------------------- | -------- | ------------------------------------------------------------ |
| Support case summary | High     | Human-readable case narrative for Care ticket entry          |
| Evidence JSON        | High     | Machine-readable evidence package for archival or automation |
| Timeline appendix    | Medium   | Event and change history supporting the escalation           |

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
| Severity/confidence tests | Prevent noisy or inflated findings                          |
| Snapshot diff tests       | Ensure historical change tracking is deterministic          |
| Export validation tests   | Verify workbook/JSON/HTML output integrity                  |
| Care packet fixture tests | Ensure escalation bundles contain the expected evidence set |

---

# Suggested Delivery Sequence

## Near-term priority

1. Correlation engine
2. User–station–extension–DID–site integrity audit
3. Queue serviceability audit
4. Flow dependency audit
5. Action model (owner + next step)
6. Support case evidence packet export

## Mid-term priority

1. Snapshot persistence
2. Drift engine
3. Change-to-symptom correlation
4. Flapping detection
5. Topology integrity checks

## Long-term priority

1. Domain health scoring
2. Rule-pack extensibility
3. HTML evidence reports
4. Advanced trend analytics
5. Customer-tunable risk models

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
