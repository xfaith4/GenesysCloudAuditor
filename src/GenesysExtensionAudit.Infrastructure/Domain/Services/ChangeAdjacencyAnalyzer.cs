using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Phase 2.1 — Change Adjacency Marker.
/// Correlates audit log configuration change events with active findings to surface
/// cases where a recent change touched the same object that now has an anomaly.
///
/// This is the first step toward the full regression-chain builder described in the
/// roadmap. At this stage it produces a flat list of (change, finding) adjacency
/// records that an operator can use to prioritise investigation.
///
/// Correlation is performed by matching the entity ID and entity name from audit log
/// entries against the object IDs and names captured in the active findings.
/// When a match is found, a <see cref="ChangeAdjacencyFinding"/> is produced that
/// links the change event to the related finding category.
/// </summary>
public sealed class ChangeAdjacencyAnalyzer
{
    /// <summary>
    /// Correlates audit log findings with active findings by object ID and name.
    /// </summary>
    /// <param name="auditLogFindings">Audit log events collected during the run.</param>
    /// <param name="activeFindings">All non-log findings from the current audit run.</param>
    /// <param name="windowMinutes">
    /// Only audit log entries within this many minutes of the run time are considered.
    /// Pass <see cref="int.MaxValue"/> to include all entries regardless of age.
    /// </param>
    /// <param name="referenceTime">
    /// The time to measure the lookback window from. Defaults to <see cref="DateTimeOffset.UtcNow"/>.
    /// </param>
    public IReadOnlyList<ChangeAdjacencyFinding> Analyze(
        IReadOnlyList<AuditLogFinding> auditLogFindings,
        ActiveFindingIndex activeFindings,
        int windowMinutes = 1440,
        DateTimeOffset? referenceTime = null)
    {
        if (auditLogFindings.Count == 0 || activeFindings.IsEmpty)
            return [];

        var cutoff = (referenceTime ?? DateTimeOffset.UtcNow).AddMinutes(-Math.Max(1, windowMinutes));
        var results = new List<ChangeAdjacencyFinding>();

        // Group audit log entries by entity, keeping only those within the time window
        var recentEntries = auditLogFindings
            .Where(e => e.TimestampUtc.HasValue && e.TimestampUtc.Value >= cutoff)
            .ToList();

        if (recentEntries.Count == 0)
            return [];

        // Group by entity: prefer entity ID, fall back to entity name
        var byEntity = recentEntries
            .GroupBy(e => NormalizeKey(e.EntityId) ?? NormalizeKey(e.EntityName) ?? string.Empty)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var (entityKey, changes) in byEntity)
        {
            // Look for a matching finding using the same entity key
            if (!activeFindings.TryGetFindingType(entityKey, out var relatedFindingType))
                continue;

            var mostRecent = changes
                .OrderByDescending(c => c.TimestampUtc ?? DateTimeOffset.MinValue)
                .First();

            var changeCount = changes.Count;
            var entityName = mostRecent.EntityName
                ?? changes.FirstOrDefault(c => c.EntityName is not null)?.EntityName;
            var entityType = mostRecent.EntityType
                ?? changes.FirstOrDefault(c => c.EntityType is not null)?.EntityType;

            // Determine finding code: repeated changes are a distinct signal
            var findingCode = changeCount >= 3
                ? ChangeAdjacencyCode.RepeatedChanges
                : ChangeAdjacencyCode.ChangeBeforeFinding;

            var severity = findingCode == ChangeAdjacencyCode.RepeatedChanges
                ? FindingSeverity.Medium
                : FindingSeverity.Info;

            var issue = findingCode == ChangeAdjacencyCode.RepeatedChanges
                ? $"Object '{entityName ?? entityKey}' ({entityType ?? "unknown type"}) had {changeCount} configuration change(s) " +
                  $"within the audit window and also has an active '{relatedFindingType}' finding. " +
                  "Repeated changes may indicate automation conflict, admin collision, or configuration instability."
                : $"Object '{entityName ?? entityKey}' ({entityType ?? "unknown type"}) was changed by " +
                  $"'{mostRecent.UserName ?? "unknown"}' at {mostRecent.TimestampUtc:u} and " +
                  $"also has an active '{relatedFindingType}' finding. The change may be related to the anomaly.";

            results.Add(new ChangeAdjacencyFinding(
                FindingCode: findingCode,
                AffectedObjectType: entityType,
                AffectedObjectId: mostRecent.EntityId,
                AffectedObjectName: entityName,
                ChangeTimestamp: mostRecent.TimestampUtc,
                ChangeAction: mostRecent.Action,
                ChangedBy: mostRecent.UserName,
                ChangeCount: changeCount,
                RelatedFindingType: relatedFindingType,
                Issue: issue,
                Severity: severity,
                RecommendedAction: findingCode == ChangeAdjacencyCode.RepeatedChanges
                    ? "Review the change history for this object. If automation is responsible, check for conflicting policies or runbooks. " +
                      "If the changes are manual, review with the change owner before taking corrective action."
                    : "Review the change made to this object and confirm it was intentional. If the change preceded the anomaly, " +
                      "consider rolling back or correcting the configuration."));
        }

