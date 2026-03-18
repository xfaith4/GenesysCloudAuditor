namespace GenesysExtensionAudit.Application;

/// <summary>
/// Input parameters for a single audit run.
/// Passed from the ViewModel to IAuditOrchestrator.
/// </summary>
public sealed class AuditRunOptions
{
    /// <summary>Records per page when paging Genesys endpoints (1–500).</summary>
    public int PageSize { get; init; } = 100;

    /// <summary>
    /// When false (default): requests /api/v2/users with &amp;state=active.
    /// When true: requests all users (active + inactive).
    /// </summary>
    public bool IncludeInactiveUsers { get; init; }

    /// <summary>
    /// Flows whose last published date is older than this many days are flagged as stale.
    /// Default: 90 days.
    /// </summary>
    public int StaleFlowThresholdDays { get; init; } = 90;

    /// <summary>
    /// Users whose last token-issued date is older than this many days are flagged as inactive.
    /// Default: 90 days.
    /// </summary>
    public int InactiveUserThresholdDays { get; init; } = 90;

    /// <summary>
    /// Run extension consistency checks (profile vs assigned extensions).
    /// </summary>
    public bool RunExtensionAudit { get; init; } = true;

    /// <summary>
    /// Run group checks (empty/single-member groups).
    /// </summary>
    public bool RunGroupAudit { get; init; } = true;

    /// <summary>
    /// Run queue checks (empty queues / duplicate names).
    /// </summary>
    public bool RunQueueAudit { get; init; } = true;

    /// <summary>
    /// Run flow checks (stale/unpublished architect flows).
    /// </summary>
    public bool RunFlowAudit { get; init; } = true;

    /// <summary>
    /// Run inactive-user checks based on last login/token issue date.
    /// </summary>
    public bool RunInactiveUserAudit { get; init; } = true;

    /// <summary>
    /// Run DID checks (unassigned/orphaned/inactive-owner/mismatch).
    /// </summary>
    public bool RunDidAudit { get; init; } = true;

    /// <summary>
    /// Run audit logs transaction flow (service mapping -> submit -> poll -> results).
    /// </summary>
    public bool RunAuditLogs { get; init; } = false;

    /// <summary>
    /// Lookback window used for audit logs query interval.
    /// Default aligns with Genesys.Core behavior.
    /// </summary>
    public int AuditLogLookbackHours { get; init; } = 1;

    /// <summary>
    /// Optional audit-log service names to include in the query.
    /// Empty means "all catalog entities" from service mapping.
    /// </summary>
    public IReadOnlyList<string> AuditLogServiceNames { get; init; } = [];

    /// <summary>
    /// Run operational events query path.
    /// </summary>
    public bool RunOperationalEventLogs { get; init; } = false;

    /// <summary>
    /// Lookback window in days for operational events query.
    /// Default: 7 days.
    /// </summary>
    public int OperationalEventLookbackDays { get; init; } = 7;

    /// <summary>
    /// Run outbound events query path.
    /// </summary>
    public bool RunOutboundEvents { get; init; } = false;

    // ─── Phase 1.5 — Site–edge–trunk topology audit ──────────────────────────

    /// <summary>
    /// Fetch telephony sites, edges, and trunks and cross-reference their state to detect:
    /// sites with no active edges, offline edges, orphaned edge-to-site bindings,
    /// and trunks that are down or out of service (ROADMAP Phase 1.5).
    /// </summary>
    public bool RunSiteTopologyAudit { get; init; } = true;

    // ─── Phase 1.4 — Flow dependency audit ───────────────────────────────────

    /// <summary>
    /// Fetch IVR configurations and cross-reference bound flows against the flow list to detect
    /// IVRs bound to draft flows, stale flows, deleted flows, or with no flow binding at all
    /// (ROADMAP Phase 1.4).
    /// Also requires <see cref="RunFlowAudit"/> to be true so flow data is fetched.
    /// </summary>
    public bool RunFlowDependencyAudit { get; init; } = true;

    // ─── Phase 1.2 — User telephony integrity ────────────────────────────────

