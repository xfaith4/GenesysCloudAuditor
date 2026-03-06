# Audit Checks

## Overview

Each audit run fetches data from the Genesys Cloud API, normalizes it to canonical form, and performs cross-reference checks. Findings are exported to a multi-sheet Excel workbook.

---

## API Endpoints Consumed

| Data | Endpoint | State Filter |
|---|---|---|
| Users | `GET /api/v2/users?pageSize={n}&pageNumber={p}` | `&state=active` when `IncludeInactive=false` |
| Edge extensions | `GET /api/v2/telephony/providers/edges/extensions?pageSize={n}&pageNumber={p}` | None |

All endpoints are paginated. The auditor fetches every page sequentially until `pageCount` is exhausted.

---

## Extension Normalization

Before any comparison, raw extension strings from both sources are passed through a deterministic normalization pipeline. This ensures that values like `"ext. 0123"` and `"0123"` resolve to the same canonical key.

### Pipeline Steps

| Step | Action |
|---|---|
| 1 | Trim leading/trailing whitespace |
| 2 | Uppercase (for alphanumeric comparison consistency) |
| 3 | Strip common "ext" prefixes: `X`, `EXT`, `EXT.`, `EXTENSION` |
| 4 | Remove separators: spaces, `-`, `.`, `(`, `)` |
| 5 | Validate: empty, non-digit-only, length bounds |
| 6 | Apply leading-zero policy (configurable: preserve or strip) |

### Configuration (`ExtensionNormalizationOptions`)

| Option | Default | Description |
|---|---|---|
| `DigitsOnly` | `true` | Extract digits only; reject alphanumeric values |
| `PreserveLeadingZeros` | `true` | Keep `"0123"` distinct from `"123"` |
| `StripExtensionPrefixes` | `true` | Remove `ext`, `x`, `extension` markers |
| `RemoveCommonSeparators` | `true` | Remove spaces, dashes, dots, parentheses |
| `MinLength` | `2` | Minimum digit length after normalization |
| `MaxLength` | `10` | Maximum digit length after normalization |

### Normalization Result

`ExtensionNormalizationResult` carries:

| Field | Description |
|---|---|
| `IsOk` | Whether the value is valid and usable as a join key |
| `Normalized` | The canonical extension key (e.g., `"1234"`) |
| `Status` | `Ok`, `Empty`, `WhitespaceOnly`, `NonDigitOnly`, `TooShort`, `TooLong` |
| `Notes` | Human-readable explanation (shown in export) |

---

## Audit Checks Reference

### Duplicate Profile Extensions — `Ext_Duplicates_Profile`

**Severity:** Critical

**What:** Multiple users have the same value in their Work Phone extension field (after normalization).

**Why it matters:** Duplicate extensions cause call routing ambiguity, failed provisioning, and inaccurate reporting.

**Export columns:** `ExtensionKey`, `RawExtension`, `UserId`, `UserName`, `UserState`

---

### Extension Ownership Mismatch — `Ext_Ownership_Mismatch`

**Severity:** Critical — Platform Bug Detection

**What:** A user's profile extension exists in the telephony assignment list, but the assignment record shows the extension is owned by a **different entity** than the user whose profile claims it.

**Why it matters:** This is a known Genesys Cloud platform synchronization bug. The user profile layer and the telephony assignment layer are out of sync. Calls may be misrouted or the user may be unreachable despite having an extension on their profile.

**Common causes:**
- Extension recycled in telephony but old user's profile not cleared
- Manual provisioning inconsistency
- Genesys Cloud sync failure between profile and telephony subsystems

**Export columns:** `ExtensionKey`, `ProfileUserId`, `ProfileUserName`, `AssignedToEntityId`, `AssignedToEntityType`

---

### Extension Assignment vs. Profile — `Ext_Assign_vs_Profile`

**Severity:** Warning

**What:** Extensions present in telephony assignments but absent from user profiles, or present on user profiles but absent from telephony assignments.

**Sub-checks:**
- **Profile-only:** Extension on a user profile with no corresponding telephony assignment
- **Assignment-only:** Telephony assignment with no corresponding user profile extension

**Export columns:** `ExtensionKey`, `Source` (`ProfileOnly` / `AssignmentOnly`), `UserId`, `UserName`, `AssignId`

