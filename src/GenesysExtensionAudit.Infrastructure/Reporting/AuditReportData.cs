using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.BestPractices;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

// ─── Finding codes for IvrFlowBindingFinding (Phase 1.4) ─────────────────────

/// <summary>
/// Identifies which specific IVR–flow dependency rule was violated.
/// Used to distinguish sub-types within <see cref="IvrFlowBindingFinding"/>.
/// </summary>
public static class IvrBindingCode
{
    /// <summary>IVR binding slot references a flow that has never been published (draft state).</summary>
    public const string FlowIsDraft = "IVR_FLOW_DRAFT";

    /// <summary>IVR binding slot references a flow that has not been republished within the stale threshold.</summary>
    public const string FlowIsStale = "IVR_FLOW_STALE";

    /// <summary>IVR binding slot references a flow ID that cannot be found — likely deleted or moved.</summary>
    public const string FlowNotFound = "IVR_FLOW_NOT_FOUND";

    /// <summary>IVR entry point has DNIS numbers but no open-hours flow binding — calls have no route.</summary>
    public const string NoOpenHoursFlow = "IVR_NO_OPEN_HOURS_FLOW";

    /// <summary>
    /// IVR entry point has DNIS numbers but no schedule group binding.
    /// Without a schedule group the IVR cannot determine which hours flow to invoke.
    /// </summary>
    public const string NoScheduleGroup = "IVR_NO_SCHEDULE_GROUP";
}

// ─── Finding codes for UserTelephonyIntegrityFinding (Phase 1.2) ──────────────

/// <summary>
/// Identifies which specific telephony integrity rule was violated.
/// Used to distinguish sub-types within <see cref="UserTelephonyIntegrityFinding"/>.
/// </summary>
public static class TelephonyIntegrityCode
{
    /// <summary>User profile claims a work-phone extension but has no station assigned.</summary>
    public const string ExtensionWithoutStation = "EXTENSION_WITHOUT_STATION";

    /// <summary>User has a station assigned but no work-phone extension on their profile.</summary>
    public const string StationWithoutExtension = "STATION_WITHOUT_EXTENSION";

    /// <summary>
    /// A DID assigned (by owner user ID) to this user does not appear on the user's profile
    /// contact info — the DID and profile are out of sync.
    /// </summary>
    public const string DidOwnerExtensionMismatch = "DID_OWNER_EXTENSION_MISMATCH";
}

// ─── Finding codes for QueueServiceabilityFinding (Phase 1.3) ────────────────

/// <summary>
/// Identifies which specific queue serviceability rule was violated.
/// Used to distinguish sub-types within <see cref="QueueServiceabilityFinding"/>.
/// </summary>
public static class QueueServiceabilityCode
{
    /// <summary>All checked members are inactive — queue cannot service work.</summary>
    public const string AllInactive = "QUEUE_ALL_INACTIVE";

    /// <summary>None of the checked members could be resolved to a known user — serviceability unknown.</summary>
    public const string AllUnresolvable = "QUEUE_ALL_UNRESOLVABLE";

    /// <summary>A mix of inactive and unresolvable members; no active member found in the checked sample.</summary>
    public const string MixedDegraded = "QUEUE_MIXED_DEGRADED";

    /// <summary>
    /// Queue member count exceeds the configured <c>QueueServiceabilityMaxMembersToCheck</c> cap.
    /// The queue was not checked — raise the cap or investigate manually for large queues.
    /// </summary>
    public const string TooLargeToCheck = "QUEUE_TOO_LARGE_TO_CHECK";
}

// ─── Finding codes for PromptHygieneFinding (Phase 2) ────────────────────────

/// <summary>
/// Identifies which specific prompt hygiene rule was violated.
/// Used to distinguish sub-types within <see cref="PromptHygieneFinding"/>.
/// </summary>
public static class PromptHygieneCode
{
    /// <summary>
    /// Prompt has no language resources configured at all.
    /// Any flow node that plays this prompt will receive no audio — callers hear silence.
    /// </summary>
    public const string NoResources = "PROMPT_NO_RESOURCES";

    /// <summary>
    /// Prompt has one or more language resource slots configured but every slot is missing
    /// both a media file (audio upload) and a TTS fallback string.
    /// Callers matching those languages will hear silence.
    /// </summary>
    public const string NoPlayableMedia = "PROMPT_NO_PLAYABLE_MEDIA";
}

