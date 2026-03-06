# Example: Ext_Duplicates_Profile

**Sheet name:** `Ext_Duplicates_Profile`  
**Severity:** 🔴 Critical  
**Purpose:** Lists every case where two or more user profiles share the same normalized Work Phone extension value. Each row represents one user who is part of a duplicate group.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `ExtensionKey` | string | Normalized canonical extension used for deduplication |
| `RawExtension` | string | Original raw value from the user's profile |
| `UserId` | string (GUID) | Genesys Cloud user ID |
| `UserName` | string | Display name of the user |
| `Email` | string | User's email address |
| `UserState` | string | `active` or `inactive` |
| `DuplicateGroupSize` | integer | Number of users sharing this extension |

---

## Sample Output

| ExtensionKey | RawExtension | UserId | UserName | Email | UserState | DuplicateGroupSize |
|---|---|---|---|---|---|---|
| `1042` | `1042` | `a3f1d2c0-1111-4abc-8001-000000000001` | Alice Nguyen | alice.nguyen@example.com | active | 2 |
| `1042` | `ext. 1042` | `b7e3c5d1-2222-4def-8002-000000000002` | Brian Kowalski | brian.kowalski@example.com | active | 2 |
| `2315` | `2315` | `c1a4b6e2-3333-4fab-8003-000000000003` | Carmen Reyes | carmen.reyes@example.com | 3 |
| `2315` | `2315` | `d4f7a8b3-4444-4cde-8004-000000000004` | David Osei | david.osei@example.com | active | 3 |
| `2315` | `x2315` | `e8b2c3f4-5555-4bca-8005-000000000005` | Emily Hartmann | emily.hartmann@example.com | inactive | 3 |

---

## Interpretation

- Each unique `ExtensionKey` that appears more than once represents a duplicate group.
- `DuplicateGroupSize` shows how many users share that extension — **2 is a conflict; 3+ is a critical routing failure**.
- Rows where `RawExtension` differs (e.g., `1042` vs `ext. 1042`) confirm that normalization is correctly detecting semantic duplicates, not just string matches.
- An `inactive` user in a duplicate group indicates a deprovisioning gap — the extension was likely recycled to a new user without clearing the old profile.

---

## Remediation

1. Identify the **intended owner** of each extension in your telephony provisioning system.
2. For users who should **not** have the extension: clear the Work Phone extension field in **Genesys Cloud Admin → People → [User] → Edit**.
3. Re-run the audit to confirm the duplicate group is resolved (finding should disappear from this sheet).
4. If unsure of ownership, cross-reference with the `Ext_Ownership_Mismatch` sheet — the telephony assignment system may already indicate the correct owner.

---

## Notes

- This check operates on **profile-layer** data only (user Work Phone extension fields). For telephony-layer duplicates, see `Ext_Assign_vs_Profile`.
- Extension `2315` in the example involves three users, one of whom is inactive — a common pattern when extensions are recycled without cleaning up old profiles.
