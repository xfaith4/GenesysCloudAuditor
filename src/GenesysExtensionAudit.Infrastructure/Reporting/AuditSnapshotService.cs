using System.Text.Json;
using System.Text.Json.Serialization;
using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Services;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

public interface IAuditSnapshotService
{
    Task<AuditSnapshotLoadResult> LoadLatestAsync(string outputDirectory, string filePrefix, CancellationToken ct);
    AuditSnapshotComparisonResult Compare(AuditReportData report, AuditSnapshotPacket? previousSnapshot);
    Task<string> SaveSnapshotAsync(AuditSnapshotPacket snapshot, string outputDirectory, string filePrefix, CancellationToken ct);
}

public sealed class AuditSnapshotLoadResult
{
    public AuditSnapshotPacket? Snapshot { get; init; }
    public string? Path { get; init; }
}

public sealed class AuditSnapshotComparisonResult
{
    public AuditSnapshotPacket Snapshot { get; init; } = new();
    public IReadOnlyList<FindingLifecycleFinding> LifecycleFindings { get; init; } = [];
    public IReadOnlyList<HistoricalDriftFinding> HistoricalDriftFindings { get; init; } = [];
    public bool HistoricalDriftWasComputed { get; init; }
}

public sealed class AuditSnapshotPacket
{
    [JsonPropertyName("snapshotVersion")]
    public string SnapshotVersion { get; init; } = "2.0";

    [JsonPropertyName("generatedUtc")]
    public DateTimeOffset GeneratedUtc { get; init; }

    [JsonPropertyName("orgRegion")]
    public string OrgRegion { get; init; } = string.Empty;

    [JsonPropertyName("previousSnapshotGeneratedUtc")]
    public DateTimeOffset? PreviousSnapshotGeneratedUtc { get; init; }

    [JsonPropertyName("findingCount")]
    public int FindingCount { get; init; }

    [JsonPropertyName("capturedFindingDomains")]
    public IReadOnlyList<string> CapturedFindingDomains { get; init; } = [];

    [JsonPropertyName("findings")]
    public IReadOnlyList<AuditSnapshotFinding> Findings { get; init; } = [];

    [JsonPropertyName("relationshipCount")]
    public int RelationshipCount { get; init; }

    [JsonPropertyName("capturedRelationshipDomains")]
    public IReadOnlyList<string> CapturedRelationshipDomains { get; init; } = [];

    [JsonPropertyName("relationships")]
    public IReadOnlyList<AuditSnapshotRelationship> Relationships { get; init; } = [];
}

public sealed class AuditSnapshotFinding
{
    [JsonPropertyName("findingKey")]
    public string FindingKey { get; init; } = string.Empty;

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("findingType")]
    public string FindingType { get; init; } = string.Empty;

    [JsonPropertyName("objectId")]
    public string? ObjectId { get; init; }

    [JsonPropertyName("objectName")]
    public string? ObjectName { get; init; }

    [JsonPropertyName("issue")]
    public string Issue { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = FindingSeverity.Info.ToString();

    [JsonPropertyName("firstSeenUtc")]
    public DateTimeOffset FirstSeenUtc { get; init; }

    [JsonPropertyName("lastSeenUtc")]
    public DateTimeOffset LastSeenUtc { get; init; }

    [JsonPropertyName("observationCount")]
    public int ObservationCount { get; init; }
}

public sealed class AuditSnapshotRelationship
{
    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("relationshipType")]
    public string RelationshipType { get; init; } = string.Empty;

    [JsonPropertyName("relationshipKey")]
    public string RelationshipKey { get; init; } = string.Empty;

    [JsonPropertyName("objectType")]
    public string ObjectType { get; init; } = string.Empty;

    [JsonPropertyName("objectId")]
    public string? ObjectId { get; init; }

    [JsonPropertyName("objectName")]
    public string? ObjectName { get; init; }

    [JsonPropertyName("normalizedValue")]
    public string NormalizedValue { get; init; } = string.Empty;

    [JsonPropertyName("displayValue")]
    public string DisplayValue { get; init; } = string.Empty;
}

