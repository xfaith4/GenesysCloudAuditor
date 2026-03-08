# Example: Stale_Flows

**Sheet name:** `Stale_Flows`  
**Severity:** 🟡 Warning  
**Purpose:** Lists Architect flows that have not been republished within a configurable staleness threshold. Unpublished flows may be in an error state or contain outdated routing logic that has not been validated against current org configuration.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `FlowId` | string (GUID) | Genesys Cloud Architect flow ID |
| `FlowName` | string | Flow display name |
| `FlowType` | string | `inboundCall`, `inboundChat`, `inboundEmail`, `outboundCall`, `inboundShortMessage`, `workflow`, `bot` |
| `PublishedVersion` | string | Version number of the currently published flow |
| `CheckedInVersion` | string | Most recent checked-in (draft) version |
| `LastPublishedDate` | date | Date the flow was last successfully published |
| `DaysSincePublished` | integer | Calendar days since last publish |
| `Division` | string | Division the flow belongs to |
| `Status` | string | `Active`, `CheckedIn`, `Published`, `Error` |
| `Issue` | string | `Unpublished` — never published; `Stale` — not republished within threshold |

---

## Sample Output

| FlowId | FlowName | FlowType | PublishedVersion | CheckedInVersion | LastPublishedDate | DaysSincePublished | Division | Status | Issue |
|---|---|---|---|---|---|---|---|---|---|
| `d9e0f1a2-1111-4abc-9050-aaaaaaaaaaaa` | Main IVR | inboundCall | 14.0 | 17.0 | 2024-03-12 | 238 | Home | Published | `Stale` |
| `e0f1a2b3-2222-4bcd-9051-bbbbbbbbbbbb` | Chat Welcome Bot | inboundChat | 3.0 | 5.0 | 2024-01-05 | 306 | Operations | Published | `Stale` |
| `f1a2b3c4-3333-4cde-9052-cccccccccccc` | After-Hours Routing | inboundCall | — | 2.0 | — | — | Finance | CheckedIn | `Unpublished` |
| `a2b3c4d5-4444-4def-9053-dddddddddddd` | Outbound Campaign Script | outboundCall | 1.0 | 1.0 | 2023-08-30 | 472 | Sales | Published | `Stale` |

---

## Interpretation

### Stale

`Main IVR` has been published, but 238 days have passed since the last publish. Meanwhile, the checked-in version (17.0) is 3 versions ahead of the published version (14.0). This means changes have been made but not deployed — callers are receiving the older routing behavior.

### Unpublished

`After-Hours Routing` has never been published. It exists in draft form but is not active. If this flow is referenced by a schedule or IVR, it will cause routing failures.

### Threshold Configuration

The staleness threshold (default: **180 days**) can be adjusted in `appsettings.json`:

```json
{
  "AuditOptions": {
    "StaleFlowThresholdDays": 180
  }
}
```

---

## Remediation

1. **Stale flows:** Review the unpublished changes in Architect. If the changes are complete and tested, publish the flow. If the draft is a work-in-progress, document it and set a target publish date.
2. **Unpublished flows:** Determine whether the flow is intended to be live. If yes, complete and publish it. If it was abandoned, delete the draft to keep the flow library clean.
3. Flows with a `Status` of `Error` should be inspected in Architect immediately — they may be causing active routing failures.
