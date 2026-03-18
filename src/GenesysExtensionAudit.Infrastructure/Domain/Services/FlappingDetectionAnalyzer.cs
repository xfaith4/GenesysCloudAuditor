using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Phase 2.2 — Flapping and Instability Detection.
/// Scans audit log events for objects that repeatedly change state within a short window,
/// indicating automation conflict, admin collision, publish churn, or infrastructure instability.
///
/// Three patterns are detected:
/// <list type="bullet">
///   <item><see cref="FlappingCode.AssignmentFlapping"/> — an object's assignment toggled
///         between two or more distinct action types (e.g. ASSIGN / UNASSIGN).</item>
///   <item><see cref="FlappingCode.PublishChurn"/> — a flow object was changed multiple
///         times within the window, suggesting repeated publish/edit cycles.</item>
///   <item><see cref="FlappingCode.ResourceOscillation"/> — a topology resource (site, edge,
///         trunk, station) accumulated high-frequency changes indicating hardware or sync
///         instability.</item>
/// </list>
///
/// Detection operates entirely on already-collected <see cref="AuditLogFinding"/> records —
/// no additional API calls are made.
/// </summary>
public sealed class FlappingDetectionAnalyzer
{
    /// <summary>
    /// Scans the provided audit log findings for flapping / oscillation patterns.
    /// </summary>
    /// <param name="auditLogFindings">Audit log events collected during the run.</param>
    /// <param name="windowMinutes">
    /// Only events within this many minutes of <paramref name="referenceTime"/> are considered.
    /// Pass <see cref="int.MaxValue"/> to consider all events regardless of age.
    /// Default: 1440 (24 hours).
    /// </param>
    /// <param name="minChangesForFlap">
    /// Minimum number of change events an object must have within the window before it is
    /// considered a flapping candidate. Default: 4.
    /// </param>
    /// <param name="referenceTime">
    /// The anchor time for the lookback window. Defaults to <see cref="DateTimeOffset.UtcNow"/>.
    /// </param>
    public IReadOnlyList<FlappingFinding> Analyze(
        IReadOnlyList<AuditLogFinding> auditLogFindings,
        int windowMinutes = 1440,
        int minChangesForFlap = 4,
        DateTimeOffset? referenceTime = null)
    {
        if (auditLogFindings.Count == 0)
            return [];

        var cutoff = (referenceTime ?? DateTimeOffset.UtcNow).AddMinutes(-Math.Max(1, windowMinutes));

        var recentEntries = auditLogFindings
            .Where(e => e.TimestampUtc.HasValue && e.TimestampUtc.Value >= cutoff)
            .ToList();

        if (recentEntries.Count == 0)
            return [];

        // Group by entity key: prefer entity ID, fall back to entity name
        var byEntity = recentEntries
            .GroupBy(e => NormalizeKey(e.EntityId) ?? NormalizeKey(e.EntityName) ?? string.Empty)
            .Where(g => !string.IsNullOrEmpty(g.Key))
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var results = new List<FlappingFinding>();

        foreach (var (_, changes) in byEntity)
        {
            if (changes.Count < Math.Max(2, minChangesForFlap))
                continue;

            var entityType = changes.Select(c => c.EntityType).FirstOrDefault(t => t is not null);
            var entityName = changes.Select(c => c.EntityName).FirstOrDefault(n => n is not null);
            var entityId   = changes.Select(c => c.EntityId).FirstOrDefault(id => id is not null);
            var entityKey  = entityId ?? entityName ?? string.Empty;

            var firstChange = changes
                .OrderBy(c => c.TimestampUtc ?? DateTimeOffset.MaxValue)
                .First();
            var lastChange = changes
                .OrderByDescending(c => c.TimestampUtc ?? DateTimeOffset.MinValue)
                .First();

            var distinctActions = changes
                .Select(c => c.Action?.Trim().ToUpperInvariant() ?? string.Empty)
                .Where(a => !string.IsNullOrEmpty(a))
                .Distinct()
                .OrderBy(a => a)
                .ToList();

            var isFlowEntity = entityType is not null
                && entityType.Contains("flow", StringComparison.OrdinalIgnoreCase);

            var isTopologyEntity = entityType is not null
                && (entityType.Contains("edge", StringComparison.OrdinalIgnoreCase)
                    || entityType.Contains("trunk", StringComparison.OrdinalIgnoreCase)
                    || entityType.Contains("site", StringComparison.OrdinalIgnoreCase)
                    || entityType.Contains("station", StringComparison.OrdinalIgnoreCase));

            var hasTogglePattern = distinctActions.Count >= 2;
            var actionsDisplay = string.Join(", ", distinctActions);

            string findingCode;
            FindingSeverity severity;
            string issue;
            string recommendedAction;

            if (isFlowEntity)
            {
                findingCode = FlappingCode.PublishChurn;
                severity = FindingSeverity.High;
                issue = $"Flow '{entityName ?? entityKey}' accumulated {changes.Count} change event(s) " +
                        $"within the audit window with action(s): {actionsDisplay}. " +
                        "Repeated publishing or editing without stabilization may indicate conflicting automation " +
                        "or concurrent admin activity.";
                recommendedAction =
                    "Review the flow's publish history and any associated automation rules. " +
                    "If the changes are unintentional, identify and disable the triggering process. " +
                    "Consider locking the flow to prevent concurrent edits during investigation.";
            }
            else if (isTopologyEntity && hasTogglePattern)
            {
                findingCode = FlappingCode.ResourceOscillation;
                severity = FindingSeverity.High;
                issue = $"Topology resource '{entityName ?? entityKey}' ({entityType}) toggled between " +
                        $"{changes.Count} change event(s) with action(s): {actionsDisplay} " +
                        "within the audit window. Oscillating topology state may indicate hardware instability, " +
                        "a connectivity loop, or a platform sync issue.";
                recommendedAction =
                    "Investigate the physical or virtual connectivity of this resource. " +
                    "If oscillation persists across multiple audit runs, escalate to Genesys Care " +
                    "with the object ID, entity type, and the full event timeline.";
            }
            else if (hasTogglePattern)
            {
                findingCode = FlappingCode.AssignmentFlapping;
                severity = FindingSeverity.Medium;
                issue = $"Object '{entityName ?? entityKey}' ({entityType ?? "unknown type"}) changed between " +
                        $"{distinctActions.Count} distinct action type(s) ({actionsDisplay}) across " +
                        $"{changes.Count} events within the audit window. " +
                        "This back-and-forth pattern suggests automation conflict, admin collision, " +
                        "or an unresolved ownership dispute.";
                recommendedAction =
                    "Review the full change history for this object. Determine whether conflicting automation " +
                    "jobs, competing admin sessions, or runbook triggers are producing the toggle. " +
                    "Stabilize the assignment state and monitor for recurrence.";
            }
            else
            {
                // Single action type repeated at high frequency — still a meaningful instability signal
                findingCode = FlappingCode.ResourceOscillation;
                severity = FindingSeverity.Medium;
                issue = $"Object '{entityName ?? entityKey}' ({entityType ?? "unknown type"}) was modified " +
                        $"{changes.Count} times using the same action '{distinctActions.FirstOrDefault()}' " +
                        "within the audit window. Excessive repeated modification suggests instability, " +
                        "automation loops, or repeated error-correction cycles.";
                recommendedAction =
                    "Investigate why this object is being modified so frequently. Check for automation loops, " +
                    "misconfigured sync rules, or repeated failed corrections. " +
                    "Confirm the current state is intentional before taking further action.";
            }

            results.Add(new FlappingFinding(
                FindingCode: findingCode,
                AffectedObjectType: entityType,
                AffectedObjectId: entityId,
                AffectedObjectName: entityName,
                FirstChangeUtc: firstChange.TimestampUtc,
                LastChangeUtc: lastChange.TimestampUtc,
                ChangeCount: changes.Count,
                DistinctActionCount: distinctActions.Count,
                ObservedActions: distinctActions,
                Issue: issue,
                Severity: severity,
                RecommendedAction: recommendedAction));
        }

        return results
            .OrderBy(f => f.Severity)
            .ThenByDescending(f => f.ChangeCount)
            .ThenBy(f => f.AffectedObjectName ?? f.AffectedObjectId ?? string.Empty,
                    StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static string? NormalizeKey(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
