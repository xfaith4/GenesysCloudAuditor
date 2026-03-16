using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Services;

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
    string? EntityName);

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

public sealed record OutboundEventFinding(
    DateTimeOffset? TimestampUtc,
    string? EventId,
    string? Name,
    string? Category,
    string? Level,
    string? Code,
    string? Message,
    string? CorrelationId);

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

// ─── Combined report ─────────────────────────────────────────────────────────

/// <summary>
/// All findings from a complete audit run, ready for Excel export.
/// </summary>
public sealed class AuditReportData
{
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

    // Phase 1.2 — User telephony integrity (cross-endpoint)
    public IReadOnlyList<UserTelephonyIntegrityFinding> UserTelephonyIntegrityFindings { get; init; } = [];

    // Phase 1.4 — IVR flow dependency (entry point → flow binding integrity)
    public IReadOnlyList<IvrFlowBindingFinding> IvrFlowBindingFindings { get; init; } = [];

    // Phase 1.3 — Queue serviceability (member active-state cross-reference)
    public IReadOnlyList<QueueServiceabilityFinding> QueueServiceabilityFindings { get; init; } = [];
}
