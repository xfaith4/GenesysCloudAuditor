# Roadmap
Version: 0.2.0  
Generated: 2026-03-31

## Objective

Move the best-practices starter pack from a useful reference set into a production-grade guidance, detection, and reporting system.

## Current state

The package now contains:

- readable guide
- strict machine-readable catalog
- strict schema
- map file
- glossary layer
- source traceability starter fields
- detection/remediation starter fields
- ownership and lifecycle metadata

## Roadmap themes

### 1. Source traceability hardening
Add precise external citations and internal provenance at the rule level.

Deliverables:
- `source_urls` populated with authoritative references
- `source_refs` aligned to internal notes and review artifacts
- `last_verified` review process
- optional `verification_owner`

Why:
The application should be able to explain where a rule came from and when it was last reviewed.

### 2. Detection logic maturity
Convert narrative detection hints into actual rule logic contracts.

Deliverables:
- `detection_strategy` refinement
- `required_inputs` normalization
- `automatable` review by rule
- optional `detection_query_template`
- optional `evidence_requirements`

Why:
The catalog should not only define what good looks like, but also how to test for it.

### 3. Remediation maturity
Turn best-practice entries into actionable operator guidance.

Deliverables:
- `recommended_action_short`
- `recommended_action_detailed`
- optional `recommended_action_cli`
- optional `recommended_action_runbook`
- optional `estimated_effort`
- optional `change_risk`

Why:
Reports should be able to tell an operator what to do next, not just what is wrong.

### 4. Evidence model
Define what proof supports a finding and what proof clears it.

Deliverables:
- `evidence_examples`
- `sample_bad_state`
- `sample_good_state`
- optional `evidence_schema`
- optional `evidence_attachment_types`

Why:
Findings become substantially more trustworthy when they carry expected evidence patterns.

### 5. Ownership and accountability
Bind each rule to an operational owner.

Deliverables:
- `owner_role`
- `owner_team`
- `review_cadence`
- optional `service_owner`
- optional `executive_owner`

Why:
Guidance without ownership tends to stall.

### 6. False positive and exception handling
Support practical operations without weakening standards.

Deliverables:
- `false_positive_notes`
- `exceptions`
- `risk_acceptance_allowed`
- optional `compensating_controls`
- optional `exception_expiration_required`

Why:
Some findings are situational; the model should support valid exceptions cleanly.

### 7. Control grouping and dashboards
Create reporting hierarchies suitable for scorecards and executive views.

Deliverables:
- `control_family`
- `pillar`
- `report_category`
- optional maturity scores per family
- optional dashboard rollups

Why:
The app should be able to summarize by control family, risk pillar, or operating area.

### 8. Mapping completeness
Complete the link between analyzer outputs and catalog rules.

Deliverables:
- expand `best-practices-map.json`
- align to exact analyzer result names
- add one-to-many mappings where required
- add severity override support
- add default remediation text per finding type

Why:
This is the bridge between detection and guidance.

### 9. Schema tightening
Keep the artifacts reliable and safe for automation.

Deliverables:
- strict entry schema
- map schema
- glossary schema
- optional versioned schema migration notes

Why:
Loose schemas allow silent drift and reduce confidence in automation.

### 10. Glossary expansion
Separate terms from rules and create a reusable help layer.

Deliverables:
- `glossary.json`
- `Glossary.md`
- optional UI help text
- optional synonym support

Why:
Operators need definitions; applications need consistent terminology.

### 11. Coverage expansion by Genesys domain
Extend the catalog into adjacent operational domains.

Planned domains:
- Users and utilization
- Roles and permission inheritance
- OAuth clients and access reviews
- Flows, prompts, and data actions
- Trunks, sites, phones, and survivability
- Outbound campaign governance
- Audit retention and evidence preservation
- Reporting and observability
- Naming conventions as a separate governance section

Why:
The current scope is strong but not yet comprehensive.

### 12. Versioning and lifecycle metadata
Support controlled evolution of the catalog.

Deliverables:
- `status`
- `introduced_in_version`
- optional `deprecated_in_version`
- optional `superseded_by`
- optional `review_status` workflow

Why:
Rules will evolve; the system needs lifecycle discipline.

## Suggested implementation sequence

### Phase 1 — Analyzer alignment
- finalize rule keys
- align analyzer finding types to the map file
- wire report output to `recommended_action_short`

### Phase 2 — Evidence and remediation
- add evidence expectations to each finding type
- add detailed remediation guidance
- add owner and cadence rollups

### Phase 3 — External source hardening
- populate `source_urls`
- define a verification process
- review each rule for authoritative backing

### Phase 4 — Dashboard and scoring model
- group findings by control family and pillar
- add scorecards and trend views
- introduce maturity scoring

### Phase 5 — Broader domain coverage
- add new rules for users, trunks, prompts, data actions, outbound, and observability
- expand glossary and schemas

## Candidate future files

- `glossary.schema.json`
- `best-practices-evidence.schema.json`
- `best-practices-runbooks.json`
- `best-practices-severity-policy.json`
- `best-practices-dashboard-groups.json`

## Definition of done for production readiness

The package can be considered production-grade when:

- every rule has source traceability
- every mapped finding has remediation text
- every rule has an owner and review cadence
- schemas are strict and versioned
- analyzer outputs map cleanly to rule keys
- evidence expectations are defined
- exceptions and risk acceptance are modeled explicitly
- dashboard rollups are stable and trustworthy
