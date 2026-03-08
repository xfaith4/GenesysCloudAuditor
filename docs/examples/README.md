# Example Exports

This folder contains representative examples of the output produced by GenesysCloudAuditor for each report sheet. Examples use realistic but entirely fictional data to illustrate the schema, severity indicators, and remediation context of each finding type.

---

## Report Sheets

| File | Sheet Name | Severity | Description |
|---|---|---|---|
| [summary.md](summary.md) | `Summary` | — | Audit run metadata and finding counts |
| [ext-duplicates-profile.md](ext-duplicates-profile.md) | `Ext_Duplicates_Profile` | 🔴 Critical | Multiple users sharing the same Work Phone extension |
| [ext-ownership-mismatch.md](ext-ownership-mismatch.md) | `Ext_Ownership_Mismatch` | 🔴 Critical | Extension claimed by a profile but assigned to a different entity in telephony |
| [ext-assign-vs-profile.md](ext-assign-vs-profile.md) | `Ext_Assign_vs_Profile` | 🟡 Warning | Extensions present in only one of profile or telephony assignment |
| [invalid-extensions.md](invalid-extensions.md) | `Invalid_Extensions` | 🟡 Warning | Malformed or non-numeric extension values |
| [empty-groups.md](empty-groups.md) | `Empty_Groups` | 🟡 Warning | Groups with zero or one member |
| [empty-queues.md](empty-queues.md) | `Empty_Queues` | 🟡 Warning | Queues with no agents or duplicate names |
| [stale-flows.md](stale-flows.md) | `Stale_Flows` | 🟡 Warning | Unpublished or long-stale Architect flows |
| [inactive-users.md](inactive-users.md) | `Inactive_Users` | 🟡 Warning | Stale or deactivated user accounts |
| [did-mismatches.md](did-mismatches.md) | `DID_Mismatches` | 🟡 Warning | Unassigned, orphaned, or misassigned DIDs |

---

## How to Read These Examples

Each example file shows:

1. **Column definitions** — what each column means and where the data comes from
2. **Sample rows** — realistic fictional data representing common findings
3. **Remediation notes** — what action to take for each finding type

---

## Generating Real Output

To produce actual export files from your org:

1. Configure credentials in `appsettings.json` or user secrets (see [Authentication](../authentication.md)).
2. Launch the application and click **Start** on the Run Audit tab.
3. After completion, click **Export Last Report...** and choose a `.xlsx` save location.

The exported workbook contains one sheet per finding type, plus the `Summary` sheet.
