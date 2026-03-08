# Example: Empty_Groups

**Sheet name:** `Empty_Groups`  
**Severity:** 🟡 Warning  
**Purpose:** Lists Genesys Cloud groups that have zero members or only one member. These groups may represent stale configuration, abandoned team structures, or provisioning gaps.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `GroupId` | string (GUID) | Genesys Cloud group ID |
| `GroupName` | string | Group display name |
| `GroupType` | string | Group type: `official`, `social`, `dynamic` |
| `MemberCount` | integer | Number of active members in the group |
| `OwnerCount` | integer | Number of group owners |
| `Visibility` | string | `public` or `members` |
| `DateModified` | date | Last modification timestamp |
| `Issue` | string | `NoMembers` — zero members; `SingleMember` — only one member |

---

## Sample Output

| GroupId | GroupName | GroupType | MemberCount | OwnerCount | Visibility | DateModified | Issue |
|---|---|---|---|---|---|---|---|
| `b1c2d3e4-1111-4abc-9030-aaaaaaaaaaaa` | West Coast Agents | official | 0 | 1 | public | 2024-06-15 | `NoMembers` |
| `c2d3e4f5-2222-4bcd-9031-bbbbbbbbbbbb` | Training Cohort Q1 | official | 1 | 0 | members | 2024-02-28 | `SingleMember` |
| `d3e4f5a6-3333-4cde-9032-cccccccccccc` | Legacy VIP Team | official | 0 | 0 | public | 2023-11-01 | `NoMembers` |
| `e4f5a6b7-4444-4def-9033-dddddddddddd` | Temp Overflow Pool | social | 1 | 1 | members | 2025-01-10 | `SingleMember` |

---

## Interpretation

- **NoMembers:** The group exists but has no members. If the group is used for skill routing, queue membership, or communication targeting, no agents will receive those interactions.
- **SingleMember:** The group has only one person. Depending on the group's purpose, this may be intentional (e.g., a manager group) or may indicate that other members were removed without decommissioning the group.
- **Old `DateModified`:** A group that has not been modified in many months and has zero members is a strong candidate for decommissioning.

---

## Remediation

1. For **NoMembers** groups: determine whether the group is still in use (check queue routing rules, skill assignments, and Architect flows that reference the group).
   - If in use: add the appropriate members.
   - If no longer needed: delete the group from Genesys Cloud Admin → Directory → Groups.
2. For **SingleMember** groups: confirm the group's intended purpose. Assign additional members if required, or document the single-member status as intentional.
3. Remove groups flagged as `Legacy` in the name if they serve no current function.
