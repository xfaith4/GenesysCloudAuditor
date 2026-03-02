```markdown
# UI-Wireframe.md
Design UI/UX wireframe and interaction flow for a Windows desktop app that audits Genesys Cloud extension assignments.

---

## 1. UX Goals & Principles

**Primary user goal**
- Configure Genesys Cloud API credentials and run an audit that:
  1) Finds duplicate extensions on **user profile work phone extension** fields
  2) Finds duplicate extensions in **extension assignments**
  3) Finds extensions present on **profiles but not in assignment list** (“unassigned”)

**Secondary goals**
- Clear progress visibility for long-running, paginated API retrieval.
- Safe handling of secrets.
- Actionable results with filtering, drill-in, and export (CSV/JSON; Excel optional).

**Design principles**
- Single primary workflow: *Configure → Run → Review → Export*.
- Non-blocking UI during audit (background task), with **Cancel**.
- Make the `IncludeInactive` toggle explicit and explain its effect (API query changes).
- Results are grouped into tabs with clear counts and consistent columns.

---

## 2. Information Architecture (Screens / Views)

### Views (WPF/MVVM)
1. **Main Window**
   - Header/status + navigation tabs
2. **Run Audit** (primary)
   - Credentials/config summary + IncludeInactive + Run/Cancel + Progress + live log
3. **Results**
   - Tabs: Summary, Duplicate Profile Extensions, Duplicate Assigned Extensions, Unassigned Profile Extensions, (Optional) Invalid/Missing Profile Extensions, Raw Data (optional)
4. **Settings**
   - Region/base URLs, page size, throttling, normalization rules
5. **About/Help**
   - Brief endpoint notes, permissions required, export formats

Navigation model: **MainWindow with TabControl** or left navigation.
Recommended: TabControl with:
- Run Audit
- Results
- Settings
- Logs (optional separate tab; or embedded in Run Audit)

---

## 3. Wireframes (Text)

> Notation: `[ ]` input, `( )` radio, `{ }` group, `[...]` button.

### 3.1 Main Window Layout (Shell)

```
+----------------------------------------------------------------------------------+
| Genesys Cloud Extension Audit                                  [Save] [Help]     |
| Tenant/Region: <region>     Last Run: <timestamp or "Never">   Status: <Idle>    |
+----------------------------------------------------------------------------------+
| [Run Audit] [Results] [Settings] [Logs]                                         |
+----------------------------------------------------------------------------------+
| TAB CONTENT AREA                                                                 |
+----------------------------------------------------------------------------------+
| Footer: <App version> | <Log file path>                                         |
+----------------------------------------------------------------------------------+
```

---

## 4. Run Audit View (Credentials/Config + Run + Progress)

### 4.1 Run Audit Wireframe

```
+-------------------------------------- Run Audit --------------------------------------+
| {Credentials}                                                                     |
|  Client ID:      [_________________________]   (i) stored locally                   |
|  Client Secret:  [***********************__]   [Show] [Test Connection]             |
|  Auth Region:    [ dropdown: usw2, use1, ... ]  (i) controls auth/api base URLs     |
|                                                                                    |
| {Audit Scope}                                                                       |
|  Page Size:      [ 100 ]   (i) clamped to API max                                  |
|  [x] Include inactive users                                                        |
|      (When off, requests: /api/v2/users?...&state=active)                          |
|      (When on, requests:  /api/v2/users?...  (no state param))                     |
|                                                                                    |
| {Normalization (quick)}                                                            |
|  Leading zeros: ( ) Preserve  ( ) Remove (numeric only)                            |
|  Allowed: [x] digits [ ] letters  (i) if letters off → flag as invalid             |
|                                                                                    |
| {Actions}                                                                          |
|  [Run Audit]   [Cancel] (disabled unless running)   [Open Results] (after run)     |
|                                                                                    |
| {Progress}                                                                         |
|  Phase: <Fetching users | Fetching extensions | Analyzing | Complete | Failed>     |
|  Progress: [########------]  62%                                                     |
|  Users:      pages <3/14>   fetched <250/1200>                                      |
|  Extensions: pages <1/5>    fetched <100/500>                                       |
|  Elapsed: <00:02:13>    Requests/min: <xx>   Retries: <n>                          |
|                                                                                    |
| {Live Log}                                                                         |
|  [ ] Auto-scroll     [Copy] [Save Log As...]                                       |
|  ------------------------------------------------------------------------------    |
|  12:01:05 Fetch users page 1... OK (100)                                           |
|  12:01:06 Fetch users page 2... OK (100)                                           |
|  ...                                                                               |
+------------------------------------------------------------------------------------+
```

### 4.2 Interaction Notes (Run Audit)

- **Test Connection**
  - Validates:
    - credentials format (non-empty)
    - token acquisition (OAuth client credentials)
    - optional: quick GET `/api/v2/users?pageSize=1&pageNumber=1&state=active` to validate permissions
  - Output:
    - green “Connected” or error dialog with actionable message (401/403/invalid region)

- **Run Audit**
  - Disables config inputs while running (except Cancel).
  - Starts background task:
    1) Fetch users (paged)
    2) Fetch extensions (paged)
    3) Analyze/normalize
  - Updates progress area continuously.
  - On completion: enables **Open Results** and switches to Results tab automatically (optional preference).

- **Cancel**
  - Requests cancellation; UI shows “Cancelling…” until task stops.
  - Partial results handling:
    - Default: do not show results; show “Cancelled” state with option to view partial raw data only (optional).
    - Simpler: cancel = run aborted, no results.

---

## 5. Settings View (Detailed Config)

### 5.1 Settings Wireframe

```
+---------------------------------------- Settings --------------------------------------+
| {API Endpoints}                                                                      |
|  Region:        [ dropdown ]                                                         |
|  Auth Base URL: [https://login.<region>.pure.cloud] (read-only or editable advanced)  |
|  API Base URL:  [https://api.<region>.pure.cloud]   (read-only or editable advanced) |
|                                                                                    |
| {Pagination & Throttling}                                                          |
|  Default Page Size:  [100]                                                          |
|  Max requests/sec:   [ 5 ]   (0 = unlimited)                                        |
|  Retry policy:       [x] Retry 429/5xx   Max retries [5]                            |
|                                                                                    |
| {Normalization Rules}                                                              |
|  Trim whitespace:        [x]                                                       |
|  Remove separators:      [x] spaces [x] hyphen [x] parens [x] dots                  |
|  Strip prefixes:         [x] "ext" / "x"                                            |
|  Leading zeros policy:   ( ) Preserve  ( ) Remove numeric-only                      |
|  Valid length:           Min [2] Max [10]                                           |
|  Alphanumeric allowed:   [ ] (if off → invalid)                                     |
|                                                                                    |
| {Storage}                                                                           |
|  Store Client Secret using: (•) Windows Credential Manager (recommended)            |
|                             ( ) DPAPI user scope                                   |
|  [Clear stored credentials]                                                        |
|                                                                                    |
| [Save Settings] [Reset to Defaults]                                                 |
+------------------------------------------------------------------------------------+
```

### 5.2 Settings Interaction Notes
- Changing Region updates base URLs.
- Page size validated with inline message: “Max allowed is 100; value clamped.”
- Credential storage operations require confirmation dialogs.

---

## 6. Results View (Tabs + Tables + Export)

### 6.1 Results Overview Wireframe

```
+---------------------------------------- Results ---------------------------------------+
| Run: <timestamp>  Duration: <00:03:41>  IncludeInactive: <Yes/No>  PageSize: <100>   |
| Users fetched: <n> (active/inactive counts if available)  Extensions fetched: <n>    |
| Normalization: <summary>                                                             |
| [Export All...] [Re-run with same settings]                                          |
+--------------------------------------------------------------------------------------+
| [Summary] [Dupes: Profile] [Dupes: Assignments] [Unassigned] [Invalid/Missing]      |
+--------------------------------------------------------------------------------------+
| TAB CONTENT                                                                         |
+--------------------------------------------------------------------------------------+
```

### 6.2 Summary Tab

Purpose: quick health view + links to other tabs.

```
Summary
- Duplicate profile extensions:   <count extensions>  (<count users impacted>)
- Duplicate assigned extensions:  <count extensions>  (<count assignments impacted>)
- Unassigned profile extensions:  <count extensions>  (<count users impacted>)
- Invalid profile extensions:     <count entries>
- Users missing extension:        <count> (optional)

[Open Duplicate Profile Tab] [Open Unassigned Tab]
```

### 6.3 Duplicate Profile Extensions Tab (Grid)

Definition: same normalized extension appears on 2+ users’ profile work phone extension field.

```
Duplicate Extensions (User Profiles)                               [Export CSV] [Export JSON]
Filter: [search box...............]  State: [All | Active | Inactive]  Min Count: [2]

+------------------------------------------------------------------------------------------+
| Normalized Ext | Count Users | Users (names)                 | User States | Notes       |
+------------------------------------------------------------------------------------------+
| 1234           | 3           | Alice; Bob; Carol             | A;A;I       |             |
| 0099           | 2           | Dan; Erin                     | A;A         | leading 0  |
+------------------------------------------------------------------------------------------+

[Details Pane / Drill-in]
Selected extension: 1234
- UserId | Name | State | Raw profile ext | Normalized ext
  ...
[Copy selected] [Export selected...]
```

Interaction:
- Click row → expands details pane or opens modal.
- Search matches normalized extension and user name.
- Export respects current filter (default) with option “Export all rows”.

### 6.4 Duplicate Assigned Extensions Tab (Grid)

Definition: same normalized extension appears 2+ times in extensions endpoint results.

```
Duplicate Extensions (Assignments)                                [Export CSV] [Export JSON]
Filter: [search...........]  Assignment Type: [All | User | Station | Room | ...] (if known)

+--------------------------------------------------------------------------------------------------+
| Normalized Ext | Count Assignments | Assignment Targets (summary)           | Notes              |
+--------------------------------------------------------------------------------------------------+
| 1234           | 2                 | user:Bob, station:Desk-12              | cross-type         |
| 5555           | 3                 | user:Alice, user:Carol, user:Dan       |                    |
+--------------------------------------------------------------------------------------------------+

Details:
- ExtensionId | Raw ext | Normalized | AssignedToType | AssignedToId | DisplayName | Site/Edge (if any)
```

Interaction:
- If assignment schema includes “type”, allow filter.
- Notes column can mark:
  - “cross-type duplicate”
  - “multiple users assigned”
  - “unknown target” (if fields missing)

### 6.5 Unassigned Profile Extensions Tab (Grid)

Definition: normalized extension present in user profiles but not present in assignments list.

```
Profile Extensions Not Found in Assignments                         [Export CSV] [Export JSON]
Filter: [search...........]   Show: [x] Active users [x] Inactive users (if IncludeInactive)

+-----------------------------------------------------------------------------------------------+
| Normalized Ext | Users Count | Users (names)              | States | Raw Examples | Notes     |
+-----------------------------------------------------------------------------------------------+
| 7777           | 1           | Carol                      | A      | "7777"       |           |
| 12A4           | 1           | Erin                       | A      | "12A4"       | invalid?  |
+-----------------------------------------------------------------------------------------------+
```

Notes:
- If normalization rules consider alphanumeric invalid, such rows should be moved to **Invalid** tab instead of unassigned.

### 6.6 Invalid/Missing Profile Extensions Tab (Optional but recommended)

Two subviews or two grids:

**A) Invalid profile extension format**
- Empty after normalization, disallowed chars, out of length bounds, “multiple extensions detected” (if implemented), etc.

**B) Missing profile extension**
- User has no work phone extension value.

```
Invalid / Missing Profile Extensions                               [Export CSV] [Export JSON]
[Invalid Format] [Missing Extension]

Invalid Format:
| User | State | Raw Value | Reason | Suggested Fix |
Missing Extension:
| User | State | Reason ("no value") |
```

---

## 7. Export UX

### 7.1 Export Entry Points
- Per-tab: **Export CSV**, **Export JSON**
- Global: **Export All…** from Results header (creates a folder with all exports + metadata)

### 7.2 Export Dialog (for any export)

```
Export
Format: (•) CSV  ( ) JSON  ( ) Excel (optional)
Scope:  (•) Current tab (filtered)  ( ) All rows in tab  ( ) All tabs
Destination: [C:\...\AuditExports\] [Browse...]
File naming: [x] include timestamp  [x] include tenant/region (if known)
[Export] [Cancel]
```

### 7.3 Export Content Requirements
All exports should include **run metadata** either:
- as a separate `metadata.json`, or
- as header comments (CSV) + additional JSON fields

Metadata includes:
- Run timestamp, duration
- Region/base URLs (safe)
- IncludeInactive toggle state
- Page size
- Counts fetched (users/extensions)
- Normalization settings summary
- App version

---

## 8. Interaction Flow (End-to-End)

### 8.1 First Run (Happy Path)
1. User opens app → lands on **Run Audit** tab.
2. Enters **Client ID/Secret**, selects **Region**.
3. Clicks **Test Connection**.
   - If OK: shows “Connected” + permission summary (optional).
4. Sets `Include inactive users` as needed.
5. Clicks **Run Audit**.
6. Sees progress:
   - Phase: Fetching users
   - Phase: Fetching extensions
   - Phase: Analyzing
7. Completion → auto-navigate to **Results** tab.
8. User reviews Summary, then drills into specific tabs.
9. User exports tab or “Export All…”.

### 8.2 Error Paths
**401 Unauthorized**
- Show modal:
  - “Authentication failed. Check Client ID/Secret and Region.”
  - Provide button: “Open Settings” / “Retry”
- Do not proceed to results.

**403 Forbidden (missing permissions)**
- Show modal listing required scopes/roles (if known).
- Do not proceed.

**429 Too Many Requests**
- Progress shows retries/backoff.
- If still failing after max retries: fail gracefully, suggest lowering RPS.

**Network failure**
- Offer Retry from last phase (optional); simpler: restart run.

### 8.3 Cancellation
- While running, Cancel enabled.
- On cancel:
  - status transitions to “Cancelled”
  - controls re-enabled
  - results cleared (or labeled partial if partial results supported)

---

## 9. Data Binding & UI State (MVVM)

### 9.1 ViewModel Properties (RunAuditViewModel)
- Credentials:
  - `ClientId`, `ClientSecret` (SecureString or protected wrapper)
  - `SelectedRegion`
- Options:
  - `PageSize`
  - `IncludeInactive`
  - normalization quick settings (leading zero policy, allow alpha)
- Commands:
  - `TestConnectionCommand`
  - `RunAuditCommand`
  - `CancelCommand`
  - `OpenResultsCommand`
- Progress:
  - `PhaseText`
  - `PercentComplete`
  - `UsersPageCurrent`, `UsersPageTotal`, `UsersFetchedCount`, `UsersTotalCount?`
  - `ExtPageCurrent`, `ExtPageTotal`, `ExtensionsFetchedCount`, `ExtensionsTotalCount?`
  - `Elapsed`
  - `IsRunning`, `CanRun`, `CanCancel`
- Log:
  - `ObservableCollection<LogLine>` or similar

### 9.2 ViewModel Properties (ResultsViewModel)
- `AuditMetadata`
- Tab datasets:
  - `ObservableCollection<DuplicateProfileExtensionRow>`
  - `ObservableCollection<DuplicateAssignedExtensionRow>`
  - `ObservableCollection<UnassignedProfileExtensionRow>`
  - `ObservableCollection<InvalidProfileExtensionRow>`
- Filtering:
  - `SearchText`, `SelectedStateFilter`, `MinCount`
- Export commands:
  - `ExportTabCommand`, `ExportAllCommand`

---

## 10. Accessibility & Usability Considerations
- Keyboard navigation: tab order, Enter triggers default action (Run/Test depending on focus).
- High-contrast friendly colors for status; don’t rely on color alone (include text labels).
- Clear inline help text for IncludeInactive and normalization.
- Avoid showing secrets in logs; “Show” toggles only local field display.

---

## 11. Copy/Labels (Suggested)

- Toggle label: **Include inactive users**
  - Help text:
    - Off: “Only active users are included. API adds `state=active`.”
    - On: “All users are included. API omits the `state` parameter.”
- Run button: **Run Audit**
- Progress phases:
  - “Fetching users (paged)”
  - “Fetching extensions (paged)”
  - “Analyzing & normalizing extensions”
  - “Complete”
  - “Failed: <reason>”

---

## 12. Minimum Viable UI (MVP) Checklist
- Run Audit tab with:
  - Region, Client ID, Client Secret
  - IncludeInactive toggle
  - PageSize
  - Run/Cancel
  - Progress bar + phase text
- Results tab with 3 required tabs:
  - Duplicate Profile Extensions
  - Duplicate Assigned Extensions
  - Unassigned Profile Extensions
- Export CSV per tab
- Basic Settings tab (page size, region)

---

- Mapped required features (credentials/config, IncludeInactive toggle, run/progress, results tabs, export) into WPF MVVM views and navigation.
- Produced low-fidelity wireframes for Run Audit, Settings, Results (with tabs), and Export dialogs.
- Defined interaction flows for happy path, errors (401/403/429), and cancellation.
- Specified table structures/columns for duplicates (profiles/assignments) and unassigned reports, aligned to normalization rules and IncludeInactive behavior.
- Added export UX and metadata requirements to support audit traceability.
```