public sealed class AuditSnapshotService : IAuditSnapshotService
{
    private const string SnapshotDirectoryName = "snapshots";
    private const string SnapshotSuffix = ".audit-snapshot.json";
    private const string TelephonyDomain = "Telephony Ownership";
    private const string RoutingDomain = "Routing Bindings";
    private const string TopologyDomain = "Topology Relationships";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public async Task<AuditSnapshotLoadResult> LoadLatestAsync(string outputDirectory, string filePrefix, CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var snapshotDirectory = Path.Combine(outputDirectory, SnapshotDirectoryName);
        if (!Directory.Exists(snapshotDirectory))
            return new AuditSnapshotLoadResult();

        var sanitizedPrefix = SanitizeFileComponent(filePrefix);
        var searchPattern = string.IsNullOrWhiteSpace(sanitizedPrefix)
            ? $"*{SnapshotSuffix}"
            : $"{sanitizedPrefix}_*{SnapshotSuffix}";

        var candidateFiles = Directory
            .EnumerateFiles(snapshotDirectory, searchPattern, SearchOption.TopDirectoryOnly)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(path))
            .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();

        foreach (var path in candidateFiles)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var json = await File.ReadAllTextAsync(path, ct).ConfigureAwait(false);
                var snapshot = JsonSerializer.Deserialize<AuditSnapshotPacket>(json, JsonOptions);
                if (snapshot is not null)
                {
                    return new AuditSnapshotLoadResult
                    {
                        Snapshot = snapshot,
                        Path = path
                    };
                }
            }
            catch (JsonException)
            {
                // Skip unreadable snapshots and try the next-most-recent file.
            }
        }

        return new AuditSnapshotLoadResult();
    }

    public AuditSnapshotComparisonResult Compare(AuditReportData report, AuditSnapshotPacket? previousSnapshot)
    {
        ArgumentNullException.ThrowIfNull(report);

        var currentCapturedFindingDomains = GetCurrentCapturedFindingDomains(report);
        var currentFindings = ExtractCurrentFindings(report, report.GeneratedAt)
            .OrderBy(f => f.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.ObjectName ?? f.ObjectId ?? f.FindingKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FindingKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var comparableFindingDomains = GetComparableFindingDomains(currentCapturedFindingDomains, previousSnapshot);

        var previousByKey = previousSnapshot?.Findings
            .GroupBy(f => f.FindingKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, AuditSnapshotFinding>(StringComparer.OrdinalIgnoreCase);

        var mergedCurrent = new List<AuditSnapshotFinding>(currentFindings.Count);
        var lifecycle = new List<FindingLifecycleFinding>();

        foreach (var current in currentFindings)
        {
            if (previousByKey.TryGetValue(current.FindingKey, out var previous))
            {
                var merged = new AuditSnapshotFinding
                {
                    FindingKey = current.FindingKey,
                    Domain = current.Domain,
                    FindingType = current.FindingType,
                    ObjectId = current.ObjectId,
                    ObjectName = current.ObjectName,
                    Issue = current.Issue,
                    Severity = current.Severity,
                    FirstSeenUtc = previous.FirstSeenUtc,
                    LastSeenUtc = report.GeneratedAt,
                    ObservationCount = previous.ObservationCount + 1
                };

                mergedCurrent.Add(merged);
                lifecycle.Add(ToLifecycleFinding(FindingLifecycleStatus.Recurrent, merged));
            }
            else
            {
                mergedCurrent.Add(current);
                lifecycle.Add(ToLifecycleFinding(FindingLifecycleStatus.New, current));
            }
        }

        var currentKeys = mergedCurrent
            .Select(f => f.FindingKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var resolved in previousByKey.Values.Where(f => comparableFindingDomains.Contains(f.Domain) && !currentKeys.Contains(f.FindingKey)))
            lifecycle.Add(ToLifecycleFinding(FindingLifecycleStatus.Resolved, resolved));

        lifecycle = lifecycle
            .OrderBy(f => LifecycleSortKey(f.LifecycleStatus))
            .ThenBy(f => f.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.ObjectName ?? f.ObjectId ?? f.FindingKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.FindingKey, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var currentCapturedRelationshipDomains = GetCurrentCapturedRelationshipDomains(report);
        var currentRelationships = report.RelationshipSnapshots
            .Select(ToSnapshotRelationship)
            .OrderBy(r => r.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.ObjectName ?? r.ObjectId ?? r.RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var comparableRelationshipDomains = GetComparableRelationshipDomains(currentCapturedRelationshipDomains, previousSnapshot);
        var driftFindings = CompareRelationships(currentRelationships, previousSnapshot?.Relationships ?? [], comparableRelationshipDomains);

        return new AuditSnapshotComparisonResult
        {
            Snapshot = new AuditSnapshotPacket
            {
                GeneratedUtc = report.GeneratedAt,
                OrgRegion = report.OrgRegion,
                PreviousSnapshotGeneratedUtc = previousSnapshot?.GeneratedUtc,
                FindingCount = mergedCurrent.Count,
                CapturedFindingDomains = currentCapturedFindingDomains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                Findings = mergedCurrent,
                RelationshipCount = currentRelationships.Count,
                CapturedRelationshipDomains = currentCapturedRelationshipDomains.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
                Relationships = currentRelationships
            },
            LifecycleFindings = lifecycle,
            HistoricalDriftFindings = driftFindings,
            HistoricalDriftWasComputed = comparableRelationshipDomains.Count > 0
        };
    }

    public async Task<string> SaveSnapshotAsync(AuditSnapshotPacket snapshot, string outputDirectory, string filePrefix, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputDirectory);

        var snapshotDirectory = Path.Combine(outputDirectory, SnapshotDirectoryName);
        Directory.CreateDirectory(snapshotDirectory);

        var prefix = SanitizeFileComponent(filePrefix);
        if (string.IsNullOrWhiteSpace(prefix))
            prefix = "GenesysAudit";

        var baseName = $"{prefix}_{snapshot.GeneratedUtc:yyyyMMdd_HHmmss}{SnapshotSuffix}";
        var path = GetNextAvailableFilePath(snapshotDirectory, baseName);

        var json = JsonSerializer.Serialize(snapshot, JsonOptions);
        await File.WriteAllTextAsync(path, json, ct).ConfigureAwait(false);
        return path;
    }

    private static FindingLifecycleFinding ToLifecycleFinding(string status, AuditSnapshotFinding snapshot)
        => new(
            LifecycleStatus: status,
            Domain: snapshot.Domain,
            FindingType: snapshot.FindingType,
            FindingKey: snapshot.FindingKey,
            ObjectId: snapshot.ObjectId,
            ObjectName: snapshot.ObjectName,
            Issue: snapshot.Issue,
            Severity: ParseSeverity(snapshot.Severity),
            FirstSeenUtc: snapshot.FirstSeenUtc,
            LastSeenUtc: snapshot.LastSeenUtc,
            ObservationCount: snapshot.ObservationCount);

    private static int LifecycleSortKey(string status) => status switch
    {
        FindingLifecycleStatus.New => 0,
        FindingLifecycleStatus.Recurrent => 1,
        FindingLifecycleStatus.Resolved => 2,
        _ => 3
    };

    private static int DriftSortKey(string changeType) => changeType switch
    {
        HistoricalDriftChangeType.Changed => 0,
        HistoricalDriftChangeType.Added => 1,
        HistoricalDriftChangeType.Removed => 2,
        _ => 3
    };

    private static FindingSeverity ParseSeverity(string? raw)
        => Enum.TryParse<FindingSeverity>(raw, ignoreCase: true, out var severity)
            ? severity
            : FindingSeverity.Info;

    private static HashSet<string> GetCurrentCapturedFindingDomains(AuditReportData report)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (report.Options.RunExtensionAudit) domains.Add("Extensions");
        if (report.Options.RunGroupAudit) domains.Add("Groups");
        if (report.Options.RunQueueAudit) domains.Add("Queue Hygiene");
        if (report.Options.RunFlowAudit) domains.Add("Flow Hygiene");
        if (report.Options.RunInactiveUserAudit) domains.Add("Inactive Users");
        if (report.Options.RunDidAudit) domains.Add("DID Hygiene");
        if (report.Options.RunUserTelephonyAudit) domains.Add("User Telephony Integrity");
        if (report.Options.RunQueueServiceabilityAudit) domains.Add("Queue Serviceability");
        if (report.Options.RunFlowDependencyAudit) domains.Add("IVR Flow Dependency");
        if (report.Options.RunSiteTopologyAudit) domains.Add("Site Topology");
        if (report.Options.RunPromptHygieneAudit) domains.Add("Prompt Hygiene");
        if (report.Options.RunAuditLogs) domains.Add("Audit Logs");
        if (report.Options.RunAuditLogs) domains.Add("Audit Log Signals");
        if (report.Options.RunOperationalEventLogs) domains.Add("Operational Events");
        if (report.Options.RunOutboundEvents) domains.Add("Outbound Events");
        if (report.Options.RunStaleLicenseAudit || report.Options.RunLicenseOverProvisioningAudit || report.Options.RunRoleGroupOverlapAudit)
            domains.Add("License Hygiene");
        if (report.Options.RunChangeAdjacencyAudit && report.Options.RunAuditLogs) domains.Add("Change Adjacency");
        if (report.Options.RunFlappingDetectionAudit && report.Options.RunAuditLogs) domains.Add("Flapping Detection");
        if (report.Options.RunHotSpotAudit) domains.Add("Hot Spots");
        return domains;
    }

    private static HashSet<string> GetComparableFindingDomains(HashSet<string> currentCapturedDomains, AuditSnapshotPacket? previousSnapshot)
    {
        var previousCapturedDomains = previousSnapshot?.CapturedFindingDomains is { Count: > 0 }
            ? previousSnapshot.CapturedFindingDomains.ToHashSet(StringComparer.OrdinalIgnoreCase)
            : previousSnapshot?.Findings.Select(f => f.Domain).ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        previousCapturedDomains.IntersectWith(currentCapturedDomains);
        return previousCapturedDomains;
    }

    private static HashSet<string> GetCurrentCapturedRelationshipDomains(AuditReportData report)
    {
        var domains = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (report.Options.RunUserTelephonyAudit)
            domains.Add(TelephonyDomain);
        if (report.Options.RunFlowDependencyAudit)
            domains.Add(RoutingDomain);
        if (report.Options.RunSiteTopologyAudit)
            domains.Add(TopologyDomain);
        return domains;
    }

    private static HashSet<string> GetComparableRelationshipDomains(HashSet<string> currentCapturedDomains, AuditSnapshotPacket? previousSnapshot)
    {
        if (previousSnapshot?.CapturedRelationshipDomains is not { Count: > 0 })
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var comparable = previousSnapshot.CapturedRelationshipDomains.ToHashSet(StringComparer.OrdinalIgnoreCase);
        comparable.IntersectWith(currentCapturedDomains);
        return comparable;
    }

    private static IReadOnlyList<HistoricalDriftFinding> CompareRelationships(
        IReadOnlyList<AuditSnapshotRelationship> currentRelationships,
        IReadOnlyList<AuditSnapshotRelationship> previousRelationships,
        HashSet<string> comparableDomains)
    {
        if (comparableDomains.Count == 0)
            return [];

        var currentByKey = currentRelationships
            .Where(r => comparableDomains.Contains(r.Domain))
            .GroupBy(r => r.RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var previousByKey = previousRelationships
            .Where(r => comparableDomains.Contains(r.Domain))
            .GroupBy(r => r.RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var drift = new List<HistoricalDriftFinding>();

        foreach (var current in currentByKey.Values)
        {
            if (!previousByKey.TryGetValue(current.RelationshipKey, out var previous))
            {
                drift.Add(ToHistoricalDriftFinding(HistoricalDriftChangeType.Added, previous: null, current));
                continue;
            }

            if (!string.Equals(current.NormalizedValue, previous.NormalizedValue, StringComparison.Ordinal))
                drift.Add(ToHistoricalDriftFinding(HistoricalDriftChangeType.Changed, previous, current));
        }

        foreach (var previous in previousByKey.Values.Where(r => !currentByKey.ContainsKey(r.RelationshipKey)))
            drift.Add(ToHistoricalDriftFinding(HistoricalDriftChangeType.Removed, previous, current: null));

        return drift
            .OrderBy(f => DriftSortKey(f.ChangeType))
            .ThenBy(f => f.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.ObjectName ?? f.ObjectId ?? f.RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.RelationshipKey, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static HistoricalDriftFinding ToHistoricalDriftFinding(
        string changeType,
        AuditSnapshotRelationship? previous,
        AuditSnapshotRelationship? current)
    {
        var relation = current ?? previous ?? throw new InvalidOperationException("A previous or current relationship is required.");
        var relationLabel = HumanizeRelationType(relation.RelationshipType);
        var objectLabel = relation.ObjectName ?? relation.ObjectId ?? relation.RelationshipKey;

        var issue = changeType switch
        {
            HistoricalDriftChangeType.Added => $"{relationLabel} for {objectLabel} was added since the previous baseline.",
            HistoricalDriftChangeType.Removed => $"{relationLabel} for {objectLabel} no longer appears in the current baseline.",
            HistoricalDriftChangeType.Changed => $"{relationLabel} for {objectLabel} changed since the previous baseline.",
            _ => $"{relationLabel} for {objectLabel} drifted since the previous baseline."
        };

        return new HistoricalDriftFinding(
            ChangeType: changeType,
            Domain: relation.Domain,
            RelationshipType: relation.RelationshipType,
            RelationshipKey: relation.RelationshipKey,
            ObjectType: relation.ObjectType,
            ObjectId: relation.ObjectId,
            ObjectName: relation.ObjectName,
            PreviousValue: previous?.DisplayValue,
            CurrentValue: current?.DisplayValue,
            Issue: issue,
            Severity: DetermineDriftSeverity(relation),
            RecommendedAction: DetermineDriftRecommendedAction(relation));
    }

    private static FindingSeverity DetermineDriftSeverity(AuditSnapshotRelationship relationship) => relationship.RelationshipType switch
    {
        "SiteEdgeMembership" => FindingSeverity.Medium,
        _ when string.Equals(relationship.Domain, TopologyDomain, StringComparison.OrdinalIgnoreCase) => FindingSeverity.High,
        _ when string.Equals(relationship.Domain, TelephonyDomain, StringComparison.OrdinalIgnoreCase) => FindingSeverity.High,
        _ when string.Equals(relationship.Domain, RoutingDomain, StringComparison.OrdinalIgnoreCase) => FindingSeverity.High,
        _ => FindingSeverity.Medium
    };

    private static string DetermineDriftRecommendedAction(AuditSnapshotRelationship relationship) => relationship.Domain switch
    {
        TelephonyDomain => "Confirm the telephony ownership change was intentional and verify the affected user, extension, DID, and station references remain aligned.",
        RoutingDomain => "Review the IVR, schedule-group, and flow binding change to confirm the current routing path is intentional and still points to published flows.",
        TopologyDomain => "Review the site, edge, or trunk relationship change to confirm the topology move was intentional and the updated service path is healthy.",
        _ => "Review the changed relationship and confirm it was intentional."
    };

    private static string HumanizeRelationType(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return "Relationship";

        var chars = new List<char>(raw.Length + 8);
        for (var i = 0; i < raw.Length; i++)
        {
            var c = raw[i];
            if (i > 0 && char.IsUpper(c) && !char.IsWhiteSpace(raw[i - 1]))
                chars.Add(' ');
            chars.Add(c);
        }

        return new string(chars.ToArray());
    }

    private static AuditSnapshotRelationship ToSnapshotRelationship(AuditRelationshipSnapshot snapshot)
        => new()
        {
            Domain = snapshot.Domain,
            RelationshipType = snapshot.RelationshipType,
            RelationshipKey = snapshot.RelationshipKey,
            ObjectType = snapshot.ObjectType,
            ObjectId = snapshot.ObjectId,
            ObjectName = snapshot.ObjectName,
            NormalizedValue = snapshot.NormalizedValue,
            DisplayValue = snapshot.DisplayValue
        };

    private static IReadOnlyList<AuditSnapshotFinding> ExtractCurrentFindings(AuditReportData report, DateTimeOffset observedAtUtc)
    {
        var findings = new List<AuditSnapshotFinding>();

        foreach (var f in report.ExtensionReport.DuplicateProfileExtensions)
            findings.Add(Create("Extensions", "DuplicateProfileExtension", $"duplicate-profile|{f.ExtensionKey}", f.ExtensionKey, f.ExtensionKey,
                $"Extension '{f.ExtensionKey}' appears on {f.Users.Count} profile(s).", FindingSeverity.Critical, observedAtUtc));

        foreach (var f in report.ExtensionReport.DuplicateAssignedExtensions)
            findings.Add(Create("Extensions", "DuplicateAssignedExtension", $"duplicate-assigned|{f.ExtensionKey}", f.ExtensionKey, f.ExtensionKey,
                $"Extension '{f.ExtensionKey}' has {f.Assignments.Count} telephony assignments.", FindingSeverity.Critical, observedAtUtc));

        foreach (var f in report.ExtensionReport.ProfileExtensionsNotAssigned)
            findings.Add(Create("Extensions", "ProfileExtensionNotAssigned", $"profile-not-assigned|{f.ExtensionKey}", f.Users.FirstOrDefault()?.UserId, f.Users.FirstOrDefault()?.UserName,
                $"Profile extension '{f.ExtensionKey}' is not present in telephony assignments.", FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.ExtensionReport.AssignedExtensionsMissingFromProfiles)
            findings.Add(Create("Extensions", "AssignedExtensionMissingFromProfiles", $"assigned-not-profile|{f.ExtensionKey}", f.Assignments.FirstOrDefault()?.TargetId, f.ExtensionKey,
                $"Assigned extension '{f.ExtensionKey}' does not appear on any profile.", FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.ExtensionReport.ExtensionAssignedToWrongEntity)
            findings.Add(Create("Extensions", "ExtensionAssignedToWrongEntity", $"ownership-mismatch|{f.ExtensionKey}|{f.User.UserId}", f.User.UserId, f.User.UserName,
                $"Extension '{f.ExtensionKey}' is assigned to a different entity than user '{f.User.UserName ?? f.User.UserId}'.", FindingSeverity.Critical, observedAtUtc));

        foreach (var f in report.ExtensionReport.InvalidProfileExtensions)
            findings.Add(Create("Extensions", "InvalidProfileExtension", $"invalid-profile-extension|{f.UserId}|{f.ExtensionRaw}|{f.Status}", f.UserId, f.UserName,
                f.Notes, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.ExtensionReport.InvalidAssignedExtensions)
            findings.Add(Create("Extensions", "InvalidAssignedExtension", $"invalid-assigned-extension|{f.AssignmentId}|{f.ExtensionRaw}|{f.Status}", f.AssignmentId, f.ExtensionRaw,
                f.Notes, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.GroupFindings)
            findings.Add(Create("Groups", "GroupFinding", $"group|{f.GroupId}|{f.Issue}", f.GroupId, f.GroupName, f.Issue, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.QueueFindings)
            findings.Add(Create("Queue Hygiene", "QueueFinding", $"queue-hygiene|{f.QueueId}|{f.Issue}", f.QueueId, f.QueueName, f.Issue, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.FlowFindings)
            findings.Add(Create("Flow Hygiene", "FlowFinding", $"flow|{f.FlowId}|{f.Issue}", f.FlowId, f.FlowName, f.Issue, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.InactiveUserFindings)
            findings.Add(Create("Inactive Users", "InactiveUserFinding", $"inactive-user|{f.UserId}", f.UserId, f.UserName, f.Issue, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.NoLocationUserFindings)
            findings.Add(Create("Inactive Users", "NoLocationUserFinding", $"missing-location|{f.UserId}", f.UserId, f.UserName, f.Issue, FindingSeverity.Low, observedAtUtc));

        foreach (var f in report.DidFindings)
            findings.Add(Create("DID Hygiene", "DidFinding", $"did|{f.DidId}|{f.Issue}", f.DidId, f.PhoneNumber ?? f.OwnerName, f.Issue, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.StaleLicenseFindings)
            findings.Add(Create("License Hygiene", "StaleLicenseFinding", $"stale-license|{f.UserId}", f.UserId, f.UserName, f.Issue, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.LicenseOverProvisioningFindings)
            findings.Add(Create("License Hygiene", "LicenseOverProvisioningFinding", $"license-over|{f.UserId}|{string.Join(",", f.OverProvisionedLicenses)}", f.UserId, f.UserName, f.Issue, FindingSeverity.Medium, observedAtUtc));

        foreach (var f in report.RoleGroupOverlapFindings)
            findings.Add(Create("License Hygiene", "RoleGroupOverlapFinding", $"role-group-overlap|{f.UserId}|{f.RoleId}|{f.DivisionId}|{f.GroupId}", f.UserId, f.UserName, f.Issue, FindingSeverity.Low, observedAtUtc));

        foreach (var f in report.UserTelephonyIntegrityFindings)
            findings.Add(Create("User Telephony Integrity", f.FindingCode, $"user-telephony|{f.FindingCode}|{f.UserId}|{f.StationId}|{f.RelatedDidNumber}", f.UserId, f.UserName, f.Issue, f.Severity, observedAtUtc));

        foreach (var f in report.QueueServiceabilityFindings)
            findings.Add(Create("Queue Serviceability", f.FindingCode, $"queue-serviceability|{f.FindingCode}|{f.QueueId}", f.QueueId, f.QueueName, f.Issue, f.Severity, observedAtUtc));

        foreach (var f in report.IvrFlowBindingFindings)
            findings.Add(Create("IVR Flow Dependency", f.FindingCode, $"ivr-flow|{f.FindingCode}|{f.IvrId}|{f.BindingSlot}|{f.BoundFlowId}", f.IvrId, f.IvrName, f.Issue, f.Severity, observedAtUtc));

        foreach (var f in report.SiteTopologyFindings)
            findings.Add(Create("Site Topology", f.FindingCode, $"site-topology|{f.FindingCode}|{f.ObjectType}|{f.ObjectId}", f.ObjectId, f.ObjectName, f.Issue, f.Severity, observedAtUtc));

        foreach (var f in report.PromptHygieneFindings)
            findings.Add(Create("Prompt Hygiene", f.FindingCode, $"prompt-hygiene|{f.FindingCode}|{f.PromptId}|{f.AffectedLanguages}", f.PromptId, f.PromptName, f.Issue, f.Severity, observedAtUtc));

        foreach (var f in report.ChangeAdjacencyFindings)
            findings.Add(Create("Change Adjacency", f.FindingCode, $"change-adjacency|{f.FindingCode}|{f.AffectedObjectId}|{f.RelatedFindingType}", f.AffectedObjectId, f.AffectedObjectName, f.Issue, f.Severity, observedAtUtc));

        foreach (var f in report.FlappingDetectionFindings)
            findings.Add(Create("Flapping Detection", f.FindingCode, $"flapping|{f.FindingCode}|{f.AffectedObjectId}", f.AffectedObjectId, f.AffectedObjectName, f.Issue, f.Severity, observedAtUtc));

        foreach (var f in report.AuditLogSignalFindings)
            findings.Add(Create("Audit Log Signals", f.FindingCode, $"audit-log-signal|{f.FindingCode}|{f.EntityId}|{f.EntityName}|{f.UserId}|{f.ClientId}|{f.Action}", f.EntityId, f.EntityName, f.Issue, f.Severity, observedAtUtc));

        foreach (var f in report.HotSpotFindings)
            findings.Add(Create("Hot Spots", "HotSpotFinding", $"hot-spot|{f.ObjectType}|{f.ObjectId}|{f.ObjectName}", f.ObjectId, f.ObjectName, f.Issue, f.Severity, observedAtUtc));

        return findings;
    }

    private static AuditSnapshotFinding Create(
        string domain,
        string findingType,
        string findingKey,
        string? objectId,
        string? objectName,
        string issue,
        FindingSeverity severity,
        DateTimeOffset observedAtUtc)
        => new()
        {
            Domain = domain,
            FindingType = findingType,
            FindingKey = findingKey,
            ObjectId = objectId,
            ObjectName = objectName,
            Issue = issue,
            Severity = severity.ToString(),
            FirstSeenUtc = observedAtUtc,
            LastSeenUtc = observedAtUtc,
            ObservationCount = 1
        };

    private static string SanitizeFileComponent(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var chars = raw.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray();
        return new string(chars).Trim('_');
    }

    private static string GetNextAvailableFilePath(string directory, string fileName)
    {
        var ext = Path.GetExtension(fileName);
        var baseName = Path.GetFileNameWithoutExtension(fileName);
        var candidate = Path.Combine(directory, fileName);
        if (!File.Exists(candidate))
            return candidate;

        var suffix = 2;
        while (true)
        {
            candidate = Path.Combine(directory, $"{baseName}-{suffix}{ext}");
            if (!File.Exists(candidate))
                return candidate;
            suffix++;
        }
    }
}
