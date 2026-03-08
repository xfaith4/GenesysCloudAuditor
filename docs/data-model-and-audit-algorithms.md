# Data Model and Audit Algorithms

This document defines the conceptual data model and comparison logic used by the audit engine.

Audience: developers and architects.

Use this guide when changing findings logic or adding new audit dimensions.

## Source Data

Primary extension consistency audits rely on:

- users dataset (profile extension source)
- extension assignments dataset (telephony assignment source)

Additional audit paths (groups, queues, flows, DIDs, logs/events) are handled in parallel workflows but follow the same orchestration pattern.

## Canonical Model

```text
Raw sources
|-- User profile extension value
`-- Assigned extension value

Normalization
`-- Canonical extension key

Findings
|-- Duplicate profile extensions
|-- Profile extensions not assigned
|-- Duplicate assigned extensions
|-- Assigned extensions missing from profiles
|-- Invalid profile extensions
`-- Invalid assigned extensions
```

## Core Entities

| Entity | Key fields |
| --- | --- |
| User profile extension record | user identity, state, raw extension, normalized key |
| Assigned extension record | assignment identity, target type/id, raw extension, normalized key |
| Finding record | normalized key plus impacted users/assignments |

## Set and Group Operations

- Profile duplicates: group profile records by normalized key, count > 1.
- Assigned duplicates: group assignment records by normalized key, count > 1.
- Profile not assigned: profile key set minus assignment key set.
- Assigned missing profile: assignment key set minus profile key set.

All operations exclude invalid normalization results.

## IncludeInactive Interaction

When inactive users are excluded, profile-source group/set operations must operate only on active users.

When included, inactive users participate fully in profile-based findings.

## Reporting Principles

- Keep raw values for operator traceability.
- Keep normalized keys for deterministic comparison.
- Separate invalid data quality findings from mismatch findings.

## Related Documents

- [normalization policy](extension-normalization-policy.md)
- [requirements baseline](requirements-inactive-pagination-normalization.md)
- [setup and operations guide](setup-and-operations.md)

