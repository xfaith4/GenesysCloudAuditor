# Example: Ext_Assign_vs_Profile

**Sheet name:** `Ext_Assign_vs_Profile`  
**Severity:** 🟡 Warning  
**Purpose:** Reports extensions that exist in only one of the two data sources — user profiles or telephony assignments. These represent gaps between what users claim and what is provisioned in telephony.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `ExtensionKey` | string | Normalized canonical extension |
| `RawExtension` | string | Original raw value from the source |
| `Source` | string | `ProfileOnly` — extension on a profile but not in telephony; `AssignmentOnly` — extension in telephony with no matching user profile |
| `UserId` | string (GUID) | User ID (populated for `ProfileOnly` rows) |
| `UserName` | string | User display name (populated for `ProfileOnly` rows) |
| `UserEmail` | string | User email (populated for `ProfileOnly` rows) |
| `UserState` | string | `active` or `inactive` (populated for `ProfileOnly` rows) |
| `AssignmentId` | string | Telephony assignment record ID (populated for `AssignmentOnly` rows) |
| `AssignedToEntityId` | string (GUID) | Assigned entity ID (populated for `AssignmentOnly` rows) |
| `AssignedToEntityType` | string | Assigned entity type (populated for `AssignmentOnly` rows) |

---

## Sample Output

| ExtensionKey | RawExtension | Source | UserId | UserName | UserEmail | UserState | AssignmentId | AssignedToEntityId | AssignedToEntityType |
|---|---|---|---|---|---|---|---|---|---|
| `5500` | `5500` | `ProfileOnly` | `d1e2f3a4-1111-4abc-8010-aaaaaaaaaaaa` | Laura Chen | laura.chen@example.com | active | — | — | — |
| `5501` | `5501` | `ProfileOnly` | `e2f3a4b5-2222-4bcd-8011-bbbbbbbbbbbb` | Mark Jimenez | mark.jimenez@example.com | inactive | — | — | — |
| `6100` | `6100` | `AssignmentOnly` | — | — | — | — | `assign-10001` | `f4a5b6c7-3333-4cde-8012-cccccccccccc` | user |
| `6201` | `6201` | `AssignmentOnly` | — | — | — | — | `assign-10002` | `a5b6c7d8-4444-4def-8013-dddddddddddd` | station |

---

## Interpretation

### ProfileOnly Rows

Extension `5500` (Laura Chen) and `5501` (Mark Jimenez) appear on user profiles but have no corresponding entry in the telephony assignment system.

**Common causes:**
- The extension was deleted from telephony but the user profile was not updated.
- The profile was manually edited without a corresponding telephony provisioning step.
- The org uses a telephony model where extensions are assigned differently (e.g., via a station, not directly to the user).

**Risk:** Calls to these extensions will not route to the intended user.

### AssignmentOnly Rows

Extensions `6100` and `6201` exist in telephony assignments but no user profile claims them.

**Common causes:**
- Provisioned extensions waiting to be assigned to users.
- Extensions for stations, conference rooms, or shared devices with no user profile.
- Users whose profiles were cleared without deprovisioning the telephony assignment.

**Risk:** Lower — these may be intentional (shared resources), but should be verified.

---

## Remediation

**For ProfileOnly findings:**
- If the extension should exist: recreate it in Genesys Cloud telephony (Telephones → Extensions).
- If the extension is stale: clear the Work Phone extension field on the user's profile.

**For AssignmentOnly findings:**
- If the assignment is for a user: update the user's profile to reflect the extension.
- If the assignment is for a shared resource (station, room): no action needed — document this as expected.
- If the assignment is orphaned: deprovision the extension in telephony.
