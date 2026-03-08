# Example: Inactive_Users

**Sheet name:** `Inactive_Users`  
**Severity:** 🟡 Warning  
**Purpose:** Lists user accounts that are in an inactive or deactivated state. This sheet is only populated when the audit is run with `IncludeInactive=true`.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `UserId` | string (GUID) | Genesys Cloud user ID |
| `UserName` | string | Display name |
| `Email` | string | User email address |
| `UserState` | string | `inactive`, `deleted` |
| `Title` | string | Job title (if set) |
| `Department` | string | Department (if set) |
| `Division` | string | Division the account belongs to |
| `HasExtension` | boolean | Whether the user has a Work Phone extension set on their profile |
| `Extension` | string | The extension value (if `HasExtension=true`) |
| `HasAdminRole` | boolean | Whether the user account has an administrative role assigned |
| `LastLoginDate` | date | Last recorded login date (may be `null` if never logged in or data unavailable) |

---

## Sample Output

| UserId | UserName | Email | UserState | Title | Department | Division | HasExtension | Extension | HasAdminRole | LastLoginDate |
|---|---|---|---|---|---|---|---|---|---|---|
| `b3c4d5e6-1111-4abc-9060-aaaaaaaaaaaa` | Greg Patel | greg.patel@example.com | inactive | Contact Center Agent | Operations | Home | true | `2047` | false | 2023-10-14 |
| `c4d5e6f7-2222-4bcd-9061-bbbbbbbbbbbb` | Samantha Wu | samantha.wu@example.com | inactive | Team Lead | Operations | Home | false | — | false | 2024-02-01 |
| `d5e6f7a8-3333-4cde-9062-cccccccccccc` | Former Admin | former.admin@example.com | inactive | IT Administrator | IT | Home | false | — | true | 2022-08-19 |
| `e6f7a8b9-4444-4def-9063-dddddddddddd` | Linda Marsh | linda.marsh@example.com | inactive | QA Analyst | Quality | East | true | `3301` | false | 2024-06-30 |

---

## Interpretation

### Inactive User with Extension (`HasExtension=true`)

**Greg Patel** and **Linda Marsh** are inactive but still have extensions set on their profiles. This creates two risks:

1. If the extension has been recycled to a new user, the old inactive profile may cause a `Ext_Duplicates_Profile` or `Ext_Ownership_Mismatch` finding.
2. The extension occupies a slot in the telephony namespace that could confuse routing queries.

**Action:** Clear the extension field on inactive user profiles as part of the offboarding process.

### Inactive User with Admin Role (`HasAdminRole=true`)

**Former Admin** has an administrative role but the account has been inactive since 2022. This is a security concern — dormant admin accounts should have their roles removed immediately.

**Action:** Revoke administrative roles from inactive accounts. This finding also appears in the planned **Security Audit** checks.

### Long-Inactive Accounts

Accounts inactive for more than 90 days with no extension and no admin role are lower priority but represent housekeeping opportunities. Consider exporting to HR for account lifecycle review.

---

## Remediation

1. **Inactive users with extensions:** Clear the Work Phone extension field during the offboarding workflow.
2. **Inactive users with admin roles:** Remove all roles from the account immediately. Open a security review if the account has been dormant for more than 30 days with active admin permissions.
3. **All inactive users:** Coordinate with HR/IT to formally deactivate or delete accounts according to your organization's identity governance policy.
4. Re-run the audit after remediation to confirm the findings are resolved.

---

## Notes

- This sheet is empty when `IncludeInactive=false` (the default). Enable `IncludeInactive=true` in `appsettings.json` to include inactive users in the scan.
- `deleted` state users are included when present — Genesys Cloud retains deleted user records for audit purposes.
- `LastLoginDate` may be `null` for accounts that were provisioned but never logged in.