// ─── Finding codes for SiteTopologyFinding (Phase 1.5) ───────────────────────

/// <summary>
/// Identifies which specific site–edge–trunk topology rule was violated.
/// Used to distinguish sub-types within <see cref="SiteTopologyFinding"/>.
/// </summary>
public static class SiteTopologyCode
{
    /// <summary>Site has no edges assigned, or all its edges are offline/inactive — cannot carry traffic.</summary>
    public const string SiteNoActiveEdges = "SITE_NO_ACTIVE_EDGES";

    /// <summary>Edge references a site that does not appear in the site list — the site may have been deleted.</summary>
    public const string EdgeOrphanedSite = "EDGE_ORPHANED_SITE";

    /// <summary>Edge is registered but currently reporting as offline — it cannot carry calls.</summary>
    public const string EdgeOffline = "EDGE_OFFLINE";

    /// <summary>Trunk is hosted on an edge that is currently offline — the trunk cannot carry traffic even if its own state is UP.</summary>
    public const string TrunkEdgeOffline = "TRUNK_EDGE_OFFLINE";

    /// <summary>Trunk is administratively disabled or not in service — calls cannot be routed through it.</summary>
    public const string TrunkOutOfService = "TRUNK_OUT_OF_SERVICE";

    /// <summary>Trunk is reporting a DOWN or UNKNOWN operational state — call routing will fail or is unreliable.</summary>
    public const string TrunkDown = "TRUNK_DOWN";
}

// ─── Finding codes for EdgePerformanceObservation (Phase 1.5 extension) ─────

/// <summary>
/// Identifies which edge-performance heuristic was applied to a per-edge observation row.
/// </summary>
public static class EdgePerformanceCode
{
    /// <summary>Observed conversation share is within the expected range for the edge cohort.</summary>
    public const string Balanced = "EDGE_DISTRIBUTION_BALANCED";

    /// <summary>The edge is online but no observed conversations were matched to it while peers carried traffic.</summary>
    public const string NoObservedTraffic = "EDGE_NO_OBSERVED_TRAFFIC";

    /// <summary>The edge is carrying materially more or less observed conversation load than expected.</summary>
    public const string LoadImbalance = "EDGE_LOAD_IMBALANCE";

    /// <summary>A standby/secondary edge is carrying observed traffic while primary edges remain online.</summary>
    public const string SecondaryCarryingTraffic = "EDGE_SECONDARY_CARRYING_TRAFFIC";

    /// <summary>The edge has a materially elevated rate of error-coded operational events.</summary>
    public const string ElevatedErrorRate = "EDGE_ERROR_RATE_ELEVATED";

    /// <summary>The current run did not capture enough observed traffic to make a distribution judgement.</summary>
    public const string InsufficientData = "EDGE_DISTRIBUTION_INSUFFICIENT_DATA";

    /// <summary>The edge is not online, so distribution analysis is informational only in this view.</summary>
    public const string Unavailable = "EDGE_UNAVAILABLE";
}

// ─── Finding codes for FlappingFinding (Phase 2.2) ───────────────────────────

/// <summary>
/// Identifies which instability pattern was detected within a <see cref="FlappingFinding"/>.
/// </summary>
public static class FlappingCode
{
    /// <summary>
    /// An object's ownership or assignment toggled repeatedly between states within the audit window,
    /// suggesting automation conflict, admin collision, or a competing sync process.
    /// </summary>
    public const string AssignmentFlapping = "ASSIGNMENT_FLAPPING";

    /// <summary>
    /// A flow was repeatedly published or altered within a short window without stabilizing,
    /// suggesting uncontrolled automation or concurrent admin activity.
    /// </summary>
    public const string PublishChurn = "PUBLISH_CHURN";

    /// <summary>
    /// A site, edge, trunk, or station relationship repeatedly transitioned between states
    /// within the audit window, indicating hardware instability or platform sync issues.
    /// </summary>
    public const string ResourceOscillation = "RESOURCE_OSCILLATION";
}

// ─── Finding codes for ChangeAdjacencyFinding (Phase 2.1) ────────────────────

