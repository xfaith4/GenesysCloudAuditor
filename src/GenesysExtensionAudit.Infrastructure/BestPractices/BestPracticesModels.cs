using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.BestPractices;

public sealed record BestPracticeCatalog
{
    public string Version { get; init; } = string.Empty;
    public string GeneratedOn { get; init; } = string.Empty;
    public string CatalogName { get; init; } = string.Empty;
    public IReadOnlyList<string> Domains { get; init; } = [];
    public IReadOnlyList<BestPracticeEntry> Catalog { get; init; } = [];
}

public sealed record BestPracticeEntry
{
    public string Key { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string Subcategory { get; init; } = string.Empty;
    public string Title { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string WhyItMatters { get; init; } = string.Empty;
    public string RecommendedState { get; init; } = string.Empty;
    public string AntiPattern { get; init; } = string.Empty;
    public string Severity { get; init; } = string.Empty;
    public string Auditability { get; init; } = string.Empty;
    public IReadOnlyList<string> Tags { get; init; } = [];
    public IReadOnlyList<string> ObjectTypes { get; init; } = [];
    public string ControlFamily { get; init; } = string.Empty;
    public string Pillar { get; init; } = string.Empty;
    public string ReportCategory { get; init; } = string.Empty;
    public string OwnerRole { get; init; } = string.Empty;
    public string OwnerTeam { get; init; } = string.Empty;
    public string ReviewCadence { get; init; } = string.Empty;
    public string RecommendedActionShort { get; init; } = string.Empty;
    public string RecommendedActionDetailed { get; init; } = string.Empty;
    public string RollbackConsiderations { get; init; } = string.Empty;
    public string DetectionStrategy { get; init; } = string.Empty;
    public IReadOnlyList<string> RequiredInputs { get; init; } = [];
    public bool Automatable { get; init; }
    public IReadOnlyList<string> EvidenceExamples { get; init; } = [];
    public string SampleGoodState { get; init; } = string.Empty;
    public string SampleBadState { get; init; } = string.Empty;
    public string FalsePositiveNotes { get; init; } = string.Empty;
    public IReadOnlyList<string> Exceptions { get; init; } = [];
    public bool RiskAcceptanceAllowed { get; init; }
    public IReadOnlyList<string> SourceRefs { get; init; } = [];
    public IReadOnlyList<string> SourceUrls { get; init; } = [];
    public string SourceNotes { get; init; } = string.Empty;
    public string LastVerified { get; init; } = string.Empty;
    public string Status { get; init; } = string.Empty;
    public string IntroducedInVersion { get; init; } = string.Empty;
    public string? DeprecatedInVersion { get; init; }
    public string ReviewStatus { get; init; } = string.Empty;
    public string SourceBasis { get; init; } = string.Empty;
    public string LogicHint { get; init; } = string.Empty;
    public string RemediationPriority { get; init; } = string.Empty;
}

public sealed record BestPracticeMap
{
    public string Version { get; init; } = string.Empty;
    public string GeneratedOn { get; init; } = string.Empty;
    public IReadOnlyList<BestPracticeMapEntry> Mappings { get; init; } = [];
}

public sealed record BestPracticeMapEntry
{
    public string FindingType { get; init; } = string.Empty;
    public IReadOnlyList<string> BestPracticeKeys { get; init; } = [];
    public string DefaultSeverity { get; init; } = string.Empty;
    public string RecommendedActionShort { get; init; } = string.Empty;
}

public sealed record GlossaryCatalog
{
    public string Version { get; init; } = string.Empty;
    public string GeneratedOn { get; init; } = string.Empty;
    public IReadOnlyList<GlossaryEntry> Terms { get; init; } = [];
}

public sealed record GlossaryEntry
{
    public string Term { get; init; } = string.Empty;
    public string Domain { get; init; } = string.Empty;
    public string Definition { get; init; } = string.Empty;
}

public sealed record BestPracticeReferenceDocument(
    string Name,
    string? Path,
    bool Exists);

public sealed record BestPracticeFileStatus(
    string Name,
    string? Path,
    bool Exists,
    bool ValidationPassed,
    bool LoadedSuccessfully,
    IReadOnlyList<string> Messages);

public sealed record BestPracticesContentStatus(
    bool RootPathResolved,
    string? ResolvedRootPath,
    bool IsHealthy,
    int CatalogCount,
    int MappingCount,
    int GlossaryCount,
    IReadOnlyList<BestPracticeFileStatus> Files,
    IReadOnlyList<BestPracticeReferenceDocument> ReferenceDocuments,
    IReadOnlyList<string> Messages)
{
    public string Summary =>
        RootPathResolved
            ? $"Root: {ResolvedRootPath} | Catalog: {CatalogCount} | Map: {MappingCount} | Glossary: {GlossaryCount}"
            : "Best-practices content could not be resolved.";
}

public sealed record BestPracticesContentSnapshot(
    BestPracticeCatalog Catalog,
    BestPracticeMap Map,
    GlossaryCatalog Glossary,
    BestPracticesContentStatus Status)
{
    public static BestPracticesContentSnapshot Empty(BestPracticesContentStatus status)
        => new(new BestPracticeCatalog(), new BestPracticeMap(), new GlossaryCatalog(), status);
}

public sealed record BestPracticeFindingContext(
    string SourceDomain,
    string SourceFindingType,
    string? SourceObjectType,
    string? SourceObjectId,
    string? SourceObjectName,
    string Issue,
    string? Severity,
    string? RecommendedAction);

public sealed record BestPracticeGuidanceFinding(
    string SourceDomain,
    string SourceFindingType,
    string? SourceObjectType,
    string? SourceObjectId,
    string? SourceObjectName,
    string Issue,
    string EffectiveSeverity,
    IReadOnlyList<string> BestPracticeKeys,
    IReadOnlyList<string> BestPracticeTitles,
    string ControlFamily,
    string Pillar,
    string RecommendedActionShort,
    string RecommendedActionDetailed,
    string WhyItMatters,
    string OwnerRole,
    string OwnerTeam,
    IReadOnlyList<string> EvidenceExamples,
    IReadOnlyList<string> GlossaryTerms,
    string MappingFindingType)
{
    [JsonIgnore]
    public string BestPracticeKeysDisplay => string.Join(", ", BestPracticeKeys);

    [JsonIgnore]
    public string BestPracticeTitlesDisplay => string.Join(" | ", BestPracticeTitles);

    [JsonIgnore]
    public string EvidenceExamplesDisplay => string.Join(" | ", EvidenceExamples);

    [JsonIgnore]
    public string GlossaryTermsDisplay => string.Join(", ", GlossaryTerms);

    [JsonIgnore]
    public string OwnerDisplay => string.IsNullOrWhiteSpace(OwnerTeam)
        ? OwnerRole
        : $"{OwnerRole} / {OwnerTeam}";
}

public sealed record BestPracticeEnrichmentResult(
    IReadOnlyList<BestPracticeGuidanceFinding> Matches,
    IReadOnlyList<string> UnmatchedFindingTypes);
