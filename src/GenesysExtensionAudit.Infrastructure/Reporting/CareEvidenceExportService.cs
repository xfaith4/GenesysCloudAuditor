using GenesysExtensionAudit.Application;
using Microsoft.Extensions.Logging;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

public interface ICareEvidenceExportService
{
    /// <summary>
    /// Derives a <see cref="CareEvidencePacket"/> from the audit report.
    /// The packet covers all Critical findings and any High findings that meet
    /// escalation criteria (currently: category is EscalateToGenesysCare).
    /// </summary>
    CareEvidencePacket BuildPacket(AuditReportData report);
}

/// <summary>
/// Builds a structured <see cref="CareEvidencePacket"/> from an <see cref="AuditReportData"/>.
///
/// Escalation criteria (Phase 3.2 roadmap):
///   – Finding severity is Critical <em>or</em> (High with Category == EscalateToGenesysCare)
///   – At least one affected object ID is known (anonymous / unresolvable objects are excluded)
///
/// This service does not make any API calls — it works entirely from already-collected data.
/// </summary>
public sealed class CareEvidenceExportService : ICareEvidenceExportService
{
    private readonly ILogger<CareEvidenceExportService> _logger;
    private const string SupportReadinessReady = "Ready";
    private const string SupportReadinessNeedsReview = "NeedsReview";
    private const string SupportReadinessMonitor = "Monitor";

