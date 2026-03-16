
# Audit Checks

Phase 1: Identity & License Hygiene (The "Quick Wins")

Focus: Cleaning up the user roster and identifying immediate cost savings.

Audit 1: Stale License Usage
Logic: Correlate GET /api/v2/users (lastLoginDate) with GET /api/v2/license/users.
Criteria: Flag users with a billable license who haven't logged in for > 60 days.

Audit 2: License Over-Provisioning
Logic: Compare assigned license tier against active permissions and usage metrics.
Criteria: Users on CX3 tiers with zero record of using WEM or Outbound features.

Audit 3: Role & Group Overlap
Logic: Inspect GET /api/v2/users/{userId}/roles.
Criteria: Flag direct license assignments that are already covered by Group-inherited roles.

Phase 2: Architectural Fragility

Focus: Ensuring the automated "brains" of the platform don't have silent failures.
Audit 4: Data Action "Fail-Fast" Integrity
Logic: Parse Flow configurations from GET /api/v2/architect/flows.
Criteria: Identify "Call Data Action" blocks where the failure or timeout paths are unlinked or lead directly to a generic "Disconnect."

Audit 5: Orphaned Prompt/Audio Analysis
Logic: Cross-reference GET /api/v2/architect/prompts with all published Flow JSONs.
Criteria: Flag prompts that exist in the library but are not called by any active routing logic.

Audit 6: Flow Outcome Health
Logic: Query /api/v2/analytics/flows/aggregates/query.
Criteria: Flag flows that have outcomes defined but have registered 0 success/fail events in 48 hours.

Phase 3: Telephony & Connectivity

Focus: Preventing "Dead Air" and capacity-related outages.

Audit 7: Queue Serviceability Correlation
Logic: Join GET /api/v2/routing/queues/{id}/members with /api/v2/analytics/queues/observations/query.
Criteria: Flag "Active" queues where 100% of members are "Offline" or "Out of Office."

Audit 8: Edge/Trunk Capacity Drift
Logic: Compare maxConcurrentCalls on External Trunks vs. the tMaxConcurrent metric from Analytics.
Criteria: Flag trunks where peak utilization exceeds 85% of configured capacity.

Phase 4: Platform Integrity & Security

Focus: Advanced monitoring and "Platform-Side" evidence gathering.

Audit 9: The "Ghost Participant" Tracker
Logic: Query /api/v2/analytics/conversations/details/query.
Criteria: Find conversations in "Routing" segment for > 5 minutes without an emit to an agent.

Audit 10: Public API Rate Limit Monitoring
Logic: Aggregate /api/v2/usage/query.
Criteria: Report Client IDs hitting > 70% of their assigned rate limit tier.
