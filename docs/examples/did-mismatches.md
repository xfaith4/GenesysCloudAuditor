# Example: DID_Mismatches

**Sheet name:** `DID_Mismatches`  
**Severity:** 🟡 Warning  
**Purpose:** Lists Direct Inward Dial (DID) numbers that are unassigned, orphaned, or assigned to inactive users. DIDs are the external-facing phone numbers in a Genesys Cloud telephony configuration.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `DID` | string | The DID phone number (E.164 format) |
| `DIDId` | string (GUID) | Genesys Cloud DID record ID |
| `PoolName` | string | DID pool the number belongs to |
| `AssignmentType` | string | `User`, `IVR`, `Unassigned` |
| `AssignedToUserId` | string (GUID) | User ID if assigned to a user (null otherwise) |
| `AssignedToUserName` | string | User display name if assigned to a user |
| `AssignedToUserEmail` | string | User email if assigned to a user |
| `AssignedToUserState` | string | `active` or `inactive` (populated for user assignments) |
| `AssignedToIVR` | string | IVR name if assigned to an IVR route (null otherwise) |
| `Issue` | string | `Unassigned` — DID not assigned to anything; `InactiveUser` — assigned to an inactive user; `OrphanedIVR` — assigned IVR no longer exists |

---

## Sample Output

| DID | DIDId | PoolName | AssignmentType | AssignedToUserId | AssignedToUserName | AssignedToUserEmail | AssignedToUserState | AssignedToIVR | Issue |
|---|---|---|---|---|---|---|---|---|---|
| `+15559001001` | `f7a8b9c0-1111-4abc-9070-aaaaaaaaaaaa` | US-Primary | Unassigned | — | — | — | — | — | `Unassigned` |
| `+15559001042` | `a8b9c0d1-2222-4bcd-9071-bbbbbbbbbbbb` | US-Primary | User | `b9c0d1e2-3333-4cde-9072-cccccccccccc` | Tom Eriksson | tom.eriksson@example.com | inactive | — | `InactiveUser` |
| `+15559001105` | `c0d1e2f3-4444-4def-9073-dddddddddddd` | US-Primary | User | `d1e2f3a4-5555-4efg-9074-eeeeeeeeeeee` | Priya Sharma | priya.sharma@example.com | active | — | — |
| `+15559001200` | `e2f3a4b5-6666-4fab-9075-ffffffffffff` | US-Primary | IVR | — | — | — | — | Legacy Holiday IVR | `OrphanedIVR` |

---

## Interpretation

### Unassigned

`+15559001001` exists in the DID pool but is not assigned to any user or routing entry. The number is provisioned and costing money but doing nothing. Inbound calls to this number will likely reach a default error treatment.

### InactiveUser

`+15559001042` is assigned to **Tom Eriksson**, whose account is inactive. Calls to this number will not reach a live agent. If Tom left the organization, this DID should be reassigned or returned to the pool.

### OrphanedIVR

`+15559001200` is assigned to `Legacy Holiday IVR`, which no longer exists in Architect. Calls to this number will fail routing. The IVR binding must be updated or removed.

---

## Remediation

1. **Unassigned DIDs:** Either assign the DID to a user, queue, or IVR routing entry, or release it from the pool if it is no longer needed (and your carrier agreement permits release).
2. **InactiveUser DIDs:** Reassign the DID to the user's replacement, or release it to the pool as part of the offboarding workflow.
3. **OrphanedIVR DIDs:** Update the DID assignment to point to the current replacement IVR flow, or remove the IVR binding if the number is being decommissioned.
4. After remediation, re-run the audit to confirm findings are cleared.

---

## Notes

- A DID assigned to an active user with no issues will appear in the raw data but will not generate a row in this sheet — only anomalies are reported.
- The `InactiveUser` finding is only generated when `IncludeInactive=true`. Run the audit in inclusive mode to catch all DID-to-inactive-user assignments.
- DID ownership mismatch (profile claims a DID assigned to a different user in telephony) is a planned future check — see [ROADMAP.md](../../ROADMAP.md).