    public CareEvidenceExportService(ILogger<CareEvidenceExportService> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public CareEvidencePacket BuildPacket(AuditReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var candidates = new List<CareEscalationCandidate>();

        // ─── IVR flow binding findings (Phase 1.4) ────────────────────────────
        foreach (var f in report.IvrFlowBindingFindings)
        {
            if (!IsEscalationCandidate(f.Severity, f.Category)) continue;

            var relatedIds = f.BoundFlowId is not null ? [f.BoundFlowId] : (IReadOnlyList<string>)[];
            var relatedNames = f.BoundFlowName is not null ? [f.BoundFlowName] : (IReadOnlyList<string>)[];

            candidates.Add(QualifyCandidate(
                report,
                new CareEscalationCandidate
                {
                    Domain = "IVR / Flow Dependency",
                    FindingCode = f.FindingCode,
                    Severity = f.Severity.ToString(),
                    Category = f.Category.ToString(),
                    AffectedObjectId = f.IvrId,
                    AffectedObjectName = f.IvrName,
                    AffectedObjectType = "IVR",
                    RelatedObjectIds = relatedIds,
                    RelatedObjectNames = relatedNames,
                    ApiSurfaces = ["GET /api/v2/architect/ivrs", "GET /api/v2/flows"],
                    EvidenceSummary = f.Issue,
                    SuggestedCaseText = BuildIvrCaseText(f),
                    RecommendedAction = f.RecommendedAction,
                    WorkbookSheet = "IVR_Flow_Bindings"
                },
                f.Severity,
                f.Category,
                FindingConfidence.High,
                suspectedOwner: "Architect / Routing Administration",
                probableCauseCategory: BuildIvrProbableCause(f),
                blastRadius: BuildIvrBlastRadius(f)));
        }

        // ─── User telephony integrity findings (Phase 1.2) ────────────────────
        foreach (var f in report.UserTelephonyIntegrityFindings)
        {
            if (!IsEscalationCandidate(f.Severity, f.Category)) continue;

            candidates.Add(QualifyCandidate(
                report,
                new CareEscalationCandidate
                {
                    Domain = "User Telephony Integrity",
                    FindingCode = f.FindingCode,
                    Severity = f.Severity.ToString(),
                    Category = f.Category.ToString(),
                    AffectedObjectId = f.UserId,
                    AffectedObjectName = f.UserName ?? f.Email,
                    AffectedObjectType = "User",
                    RelatedObjectIds = [],
                    RelatedObjectNames = [],
                    ApiSurfaces = ["GET /api/v2/users", "GET /api/v2/telephony/providers/edges/extensions", "GET /api/v2/telephony/providers/edges/dids"],
                    EvidenceSummary = f.Issue,
                    SuggestedCaseText = BuildUserTelephonyCaseText(f),
                    RecommendedAction = f.RecommendedAction,
                    WorkbookSheet = "User_Telephony_Integrity"
                },
                f.Severity,
                f.Category,
                FindingConfidence.High,
                suspectedOwner: "Telephony Provisioning",
                probableCauseCategory: "Provisioning drift or telephony sync inconsistency",
                blastRadius: "Single-user telephony impact"));
        }

        // ─── Queue serviceability findings (Phase 1.3) ────────────────────────
        foreach (var f in report.QueueServiceabilityFindings)
        {
            if (!IsEscalationCandidate(f.Severity, f.Category)) continue;

            candidates.Add(QualifyCandidate(
                report,
                new CareEscalationCandidate
                {
                    Domain = "Queue Serviceability",
                    FindingCode = f.FindingCode,
                    Severity = f.Severity.ToString(),
                    Category = f.Category.ToString(),
                    AffectedObjectId = f.QueueId,
                    AffectedObjectName = f.QueueName,
                    AffectedObjectType = "Queue",
                    RelatedObjectIds = [],
                    RelatedObjectNames = [],
                    ApiSurfaces = ["GET /api/v2/routing/queues", "GET /api/v2/routing/queues/{id}/members", "GET /api/v2/users"],
                    EvidenceSummary = f.Issue,
                    SuggestedCaseText = BuildQueueCaseText(f),
                    RecommendedAction = f.RecommendedAction,
                    WorkbookSheet = "Queue_Serviceability"
                },
                f.Severity,
                f.Category,
                FindingConfidence.Medium,
                suspectedOwner: "Routing / Workforce Administration",
                probableCauseCategory: "Membership drift or stale workforce configuration",
                blastRadius: "Queue-wide service impact"));
        }

        // ─── Site topology findings (Phase 1.5) ──────────────────────────────
        foreach (var f in report.SiteTopologyFindings)
        {
            if (!IsEscalationCandidate(f.Severity, f.Category)) continue;

            var relatedIds = new List<string>();
            var relatedNames = new List<string>();

            if (!string.IsNullOrWhiteSpace(f.SiteId) &&
                !string.Equals(f.SiteId, f.ObjectId, StringComparison.OrdinalIgnoreCase))
            {
                relatedIds.Add(f.SiteId);
                if (!string.IsNullOrWhiteSpace(f.SiteName))
                    relatedNames.Add(f.SiteName);
            }

            if (!string.IsNullOrWhiteSpace(f.EdgeId) &&
                !string.Equals(f.EdgeId, f.ObjectId, StringComparison.OrdinalIgnoreCase))
            {
                relatedIds.Add(f.EdgeId);
                if (!string.IsNullOrWhiteSpace(f.EdgeName))
                    relatedNames.Add(f.EdgeName);
            }

            candidates.Add(QualifyCandidate(
                report,
                new CareEscalationCandidate
                {
                    Domain = "Site Topology",
                    FindingCode = f.FindingCode,
                    Severity = f.Severity.ToString(),
                    Category = f.Category.ToString(),
                    AffectedObjectId = f.ObjectId,
                    AffectedObjectName = f.ObjectName,
                    AffectedObjectType = f.ObjectType,
                    RelatedObjectIds = relatedIds,
                    RelatedObjectNames = relatedNames,
                    ApiSurfaces =
                    [
                        "GET /api/v2/telephony/providers/edges/sites",
                        "GET /api/v2/telephony/providers/edges",
                        "GET /api/v2/telephony/providers/edges/trunks"
                    ],
                    EvidenceSummary = f.Issue,
                    SuggestedCaseText = BuildSiteTopologyCaseText(f),
                    RecommendedAction = f.RecommendedAction,
                    WorkbookSheet = "Site_Topology"
                },
                f.Severity,
                f.Category,
                FindingConfidence.High,
                suspectedOwner: "Telephony Engineering",
                probableCauseCategory: BuildSiteTopologyProbableCause(f),
                blastRadius: BuildSiteTopologyBlastRadius(f)));
        }

        // ─── Build summary counts across all findings ─────────────────────────
        int totalFindings = CountAllFindings(report);
        int criticalCount = CountBySeverity(report, FindingSeverity.Critical);
        int highCount = CountBySeverity(report, FindingSeverity.High);
        int mediumCount = CountBySeverity(report, FindingSeverity.Medium);
        int infoCount = CountBySeverity(report, FindingSeverity.Info);

        var runDuration = (report.RunCompletedAtUtc - report.RunStartedAtUtc).TotalSeconds;

        var readyCount = candidates.Count(c => c.SupportReadiness == SupportReadinessReady);
        var needsReviewCount = candidates.Count(c => c.SupportReadiness == SupportReadinessNeedsReview);
        var monitorCount = candidates.Count(c => c.SupportReadiness == SupportReadinessMonitor);

        _logger.LogInformation(
            "Care evidence packet built. EscalationCandidates={Count} Ready={Ready} NeedsReview={NeedsReview} Monitor={Monitor} Critical={Critical} High={High}",
            candidates.Count, readyCount, needsReviewCount, monitorCount, criticalCount, highCount);

        return new CareEvidencePacket
        {
            GeneratedUtc = report.GeneratedAt,
            OrgRegion = report.OrgRegion,
            AuditDurationSeconds = runDuration,
            Summary = new CareEvidenceSummary
            {
                TotalFindingsInRun = totalFindings,
                CriticalCount = criticalCount,
                HighCount = highCount,
                MediumCount = mediumCount,
                InformationalCount = infoCount,
                EscalationCandidateCount = candidates.Count,
                ReadyForCareCount = readyCount,
                NeedsReviewCount = needsReviewCount,
                MonitorCount = monitorCount
            },
            EscalationCandidates = candidates
                .OrderByDescending(c => c.SupportReadinessScore)
                .ThenBy(c => c.Domain, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    // ─── Escalation qualifier ─────────────────────────────────────────────────

    private static bool IsEscalationCandidate(FindingSeverity severity, FindingCategory category)
        => severity == FindingSeverity.Critical
           || (severity == FindingSeverity.High && category == FindingCategory.EscalateToGenesysCare);

    private static CareEscalationCandidate QualifyCandidate(
        AuditReportData report,
        CareEscalationCandidate seed,
        FindingSeverity severity,
        FindingCategory category,
        FindingConfidence confidence,
        string suspectedOwner,
        string probableCauseCategory,
        string blastRadius)
    {
        var qualificationNotes = new List<string>();

        if (!string.IsNullOrWhiteSpace(seed.AffectedObjectId))
            qualificationNotes.Add("Primary object ID is present for support case correlation.");

        if (seed.ApiSurfaces.Count >= 2)
            qualificationNotes.Add($"Finding is corroborated across {seed.ApiSurfaces.Count} API surfaces.");

        if (seed.RelatedObjectIds.Count > 0)
            qualificationNotes.Add("Related object IDs are available for evidence chaining.");

        var recentChangeContext = BuildRecentChangeContext(report, seed);
        if (recentChangeContext is null)
            qualificationNotes.Add("No recent correlated admin change was found in the audit-log window.");
        else
            qualificationNotes.Add("Recent admin change activity remains a plausible local cause.");

        if (MatchesHotSpot(report, seed))
            qualificationNotes.Add("Object also appears in the hot-spot ranking, increasing blast-radius confidence.");

        if (MatchesFlapping(report, seed))
            qualificationNotes.Add("Audit logs show repeated churn for the same object, suggesting persistence or instability.");

        qualificationNotes.Add(category switch
        {
            FindingCategory.EscalateToGenesysCare => "Finding category already recommends escalation to Genesys Care.",
            FindingCategory.ChangeReviewRequired => "Change review is still recommended before escalation.",
            FindingCategory.LocalConfigFix => "Finding currently leans toward tenant-side remediation.",
            _ => "Additional observation may still be required before escalation."
        });

        var score = CalculateSupportReadinessScore(
            seed,
            severity,
            category,
            confidence,
            blastRadius,
            recentChangeContext is not null,
            MatchesHotSpot(report, seed),
            MatchesFlapping(report, seed));

        var supportReadiness = score >= 70
            ? SupportReadinessReady
            : score >= 45
                ? SupportReadinessNeedsReview
                : SupportReadinessMonitor;

        return new CareEscalationCandidate
        {
            CandidateId = seed.CandidateId,
            Domain = seed.Domain,
            FindingCode = seed.FindingCode,
            Severity = seed.Severity,
            Category = seed.Category,
            Confidence = confidence.ToString(),
            SuspectedOwner = suspectedOwner,
            ProbableCauseCategory = probableCauseCategory,
            BlastRadius = blastRadius,
            SupportReadiness = supportReadiness,
            SupportReadinessScore = score,
            AffectedObjectId = seed.AffectedObjectId,
            AffectedObjectName = seed.AffectedObjectName,
            AffectedObjectType = seed.AffectedObjectType,
            RelatedObjectIds = seed.RelatedObjectIds,
            RelatedObjectNames = seed.RelatedObjectNames,
            ApiSurfaces = seed.ApiSurfaces,
            RecentChangeContext = recentChangeContext,
            QualificationNotes = qualificationNotes,
            EvidenceSummary = seed.EvidenceSummary,
            SuggestedCaseText = seed.SuggestedCaseText,
            RecommendedAction = seed.RecommendedAction,
            WorkbookSheet = seed.WorkbookSheet
        };
    }

    private static int CalculateSupportReadinessScore(
        CareEscalationCandidate candidate,
        FindingSeverity severity,
        FindingCategory category,
        FindingConfidence confidence,
        string blastRadius,
        bool hasRecentChange,
        bool isHotSpot,
        bool isFlapping)
    {
        var score = 0;

        score += severity switch
        {
            FindingSeverity.Critical => 25,
            FindingSeverity.High => 18,
            FindingSeverity.Medium => 10,
            _ => 4
        };

        score += category switch
        {
            FindingCategory.EscalateToGenesysCare => 25,
            FindingCategory.ChangeReviewRequired => 5,
            FindingCategory.LocalConfigFix => -10,
            FindingCategory.MonitorRerun => -15,
            _ => 0
        };

        score += confidence switch
        {
            FindingConfidence.High => 15,
            FindingConfidence.Medium => 8,
            _ => 2
        };

        if (!string.IsNullOrWhiteSpace(candidate.AffectedObjectId))
            score += 10;

        if (candidate.RelatedObjectIds.Count > 0)
            score += 5;

        if (candidate.ApiSurfaces.Count >= 2)
            score += 5;

        if (!blastRadius.Contains("single-user", StringComparison.OrdinalIgnoreCase))
            score += 5;

        score += hasRecentChange ? -15 : 10;

        if (isHotSpot)
            score += 7;

        if (isFlapping)
            score += 5;

        return Math.Clamp(score, 0, 100);
    }

    private static string? BuildRecentChangeContext(AuditReportData report, CareEscalationCandidate candidate)
    {
        var matches = FindChangeAdjacencyMatches(report, candidate);
        if (matches.Count == 0)
            return null;

        var latest = matches
            .OrderByDescending(m => m.ChangeTimestamp ?? DateTimeOffset.MinValue)
            .First();

        var timestamp = latest.ChangeTimestamp?.ToString("yyyy-MM-dd HH:mm:ss 'UTC'") ?? "unknown time";
        var changedBy = latest.ChangedBy ?? "unknown actor";
        var action = latest.ChangeAction ?? "UPDATE";
        var totalChanges = matches.Sum(m => Math.Max(m.ChangeCount, 1));

        return $"Recent change context: {action} by {changedBy} at {timestamp}; {totalChanges} correlated change event(s) in the active audit-log window.";
    }

    private static IReadOnlyList<ChangeAdjacencyFinding> FindChangeAdjacencyMatches(
        AuditReportData report,
        CareEscalationCandidate candidate)
    {
        var objectIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var objectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(candidate.AffectedObjectId))
            objectIds.Add(candidate.AffectedObjectId);

        if (!string.IsNullOrWhiteSpace(candidate.AffectedObjectName))
            objectNames.Add(candidate.AffectedObjectName);

        foreach (var relatedId in candidate.RelatedObjectIds.Where(id => !string.IsNullOrWhiteSpace(id)))
            objectIds.Add(relatedId);

        foreach (var relatedName in candidate.RelatedObjectNames.Where(name => !string.IsNullOrWhiteSpace(name)))
            objectNames.Add(relatedName);

        return report.ChangeAdjacencyFindings
            .Where(f =>
                (!string.IsNullOrWhiteSpace(f.AffectedObjectId) && objectIds.Contains(f.AffectedObjectId)) ||
                (!string.IsNullOrWhiteSpace(f.AffectedObjectName) && objectNames.Contains(f.AffectedObjectName)))
            .ToList();
    }

    private static bool MatchesHotSpot(AuditReportData report, CareEscalationCandidate candidate)
        => report.HotSpotFindings.Any(h => CandidateMatches(candidate, h.ObjectId, h.ObjectName));

    private static bool MatchesFlapping(AuditReportData report, CareEscalationCandidate candidate)
        => report.FlappingDetectionFindings.Any(f => CandidateMatches(candidate, f.AffectedObjectId, f.AffectedObjectName));

    private static bool CandidateMatches(CareEscalationCandidate candidate, string? objectId, string? objectName)
    {
        if (!string.IsNullOrWhiteSpace(objectId))
        {
            if (string.Equals(candidate.AffectedObjectId, objectId, StringComparison.OrdinalIgnoreCase))
                return true;

            if (candidate.RelatedObjectIds.Any(id => string.Equals(id, objectId, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        if (!string.IsNullOrWhiteSpace(objectName))
        {
            if (string.Equals(candidate.AffectedObjectName, objectName, StringComparison.OrdinalIgnoreCase))
                return true;

            if (candidate.RelatedObjectNames.Any(name => string.Equals(name, objectName, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    // ─── Case text builders ───────────────────────────────────────────────────

    private static string BuildIvrCaseText(IvrFlowBindingFinding f)
    {
        var dnisStr = f.Dnis.Count > 0
            ? $"The following phone numbers are affected: {string.Join(", ", f.Dnis)}. "
            : "";

        return f.FindingCode switch
        {
            IvrBindingCode.FlowNotFound =>
                $"IVR entry point '{f.IvrName ?? f.IvrId}' ({f.IvrId}) has its {f.BindingSlot} slot bound to flow " +
                $"ID '{f.BoundFlowId}' ('{f.BoundFlowName}') which does not appear in the Architect flow list. " +
                $"{dnisStr}" +
                "This indicates the flow may have been deleted or its ID changed without updating the IVR binding. " +
                "Calls reaching this entry point during the affected hours will fail.",

            IvrBindingCode.FlowIsDraft =>
                $"IVR entry point '{f.IvrName ?? f.IvrId}' ({f.IvrId}) has its {f.BindingSlot} slot bound to flow " +
                $"'{f.BoundFlowName ?? f.BoundFlowId}' which has never been published (remains in draft). " +
                $"{dnisStr}" +
                "Callers reaching this entry point during affected hours will encounter an error because draft flows are not executable.",

            IvrBindingCode.FlowIsStale =>
                $"IVR entry point '{f.IvrName ?? f.IvrId}' ({f.IvrId}) has its {f.BindingSlot} slot bound to flow " +
                $"'{f.BoundFlowName ?? f.BoundFlowId}' which has not been republished in {f.FlowDaysSincePublished} days. " +
                $"{dnisStr}" +
                "The flow may not reflect the current intended routing configuration.",

            IvrBindingCode.NoOpenHoursFlow =>
                $"IVR entry point '{f.IvrName ?? f.IvrId}' ({f.IvrId}) has {f.Dnis.Count} DNIS numbers " +
                $"({string.Join(", ", f.Dnis)}) but no open-hours flow bound. " +
                "All inbound calls through these numbers during open hours have no route.",

            IvrBindingCode.NoScheduleGroup =>
                $"IVR entry point '{f.IvrName ?? f.IvrId}' ({f.IvrId}) has {f.Dnis.Count} DNIS numbers " +
                $"({string.Join(", ", f.Dnis)}) and flow bindings but no schedule group assigned. " +
                "Without a schedule group the IVR cannot determine which hours flow to invoke.",

            _ => f.Issue
        };
    }

    private static string BuildUserTelephonyCaseText(UserTelephonyIntegrityFinding f)
        => $"User '{f.UserName ?? f.Email ?? f.UserId}' ({f.UserId}) has a telephony integrity contradiction: {f.Issue} " +
           $"Profile extension: '{f.ProfileExtensionRaw ?? "none"}'. " +
           $"Station ID: '{f.StationId ?? "none"}'. " +
           $"Related DID: '{f.RelatedDidNumber ?? "none"}'. " +
           "This may indicate incomplete provisioning, a failed sync, or a platform-side assignment inconsistency.";

    private static string BuildQueueCaseText(QueueServiceabilityFinding f)
        => $"Queue '{f.QueueName ?? f.QueueId}' ({f.QueueId}) has {f.TotalMembersOnRecord} member(s) on record but " +
           $"zero serviceable agents in the checked sample ({f.MembersChecked} checked; " +
           $"{f.ActiveMemberCount} active, {f.InactiveMemberCount} inactive, {f.UnresolvableMemberCount} unresolvable). " +
           "Work routed to this queue will not be answered.";

    private static string BuildSiteTopologyCaseText(SiteTopologyFinding f)
        => f.FindingCode switch
        {
            SiteTopologyCode.SiteNoActiveEdges =>
                $"Site '{f.SiteName ?? f.ObjectName ?? f.SiteId ?? f.ObjectId}' ({f.SiteId ?? f.ObjectId}) has no active edges available to carry traffic. " +
                "Inbound and outbound telephony through this site is expected to fail.",

            SiteTopologyCode.EdgeOrphanedSite =>
                $"Edge '{f.ObjectName ?? f.EdgeName ?? f.ObjectId}' ({f.ObjectId}) references site '{f.SiteName ?? f.SiteId}' " +
                $"({f.SiteId}) which was not returned by the site inventory API. This indicates an orphaned topology relationship.",

            SiteTopologyCode.EdgeOffline =>
                $"Edge '{f.ObjectName ?? f.EdgeName ?? f.ObjectId}' ({f.ObjectId}) under site '{f.SiteName ?? f.SiteId}' is reporting offline. " +
                "Telephony resources hosted on this edge are degraded or unavailable.",

            SiteTopologyCode.TrunkEdgeOffline =>
                $"Trunk '{f.ObjectName ?? f.ObjectId}' ({f.ObjectId}) depends on edge '{f.EdgeName ?? f.EdgeId}' " +
                $"({f.EdgeId}) which is offline. Even if the trunk configuration is otherwise valid, traffic cannot flow while the host edge is down.",

            SiteTopologyCode.TrunkOutOfService =>
                $"Trunk '{f.ObjectName ?? f.ObjectId}' ({f.ObjectId}) at site '{f.SiteName ?? f.SiteId}' is administratively disabled or not in service. " +
                "Traffic for this route is unavailable until the trunk is returned to service.",

            SiteTopologyCode.TrunkDown =>
                $"Trunk '{f.ObjectName ?? f.ObjectId}' ({f.ObjectId}) at site '{f.SiteName ?? f.SiteId}' is reporting '{f.TrunkState ?? "DOWN"}'. " +
                "Carrier or platform connectivity is impaired for this telephony path.",

            _ => f.Issue
        };

    private static string BuildIvrProbableCause(IvrFlowBindingFinding f)
        => f.FindingCode switch
        {
            IvrBindingCode.FlowNotFound => "Broken IVR to flow reference",
            IvrBindingCode.FlowIsDraft => "Unpublished flow bound to live entry point",
            IvrBindingCode.NoOpenHoursFlow => "Missing entry-point routing configuration",
            IvrBindingCode.NoScheduleGroup => "Incomplete time-based routing configuration",
            _ => "Flow publish or routing drift"
        };

    private static string BuildIvrBlastRadius(IvrFlowBindingFinding f)
        => f.Dnis.Count switch
        {
            > 1 => $"{f.Dnis.Count} inbound numbers routed through the affected IVR",
            1 => $"Single inbound number ({f.Dnis[0]}) routed through the affected IVR",
            _ => "Single IVR entry point impacted"
        };

    private static string BuildSiteTopologyProbableCause(SiteTopologyFinding f)
        => f.FindingCode switch
        {
            SiteTopologyCode.EdgeOrphanedSite => "Topology inventory contradiction",
            SiteTopologyCode.EdgeOffline => "Edge or infrastructure outage",
            SiteTopologyCode.SiteNoActiveEdges => "Site-wide telephony path unavailable",
            SiteTopologyCode.TrunkEdgeOffline => "Host edge outage affecting trunk path",
            SiteTopologyCode.TrunkOutOfService => "Administrative trunk shutdown or service disablement",
            SiteTopologyCode.TrunkDown => "Carrier or platform connectivity failure",
            _ => "Telephony topology inconsistency"
        };

    private static string BuildSiteTopologyBlastRadius(SiteTopologyFinding f)
        => f.FindingCode switch
        {
            SiteTopologyCode.SiteNoActiveEdges => $"Site-wide telephony outage for site '{f.SiteName ?? f.SiteId ?? f.ObjectName ?? f.ObjectId}'",
            SiteTopologyCode.EdgeOffline => $"Edge-hosted telephony degraded for site '{f.SiteName ?? f.SiteId ?? "unknown"}'",
            SiteTopologyCode.EdgeOrphanedSite => "Topology orphaning can strand edge-scoped telephony resources",
            SiteTopologyCode.TrunkEdgeOffline => $"Trunk path unavailable through edge '{f.EdgeName ?? f.EdgeId ?? "unknown"}'",
            SiteTopologyCode.TrunkOutOfService => $"Trunk-level service interruption at site '{f.SiteName ?? f.SiteId ?? "unknown"}'",
            SiteTopologyCode.TrunkDown => $"Carrier-facing trunk failure at site '{f.SiteName ?? f.SiteId ?? "unknown"}'",
            _ => "Telephony topology impact"
        };

    // ─── Count helpers ────────────────────────────────────────────────────────

    private static int CountAllFindings(AuditReportData r)
        => r.IvrFlowBindingFindings.Count
         + r.UserTelephonyIntegrityFindings.Count
         + r.QueueServiceabilityFindings.Count
         + r.SiteTopologyFindings.Count
         + r.PromptHygieneFindings.Count
         + r.ChangeAdjacencyFindings.Count
         + r.FlappingDetectionFindings.Count
         + r.HotSpotFindings.Count
         + r.ExtensionReport.DuplicateProfileExtensions.Count
         + r.ExtensionReport.DuplicateAssignedExtensions.Count
         + r.ExtensionReport.ProfileExtensionsNotAssigned.Count
         + r.ExtensionReport.AssignedExtensionsMissingFromProfiles.Count
         + r.GroupFindings.Count
         + r.QueueFindings.Count
         + r.FlowFindings.Count
         + r.InactiveUserFindings.Count
         + r.NoLocationUserFindings.Count
         + r.DidFindings.Count;

    private static int CountBySeverity(AuditReportData r, FindingSeverity sev)
        => r.IvrFlowBindingFindings.Count(f => f.Severity == sev)
         + r.UserTelephonyIntegrityFindings.Count(f => f.Severity == sev)
         + r.QueueServiceabilityFindings.Count(f => f.Severity == sev)
         + r.SiteTopologyFindings.Count(f => f.Severity == sev)
         + r.PromptHygieneFindings.Count(f => f.Severity == sev)
         + r.ChangeAdjacencyFindings.Count(f => f.Severity == sev)
         + r.FlappingDetectionFindings.Count(f => f.Severity == sev)
         + r.HotSpotFindings.Count(f => f.Severity == sev);
}