/// <summary>
/// Identifies the relationship between a configuration change and a correlated finding.
/// Used to distinguish sub-types within <see cref="ChangeAdjacencyFinding"/>.
/// </summary>
public static class ChangeAdjacencyCode
{
    /// <summary>
    /// A configuration change event in the audit log touched the same object that has an active finding.
    /// The change preceded or co-occurred with the finding window, suggesting it may be the root cause.
    /// </summary>
    public const string ChangeBeforeFinding = "CHANGE_BEFORE_FINDING";

    /// <summary>
    /// Multiple configuration change events touched the same object within a short window,
    /// suggesting potential churn or conflicting automation that may be contributing to instability.
    /// </summary>
    public const string RepeatedChanges = "REPEATED_CHANGES";
}

// ─── Phase 4.1 — Finding lifecycle classification ───────────────────────────

/// <summary>
/// Lifecycle states assigned when comparing the current run to the previous saved snapshot.
/// </summary>
public static class FindingLifecycleStatus
{
    public const string New = "New";
    public const string Recurrent = "Recurrent";
    public const string Resolved = "Resolved";
}

/// <summary>
/// Change types emitted by historical drift comparison.
/// </summary>
public static class HistoricalDriftChangeType
{
    public const string Added = "Added";
    public const string Changed = "Changed";
    public const string Removed = "Removed";
}

// ─── Findings ───────────────────────────────────────────────────────────────

public sealed record GroupFinding(
    string GroupId,
    string? GroupName,
    string? Type,
    string? State,
    int MemberCount,
    DateTime? DateModified,
    string Issue);

public sealed record QueueFinding(
    string QueueId,
    string? QueueName,
    string? Description,
    int MemberCount,
    string Issue);

public sealed record FlowFinding(
    string FlowId,
    string? FlowName,
    string? FlowType,
    bool IsPublished,
    DateTime? PublishedDate,
    DateTime? DateModified,
    int? DaysSincePublished,
    string Issue);

public sealed record InactiveUserFinding(
    string UserId,
    string? UserName,
    string? Email,
    string? State,
    DateTimeOffset? TokenLastIssuedDate,
    int? DaysSinceLogin,
    string Issue);

public sealed record NoLocationUserFinding(
    string UserId,
    string? UserName,
    string? Email,
    string? State,
    int LocationCount,
    string Issue);

public sealed record DidFinding(
    string DidId,
    string? PhoneNumber,
    string? PoolId,
    string? OwnerType,
    string? OwnerId,
    string? OwnerName,
    string Issue);

public sealed record AuditLogFinding(
    string? AuditId,
    DateTimeOffset? TimestampUtc,
    string? ServiceName,
    string? Action,
    string? UserName,
    string? UserEmail,
    string? EntityType,
    string? EntityName,
    /// <summary>ID of the user who performed the action. Populated when expand=user is applied.</summary>
    string? UserId,
    /// <summary>OAuth client ID used to perform the action.</summary>
    string? ClientId,
    /// <summary>Entity ID extracted from the audit log event (e.g. queue ID, user ID). Used for change-adjacency correlation.</summary>
    string? EntityId,
    /// <summary>Correlation ID linking related audit events in a single operation.</summary>
    string? CorrelationId,
    /// <summary>Severity/level of the audit event (e.g. INFO, WARNING).</summary>
    string? Level);

public sealed record OperationalEventFinding(
    DateTimeOffset? TimestampUtc,
    string? EventDefinitionId,
    string? EventDefinitionName,
    string? EntityId,
    string? EntityName,
    string? CurrentValue,
    string? PreviousValue,
    string? ErrorCode,
    string? ConversationId);

/// <summary>
/// Per-edge operational load observation derived from matched operational events.
/// This is a hybrid metrics/finding row: every analyzed edge receives a row, but only
/// rows where <see cref="IsAnomalous"/> is true should be treated as active findings.
/// </summary>
public sealed record EdgePerformanceObservation(
    string? SiteId,
    string? SiteName,
    string EdgeId,
    string? EdgeName,
    string EdgeRole,
    string? OnlineStatus,
    bool ExpectedToCarryLoad,
    string FindingCode,
    string StatusLabel,
    bool IsAnomalous,
    FindingSeverity Severity,
    int ObservedConversationCount,
    int SiteObservedConversationCount,
    int ExpectedEdgeCount,
    double ObservedSharePercent,
    double ExpectedSharePercent,
    double ShareDeltaPercent,
    int OperationalEventCount,
    int ErrorEventCount,
    double ErrorRatePercent,
    DateTimeOffset? LastEventUtc,
    string Issue,
    string RecommendedAction);

