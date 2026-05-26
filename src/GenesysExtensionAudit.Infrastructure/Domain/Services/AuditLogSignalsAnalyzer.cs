using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Interprets a bounded audit-log result set into compact operator-ready signals.
/// The initial implementation focuses on security/admin risk themes that are
/// high value but still defensible from event metadata alone.
/// </summary>
public sealed class AuditLogSignalsAnalyzer
{
    public IReadOnlyList<AuditLogSignalFinding> Analyze(IReadOnlyList<AuditLogFinding> auditLogFindings)
    {
        if (auditLogFindings.Count == 0)
            return [];

        var matched = auditLogFindings
            .Select(log => (Log: log, Match: TryInterpret(log)))
            .Where(x => x.Match is not null)
            .Select(x => new SignalEvent(x.Log, x.Match!))
            .ToList();

        if (matched.Count == 0)
            return [];

        return matched
            .GroupBy(
                x => BuildGroupingKey(x.Log, x.Match),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => TryBuildFinding(group.ToList()))
            .Where(f => f is not null)
            .Select(f => f!)
            .OrderBy(f => f.Severity)
            .ThenByDescending(f => f.EventCount)
            .ThenByDescending(f => f.LastEventUtc ?? DateTimeOffset.MinValue)
            .ThenBy(f => f.EntityName ?? f.EntityId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static AuditLogSignalFinding? TryBuildFinding(IReadOnlyList<SignalEvent> events)
    {
        var ordered = events
            .OrderBy(x => x.Log.TimestampUtc ?? DateTimeOffset.MinValue)
            .ToList();

        var first = ordered.First();
        var last = ordered.Last();
        var match = first.Match;
        var distinctActions = ordered
            .Select(x => Normalize(x.Log.Action))
            .Where(x => x is not null)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var actionSummary = distinctActions.Count == 0
            ? "unknown actions"
            : string.Join(", ", distinctActions);

        if (!ShouldEmit(match.Code, ordered.Count, distinctActions))
            return null;

        var actorDisplay = BuildActorDisplay(last.Log);
        var entityDisplay = BuildEntityDisplay(last.Log);
        var serviceDisplay = last.Log.ServiceName ?? "unknown service";
        var actionDisplay = last.Log.Action ?? "unknown action";

        var issue = match.Code switch
        {
            AuditLogSignalCode.AdminRoleGrantRevoke =>
                $"{events.Count} audit-log event(s) indicate privileged role grant or revoke activity affecting {entityDisplay} " +
                $"via service '{serviceDisplay}' and action '{actionDisplay}'. " +
                "Explicit role grants and revocations directly alter administrative reach across the tenant. " +
                "These changes should align with approved access management processes and be reviewed against the principle of least privilege.",

            AuditLogSignalCode.DivisionScopeChange =>
                $"{events.Count} audit-log event(s) indicate division or scope-related changes affecting {entityDisplay} " +
                $"via service '{serviceDisplay}' and action '{actionDisplay}'. Scope changes can materially alter visibility, " +
                "routing reach, or administrative blast radius and should be reviewed before treating downstream symptoms as platform-side.",

            AuditLogSignalCode.OAuthClientChange =>
                $"{events.Count} audit-log event(s) indicate OAuth client or credential changes affecting {entityDisplay} " +
                $"via service '{serviceDisplay}' and action '{actionDisplay}'. API access posture may have changed, which can alter automation " +
                "behavior or invalidate assumptions about trusted client activity.",

            AuditLogSignalCode.QueueMembershipChurn =>
                $"{events.Count} audit-log event(s) indicate repeated queue membership activity affecting {entityDisplay}. " +
                $"Observed action set: {actionSummary}. Repeated membership changes can destabilize routing behavior, skill coverage, or service ownership and should be reviewed as managed churn rather than isolated edits.",

            AuditLogSignalCode.FlowPublicationChange =>
                $"{events.Count} audit-log event(s) indicate flow publication activity affecting {entityDisplay}. " +
                $"Observed action set: {actionSummary}. Publish, restore, rollback, or unpublish activity can change live routing behavior quickly and should be reviewed against recent incidents or downstream findings.",

            AuditLogSignalCode.PlatformConfigChange =>
                $"{events.Count} audit-log event(s) indicate platform infrastructure configuration changes affecting {entityDisplay}. " +
                $"Observed action set: {actionSummary}. Changes to sites, edges, trunks, IVR configurations, or telephony resources " +
                "can cause routing disruptions if made without adequate testing or change management review.",

            _ =>
                $"{events.Count} audit-log event(s) indicate access-control related changes affecting {entityDisplay} " +
                $"via service '{serviceDisplay}' and action '{actionDisplay}'. Role or permission changes can broaden or reduce administrative " +
                "reach and should be reviewed before attributing adjacent findings to random drift."
        };

        var recommendedAction = match.Code switch
        {
            AuditLogSignalCode.AdminRoleGrantRevoke =>
                "Review the role grant or revocation, confirm the actor had authority to make this change, verify the role scope aligns with the principle of least privilege, and check whether downstream audit findings may be related to the access change.",

            AuditLogSignalCode.DivisionScopeChange =>
                "Review the division/scope change, confirm the actor and intended blast radius, and compare the timing to any routing, access, or ownership anomalies detected in this run.",

            AuditLogSignalCode.OAuthClientChange =>
                "Review the OAuth client change, confirm whether it was part of an approved automation rollout, and verify the client still has only the intended permissions and target scope.",

            AuditLogSignalCode.QueueMembershipChurn =>
                "Review the affected queue's membership timeline, confirm whether the churn was part of an intentional staffing or automation event, and verify that routing coverage remains coherent after the changes.",

            AuditLogSignalCode.FlowPublicationChange =>
                "Review the flow publication timeline, confirm whether the publish or rollback activity was intentional, and compare it to any IVR, queue, or customer-impact findings from the same window.",

            AuditLogSignalCode.PlatformConfigChange =>
                "Review the infrastructure change, confirm it was part of an approved change window or planned maintenance, and compare the timing against any telephony, routing, or site-topology findings detected in this run.",

            _ =>
                "Review the access-control change, confirm whether the role or permission update was approved, and verify that it aligns with the current operational and security model."
        };

        var severity = DetermineSeverity(match.Code, match.Severity, ordered.Count, distinctActions);

        return new AuditLogSignalFinding(
            FindingCode: match.Code,
            SignalCategory: match.SignalCategory,
            FirstEventUtc: first.Log.TimestampUtc,
            LastEventUtc: last.Log.TimestampUtc,
            EventCount: events.Count,
            ServiceName: last.Log.ServiceName,
            Action: last.Log.Action,
            UserName: last.Log.UserName,
            UserEmail: last.Log.UserEmail,
            UserId: last.Log.UserId,
            ClientId: last.Log.ClientId,
            EntityType: last.Log.EntityType,
            EntityName: last.Log.EntityName,
            EntityId: last.Log.EntityId,
            Issue: $"{issue} Most recent actor: {actorDisplay}.",
            Severity: severity,
            Category: FindingCategory.ChangeReviewRequired,
            RecommendedAction: recommendedAction);
    }

    private static string BuildGroupingKey(AuditLogFinding log, SignalMatch match)
    {
        var entityKey = Normalize(log.EntityId) ?? Normalize(log.EntityName) ?? "(unknown-entity)";
        var actorKey = Normalize(log.UserId) ?? Normalize(log.UserEmail) ?? Normalize(log.UserName) ?? Normalize(log.ClientId) ?? "(unknown-actor)";
        var actionKey = Normalize(log.Action) ?? "(unknown-action)";
        return match.Code switch
        {
            AuditLogSignalCode.QueueMembershipChurn => $"{match.Code}|{entityKey}",
            AuditLogSignalCode.FlowPublicationChange => $"{match.Code}|{entityKey}",
            AuditLogSignalCode.PlatformConfigChange => $"{match.Code}|{entityKey}|{actionKey}",
            _ => $"{match.Code}|{entityKey}|{actorKey}|{actionKey}"
        };
    }

    private static SignalMatch? TryInterpret(AuditLogFinding log)
    {
        var service = Normalize(log.ServiceName);
        var action = Normalize(log.Action);
        var entityType = Normalize(log.EntityType);
        var entityName = Normalize(log.EntityName);
        var corpus = string.Join(" ", new[] { service, entityType, entityName }.Where(x => !string.IsNullOrWhiteSpace(x)));
        var queueLike = ContainsAny(corpus, "queue");
        var flowLike = ContainsAny(corpus, "flow", "architect");

        if (ContainsAny(corpus, "division"))
        {
            return new SignalMatch(
                AuditLogSignalCode.DivisionScopeChange,
                "Security / Scope",
                FindingSeverity.High);
        }

        if (ContainsAny(corpus, "oauth", "oauthclient", "clientcredential", "credential")
            || (ContainsAny(service, "oauth") && ContainsAny(corpus, "client", "token")))
        {
            return new SignalMatch(
                AuditLogSignalCode.OAuthClientChange,
                "Security / OAuth",
                FindingSeverity.High);
        }

        // AdminRoleGrantRevoke: check before the generic access-control catch-all.
        // Fires when the action is explicitly a grant or revoke on a role entity.
        var isRoleType = ContainsAny(corpus, "role");
        var isGrantRevoke = ContainsAny(action, "grant", "revoke");
        if (isRoleType && isGrantRevoke)
        {
            return new SignalMatch(
                AuditLogSignalCode.AdminRoleGrantRevoke,
                "Security / Privileged Access",
                FindingSeverity.High);
        }

        var isAccessEntity = ContainsAny(corpus, "role", "permission", "grant", "authorization", "access", "security");
        var isAccessAction = ContainsAny(action, "assign", "unassign", "grant", "revoke", "create", "delete", "update", "remove");
        if (isAccessEntity && isAccessAction)
        {
            return new SignalMatch(
                AuditLogSignalCode.AccessControlChange,
                "Security / Access Control",
                FindingSeverity.High);
        }

        var isQueueMembershipEvent = queueLike &&
            (ContainsAny(corpus, "member", "membership", "agent")
             || ContainsAny(action, "assign", "unassign", "add", "remove"));
        if (isQueueMembershipEvent)
        {
            return new SignalMatch(
                AuditLogSignalCode.QueueMembershipChurn,
                "Routing / Queue Membership",
                FindingSeverity.Medium);
        }

        var isFlowPublicationEvent = flowLike &&
            ContainsAny(action, "publish", "rollback", "restore", "revert", "unpublish", "deploy");
        if (isFlowPublicationEvent)
        {
            return new SignalMatch(
                AuditLogSignalCode.FlowPublicationChange,
                "Routing / Flow Publication",
                FindingSeverity.Medium);
        }

        // PlatformConfigChange: core infrastructure objects excluding queues and flows
        // (those have dedicated higher-priority signal codes above).
        var isPlatformEntity = ContainsAny(corpus, "site", "edge", "trunk", "ivr", "telephony", "location", "schedulegroup", "schedule");
        var isConfigAction = ContainsAny(action, "create", "update", "delete", "patch");
        if (isPlatformEntity && isConfigAction && !queueLike && !flowLike)
        {
            return new SignalMatch(
                AuditLogSignalCode.PlatformConfigChange,
                "Infrastructure / Platform Config",
                FindingSeverity.Medium);
        }

        return null;
    }

    private static bool ShouldEmit(
        string code,
        int eventCount,
        IReadOnlyList<string?> distinctActions)
        => code switch
        {
            AuditLogSignalCode.QueueMembershipChurn =>
                eventCount >= 3 || (eventCount >= 2 && distinctActions.Count >= 2),

            AuditLogSignalCode.FlowPublicationChange =>
                eventCount >= 2 || distinctActions.Any(IsRollbackLikeAction),

            _ => true
        };

    private static FindingSeverity DetermineSeverity(
        string code,
        FindingSeverity fallback,
        int eventCount,
        IReadOnlyList<string?> distinctActions)
        => code switch
        {
            AuditLogSignalCode.QueueMembershipChurn when eventCount >= 5 || distinctActions.Count >= 3 => FindingSeverity.High,
            AuditLogSignalCode.QueueMembershipChurn => FindingSeverity.Medium,
            AuditLogSignalCode.FlowPublicationChange when distinctActions.Any(IsRollbackLikeAction) || eventCount >= 4 => FindingSeverity.High,
            AuditLogSignalCode.FlowPublicationChange => FindingSeverity.Medium,
            AuditLogSignalCode.PlatformConfigChange when distinctActions.Any(IsDeleteLikeAction) => FindingSeverity.High,
            AuditLogSignalCode.PlatformConfigChange => FindingSeverity.Medium,
            _ => fallback
        };

    private static bool IsRollbackLikeAction(string? action)
        => ContainsAny(action, "rollback", "restore", "revert", "unpublish");

    private static bool IsDeleteLikeAction(string? action)
        => ContainsAny(action, "delete", "remove", "destroy");

    private static string BuildActorDisplay(AuditLogFinding log)
        => log.UserName
           ?? log.UserEmail
           ?? log.UserId
           ?? (!string.IsNullOrWhiteSpace(log.ClientId) ? $"client {log.ClientId}" : "unknown actor");

    private static string BuildEntityDisplay(AuditLogFinding log)
    {
        var type = log.EntityType ?? "entity";
        var nameOrId = log.EntityName ?? log.EntityId ?? "unknown target";
        return $"{type} '{nameOrId}'";
    }

    private static bool ContainsAny(string? value, params string[] needles)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static string? Normalize(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private sealed record SignalEvent(AuditLogFinding Log, SignalMatch Match);
    private sealed record SignalMatch(string Code, string SignalCategory, FindingSeverity Severity);
}