---

### Invalid Extensions — `Invalid_Extensions`

**Severity:** Warning

**What:** Malformed or non-numeric extension values found on user profiles or in telephony assignments.

**Common causes:** Data entry errors, full phone numbers entered in extension fields, legacy formatting.

**Export columns:** `Source`, `RawValue`, `NormalizationStatus`, `NormalizationNotes`, `EntityId`, `EntityName`

---

### Empty / Single-Member Groups — `Empty_Groups`

**Severity:** Warning

**What:** Groups with zero or one member.

**Export columns:** `GroupId`, `GroupName`, `MemberCount`

---

### Empty / Duplicate Queues — `Empty_Queues`

**Severity:** Warning

**What:** Queues with zero agents, or queues sharing a duplicate name.

**Export columns:** `QueueId`, `QueueName`, `AgentCount`, `IsDuplicateName`

---

### Stale / Unpublished Flows — `Stale_Flows`

**Severity:** Warning

**What:** Architect flows that have not been republished within a configurable threshold.

**Export columns:** `FlowId`, `FlowName`, `FlowType`, `LastPublishedDate`, `DaysSincePublished`

---

### Inactive Users — `Inactive_Users`

**Severity:** Warning

**What:** User accounts that have not been active recently. Only populated when `IncludeInactive=true`.

**Export columns:** `UserId`, `UserName`, `Email`, `State`, `LastLoginDate`

---

### DID Mismatches — `DID_Mismatches`

**Severity:** Warning

**What:** DIDs (Direct Inward Dial numbers) that are unassigned, orphaned, or assigned to inactive users.

**Export columns:** `DID`, `AssignedToUserId`, `AssignedToUserName`, `UserState`, `Issue`

---

### Audit Logs — `Audit_Logs`

**Severity:** Info

**What:** Genesys Cloud audit transaction log entries for the selected time window. Exported only when the **Audit Logs** path is selected.

---

### Operational Events — `Operational_Events`

**Severity:** Info

**What:** Operational event log entries. Exported only when the **Operational Event Logs** path is selected.

---

### Outbound Events — `Outbound_Events`

**Severity:** Info

**What:** Outbound event log entries. Exported only when the **Outbound Events** path is selected.

---

## Summary Sheet

Every export includes a `Summary` sheet with:

| Column | Description |
|---|---|
| `AuditPath` | Name of the check performed |
| `FindingCount` | Number of findings |
| `Severity` | Critical / Warning / Info |
| `RunTimestamp` | UTC timestamp of the audit run |
| `RegionAudited` | Genesys Cloud region |
| `IncludeInactive` | Whether inactive users were included |
| `TotalUsersScanned` | Total users retrieved from the API |
| `TotalExtensionsScanned` | Total telephony extensions retrieved |

---

## Data Model

### UserProfileExtensionRecord

```
UserId          : string (GUID)
UserName        : string
UserState       : string (active / inactive)
WorkPhoneExtRaw : string (raw value from primaryContactInfo)
ExtKeyNormalized: string (normalized canonical key)
NormStatus      : ExtensionNormalizationStatus
NormNotes       : string
```

### AssignedExtensionRecord

```
AssignId        : string
ExtensionRaw    : string
ExtKeyNormalized: string
TargetType      : string (user / station / phoneBase / edgeGroup)
TargetId        : string (GUID of the assigned entity)
```

---

## Edge Cases

| Scenario | Behavior |
|---|---|
| User has no Work Phone extension | Excluded from extension checks |
| Extension is a full E.164 number | Flagged as `Invalid_Extensions` (not treated as a valid short extension) |
| `IncludeInactive=false` | Inactive users excluded from all extension checks |
| `IncludeInactive=true` | Inactive users included in all checks; reported in `Inactive_Users` |
| Leading zeros differ | Treated as distinct extensions when `PreserveLeadingZeros=true` |
| Paginated API returns a different sort order mid-run | Run timestamp recorded; minor drift noted in Summary |

---

## See Also

- [Examples](examples/README.md) — Sample export output for each sheet
- [Authentication](authentication.md) — OAuth and permissions setup
- [QA Test Plan](qa-test-plan.md) — Verification and test matrix
