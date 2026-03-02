## Functional requirements and edge cases to confirm

### 1) Inactive users toggle (`$IncludeInactive`)

**Requirement**

- When `$IncludeInactive = $false`, the app must request only active users:
  - `GET /api/v2/users?pageSize=...&pageNumber=...&state=active`
- When `$IncludeInactive = $true`, the app must request all users (no state filter):
  - `GET /api/v2/users?pageSize=...&pageNumber=...` (no `state=` parameter at all)

**Edge cases / confirmations**

- Confirm Genesys Cloud accepts omission of `state` to mean “all states”. (This is implied by the script logic.)
- Confirm that `state` values other than `active` are not needed (e.g., `inactive`, `deleted`) for this audit; otherwise expand logic.
- If `$IncludeInactive = $true`, ensure the code does **not** send `&state=` (empty value) because some APIs interpret empty differently than absent.
- Decide whether “inactive users” should still participate in:
  - duplicate detection (yes/no)
  - “unassigned profile extension” detection (yes/no)
  Typically: include them if toggled on; otherwise exclude them entirely from all computations.

---

### 2) Pagination limits and behavior (both endpoints)

**Requirement**

- The app must page through all results from:
  - `/api/v2/users?pageSize=$PageSize&pageNumber=$page...`
  - `/api/v2/telephony/providers/edges/extensions?pageSize=$PageSize&pageNumber=$PageNumber`
- Combine all pages into complete in-memory lists (or stream process) before generating final audit outputs.

**Edge cases / confirmations**

- **Max page size**: confirm the API’s maximum `pageSize` for both endpoints (Genesys commonly caps at 100). If user config exceeds max, clamp to max and log it.
- **Loop termination**: stop when:
  - returned `entities` count is 0, or
  - pageNumber exceeds `pageCount - 1` (if metadata is returned), or
  - accumulated count >= `total` (if provided).
- **Off-by-one**: confirm whether `pageNumber` is 1-based or 0-based for each endpoint. (Genesys commonly uses 1-based in many endpoints, but confirm via observed responses.)
- **Sorting stability**: if the API does not guarantee stable ordering, results could shift between pages if changes occur during the run. Mitigation:
  - if supported, apply a deterministic `sortOrder`/`sortBy`, or
  - accept minor drift and note it in the report metadata (run timestamp).
- **Rate limits / transient failures**:
  - implement retry with backoff for HTTP 429/5xx,
  - record partial completion if aborting.
- **Permissions**: lack of permissions may cause missing pages/entities; treat as fatal with clear error messaging (cannot trust audit).

---

### 3) Extension normalization rules (matching user profile vs assignments)

Because the goal compares:

- user profile “work phone extension” field
- extension assignment list from `/telephony/providers/edges/extensions`

**Requirement**

- Normalize both sides to a canonical “extension key” before comparison and duplicate detection.

**Proposed normalization (confirm/approve)**

1. `Trim()` leading/trailing whitespace.
2. Convert to a consistent case (extensions are usually numeric; but if alphanumeric, use uppercase).
3. Remove common separators: spaces, hyphens, parentheses, dots.
   - Example: `"123-4"` → `"1234"`
4. If the field includes an explicit “ext” prefix/suffix (e.g., `"x1234"`, `"ext. 1234"`), strip non-alphanumeric prefixes and keep the core token.
5. Decide policy for **leading zeros**:
   - Option A (strict): keep leading zeros (treat `"0123"` different from `"123"`).
   - Option B (lenient): drop leading zeros for purely numeric strings.
   This must be confirmed because many PBX/edge systems treat leading zeros as meaningful.
6. Reject/flag invalid extensions:
   - empty after normalization
   - too short/too long (confirm allowable length; common is 2–10 digits)
   - contains unsupported characters (if assignment endpoint returns strictly numeric, treat alphanumeric profile entries as “invalid format” rather than “unassigned”)

**Edge cases**

- User profile might contain full DID/E.164 number in the “extension” field by mistake. Decide whether to:
  - treat as invalid, or
  - attempt to extract last N digits (not recommended unless explicitly desired).
- International formats and `+` signs: likely invalid for an internal extension field; treat as invalid unless business rules say otherwise.
- Multiple extensions in one field (e.g., `"1234 / 5678"`): define whether to split on delimiters or flag as invalid. Prefer: flag as invalid to avoid false matches unless splitting is requested.

---

### 4) “Duplicate” definition (what counts as a duplicate)

You effectively have two datasets:

A) **User profile extension values** (from `/users`, work phone extension field)
B) **Assigned extensions list** (from `/telephony/providers/edges/extensions`)

**Requirement: duplicates to report**

1. **Duplicate in user profiles**
   - Same normalized extension appears on 2+ users’ profile work phone extension fields.
   - Output: extension, list of users (id, name, state), count.
2. **Duplicate in assignment list**
   - Same normalized extension appears 2+ times in the extensions endpoint results.
   - Output: extension, associated assignment identifiers (edge group/site? user? phone? depending on response schema), count.
3. **Cross-duplicate / collision (optional but useful)**
   - A normalized extension appears in assignment list and on multiple profiles (or on a profile that differs from the assignee).
   This is more of a “mismatch/collision” report:
   - extension present on profile(s) but assigned to a different target, or assigned multiple ways.

**Edge cases / confirmations**

- Confirm what the “extensions” endpoint returns:
  - is it per DID? per station? per user? per phone?
  The definition of “duplicate assignment” depends on whether duplicates are actually possible in the source system vs representing multiple objects legitimately sharing an extension.
- If the assignment list includes multiple “types” (user, station, room), decide whether duplicates across types are considered duplicates or acceptable. This needs explicit confirmation.

---

### 5) “Unassigned” definition (profile-only extensions)

**Requirement**

- Report extensions that appear in user profiles but are not present in the extension assignment list **after normalization**.

**Precise definition**

- Let `U = set of normalized extensions from user profile field` (excluding null/empty/invalid per rules).
- Let `A = set of normalized extensions from assignments endpoint`.
- “Unassigned” = `U \ A`.

**Output**

- extension
- user(s) whose profile contains it (id, name, state)
- notes if the profile field was invalid or ambiguous (if you choose to include invalid separately, keep “unassigned” strictly for valid extensions)

**Edge cases / confirmations**

- Users without an extension value: should be excluded from this specific report (but may be listed in a “missing extension” report if desired).
- If `$IncludeInactive = $false`, then an extension that exists only on inactive users’ profiles should not appear in “unassigned”.
- If an extension appears on multiple profiles and is unassigned, report it once with all users listed.
- If the assignment endpoint is scoped (e.g., only certain sites/edges) ensure you are retrieving all relevant assignments; otherwise you will falsely classify “unassigned”.

---

## Questions to confirm with stakeholders (to lock requirements)

1. Should leading zeros be preserved in extensions (e.g., `"0123"` ≠ `"123"`)?
2. Are alphanumeric extensions allowed? If yes, what characters are valid?
3. Should we attempt to parse multiple extensions in one profile field, or treat as invalid?
4. Does “duplicate assignment” mean “same extension assigned to more than one target” regardless of type, or only duplicates within the same assignment type?
5. Page numbering base (0 vs 1) for each endpoint in your tenant—confirm from a sample response.
6. When `$IncludeInactive = $true`, do we include inactive users in all reports (duplicates/unassigned), or only in a separate section?

These confirmations will prevent false positives in duplicate/unassigned reporting and ensure pagination and state filtering behave as intended.
