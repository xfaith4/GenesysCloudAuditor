# Example: Ext_Ownership_Mismatch

**Sheet name:** `Ext_Ownership_Mismatch`  
**Severity:** 🔴 Critical — Platform Bug Detection  
**Purpose:** Identifies extensions where the user profile claims ownership but the Genesys Cloud telephony assignment system has that extension assigned to a **different entity**. This is a known Genesys Cloud platform synchronization issue.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `ExtensionKey` | string | Normalized canonical extension |
| `RawExtension` | string | Raw extension value from the user profile |
| `ProfileUserId` | string (GUID) | ID of the user whose profile claims this extension |
| `ProfileUserName` | string | Display name of the profile owner |
| `ProfileUserEmail` | string | Email of the profile owner |
| `ProfileUserState` | string | `active` or `inactive` |
| `AssignedToEntityId` | string (GUID) | ID of the entity the telephony assignment actually points to |
| `AssignedToEntityType` | string | Type of the assigned entity: `user`, `station`, `phoneBase`, `edgeGroup` |
| `AssignedToEntityName` | string | Display name of the actually-assigned entity (when resolvable) |
| `AssignmentId` | string | Telephony assignment record ID |

---

## Sample Output

| ExtensionKey | RawExtension | ProfileUserId | ProfileUserName | ProfileUserEmail | ProfileUserState | AssignedToEntityId | AssignedToEntityType | AssignedToEntityName | AssignmentId |
|---|---|---|---|---|---|---|---|---|---|
| `3072` | `3072` | `f9c1d4e5-aaaa-4abc-9001-100000000001` | Sandra Flores | sandra.flores@example.com | active | `1bc2d3e4-bbbb-4fed-9002-200000000002` | user | Marcus Obi | `assign-00001` |
| `4401` | `x4401` | `a0b1c2d3-cccc-4abc-9003-300000000003` | James Thornton | james.thornton@example.com | active | `e5f6a7b8-dddd-4cde-9004-400000000004` | station | Reception Desk A | `assign-00002` |

---

## Interpretation

### Row 1 — User-to-User Mismatch

Extension `3072` is on **Sandra Flores**'s profile, but the telephony assignment for that extension points to **Marcus Obi**. This means:

- Calls to `3072` will ring Marcus's phone, not Sandra's.
- Sandra's profile shows an extension she does not effectively own in the telephony system.
- This is the most common form of ownership mismatch — typically caused by an extension being reassigned in telephony without clearing the old user's profile.

### Row 2 — User-to-Station Mismatch

Extension `4401` is on **James Thornton**'s profile, but the telephony assignment points to a **station** (`Reception Desk A`). This typically means:

- The extension belongs to a physical or soft phone station, not a personal user extension.
- James's profile contains a stale or incorrectly entered value.
- Calls to `4401` will reach the station, not James directly.

---

## Remediation

For each mismatch row:

1. **Determine the intended owner.** Check your telephony provisioning records to confirm who should own the extension.
2. **If the profile is wrong:** Clear the Work Phone extension field on the profile owner's account in Genesys Cloud Admin → People.
3. **If the telephony assignment is wrong:** Correct the assignment in Genesys Cloud telephony administration (Telephones → Extensions).
4. **If both are out of date:** Clear the profile and reassign the extension cleanly.
5. For persistent mismatches where both sides appear correct, **open a support case with Genesys Cloud** — this may indicate a platform synchronization failure.

---

## Notes

- This check detects **three-way inconsistencies**: a user profile value that exists in telephony but is owned by a third party.
- Simple orphans (profile extension not in telephony at all) are reported in `Ext_Assign_vs_Profile`, not here.
- An `AssignedToEntityType` of `phoneBase` or `edgeGroup` almost always indicates a stale profile value rather than an intentional assignment.
