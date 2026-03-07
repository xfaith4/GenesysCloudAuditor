# Example: Empty_Queues

**Sheet name:** `Empty_Queues`  
**Severity:** 🟡 Warning  
**Purpose:** Lists queues that have no assigned agents, or queues that share a duplicate name with another queue. Empty queues cause interactions to queue indefinitely with no agent to handle them. Duplicate names cause reporting and routing confusion.

---

## Column Definitions

| Column | Type | Description |
|---|---|---|
| `QueueId` | string (GUID) | Genesys Cloud queue ID |
| `QueueName` | string | Queue display name |
| `MediaType` | string | `voice`, `chat`, `email`, `callback`, `message` |
| `AgentCount` | integer | Number of agents assigned to the queue |
| `ActiveAgentCount` | integer | Number of agents currently active/on-queue |
| `Division` | string | Division the queue belongs to |
| `DateModified` | date | Last modification timestamp |
| `Issue` | string | `NoAgents` — zero agents assigned; `DuplicateName` — another queue shares this name |
| `DuplicateQueueIds` | string | Comma-separated IDs of queues with the same name (populated for `DuplicateName` rows) |

---

## Sample Output

| QueueId | QueueName | MediaType | AgentCount | ActiveAgentCount | Division | DateModified | Issue | DuplicateQueueIds |
|---|---|---|---|---|---|---|---|---|
| `f5a6b7c8-1111-4abc-9040-aaaaaaaaaaaa` | Billing Support | voice | 0 | 0 | Finance | 2024-08-20 | `NoAgents` | — |
| `a6b7c8d9-2222-4bcd-9041-bbbbbbbbbbbb` | Chat General | chat | 0 | 0 | Operations | 2024-04-11 | `NoAgents` | — |
| `b7c8d9e0-3333-4cde-9042-cccccccccccc` | Sales Inbound | voice | 12 | 4 | Sales | 2025-09-03 | `DuplicateName` | `c8d9e0f1-4444-4def-9043-dddddddddddd` |
| `c8d9e0f1-4444-4def-9043-dddddddddddd` | Sales Inbound | voice | 5 | 2 | Sales – EMEA | 2025-07-18 | `DuplicateName` | `b7c8d9e0-3333-4cde-9042-cccccccccccc` |

---

## Interpretation

### NoAgents

`Billing Support` and `Chat General` have no agents assigned. Interactions routed to these queues will wait indefinitely or until the queue overflow rule triggers.

- Check whether these queues are actively used in routing flows (Architect, IVR menus, campaigns).
- If they are in use: assign agents or configure a fallback overflow.
- If they are not in use: deactivate or delete the queue.

### DuplicateName

`Sales Inbound` exists in two divisions (`Sales` and `Sales – EMEA`) with the same name. This causes confusion in:

- Reports and dashboards that group by queue name
- Architect flows and campaigns that reference queues by name
- Agent interfaces that display queue names

Consider renaming the EMEA variant to `Sales Inbound – EMEA` to make the distinction explicit.

---

## Remediation

1. **NoAgents:** Verify the queue's role in your routing architecture. Assign agents via Genesys Cloud Admin → Contact Center → Queues → Membership, or configure an overflow route.
2. **DuplicateName:** Rename one or both duplicate queues to ensure names are unique. Update any Architect flows, campaigns, or reports that reference the old name.
3. Queues that are no longer needed should be deactivated, not deleted, to preserve historical reporting data.
