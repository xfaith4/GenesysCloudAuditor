# Extension Collision Test Specification

This document defines high-value test scenarios for extension collision and invalid-value handling.

Audience: QA and developers writing automated tests.

Use this guide when validating normalization outcomes and duplicate/unassigned findings.

## Scenario Set

| Scenario | Input pattern | Expected outcome |
| --- | --- | --- |
| Prefix and separator normalization | `ext.1001`, `x1001`, `EXTENSION 1001` | Normalize to same key and report duplicate profile extension |
| Assignment duplicate normalization | `1002`, `1-0-0-2`, `ext 1002` | Normalize to same key and report duplicate assigned extension |
| Null/empty profile values | `null`, empty string, whitespace | Excluded from duplicate/unassigned; not treated as invalid |
| Non-empty invalid profile value | `12#34` under strict numeric mode | Report in invalid profile section |
| Leading zero policy preserve | `0012` and `12` with preserve enabled | Distinct keys, no duplicate |
| Leading zero policy trim | `0012` and `12` with preserve disabled | Same key, duplicate |
| Include inactive off | Active + inactive same extension | Inactive excluded from profile duplicate logic |
| Include inactive on | Active + inactive same extension | Both included, duplicate reported |
| Normalized join key match | `ext 3003` profile vs `3003` assignment | Not unassigned after normalization |

## Assertion Guidance

For each scenario assert:

1. Normalized key values.
2. Finding count per category.
3. Identity of impacted users/assignments.
4. `TotalUsersConsidered` behavior when inactive filtering changes.

## Implementation Notes

- Keep fixture data minimal to isolate each rule.
- Use explicit option objects in tests; avoid relying on defaults.
- Add regression tests whenever normalization rules change.

## Related Documents

- [normalization policy](extension-normalization-policy.md)
- [data model and algorithm design](data-model-and-audit-algorithms.md)
- [QA matrix](detailed-qa-matrix.md)