    /// <summary>
    /// Cross-reference user profile extensions, station assignments, and DID ownership
    /// to detect telephony identity contradictions (ROADMAP Phase 1.2).
    /// Requires users, extensions, and DIDs to be fetched.
    /// </summary>
    public bool RunUserTelephonyAudit { get; init; } = true;

    // ─── Phase 1.3 — Queue serviceability ────────────────────────────────────

    /// <summary>
    /// Fetch queue members for non-empty queues and cross-reference against user active state
    /// to detect queues with no serviceable membership (ROADMAP Phase 1.3).
    /// Fetches up to <see cref="QueueServiceabilityMemberPageSize"/> members per queue.
    /// </summary>
    public bool RunQueueServiceabilityAudit { get; init; } = true;

    /// <summary>
    /// Number of members fetched per queue for serviceability analysis.
    /// Capped at 100 to limit API call volume on large tenants.
    /// Default: 100.
    /// </summary>
    public int QueueServiceabilityMemberPageSize { get; init; } = 100;

    /// <summary>
    /// Queues with a member count above this threshold are skipped during serviceability
    /// member fetching to prevent excessive API load on extremely large queues.
    /// Set to 0 to disable the cap (check all queues regardless of size).
    /// Default: 500.
    /// </summary>
    public int QueueServiceabilityMaxMembersToCheck { get; init; } = 500;

    // ─── Phase 1 Identity & License Hygiene ──────────────────────────────────

    /// <summary>
    /// Audit 1: Correlate license assignments with last login date.
    /// Flags users with a billable license who have not logged in within
    /// <see cref="StaleLicenseThresholdDays"/> days.
    /// Requires fetching GET /api/v2/license/users in addition to user data.
    /// </summary>
    public bool RunStaleLicenseAudit { get; init; } = true;

    /// <summary>
    /// Number of days of login inactivity after which a licensed user is considered stale.
    /// Default: 60 days (as specified in the audit criteria).
    /// </summary>
    public int StaleLicenseThresholdDays { get; init; } = 60;

    /// <summary>
    /// Audit 2: Flag users on a premium (CX3/WEM/Outbound) license tier who show
    /// no evidence of using the features that justify the higher tier.
    /// Reuses license data already fetched for <see cref="RunStaleLicenseAudit"/>.
    /// </summary>
    public bool RunLicenseOverProvisioningAudit { get; init; } = true;

    /// <summary>
    /// Audit 3: Fetch per-user roles and flag direct role assignments that are already
    /// covered by a group-inherited role in the same division.
    /// Makes one API call per user up to <see cref="RoleGroupOverlapMaxUsersToCheck"/>.
    /// </summary>
    public bool RunRoleGroupOverlapAudit { get; init; } = true;

    /// <summary>
    /// Maximum number of users for which role data is fetched during the Role & Group
    /// Overlap audit. Set to 0 to check all users (may be slow for very large orgs).
    /// Default: 200.
    /// </summary>
    public int RoleGroupOverlapMaxUsersToCheck { get; init; } = 200;

    // ─── Phase 2 — Architect Prompt Hygiene ──────────────────────────────────

    /// <summary>
    /// Fetch all architect prompts and flag those with no usable audio resources:
    /// no language resources configured at all, or all configured languages missing
    /// both a media file and a TTS fallback string. Calls to these prompts play silence
    /// or fail, depending on the flow's error-handling configuration.
    /// </summary>
    public bool RunPromptHygieneAudit { get; init; } = true;

    // ─── Phase 2.1 — Change adjacency marker ─────────────────────────────────

    /// <summary>
    /// Cross-reference audit log events with active findings to identify config changes
    /// that preceded or co-occurred with anomalies on the same objects (ROADMAP Phase 2.1).
    /// Only produces results when <see cref="RunAuditLogs"/> is also enabled.
    /// </summary>
    public bool RunChangeAdjacencyAudit { get; init; } = true;

    /// <summary>
    /// How many minutes before the current run to consider an audit log event "adjacent"
    /// to a finding. Events older than this window are excluded from correlation.
    /// Default: 1440 (24 hours).
    /// </summary>
    public int ChangeAdjacencyWindowMinutes { get; init; } = 1440;
}
