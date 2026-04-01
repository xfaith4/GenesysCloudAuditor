# Genesys Cloud Best Practices Reference

Version: 0.3.0
Generated: 2026-04-01

## Purpose

This reference defines a human-readable and machine-referenceable set of Genesys Cloud best practices for use in:

- operator guidance
- audit reporting
- analyzer-to-guidance mapping
- future rule automation
- governance dashboards

The catalog combines in-session research and live web research sourced from official Genesys Cloud documentation (onboarding and predictive dialing best practices), the Genesys Cloud Terraform provider README, and the Genesys Cloud SDK READMEs (JavaScript and Python).

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

Total entries: 49 (26 original + 23 added 2026-04-01)

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
- predictive campaign Set Stage requirement
- minimum effective agent pool for predictive accuracy
- contact list randomization for steady call rates
- dedicated per-media-type queues for workitem routing

### API

Focus areas:
- explicit 429 handling
- notifications over polling
- bulk operations for scale
- minimum necessary OAuth scope
- PKCE-only authentication in browser contexts
- no preview APIs in production
- SDK version currency
- PII-safe logging defaults
- async endpoint for large historical analytics queries
- subscription (not polling) for outbound campaign state

### EdgeSite

Focus areas:
- N+1 resilience
- primary/secondary Edge registration paths
- compatible capacity classes
- low-latency and low-jitter network design
- active/active SIP trunk group configuration with dedicated per-Edge trunks

### Architect

Focus areas:
- build for the organization rather than from samples alone
- clear naming
- explicit schedule group timezone handling
- validation and versioning discipline
- explicit failure paths on all failable actions
- flow size kept below publish threshold using common modules
- bot intent training minimum 20–30 examples per intent
- Queue ID preferred over Queue Name in conditional script logic

### Security

Focus areas:
- least privilege
- custom roles over broad admin grants
- role catalog control
- deliberate division scoping
- MFA enforced for all administrative users via IdP for SSO users
- proactive system status monitoring subscription
- SSO MFA enforced at identity provider, not only at Genesys Cloud

### Telephony

Focus areas:
- proactive limit monitoring
- structured DID/extension ownership
- correct group ring addressability
- auto-answer enabled for predictive outbound queues
- persistent connections for Polycom and WebRTC outbound agents
- concurrent campaign and CPS limits awareness and scheduling

### DevOps

Focus areas:
- CX as Code for repeatability
- CI/CD promotion
- Terraform-backed deployment where practical
- no hardcoded Terraform credentials (use env vars or vault)
- deliberate eventual consistency checker configuration

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
