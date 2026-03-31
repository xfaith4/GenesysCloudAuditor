using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.BestPractices;
using GenesysExtensionAudit.Infrastructure.Configuration;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class BestPracticesIntegrationTests
{
    [Fact]
    public void ContentService_LoadsSharedPackageFromConfiguredRoot()
    {
        var service = CreateContentService(rootPath: GetSharedPackageRoot());

        var status = service.GetStatus();

        Assert.True(status.RootPathResolved);
        Assert.True(status.CatalogCount > 0);
        Assert.True(status.MappingCount > 0);
        Assert.True(status.GlossaryCount > 0);
        Assert.Contains(status.ReferenceDocuments, document => document.Name == "BestPractices" && document.Exists);
    }

    [Fact]
    public void Repositories_ReturnCatalogAndGlossaryEntries()
    {
        var service = CreateContentService(rootPath: GetSharedPackageRoot());
        var bestPracticeRepository = new BestPracticeRepository(service);
        var glossaryRepository = new GlossaryRepository(service);

        var practice = bestPracticeRepository.GetByKey("telephony.did_extension.structured_assignment");
        var glossary = glossaryRepository.GetGlossaryTerm("Edge");

        Assert.NotNull(practice);
        Assert.Equal("Telephony", practice!.Domain);
        Assert.NotNull(glossary);
        Assert.Equal("EdgeSite", glossary!.Domain);
    }

    [Fact]
    public void ContentService_MissingFiles_ReturnsEmptyRepositoriesAndWarnings()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var service = CreateContentService(rootPath: tempRoot);

            var snapshot = service.GetSnapshot();

            Assert.True(snapshot.Status.RootPathResolved);
            Assert.False(snapshot.Status.IsHealthy);
            Assert.Empty(snapshot.Catalog.Catalog);
            Assert.Empty(snapshot.Map.Mappings);
            Assert.Empty(snapshot.Glossary.Terms);
            Assert.Contains(snapshot.Status.Files, file => file.Name == "Catalog" && !file.Exists);
            Assert.Contains(snapshot.Status.Messages, message => message.Contains("file not found", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void ContentService_InvalidCatalogSchema_ContinuesSafely_WhenFailOnValidationErrorIsFalse()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            CreateMinimalPackage(tempRoot, invalidCatalog: true);
            var service = CreateContentService(rootPath: tempRoot, failOnValidationError: false);

            var snapshot = service.GetSnapshot();
            var catalogStatus = Assert.Single(snapshot.Status.Files, file => file.Name == "Catalog");

            Assert.False(catalogStatus.ValidationPassed);
            Assert.True(catalogStatus.LoadedSuccessfully);
            Assert.False(snapshot.Status.IsHealthy);
            Assert.Empty(snapshot.Catalog.Catalog);
            Assert.Single(snapshot.Map.Mappings);
            Assert.Single(snapshot.Glossary.Terms);
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }

    [Fact]
    public void Enricher_MapsCurrentFindingsAndTracksUnmappedTypes()
    {
        var service = CreateContentService(rootPath: GetSharedPackageRoot());
        var enricher = CreateEnricher(service);
        var report = new AuditReportData
        {
            UserTelephonyIntegrityFindings =
            [
                new UserTelephonyIntegrityFinding(
                    UserId: "user-1",
                    UserName: "Operator One",
                    Email: "operator.one@example.invalid",
                    UserState: "active",
                    ProfileExtensionRaw: "1001",
                    StationId: "station-1",
                    StationName: "Station 1",
                    RelatedDidNumber: "+13175550100",
                    FindingCode: TelephonyIntegrityCode.DidOwnerExtensionMismatch,
                    Issue: "DID assignment does not align with the user profile.",
                    Severity: FindingSeverity.High,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Normalize DID ownership.")
            ],
            SiteTopologyFindings =
            [
                new SiteTopologyFinding(
                    FindingCode: SiteTopologyCode.EdgeOffline,
                    ObjectType: "Edge",
                    ObjectId: "edge-1",
                    ObjectName: "Edge One",
                    SiteId: "site-1",
                    SiteName: "Main Site",
                    EdgeId: "edge-1",
                    EdgeName: "Edge One",
                    TrunkState: null,
                    Issue: "Edge is offline.",
                    Severity: FindingSeverity.Critical,
                    Category: FindingCategory.EscalateToGenesysCare,
                    RecommendedAction: "Review edge state.")
            ]
        };

        var result = enricher.Enrich(report);

        var match = Assert.Single(result.Matches);
        Assert.Contains("telephony.did_extension.structured_assignment", match.BestPracticeKeys);
        Assert.Equal("User Telephony Integrity", match.SourceDomain);
        Assert.Contains(SiteTopologyCode.EdgeOffline, result.UnmatchedFindingTypes);
    }

    [Fact]
    public void Enricher_ContextPath_HandlesZeroOneAndManyMatches()
    {
        var service = CreateContentService(rootPath: GetSharedPackageRoot());
        var enricher = CreateEnricher(service);

        var zero = enricher.Enrich(new BestPracticeFindingContext("Test", "UnknownFinding", "Object", "1", "Object One", "No mapping", "Low", null));
        var one = enricher.Enrich(new BestPracticeFindingContext("Telephony", "DidOrExtensionAssignmentInconsistent", "User", "user-1", "Operator One", "Telephony mismatch", "High", null));
        var many = enricher.Enrich(new BestPracticeFindingContext("Security", "OAuthClientScopeExceedsFunction", "OAuthClient", "client-1", "Client One", "Scope is too broad", "High", null));

        Assert.Empty(zero);
        Assert.Single(one);
        Assert.Equal("telephony.did_extension.structured_assignment", Assert.Single(one).BestPracticeKeys.Single());
        Assert.Single(many);
        Assert.Equal(2, Assert.Single(many).BestPracticeKeys.Count);
    }

    [Fact]
    public async Task ExcelReport_WritesBestPracticeGuidanceSheet()
    {
        var report = new AuditReportData
        {
            GeneratedAt = new DateTimeOffset(2026, 04, 07, 12, 00, 00, TimeSpan.Zero),
            OrgRegion = "us-east-1",
            BestPracticeGuidanceWasComputed = true,
            BestPracticeGuidanceFindings =
            [
                new BestPracticeGuidanceFinding(
                    SourceDomain: "Telephony",
                    SourceFindingType: TelephonyIntegrityCode.DidOwnerExtensionMismatch,
                    SourceObjectType: "User",
                    SourceObjectId: "user-1",
                    SourceObjectName: "Operator One",
                    Issue: "Telephony assignment mismatch.",
                    EffectiveSeverity: "High",
                    BestPracticeKeys: ["telephony.did_extension.structured_assignment"],
                    BestPracticeTitles: ["Structured DID and extension assignment"],
                    ControlFamily: "Telephony Hygiene",
                    Pillar: "Operational Excellence",
                    RecommendedActionShort: "Normalize DID ownership.",
                    RecommendedActionDetailed: "Align the DID owner, station, and profile extension.",
                    WhyItMatters: "Misaligned assignment obscures ownership and can break routing.",
                    OwnerRole: "Telephony Engineer",
                    OwnerTeam: "Voice Platform",
                    EvidenceExamples: ["DID owner does not match user profile extension."],
                    GlossaryTerms: ["Edge"],
                    MappingFindingType: "DidOrExtensionAssignmentInconsistent")
            ]
        };

        var bytes = await new ExcelReportService().GenerateAsync(
            report,
            CancellationToken.None,
            new ExcelWorkbookScopeOptions
            {
                IncludeSummary = false,
                IncludeExtensions = false,
                IncludeGroups = false,
                IncludeQueues = false,
                IncludeFlows = false,
                IncludeInactiveUsers = false,
                IncludeDids = false,
                IncludeAuditLogs = false,
                IncludeOperationalEvents = false,
                IncludeOutboundEvents = false,
                IncludeStaleLicenses = false,
                IncludeLicenseOverProvisioning = false,
                IncludeRoleGroupOverlap = false,
                IncludeSiteTopology = false,
                IncludeEdgePerformance = false,
                IncludePromptHygiene = false,
                IncludeChangeAdjacency = false,
                IncludeFlappingDetection = false,
                IncludeHotSpot = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false,
                IncludeBestPracticeGuidance = true
            });

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);
        var sheet = workbook.Worksheet("Best_Practice_Guidance");
        var values = sheet.CellsUsed()
            .Select(cell => cell.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        Assert.Contains("  Best Practice Guidance", values);
        Assert.Contains("telephony.did_extension.structured_assignment", values);
        Assert.Contains("Structured DID and extension assignment", values);
    }

    private static FindingBestPracticeEnricher CreateEnricher(IBestPracticesContentService contentService)
        => new(
            new BestPracticeRepository(contentService),
            new GlossaryRepository(contentService),
            NullLogger<FindingBestPracticeEnricher>.Instance);

    private static BestPracticesContentService CreateContentService(string rootPath, bool failOnValidationError = false)
        => new(
            Options.Create(new BestPracticesOptions
            {
                RootPath = rootPath,
                FailOnValidationError = failOnValidationError
            }),
            NullLogger<BestPracticesContentService>.Instance);

    private static string GetSharedPackageRoot()
    {
        for (var current = new DirectoryInfo(AppContext.BaseDirectory); current is not null; current = current.Parent)
        {
            var candidate = Path.Combine(current.FullName, "shared", "Genesys.BestPractices");
            if (Directory.Exists(candidate))
                return candidate;
        }

        throw new DirectoryNotFoundException("Could not locate shared/Genesys.BestPractices from the test base directory.");
    }

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "genesys-best-practices-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateMinimalPackage(string rootPath, bool invalidCatalog)
    {
        var bestPracticesDirectory = Path.Combine(rootPath, "best-practices");
        Directory.CreateDirectory(bestPracticesDirectory);

        File.Copy(
            Path.Combine(GetSharedPackageRoot(), "best-practices", "best-practices.schema.json"),
            Path.Combine(bestPracticesDirectory, "best-practices.schema.json"));
        File.Copy(
            Path.Combine(GetSharedPackageRoot(), "best-practices", "best-practices-map.schema.json"),
            Path.Combine(bestPracticesDirectory, "best-practices-map.schema.json"));

        var catalogJson = invalidCatalog
            ? """
              {
                "version": "0.2.0"
              }
              """
            : """
              {
                "version": "0.2.0",
                "generated_on": "2026-03-31",
                "catalog_name": "Test Catalog",
                "domains": ["Telephony"],
                "catalog": [
                  {
                    "key": "telephony.did_extension.structured_assignment",
                    "domain": "Telephony",
                    "subcategory": "Assignments",
                    "control_family": "Telephony Hygiene",
                    "pillar": "Operational Excellence",
                    "report_category": "Weekly Audit",
                    "title": "Structured DID and extension assignment",
                    "summary": "DIDs and extensions should align with managed ownership.",
                    "why_it_matters": "Assignment drift breaks explainability.",
                    "recommended_state": "Assignments are documented and aligned.",
                    "anti_pattern": "Assignments are changed ad hoc.",
                    "severity": "Medium",
                    "auditability": "Deterministic",
                    "tags": ["telephony", "did"],
                    "object_types": ["User", "DID"],
                    "source_basis": "Guidance",
                    "source_refs": ["Ref 1"],
                    "source_notes": "Test",
                    "source_urls": ["https://example.invalid/best-practice"],
                    "last_verified": "2026-03-31",
                    "detection_strategy": "Compare DID and extension ownership.",
                    "required_inputs": ["Users", "DIDs"],
                    "logic_hint": "ownership comparison",
                    "automatable": true,
                    "recommended_action_short": "Normalize DID ownership.",
                    "recommended_action_detailed": "Align DID and user ownership records.",
                    "remediation_priority": "P2",
                    "rollback_considerations": "Review prior assignment before rollback.",
                    "evidence_examples": ["DID owner and user profile differ."],
                    "sample_bad_state": "DID belongs to the wrong user.",
                    "sample_good_state": "DID and extension belong to the same user.",
                    "owner_role": "Telephony Engineer",
                    "owner_team": "Voice Platform",
                    "review_cadence": "Weekly",
                    "false_positive_notes": "Check recent migrations.",
                    "exceptions": "Documented pilot migrations.",
                    "risk_acceptance_allowed": false,
                    "status": "Active",
                    "introduced_in_version": "0.2.0",
                    "review_status": "Verified"
                  }
                ]
              }
              """;

        var mapJson = """
            {
              "version": "0.2.0",
              "generated_on": "2026-03-31",
              "mappings": [
                {
                  "finding_type": "DidOrExtensionAssignmentInconsistent",
                  "best_practice_keys": ["telephony.did_extension.structured_assignment"],
                  "default_severity": "Medium",
                  "recommended_action_short": "Normalize DID ownership."
                }
              ]
            }
            """;

        var glossaryJson = """
            {
              "version": "0.2.0",
              "generated_on": "2026-03-31",
              "terms": [
                {
                  "term": "Edge",
                  "domain": "EdgeSite",
                  "definition": "A premises telephony component."
                }
              ]
            }
            """;

        File.WriteAllText(Path.Combine(bestPracticesDirectory, "best-practices.catalog.json"), catalogJson);
        File.WriteAllText(Path.Combine(bestPracticesDirectory, "best-practices-map.json"), mapJson);
        File.WriteAllText(Path.Combine(bestPracticesDirectory, "glossary.json"), glossaryJson);
        File.WriteAllText(Path.Combine(rootPath, "README.md"), "# Test");
        File.WriteAllText(Path.Combine(rootPath, "Glossary.md"), "# Test");
        File.WriteAllText(Path.Combine(rootPath, "Roadmap.md"), "# Test");
        File.WriteAllText(Path.Combine(bestPracticesDirectory, "BestPractices.md"), "# Test");
    }
}