public sealed record OutboundEventFinding(
    DateTimeOffset? TimestampUtc,
    string? EventId,
    string? Name,
    string? Category,
    string? Level,
    string? Code,
    string? Message,
    string? CorrelationId);

// ─── Phase 2.1 — Change adjacency marker ─────────────────────────────────────

/// <summary>
/// A correlation finding that links a recent audit log configuration change
/// to an existing audit finding on the same object.
/// Surfaces the "what changed before the problem appeared?" signal.
/// </summary>
public sealed record ChangeAdjacencyFinding(
    /// <summary>One of the <see cref="ChangeAdjacencyCode"/> constants.</summary>
    string FindingCode,
    /// <summary>The type of the affected object (e.g. Queue, User, Flow, Site).</summary>
    string? AffectedObjectType,
    /// <summary>The ID of the object that was changed and also has an active finding.</summary>
    string? AffectedObjectId,
    /// <summary>The name of the affected object.</summary>
    string? AffectedObjectName,
    /// <summary>Timestamp of the most recent change event for this object.</summary>
    DateTimeOffset? ChangeTimestamp,
    /// <summary>The action performed (e.g. UPDATE, DELETE) from the audit log entry.</summary>
    string? ChangeAction,
    /// <summary>The user who made the change.</summary>
    string? ChangedBy,
    /// <summary>Number of change events found for this object within the lookback window.</summary>
    int ChangeCount,
    /// <summary>Brief description of the correlated finding category (e.g. "Queue serviceability degraded").</summary>
    string? RelatedFindingType,
    string Issue,
    FindingSeverity Severity,
    string RecommendedAction);

// ─── Phase 2.2 — Flapping and instability detection ──────────────────────────

/// <summary>
/// A finding that identifies an object repeatedly changing state or assignment
/// within a short audit window — a signal of automation conflict, admin collision,
/// platform sync instability, or uncontrolled churn.
/// </summary>
public sealed record FlappingFinding(
    /// <summary>One of the <see cref="FlappingCode"/> constants.</summary>
    string FindingCode,
    /// <summary>The primary type of the affected object (e.g. Queue, User, Flow, Edge).</summary>
    string? AffectedObjectType,
    /// <summary>The ID of the object whose change log shows a flapping pattern.</summary>
    string? AffectedObjectId,
    /// <summary>The name of the affected object.</summary>
    string? AffectedObjectName,
    /// <summary>Timestamp of the earliest change event observed in the flapping window.</summary>
    DateTimeOffset? FirstChangeUtc,
    /// <summary>Timestamp of the most recent change event observed in the flapping window.</summary>
    DateTimeOffset? LastChangeUtc,
    /// <summary>Total number of change events found for this object within the lookback window.</summary>
    int ChangeCount,
    /// <summary>Number of distinct action types (e.g. CREATE, DELETE, UPDATE) observed.</summary>
    int DistinctActionCount,
    /// <summary>Sorted list of the unique action verbs seen for this object in the window.</summary>
    IReadOnlyList<string> ObservedActions,
    string Issue,
    FindingSeverity Severity,
    string RecommendedAction);

// ─── Phase 2.3 — Hot spot ranking ────────────────────────────────────────────

/// <summary>
/// A cross-domain anomaly concentration finding: an object that appears in findings
/// from two or more distinct audit domains, indicating a higher-priority investigation target.
/// Objects surfaced here may represent the root of cascading failures.
/// </summary>
public sealed record HotSpotFinding(
    /// <summary>Rank among hot-spot findings (1 = highest impact).</summary>
    int Rank,
    /// <summary>The object ID, if available.</summary>
    string? ObjectId,
    /// <summary>The object name, if available.</summary>
    string? ObjectName,
    /// <summary>The object type (e.g. Queue, User, Flow, Edge).</summary>
    string? ObjectType,
    /// <summary>Total number of finding entries across all domains that reference this object.</summary>
    int TotalFindingCount,
    /// <summary>Number of distinct audit domains (e.g. Queue Serviceability, Stale Flow) this object appears in.</summary>
    int DistinctDomainCount,
    /// <summary>The audit domains this object appears in, sorted alphabetically.</summary>
    IReadOnlyList<string> AffectedDomains,
    string Issue,
    FindingSeverity Severity,
    string RecommendedAction);

