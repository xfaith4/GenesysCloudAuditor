# Example: Summary Sheet

**Sheet name:** `Summary`  
**Purpose:** Provides a single-glance health status for the audited Genesys Cloud org, including finding counts per check, run metadata, and scope parameters.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `AuditPath` | string | Name of the audit check performed |
| `FindingCount` | integer | Number of findings for this check |
| `Severity` | string | `Critical`, `Warning`, or `Info` |
| `RunTimestamp` | datetime (UTC) | When the audit run started |
| `RegionAudited` | string | Genesys Cloud API region (e.g., `mypurecloud.com`) |
| `IncludeInactive` | boolean | Whether inactive users were included in the run |
| `TotalUsersScanned` | integer | Total user records retrieved from the API |
| `TotalExtensionsScanned` | integer | Total telephony extension records retrieved |
| `DurationSeconds` | decimal | Wall-clock time for the complete audit run |

---

## Sample Output

| AuditPath | FindingCount | Severity | RunTimestamp | RegionAudited | IncludeInactive | TotalUsersScanned | TotalExtensionsScanned | DurationSeconds |
|---|---|---|---|---|---|---|---|---|
| `Ext_Duplicates_Profile` | 4 | Critical | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |
| `Ext_Ownership_Mismatch` | 2 | Critical | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |
| `Ext_Assign_vs_Profile` | 31 | Warning | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |
| `Invalid_Extensions` | 7 | Warning | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |
| `Empty_Groups` | 5 | Warning | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |
| `Empty_Queues` | 3 | Warning | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |
| `Stale_Flows` | 9 | Warning | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |
| `Inactive_Users` | 0 | Warning | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |
| `DID_Mismatches` | 6 | Warning | 2025-11-14 08:31:02 UTC | mypurecloud.com | false | 1842 | 1786 | 47.3 |

---

## Interpretation

- **Critical findings require immediate action.** Duplicate profile extensions and ownership mismatches directly impact call routing and may cause callers to reach the wrong agent or no agent at all.
- **Warning findings should be investigated.** They may represent stale configuration, deprovisioning gaps, or data quality issues that grow into larger problems over time.
- **Zero findings is the goal.** A healthy org will show `0` for all Critical rows and low counts for Warning rows that reflect expected org maintenance backlogs.
- **`Inactive_Users = 0`** when `IncludeInactive=false` — run the audit with `IncludeInactive=true` to include inactive accounts in the scan.

---

## Healthy Baseline

| Check | Target |
|---|---|
| `Ext_Duplicates_Profile` | **0** |
| `Ext_Ownership_Mismatch` | **0** |
| `Ext_Assign_vs_Profile` | Low — investigate profile-only entries |
| `Invalid_Extensions` | **0** — clear malformed values |
| `Empty_Groups` | Low — clean up unused groups |
| `Empty_Queues` | Low — clean up unused queues |
| `Stale_Flows` | Low — republish flows in active use |
| `DID_Mismatches` | **0** — reassign or release orphaned DIDs |
