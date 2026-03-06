# Example: Invalid_Extensions

**Sheet name:** `Invalid_Extensions`  
**Severity:** 🟡 Warning  
**Purpose:** Lists extension values that failed normalization validation — either on user profiles or in telephony assignments. These values cannot be used as reliable join keys for audit comparisons.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `Source` | string | `Profile` — value on a user profile; `Assignment` — value in a telephony assignment record |
| `RawValue` | string | The original unmodified value |
| `NormalizationStatus` | string | Why the value failed: `Empty`, `WhitespaceOnly`, `NonDigitOnly`, `TooShort`, `TooLong` |
| `NormalizationNotes` | string | Human-readable explanation |
| `EntityId` | string (GUID) | User ID (for Profile) or Assignment ID (for Assignment) |
| `EntityName` | string | User display name or assignment label |
| `EntityState` | string | `active` or `inactive` (Profile rows only) |

---

## Sample Output

| Source | RawValue | NormalizationStatus | NormalizationNotes | EntityId | EntityName | EntityState |
|---|---|---|---|---|---|---|
| `Profile` | `+1 (555) 867-5309` | `TooLong` | Value appears to be a full PSTN number, not a short extension. After normalization: `15558675309` (11 digits, exceeds MaxLength=10). | `c3d4e5f6-aaaa-4abc-8020-111111111111` | Patricia Gould | active |
| `Profile` | `ext` | `Empty` | After stripping the "ext" prefix and removing separators, the remaining value is empty. | `d4e5f6a7-bbbb-4bcd-8021-222222222222` | Robert Yuen | active |
| `Profile` | ` ` | `WhitespaceOnly` | Value contains only whitespace characters. | `e5f6a7b8-cccc-4cde-8022-333333333333` | Hannah Okafor | inactive |
| `Profile` | `N/A` | `NonDigitOnly` | After normalization, no digit characters remain. | `f6a7b8c9-dddd-4def-8023-444444444444` | Thomas Brewer | active |
| `Assignment` | `ab` | `TooShort` | Normalized value `AB` has length 2 but MinLength is configured for digits-only mode; no digits found. | `assign-20001` | (station assignment) | — |
| `Profile` | `12345678901234` | `TooLong` | Normalized value has 14 digits, exceeds MaxLength=10. | `a7b8c9d0-eeee-4efg-8024-555555555555` | Cynthia Nakamura | active |

---

## Interpretation

### TooLong — Full PSTN Numbers

The most common invalid profile extension is a full PSTN/E.164 number entered in the Work Phone extension field. These are not short extensions and cannot be matched against telephony assignments. Users or administrators sometimes copy-paste a full phone number into the extension field by mistake.

### Empty / WhitespaceOnly

The field was set but contains no usable value. This is a data quality issue but has no operational impact (blank extensions are skipped in routing).

### NonDigitOnly

Values like `N/A`, `TBD`, `-`, or similar placeholder strings that are semantically empty. These should be cleared.

### TooShort

One- or zero-character values that are too short to represent a valid extension in the configured range.

---

## Remediation

1. For **Profile** rows: open the user's record in Genesys Cloud Admin → People → Edit and correct or clear the Work Phone extension field.
2. For **Assignment** rows: review the telephony assignment in Genesys Cloud Admin → Telephony → Extensions and correct the extension value.
3. After correcting values, re-run the audit to confirm the rows are no longer present in this sheet.

---

## Notes

- The validation thresholds (`MinLength`, `MaxLength`) are configurable in `ExtensionNormalizationOptions`. Adjust them to match your org's extension range if needed.
- Full PSTN numbers in the extension field do **not** affect the `Ext_Duplicates_Profile` or `Ext_Ownership_Mismatch` checks — invalid extensions are excluded from those analyses.