// ─── Phase 2 — Architect Prompt Hygiene ──────────────────────────────────────

/// <summary>
/// An architect prompt that cannot produce audio for one or more language variants:
/// either no language resources are configured, or all configured resources lack
/// both a media file and a TTS fallback string.
/// </summary>
public sealed record PromptHygieneFinding(
    string PromptId,
    string? PromptName,
    string? Description,
    bool IsSystemPrompt,
    /// <summary>Number of language resource slots configured on this prompt.</summary>
    int ResourceCount,
    /// <summary>Comma-separated list of affected language codes (e.g. "en-us, fr-fr").</summary>
    string AffectedLanguages,
    /// <summary>One of the <see cref="PromptHygieneCode"/> constants.</summary>
    string FindingCode,
    string Issue,
    FindingSeverity Severity,
    FindingCategory Category,
    string RecommendedAction);

// ─── Phase 1.2 — User telephony integrity ────────────────────────────────────

/// <summary>
/// A cross-endpoint contradiction involving a user's telephony identity:
/// profile extension, assigned station, and/or DID ownership do not cohere.
/// </summary>
public sealed record UserTelephonyIntegrityFinding(
    string UserId,
    string? UserName,
    string? Email,
    string? UserState,
    /// <summary>Raw extension from the user's profile work-phone contact info (if any).</summary>
    string? ProfileExtensionRaw,
    /// <summary>Station ID referenced on the user account (if any).</summary>
    string? StationId,
    string? StationName,
    /// <summary>DID phone number that triggered this finding (if applicable).</summary>
    string? RelatedDidNumber,
    /// <summary>One of the <see cref="TelephonyIntegrityCode"/> constants.</summary>
    string FindingCode,
    string Issue,
    FindingSeverity Severity,
    FindingCategory Category,
    string RecommendedAction);

// ─── Phase 1.4 — IVR flow dependency ─────────────────────────────────────────

/// <summary>
/// A binding slot on an IVR entry point that references a flow in a degraded state:
/// the flow is a draft, stale, deleted, or the IVR has no flow bound at all.
/// Each finding represents one (IVR, binding slot) pair.
/// </summary>
public sealed record IvrFlowBindingFinding(
    string IvrId,
    string? IvrName,
    /// <summary>Phone numbers (DNIS) routed through this IVR.</summary>
    IReadOnlyList<string> Dnis,
    /// <summary>Which slot on the IVR: OpenHours, ClosedHours, HolidayHours, or NoBinding.</summary>
    string BindingSlot,
    string? BoundFlowId,
    string? BoundFlowName,
    /// <summary>Days since the bound flow was last published. Null if never published or not found.</summary>
    int? FlowDaysSincePublished,
    /// <summary>One of the <see cref="IvrBindingCode"/> constants.</summary>
    string FindingCode,
    string Issue,
    FindingSeverity Severity,
    FindingCategory Category,
    string RecommendedAction);

// ─── Phase 1 Identity & License Hygiene ──────────────────────────────────────

/// <summary>
/// Audit 1 — Stale License Usage:
/// A user who holds a billable license but has not logged in within the threshold period.
/// </summary>
public sealed record StaleLicenseFinding(
    string UserId,
    string? UserName,
    string? Email,
    string? State,
    IReadOnlyList<string> AssignedLicenses,
    DateTimeOffset? TokenLastIssuedDate,
    int? DaysSinceLogin,
    string Issue);

/// <summary>
/// Audit 2 — License Over-Provisioning:
/// A user assigned a premium (CX3/WEM/Outbound) license with no evidence of recent usage.
/// </summary>
public sealed record LicenseOverProvisioningFinding(
    string UserId,
    string? UserName,
    string? Email,
    string? State,
    IReadOnlyList<string> AllAssignedLicenses,
    IReadOnlyList<string> OverProvisionedLicenses,
    DateTimeOffset? TokenLastIssuedDate,
    int? DaysSinceLogin,
    string Issue,
    string RecommendedAction);