        return results
            .OrderBy(f => f.Severity)
            .ThenByDescending(f => f.ChangeTimestamp ?? DateTimeOffset.MinValue)
            .ToList();
    }

    private static string? NormalizeKey(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}

/// <summary>
/// An index of active finding object IDs and names for O(1) lookup during change-adjacency correlation.
/// Built from all non-log findings in a completed audit run.
/// </summary>
public sealed class ActiveFindingIndex
{
    private readonly Dictionary<string, string> _index;

    private ActiveFindingIndex(Dictionary<string, string> index)
        => _index = index;

    /// <summary>True if the index contains no entries (no active findings to correlate against).</summary>
    public bool IsEmpty => _index.Count == 0;

    /// <summary>
    /// Returns true if the given key (entity ID or name) matches a known finding object,
    /// and outputs the finding type label for use in the adjacency message.
    /// </summary>
    public bool TryGetFindingType(string key, out string findingType)
        => _index.TryGetValue(key, out findingType!);

    /// <summary>
    /// Builds an index from all finding collections in a completed <see cref="AuditReportData"/>.
    /// Only findings with non-null, non-empty object identifiers are indexed.
    /// </summary>
    public static ActiveFindingIndex Build(AuditReportData report)
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void Add(string? id, string? name, string label)
        {
            if (!string.IsNullOrWhiteSpace(id))   index.TryAdd(id, label);
            if (!string.IsNullOrWhiteSpace(name))  index.TryAdd(name, label);
        }

        foreach (var f in report.QueueServiceabilityFindings)
            Add(f.QueueId, f.QueueName, "Queue Serviceability");
        foreach (var f in report.QueueFindings)
            Add(f.QueueId, f.QueueName, "Queue Hygiene");
        foreach (var f in report.FlowFindings)
            Add(f.FlowId, f.FlowName, "Stale Flow");
        foreach (var f in report.IvrFlowBindingFindings)
            Add(f.IvrId, f.IvrName, "IVR Flow Binding");
        foreach (var f in report.UserTelephonyIntegrityFindings)
            Add(f.UserId, f.UserName, "User Telephony Integrity");
        foreach (var f in report.SiteTopologyFindings)
            Add(f.ObjectId, f.ObjectName, $"Site Topology ({f.ObjectType})");
        foreach (var f in report.GroupFindings)
            Add(f.GroupId, f.GroupName, "Group Hygiene");
        foreach (var f in report.DidFindings)
            Add(f.DidId, f.PhoneNumber, "DID Mismatch");
        foreach (var f in report.InactiveUserFindings)
            Add(f.UserId, f.UserName, "Inactive User");
        foreach (var f in report.PromptHygieneFindings)
            Add(f.PromptId, f.PromptName, "Prompt Hygiene");
        foreach (var f in report.StaleLicenseFindings)
            Add(f.UserId, f.UserName, "Stale License");
        foreach (var f in report.LicenseOverProvisioningFindings)
            Add(f.UserId, f.UserName, "License Over-Provisioning");

        return new ActiveFindingIndex(index);
    }
}
