using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Microsoft.Extensions.Logging;

namespace GenesysExtensionAudit.Infrastructure.BestPractices;

public interface IFindingBestPracticeEnricher
{
    IReadOnlyList<BestPracticeGuidanceFinding> Enrich(BestPracticeFindingContext context);
    BestPracticeEnrichmentResult Enrich(AuditReportData report);
}

public sealed class FindingBestPracticeEnricher : IFindingBestPracticeEnricher
{
    private static readonly IReadOnlyDictionary<string, string> FindingTypeAliases =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [TelephonyIntegrityCode.ExtensionWithoutStation] = "DidOrExtensionAssignmentInconsistent",
            [TelephonyIntegrityCode.StationWithoutExtension] = "DidOrExtensionAssignmentInconsistent",
            [TelephonyIntegrityCode.DidOwnerExtensionMismatch] = "DidOrExtensionAssignmentInconsistent",
            [TelephonyIntegrityCode.GhostTelephonyAssignment] = "DidOrExtensionAssignmentInconsistent",
            ["DidFinding"] = "DidOrExtensionAssignmentInconsistent",
            ["DuplicateProfileExtension"] = "DidOrExtensionAssignmentInconsistent",
            ["ProfileExtensionNotAssigned"] = "DidOrExtensionAssignmentInconsistent",
            ["FlowFinding"] = "FlowLifecycleDisciplineWeak",
            ["RoleGroupOverlapFinding"] = "RoleAssignmentOverprivileged"
        };

    private readonly IBestPracticeRepository _bestPracticeRepository;
    private readonly IGlossaryRepository _glossaryRepository;
    private readonly ILogger<FindingBestPracticeEnricher> _logger;

    public FindingBestPracticeEnricher(
        IBestPracticeRepository bestPracticeRepository,
        IGlossaryRepository glossaryRepository,
        ILogger<FindingBestPracticeEnricher> logger)
    {
        _bestPracticeRepository = bestPracticeRepository ?? throw new ArgumentNullException(nameof(bestPracticeRepository));
        _glossaryRepository = glossaryRepository ?? throw new ArgumentNullException(nameof(glossaryRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public IReadOnlyList<BestPracticeGuidanceFinding> Enrich(BestPracticeFindingContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var mappingFindingType = ResolveMappingFindingType(context.SourceFindingType);
        if (mappingFindingType is null)
            return [];

        var mappings = _bestPracticeRepository.GetMappingsForFindingType(mappingFindingType);
        if (mappings.Count == 0)
            return [];

        var matches = new List<BestPracticeGuidanceFinding>();
        foreach (var mapping in mappings)
        {
            var entries = mapping.BestPracticeKeys
                .Select(_bestPracticeRepository.GetByKey)
                .Where(entry => entry is not null)
                .Cast<BestPracticeEntry>()
                .ToList();

            if (entries.Count == 0)
                continue;

            var glossaryTerms = FindGlossaryTerms(entries, context);
            matches.Add(new BestPracticeGuidanceFinding(
                SourceDomain: context.SourceDomain,
                SourceFindingType: context.SourceFindingType,
                SourceObjectType: context.SourceObjectType,
                SourceObjectId: context.SourceObjectId,
                SourceObjectName: context.SourceObjectName,
                Issue: context.Issue,
                EffectiveSeverity: string.IsNullOrWhiteSpace(context.Severity) ? mapping.DefaultSeverity : context.Severity!,
                BestPracticeKeys: entries.Select(entry => entry.Key).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                BestPracticeTitles: entries.Select(entry => entry.Title).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                ControlFamily: string.Join(", ", entries.Select(entry => entry.ControlFamily).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)),
                Pillar: string.Join(", ", entries.Select(entry => entry.Pillar).Where(value => !string.IsNullOrWhiteSpace(value)).Distinct(StringComparer.OrdinalIgnoreCase)),
                RecommendedActionShort: FirstNonEmpty(context.RecommendedAction, mapping.RecommendedActionShort, entries.Select(entry => entry.RecommendedActionShort)),
                RecommendedActionDetailed: FirstNonEmpty(entries.Select(entry => entry.RecommendedActionDetailed)),
                WhyItMatters: FirstNonEmpty(entries.Select(entry => entry.WhyItMatters)),
                OwnerRole: FirstNonEmpty(entries.Select(entry => entry.OwnerRole)),
                OwnerTeam: FirstNonEmpty(entries.Select(entry => entry.OwnerTeam)),
                EvidenceExamples: entries.SelectMany(entry => entry.EvidenceExamples).Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                GlossaryTerms: glossaryTerms,
                MappingFindingType: mappingFindingType));
        }

        return matches;
    }

    public BestPracticeEnrichmentResult Enrich(AuditReportData report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var matches = new List<BestPracticeGuidanceFinding>();
        var unmatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendExtensionFindings(report, unmatched, matches);
        AppendBatch(report.GroupFindings, unmatched, matches, finding => new BestPracticeFindingContext("Group Hygiene", "GroupFinding", "Group", finding.GroupId, finding.GroupName, finding.Issue, "Medium", null));
        AppendBatch(report.QueueFindings, unmatched, matches, finding => new BestPracticeFindingContext("Queue Hygiene", "QueueFinding", "Queue", finding.QueueId, finding.QueueName, finding.Issue, "Medium", null));
        AppendBatch(report.InactiveUserFindings, unmatched, matches, finding => new BestPracticeFindingContext("Inactive User", "InactiveUserFinding", "User", finding.UserId, finding.UserName, finding.Issue, "Medium", null));
        AppendBatch(report.NoLocationUserFindings, unmatched, matches, finding => new BestPracticeFindingContext("User Location", "NoLocationUserFinding", "User", finding.UserId, finding.UserName, finding.Issue, "Low", null));
        foreach (var finding in report.UserTelephonyIntegrityFindings)
            Append(matches, unmatched, new BestPracticeFindingContext("User Telephony Integrity", finding.FindingCode, "User", finding.UserId, finding.UserName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));

        foreach (var finding in report.DidFindings)
            Append(matches, unmatched, new BestPracticeFindingContext("DID Mismatch", "DidFinding", "DID", finding.DidId, finding.PhoneNumber, finding.Issue, "Medium", null));

        foreach (var finding in report.FlowFindings)
            Append(matches, unmatched, new BestPracticeFindingContext("Stale Flow", "FlowFinding", "Flow", finding.FlowId, finding.FlowName, finding.Issue, "High", null));

        // Raw exported events are not best-practice violations by themselves.
        // The sentinel layer should map interpreted signals, not event rows.
        AppendBatch(report.QueueServiceabilityFindings, unmatched, matches, finding => new BestPracticeFindingContext("Queue Serviceability", finding.FindingCode, "Queue", finding.QueueId, finding.QueueName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));
        AppendBatch(report.IvrFlowBindingFindings, unmatched, matches, finding => new BestPracticeFindingContext("IVR Flow Dependency", finding.FindingCode, "IVR", finding.IvrId, finding.IvrName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));
        AppendBatch(report.StaleLicenseFindings, unmatched, matches, finding => new BestPracticeFindingContext("Stale License", "StaleLicenseFinding", "User", finding.UserId, finding.UserName, finding.Issue, "Medium", null));
        AppendBatch(report.LicenseOverProvisioningFindings, unmatched, matches, finding => new BestPracticeFindingContext("License Over-Provisioning", "LicenseOverProvisioningFinding", "User", finding.UserId, finding.UserName, finding.Issue, "Medium", finding.RecommendedAction));
        foreach (var finding in report.RoleGroupOverlapFindings)
            Append(matches, unmatched, new BestPracticeFindingContext("Role & Group Overlap", "RoleGroupOverlapFinding", "User", finding.UserId, finding.UserName, finding.Issue, "High", finding.RecommendedAction));

        AppendBatch(report.SiteTopologyFindings, unmatched, matches, finding => new BestPracticeFindingContext("Site Topology", finding.FindingCode, finding.ObjectType, finding.ObjectId, finding.ObjectName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));
        AppendBatch(report.EdgePerformanceObservations.Where(finding => finding.IsAnomalous), unmatched, matches, finding => new BestPracticeFindingContext("Edge Performance", finding.FindingCode, "Edge", finding.EdgeId, finding.EdgeName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));
        AppendBatch(report.PromptHygieneFindings, unmatched, matches, finding => new BestPracticeFindingContext("Prompt Hygiene", finding.FindingCode, "Prompt", finding.PromptId, finding.PromptName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));
        AppendBatch(report.ChangeAdjacencyFindings, unmatched, matches, finding => new BestPracticeFindingContext("Change Adjacency", finding.FindingCode, finding.AffectedObjectType, finding.AffectedObjectId, finding.AffectedObjectName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));
        AppendBatch(report.FlappingDetectionFindings, unmatched, matches, finding => new BestPracticeFindingContext("Flapping Detection", finding.FindingCode, finding.AffectedObjectType, finding.AffectedObjectId, finding.AffectedObjectName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));
        AppendBatch(report.AuditLogSignalFindings, unmatched, matches, finding => new BestPracticeFindingContext("Audit Log Signals", finding.FindingCode, finding.EntityType, finding.EntityId, finding.EntityName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));
        AppendBatch(report.HistoricalDriftFindings, unmatched, matches, finding => new BestPracticeFindingContext("Historical Drift", "HistoricalDriftFinding", finding.ObjectType, finding.ObjectId, finding.ObjectName, finding.Issue, finding.Severity.ToString(), finding.RecommendedAction));

        var unmatchedList = unmatched.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        if (unmatchedList.Count > 0)
        {
            _logger.LogInformation(
                "Best-practice enrichment encountered unmapped finding types: {FindingTypes}",
                string.Join(", ", unmatchedList));
        }

        return new BestPracticeEnrichmentResult(matches, unmatchedList);
    }

    private void AppendExtensionFindings(
        AuditReportData report,
        ISet<string> unmatched,
        ICollection<BestPracticeGuidanceFinding> matches)
    {
        var extensionReport = report.ExtensionReport;

        AppendBatch(extensionReport.DuplicateProfileExtensions, unmatched, matches, finding =>
            new BestPracticeFindingContext(
                "Extension Integrity",
                "DuplicateProfileExtension",
                "Extension",
                finding.ExtensionKey,
                finding.ExtensionKey,
                $"Extension '{finding.ExtensionKey}' is present on multiple user profiles.",
                FindingSeverity.Critical.ToString(),
                "Normalize profile extension ownership so each active user has a unique managed extension."));

        TrackUnmappedIfAny(extensionReport.ExtensionAssignedToWrongEntity.Count, "ExtensionAssignedToWrongEntity", unmatched);

        AppendBatch(extensionReport.ProfileExtensionsNotAssigned, unmatched, matches, finding =>
            new BestPracticeFindingContext(
                "Extension Integrity",
                "ProfileExtensionNotAssigned",
                "Extension",
                finding.ExtensionKey,
                finding.ExtensionKey,
                $"Extension '{finding.ExtensionKey}' exists on a user profile but no matching telephony assignment was found.",
                FindingSeverity.High.ToString(),
                "Assign the extension through managed telephony ownership so the profile and assignment layers agree."));

        TrackUnmappedIfAny(extensionReport.AssignedExtensionsMissingFromProfiles.Count, "AssignedExtensionMissingFromProfile", unmatched);
        TrackUnmappedIfAny(extensionReport.InvalidProfileExtensions.Count, "InvalidProfileExtension", unmatched);
        TrackUnmappedIfAny(extensionReport.InvalidAssignedExtensions.Count, "InvalidAssignedExtension", unmatched);
    }

    private static void TrackUnmappedIfAny(int count, string findingType, ISet<string> unmatched)
    {
        if (count > 0)
            unmatched.Add(findingType);
    }

    private void Append(
        ICollection<BestPracticeGuidanceFinding> matches,
        ISet<string> unmatched,
        BestPracticeFindingContext context)
    {
        var guidance = Enrich(context);
        if (guidance.Count == 0)
        {
            unmatched.Add(context.SourceFindingType);
            return;
        }

        foreach (var match in guidance)
            matches.Add(match);
    }

    private void AppendBatch<T>(
        IEnumerable<T> findings,
        ISet<string> unmatched,
        ICollection<BestPracticeGuidanceFinding> matches,
        Func<T, BestPracticeFindingContext> projector)
    {
        foreach (var finding in findings)
            Append(matches, unmatched, projector(finding));
    }

    private static string? ResolveMappingFindingType(string findingType)
    {
        if (FindingTypeAliases.TryGetValue(findingType, out var mapped))
            return mapped;

        return string.IsNullOrWhiteSpace(findingType) ? null : findingType;
    }

    private IReadOnlyList<string> FindGlossaryTerms(
        IReadOnlyList<BestPracticeEntry> entries,
        BestPracticeFindingContext context)
    {
        var domains = entries
            .Select(entry => entry.Domain)
            .Where(domain => !string.IsNullOrWhiteSpace(domain))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var corpus = string.Join(
            ' ',
            entries.SelectMany(entry => new[]
            {
                entry.Title,
                entry.Summary,
                entry.WhyItMatters,
                entry.RecommendedActionDetailed,
                entry.ControlFamily,
                entry.Pillar
            }).Append(context.SourceObjectType ?? string.Empty).Append(context.Issue));

        return domains
            .SelectMany(_glossaryRepository.GetByDomain)
            .Where(term => corpus.IndexOf(term.Term, StringComparison.OrdinalIgnoreCase) >= 0)
            .Select(term => term.Term)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(term => term, StringComparer.OrdinalIgnoreCase)
            .Take(6)
            .ToList();
    }

    private static string FirstNonEmpty(IEnumerable<string?> values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? string.Empty;

    private static string FirstNonEmpty(string? first, string? second, IEnumerable<string?> remainder)
        => FirstNonEmpty(new[] { first, second }.Concat(remainder));
}