/// <summary>
/// Audit 3 — Role & Group Overlap:
/// A role is directly assigned to a user but is already inherited through a group membership.
/// The direct assignment is redundant and increases access-management complexity.
/// </summary>
public sealed record RoleGroupOverlapFinding(
    string UserId,
    string? UserName,
    string? Email,
    string? UserState,
    string RoleId,
    string? RoleName,
    string? DivisionId,
    string? DivisionName,
    string GroupId,
    string? GroupName,
    string Issue,
    string RecommendedAction);

// ─── Phase 1.5 — Site–edge–trunk topology ────────────────────────────────────

/// <summary>
/// A finding representing an anomaly in the site–edge–trunk topology:
/// an offline edge, an edge orphaned from its site, a trunk that is down or
/// out of service, or a site with no active edges to carry traffic.
/// Each finding covers one object (site, edge, or trunk) and one anomaly.
/// </summary>
public sealed record SiteTopologyFinding(
    /// <summary>One of the <see cref="SiteTopologyCode"/> constants.</summary>
    string FindingCode,
    /// <summary>The primary object type for this finding: Site, Edge, or Trunk.</summary>
    string ObjectType,
    string? ObjectId,
    string? ObjectName,
    /// <summary>The site that this edge or trunk ultimately belongs to (null if site cannot be determined).</summary>
    string? SiteId,
    string? SiteName,
    /// <summary>For trunk findings: the edge that hosts the trunk.</summary>
    string? EdgeId,
    string? EdgeName,
    string? TrunkState,
    string Issue,
    FindingSeverity Severity,
    FindingCategory Category,
    string RecommendedAction);

// ─── Phase 1.3 — Queue serviceability ────────────────────────────────────────

/// <summary>
/// A queue that appears configured but has degraded or zero serviceability:
/// its member list contains too many inactive or unresolvable users to handle work.
/// </summary>
public sealed record QueueServiceabilityFinding(
    string QueueId,
    string? QueueName,
    int TotalMembersOnRecord,
    /// <summary>Number of members examined (may be capped at the configured page size).</summary>
    int MembersChecked,
    int ActiveMemberCount,
    int InactiveMemberCount,
    /// <summary>Members whose user ID could not be found in the fetched user list.</summary>
    int UnresolvableMemberCount,
    /// <summary>One of the <see cref="QueueServiceabilityCode"/> constants.</summary>
    string FindingCode,
    string Issue,
    FindingSeverity Severity,
    FindingCategory Category,
    string RecommendedAction);

/// <summary>
/// A lifecycle view over finding summaries when comparing the current run to the previously saved snapshot.
/// </summary>
public sealed record FindingLifecycleFinding(
    /// <summary>One of the <see cref="FindingLifecycleStatus"/> constants.</summary>
    string LifecycleStatus,
    string Domain,
    string FindingType,
    string FindingKey,
    string? ObjectId,
    string? ObjectName,
    string Issue,
    FindingSeverity Severity,
    DateTimeOffset FirstSeenUtc,
    DateTimeOffset LastSeenUtc,
    int ObservationCount);

/// <summary>
/// A normalized relationship captured for historical comparison across audit runs.
/// </summary>
public sealed record AuditRelationshipSnapshot(
    string Domain,
    string RelationshipType,
    string RelationshipKey,
    string ObjectType,
    string? ObjectId,
    string? ObjectName,
    string NormalizedValue,
    string DisplayValue);

/// <summary>
/// A material relationship change detected between the current run and the previous saved snapshot.
/// </summary>
public sealed record HistoricalDriftFinding(
    /// <summary>One of the <see cref="HistoricalDriftChangeType"/> constants.</summary>
    string ChangeType,
    string Domain,
    string RelationshipType,
    string RelationshipKey,
    string ObjectType,
    string? ObjectId,
    string? ObjectName,
    string? PreviousValue,
    string? CurrentValue,
    string Issue,
    FindingSeverity Severity,
    string RecommendedAction);

// ─── Combined report ─────────────────────────────────────────────────────────

