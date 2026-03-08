# Extension Normalization Policy

This document describes how extension strings are normalized before comparison.

Audience: developers and QA.

Use this guide when adjusting matching logic or interpreting normalization-related findings.

## Purpose

Different sources can represent the same extension differently. Normalization produces a canonical join key used by duplicate and mismatch logic.

## Policy Inputs

The normalizer supports configuration for:

- numeric filtering (`DigitsOnly`)
- alphanumeric allowance (`AllowAlphanumeric`)
- leading-zero behavior (`PreserveLeadingZeros`)
- extension prefix stripping (`StripExtensionPrefixes`)
- common separator removal (`RemoveCommonSeparators`)
- min/max length validation

## Normalization Pipeline

1. Reject null, empty, or whitespace-only values as `Empty`.
2. Trim and uppercase input for stable comparison.
3. Optionally strip known extension prefixes.
4. Apply character filtering/validation according to mode.
5. Apply leading-zero policy.
6. Validate length bounds if configured.
7. Return `Ok` with canonical key or an explicit non-OK status.

## Status Semantics

| Status | Meaning |
| --- | --- |
| `Ok` | Valid normalized key available |
| `Empty` | No usable extension value |
| `InvalidFormat` | Characters violate configured mode |
| `InvalidLength` | Key outside configured length bounds |

## Comparison Rules

- Comparisons are valid only when both sides normalize to `Ok`.
- Non-OK values are excluded from set-join logic and should appear in invalid-value reports.

## Change Control

Any normalization rule change can materially alter audit findings. Required process:

1. Update this document.
2. Add or update collision/regression tests.
3. Re-run detailed QA matrix.

## Related Documents

- [collision test specification](extension-collision-tests.md)
- [requirements for include inactive and pagination](requirements-inactive-pagination-normalization.md)

