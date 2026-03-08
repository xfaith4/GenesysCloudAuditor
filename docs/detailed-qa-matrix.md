# Detailed QA Matrix

This document is the detailed execution matrix for end-to-end validation.

Audience: QA and release engineers.

Use this matrix during release readiness testing.

## Test Areas

1. Large-tenant pagination
2. Rate-limit and transient retry behavior
3. Cancellation safety
4. Export integrity
5. UI responsiveness and state transitions

## A. Pagination

### A1. Users endpoint state filter

- Run with `IncludeInactive=false`: every users request includes `state=active`.
- Run with `IncludeInactive=true`: users requests omit `state`.

Pass criteria:

- No missing pages.
- No duplicate pages.
- Total count matches fixture expectation.

### A2. Extensions endpoint high page count

- Simulate high page count (for example 200 pages).

Pass criteria:

- All expected pages fetched.
- No deadlock or thread starvation.

### A3. Page-size bounds

- Input values: `0`, `-1`, `9999`, `1`, `500`.

Pass criteria:

- UI clamps to valid bounds.
- Runtime requests use clamped value.

## B. Rate Limit and Retry

### B1. 429 with Retry-After

Pass criteria:

- Delay follows `Retry-After`.
- Request eventually succeeds within retry limits.

### B2. 429 without Retry-After

Pass criteria:

- Exponential backoff with jitter occurs.
- Successful completion within retry policy bounds.

### B3. Sustained 429

Pass criteria:

- Run fails with actionable error.
- UI exits running state cleanly.

## C. Cancellation

### C1. Cancel during first page fetch

Pass criteria:

- Status transitions to canceled.
- No crash.

### C2. Cancel during concurrent or queued page requests

Pass criteria:

- Outstanding tasks observe cancellation promptly.
- No deadlock.

### C3. Cancel during retry delay

Pass criteria:

- Delay is interruptible.
- Result is canceled, not failed.

## D. Export Integrity

### D1. Workbook generation

Pass criteria:

- Workbook created.
- Sheets exist for selected audits.

### D2. Row and summary consistency

Pass criteria:

- Summary counts align with detailed sheets.
- Flattened detail rows map correctly to findings.

### D3. Special character handling

Pass criteria:

- Names and fields with commas/quotes/newlines remain readable in Excel.

## E. UI Behavior

### E1. Command enablement

Pass criteria:

- Start disabled while run is active.
- Cancel enabled only while active.
- Export enabled only after successful report generation.

### E2. Responsiveness

Pass criteria:

- Window remains interactive during long runs.
- Progress and status update without freezing.

## Exit Criteria

A release candidate passes only when all critical scenarios above are green and no unresolved critical defects remain.

## Related Documents

- [QA strategy](../QA.md)
- [run audit workflow](run-audit-workflow.md)

