# Requirements: Inactive Users, Pagination, and Normalization

This document captures functional requirements that directly affect finding accuracy.

Audience: product owner, developers, and QA.

Use this guide when confirming behavior changes that can alter audit output.

## Include Inactive Behavior

When `IncludeInactiveUsers` is:

- `false`: users endpoint must include `state=active`.
- `true`: users endpoint must omit the state parameter.

Required consistency rule:

- The same inclusion choice must apply to all profile-based computations (duplicates, unassigned, invalid profile extension counts).

## Pagination Requirements

Applies to users and extensions endpoints.

Required behavior:

1. Fetch all pages.
2. Stop only when pagination metadata or empty page indicates completion.
3. Retry transient failures without skipping page numbers.
4. Fail run with actionable error if page retrieval cannot complete reliably.

Validation points:

- No duplicate or missing pages.
- Correct page-size clamping.
- Stable behavior under high page counts.

## Extension Normalization Requirements

Both profile and assignment values must be normalized before comparison.

Minimum required rules:

1. Trim whitespace.
2. Optional prefix stripping (`x`, `ext`, `extension`).
3. Character filtering or strict validation based on configured mode.
4. Configurable leading-zero behavior.
5. Length validation when bounds are configured.

Invalid values must be reported separately and excluded from unassigned set logic.

## Duplicate and Unassigned Definitions

- Duplicate profile extension: same normalized key on multiple users.
- Duplicate assigned extension: same normalized key on multiple assignment records.
- Profile extension not assigned: valid profile key absent from assignment key set.

Use normalized keys for all joins.

## Open Decisions Requiring Stakeholder Confirmation

- Whether leading zeros are semantically distinct in the target tenant.
- Whether alphanumeric extensions are valid.
- Whether duplicates across different assignment target types are acceptable.

## Related Documents

- [normalization policy](extension-normalization-policy.md)
- [algorithm design](data-model-and-audit-algorithms.md)
- [QA matrix](detailed-qa-matrix.md)

