using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

/// <summary>
/// A structured Genesys Care escalation evidence packet derived from an audit run.
/// Contains all critical findings with pre-written case narrative text, affected object lists,
/// and API surface references needed to open a Genesys Care support ticket.
///
/// Serializes to JSON for archival and automation pipelines.
/// Also drives the Care_Case_Summary Excel worksheet.
/// </summary>
public sealed class CareEvidencePacket
{
    [JsonPropertyName("packetVersion")]
    public string PacketVersion { get; init; } = "1.0";

    [JsonPropertyName("generatedUtc")]
    public DateTimeOffset GeneratedUtc { get; init; }

    [JsonPropertyName("orgRegion")]
    public string OrgRegion { get; init; } = string.Empty;

    [JsonPropertyName("auditDurationSeconds")]
    public double AuditDurationSeconds { get; init; }

    [JsonPropertyName("summary")]
    public CareEvidenceSummary Summary { get; init; } = new();

    [JsonPropertyName("escalationCandidates")]
    public IReadOnlyList<CareEscalationCandidate> EscalationCandidates { get; init; } = [];
}

public sealed class CareEvidenceSummary
{
    [JsonPropertyName("totalFindingsInRun")]
    public int TotalFindingsInRun { get; init; }

    [JsonPropertyName("criticalCount")]
    public int CriticalCount { get; init; }

    [JsonPropertyName("highCount")]
    public int HighCount { get; init; }

    [JsonPropertyName("mediumCount")]
    public int MediumCount { get; init; }

    [JsonPropertyName("informationalCount")]
    public int InformationalCount { get; init; }

    [JsonPropertyName("escalationCandidateCount")]
    public int EscalationCandidateCount { get; init; }

    [JsonPropertyName("readyForCareCount")]
    public int ReadyForCareCount { get; init; }

    [JsonPropertyName("needsReviewCount")]
    public int NeedsReviewCount { get; init; }

    [JsonPropertyName("monitorCount")]
    public int MonitorCount { get; init; }
}

/// <summary>
/// A single escalation candidate — one finding that has sufficient evidence and business impact
/// to warrant a Genesys Care support case.
/// </summary>
public sealed class CareEscalationCandidate
{
    [JsonPropertyName("candidateId")]
    public string CandidateId { get; init; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("domain")]
    public string Domain { get; init; } = string.Empty;

    [JsonPropertyName("findingCode")]
    public string FindingCode { get; init; } = string.Empty;

    [JsonPropertyName("severity")]
    public string Severity { get; init; } = string.Empty;

    [JsonPropertyName("category")]
    public string Category { get; init; } = string.Empty;

    [JsonPropertyName("confidence")]
    public string Confidence { get; init; } = string.Empty;

    [JsonPropertyName("suspectedOwner")]
    public string SuspectedOwner { get; init; } = string.Empty;

    [JsonPropertyName("probableCauseCategory")]
    public string ProbableCauseCategory { get; init; } = string.Empty;

    [JsonPropertyName("blastRadius")]
    public string BlastRadius { get; init; } = string.Empty;

    [JsonPropertyName("supportReadiness")]
    public string SupportReadiness { get; init; } = string.Empty;

    [JsonPropertyName("supportReadinessScore")]
    public int SupportReadinessScore { get; init; }

    [JsonPropertyName("affectedObjectId")]
    public string? AffectedObjectId { get; init; }

    [JsonPropertyName("affectedObjectName")]
    public string? AffectedObjectName { get; init; }

    [JsonPropertyName("affectedObjectType")]
    public string AffectedObjectType { get; init; } = string.Empty;

    /// <summary>Any secondary object IDs referenced by the finding (e.g. the bound flow ID).</summary>
    [JsonPropertyName("relatedObjectIds")]
    public IReadOnlyList<string> RelatedObjectIds { get; init; } = [];

    [JsonPropertyName("relatedObjectNames")]
    public IReadOnlyList<string> RelatedObjectNames { get; init; } = [];

    /// <summary>Human-readable relationship chain showing how the affected object connects to downstream impact.</summary>
    [JsonPropertyName("dependencyChain")]
    public string DependencyChain { get; init; } = string.Empty;

    /// <summary>Which Genesys Cloud API endpoints were involved in detecting this finding.</summary>
    [JsonPropertyName("apiSurfaces")]
    public IReadOnlyList<string> ApiSurfaces { get; init; } = [];

    /// <summary>Ordered evidence steps showing which comparisons produced the conclusion.</summary>
    [JsonPropertyName("evidenceChain")]
    public IReadOnlyList<string> EvidenceChain { get; init; } = [];

    /// <summary>Plain-language explanation of the operational risk or business impact.</summary>
    [JsonPropertyName("whyThisMatters")]
    public string WhyThisMatters { get; init; } = string.Empty;

    [JsonPropertyName("recentChangeContext")]
    public string? RecentChangeContext { get; init; }

    [JsonPropertyName("qualificationNotes")]
    public IReadOnlyList<string> QualificationNotes { get; init; } = [];

    /// <summary>Human-readable summary of the contradictory or invalid state observed.</summary>
    [JsonPropertyName("evidenceSummary")]
    public string EvidenceSummary { get; init; } = string.Empty;

    /// <summary>Pre-written text suitable for pasting into a Genesys Care case description.</summary>
    [JsonPropertyName("suggestedCaseText")]
    public string SuggestedCaseText { get; init; } = string.Empty;

    [JsonPropertyName("recommendedAction")]
    public string RecommendedAction { get; init; } = string.Empty;

    /// <summary>Excel workbook sheet where this finding appears in detail.</summary>
    [JsonPropertyName("workbookSheet")]
    public string WorkbookSheet { get; init; } = string.Empty;
}
