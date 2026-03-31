# Genesys Cloud Best Practices Reference
Version: 0.2.0  
Generated: 2026-03-31

## Purpose

This reference defines a human-readable and machine-referenceable set of Genesys Cloud best practices for use in:

- operator guidance
- audit reporting
- analyzer-to-guidance mapping
- future rule automation
- governance dashboards

The source basis for this first pass is the research you provided in-session around queue routing, skills, wrap-up alignment, API behaviors, Edge resilience, Architect lifecycle, telephony limits, least privilege, and CX as Code patterns. fileciteturn1file0

## Design principles

- Stable dotted keys for machine lookup
- Readable titles for reports
- Operational metadata for ownership and remediation
- Detection hints for future analyzers
- Traceability fields for evidence and source review

## Domains

- Queue
- API
- EdgeSite
- Architect
- Security
- Telephony
- DevOps

## Catalog summary

Total entries: 26

## Entry layout

Each catalog entry contains:

- identity: key, domain, subcategory
- reporting structure: control family, pillar, report category
- narrative guidance: title, summary, why it matters
- target state: recommended state, anti-pattern
- operational fields: severity, auditability, owner, cadence
- automation fields: detection strategy, inputs, logic hint
- remediation fields: short action, detailed action, rollback notes
- evidence fields: evidence examples, sample bad state, sample good state
- governance fields: false positive notes, exceptions, risk acceptance
- lifecycle fields: status, introduced version, review status

## Domain highlights

### Queue

Focus areas:
- intentional routing method selection
- scoring changes only during empty-queue windows
- meaningful skill governance
- division-aligned wrap-up taxonomy

### API

Focus areas:
- explicit 429 handling
- notifications over polling
- bulk operations for scale
- minimum necessary OAuth scope

### EdgeSite

Focus areas:
- N+1 resilience
- primary/secondary Edge registration paths
- compatible capacity classes
- low-latency and low-jitter network design

### Architect

Focus areas:
- build for the organization rather than from samples alone
- clear naming
- explicit schedule group timezone handling
- validation and versioning discipline

### Security

Focus areas:
- least privilege
- custom roles over broad admin grants
- role catalog control
- deliberate division scoping

### Telephony

Focus areas:
- proactive limit monitoring
- structured DID/extension ownership
- correct group ring addressability

### DevOps

Focus areas:
- CX as Code for repeatability
- CI/CD promotion
- Terraform-backed deployment where practical

## Recommended next implementation steps

1. Map your real analyzer outputs to `best-practices-map.json`.
2. Add source URLs as you formalize external references.
3. Expand glossary terms into a true user-facing help layer.
4. Add report templates that consume `recommended_action_short`.
5. Add analyzer logic that emits evidence aligned to `evidence_examples`.

## File relationships

- `best-practices.catalog.json` is the canonical rule source.
- `BestPractices.md` is the readable narrative companion.
- `best-practices-map.json` maps findings to rules.
- `glossary.json` defines reusable terms.
- `Roadmap.md` captures the maturity path from starter to production-grade system.