/// <summary>
/// All findings from a complete audit run, ready for Excel export.
/// </summary>
public sealed class AuditReportData
{
    public string RunId { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset GeneratedAt { get; init; } = DateTimeOffset.Now;
    public DateTimeOffset RunStartedAtUtc { get; init; }
    public DateTimeOffset RunCompletedAtUtc { get; init; }
    public string OrgRegion { get; init; } = string.Empty;
    public AuditRunOptions Options { get; init; } = new();

    // Extension audit (existing engine)
    public AuditEngine.AuditReport ExtensionReport { get; init; } = new();

    // New checks
    public IReadOnlyList<GroupFinding> GroupFindings { get; init; } = [];
    public IReadOnlyList<QueueFinding> QueueFindings { get; init; } = [];
    public IReadOnlyList<FlowFinding> FlowFindings { get; init; } = [];
    public IReadOnlyList<InactiveUserFinding> InactiveUserFindings { get; init; } = [];
    public IReadOnlyList<NoLocationUserFinding> NoLocationUserFindings { get; init; } = [];
    public IReadOnlyList<DidFinding> DidFindings { get; init; } = [];
    public IReadOnlyList<AuditLogFinding> AuditLogFindings { get; init; } = [];
    public IReadOnlyList<OperationalEventFinding> OperationalEventFindings { get; init; } = [];
    public IReadOnlyList<OutboundEventFinding> OutboundEventFindings { get; init; } = [];

    // Phase 2 — Architect Prompt Hygiene
    public IReadOnlyList<PromptHygieneFinding> PromptHygieneFindings { get; init; } = [];

    // Phase 1.2 — User telephony integrity (cross-endpoint)
    public IReadOnlyList<UserTelephonyIntegrityFinding> UserTelephonyIntegrityFindings { get; init; } = [];

    // Phase 1.4 — IVR flow dependency (entry point → flow binding integrity)
    public IReadOnlyList<IvrFlowBindingFinding> IvrFlowBindingFindings { get; init; } = [];

    // Phase 1.5 — Site–edge–trunk topology integrity
    public IReadOnlyList<SiteTopologyFinding> SiteTopologyFindings { get; init; } = [];

    // Phase 1.5 extension — per-edge operational load and distribution
    public IReadOnlyList<EdgePerformanceObservation> EdgePerformanceObservations { get; init; } = [];

    // Phase 1.3 — Queue serviceability (member active-state cross-reference)
    public IReadOnlyList<QueueServiceabilityFinding> QueueServiceabilityFindings { get; init; } = [];

    // Phase 1 Identity & License Hygiene
    public IReadOnlyList<StaleLicenseFinding> StaleLicenseFindings { get; init; } = [];
    public IReadOnlyList<LicenseOverProvisioningFinding> LicenseOverProvisioningFindings { get; init; } = [];
    public IReadOnlyList<RoleGroupOverlapFinding> RoleGroupOverlapFindings { get; init; } = [];

    // Phase 2.1 — Change adjacency marker (config changes correlated with active findings)
    public IReadOnlyList<ChangeAdjacencyFinding> ChangeAdjacencyFindings { get; init; } = [];

    // Phase 2.2 — Flapping and instability detection (audit-log driven change oscillation)
    public IReadOnlyList<FlappingFinding> FlappingDetectionFindings { get; init; } = [];

    // Phase 2.3 — Hot spot ranking (objects appearing in multiple audit domains)
    public IReadOnlyList<HotSpotFinding> HotSpotFindings { get; init; } = [];

    // Phase 4.1 — Historical baseline comparison
    public bool FindingLifecycleWasComputed { get; set; }
    public DateTimeOffset? PreviousSnapshotGeneratedAtUtc { get; set; }
    public string? PreviousSnapshotPath { get; set; }
    public IReadOnlyList<FindingLifecycleFinding> FindingLifecycleFindings { get; set; } = [];

    // Phase 4.2 — Normalized relationship baselines + drift detection
    public IReadOnlyList<AuditRelationshipSnapshot> RelationshipSnapshots { get; init; } = [];
    public bool HistoricalDriftWasComputed { get; set; }
    public IReadOnlyList<HistoricalDriftFinding> HistoricalDriftFindings { get; set; } = [];

    // Phase 2.4 — Best-practice policy guidance
    public bool BestPracticeGuidanceWasComputed { get; set; }
    public IReadOnlyList<BestPracticeGuidanceFinding> BestPracticeGuidanceFindings { get; set; } = [];
    public IReadOnlyList<string> UnmappedBestPracticeFindingTypes { get; set; } = [];
}
