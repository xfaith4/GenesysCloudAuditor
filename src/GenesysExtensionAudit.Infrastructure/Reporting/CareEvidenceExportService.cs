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

            candidates.Add(new CareEscalationCandidate
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
            });
        }

        // ─── User telephony integrity findings (Phase 1.2) ────────────────────
        foreach (var f in report.UserTelephonyIntegrityFindings)
        {
            if (!IsEscalationCandidate(f.Severity, f.Category)) continue;

            candidates.Add(new CareEscalationCandidate
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
            });
        }

        // ─── Queue serviceability findings (Phase 1.3) ────────────────────────
        foreach (var f in report.QueueServiceabilityFindings)
        {
            if (!IsEscalationCandidate(f.Severity, f.Category)) continue;

            candidates.Add(new CareEscalationCandidate
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
            });
        }

        // ─── Build summary counts across all findings ─────────────────────────
        int totalFindings = CountAllFindings(report);
        int criticalCount = CountBySeverity(report, FindingSeverity.Critical);
        int highCount = CountBySeverity(report, FindingSeverity.High);
        int mediumCount = CountBySeverity(report, FindingSeverity.Medium);
        int infoCount = CountBySeverity(report, FindingSeverity.Info);

        var runDuration = (report.RunCompletedAtUtc - report.RunStartedAtUtc).TotalSeconds;

        _logger.LogInformation(
            "Care evidence packet built. EscalationCandidates={Count} Critical={Critical} High={High}",
            candidates.Count, criticalCount, highCount);

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
                EscalationCandidateCount = candidates.Count
            },
            EscalationCandidates = candidates
                .OrderByDescending(c => c.Severity)
                .ThenBy(c => c.Domain, StringComparer.OrdinalIgnoreCase)
                .ToList()
        };
    }

    // ─── Escalation qualifier ─────────────────────────────────────────────────

    private static bool IsEscalationCandidate(FindingSeverity severity, FindingCategory category)
        => severity == FindingSeverity.Critical
           || (severity == FindingSeverity.High && category == FindingCategory.EscalateToGenesysCare);

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

    // ─── Count helpers ────────────────────────────────────────────────────────

    private static int CountAllFindings(AuditReportData r)
        => r.IvrFlowBindingFindings.Count
         + r.UserTelephonyIntegrityFindings.Count
         + r.QueueServiceabilityFindings.Count
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
         + r.QueueServiceabilityFindings.Count(f => f.Severity == sev);
}
