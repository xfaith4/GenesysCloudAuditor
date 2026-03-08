# QA Test Plan

## Scope

This document defines the end-to-end quality assurance test matrix for GenesysCloudAuditor. It covers pagination correctness, rate-limit handling, cancellation safety, export accuracy, and UI responsiveness.

---

## Test Matrix

### A — Pagination

| ID | Test | Pass Criteria |
|---|---|---|
| A1 | Users pagination with `IncludeInactive=false` | Every request contains `&state=active`; no missing or duplicate pages |
| A2 | Users pagination with `IncludeInactive=true` | No `state=` parameter in any request |
| A3 | Extensions pagination across 200+ pages | All pages fetched exactly once; result count matches expected total |
| A4 | PageSize boundary values (`0`, `-1`, `9999`, `1`, `500`) | Values clamped to `[1..500]`; audit uses clamped value |
| A5 | Bounded parallelism | Peak concurrent in-flight requests never exceeds `MaxParallelRequests` |

---

### B — Rate Limiting

| ID | Test | Pass Criteria |
|---|---|---|
| B1 | 429 with `Retry-After` header | Wait time matches header value (±tolerance); eventual success |
| B2 | 429 without `Retry-After` | Exponential backoff applied; succeeds within `MaxRetries` |
| B3 | Sustained 429 exceeding `MaxRetries` | Audit fails with clear error; `IsRunning` resets; Start re-enabled |
| B4 | Mixed 502/503 transient errors | Retries occur; correct final result or actionable error surfaced |

---

### C — Cancellation

| ID | Test | Pass Criteria |
|---|---|---|
| C1 | Cancel during page 1 fetch | Status becomes "Audit cancelled."; no partial export written; no crash |
| C2 | Cancel during parallel page fetch | All in-flight tasks observe cancellation; run terminates cleanly |
| C3 | Cancel while waiting on `Retry-After` delay | Cancel interrupts the wait immediately; run ends as cancelled, not failed |
| C4 | Rapid / double Cancel clicks | No exceptions; state remains consistent |

---

### D — Export Correctness

| ID | Test | Pass Criteria |
|---|---|---|
| D1 | All expected sheets present | `Summary`, `Ext_Duplicates_Profile`, `Ext_Ownership_Mismatch`, `Ext_Assign_vs_Profile`, `Invalid_Extensions`, `Empty_Groups`, `Empty_Queues`, `Stale_Flows`, `Inactive_Users`, `DID_Mismatches` all created |
| D2 | CSV headers correct and stable | First row of each file matches defined schema exactly |
| D3 | Row counts correct | Rows equal sum of individual finding detail rows |
| D4 | UTF-8 BOM present | File begins with bytes `EF BB BF` |
| D5 | CSV quoting for special characters | Commas, quotes, and newlines in data are RFC-correctly escaped |
| D6 | Overwrite behavior | `Overwrite=false` raises `IOException`; `Overwrite=true` replaces existing files |

---

### E — UI Responsiveness

| ID | Test | Pass Criteria |
|---|---|---|
| E1 | Start/Cancel command enablement | Start enabled ↔ Cancel disabled; inverted during run; both reset on completion |
| E2 | Progress updates during long run | UI thread stays responsive; `ProgressPercent` and `StatusMessage` update; window remains movable |
| E3 | Large results rendering | Thousands of detail rows load without extreme lag; scrolling in result DataGrids is smooth |
| E4 | Error surfacing | On API failure (401/403/500), `ErrorMessage` is populated; app remains usable |

---

## Automation Recommendations

### Infrastructure Integration Tests (Recommended)

Extend `ApiClientIntegrationTests` with a configurable mock handler that can:

- Simulate `pageCount` up to 200+
- Introduce per-route response delays
- Emit `429` with or without `Retry-After`
- Track the maximum number of concurrent in-flight requests

**Key assertions:**
- Call count per page number (exactly 1)
- Peak concurrency ≤ configured `MaxParallelRequests`
- Total duration roughly matches expected given `Retry-After` delays
- Cancellation results in fewer total calls and terminates quickly

### Application Layer Tests (Recommended)

- Test `IAuditRunner.RunAsync` with a mock orchestrator verifying `CancellationToken` propagation.
- Confirm that cancellation surfaces as `OperationCanceledException` (not a generic failure).

### UI Automation (Optional)

Manual smoke testing is sufficient initially. If automating, use **WinAppDriver** or **FlaUI** to verify:

- Start/Cancel button state transitions
- Window responsiveness during a simulated long-running audit
- Error message display on injected API failure

---

## Acceptance Criteria

| Area | Exit Condition |
|---|---|
| Pagination | No missing pages; correct `state` query parameter behavior in both modes |
| Rate limits | Retries obey `Retry-After`; bounded retry count; no infinite loops |
| Cancellation | Cancels within <1 second of request for mock delays; no partial export; no resource leaks |
| Export | All expected sheets generated; correct headers, rows, and UTF-8 BOM; special characters properly quoted |
| UI | No UI-thread blocking; command states consistent; results view usable for large tenant outputs |

---

## See Also

- [Audit Checks](audit-checks.md) — Definition of each audit type and expected output
- [Examples](examples/README.md) — Sample export output for visual verification
