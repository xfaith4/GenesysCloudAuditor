using System.IO;
using System.Linq;
using ClosedXML.Excel;
using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class ExcelReportServiceSummaryTests
{
    [Fact]
    public async Task GenerateAsync_EdgePerformanceSheet_WritesPerEdgeDistributionRows()
    {
        var report = new AuditReportData
        {
            GeneratedAt = new DateTimeOffset(2026, 04, 06, 10, 00, 00, TimeSpan.Zero),
            OrgRegion = "us-east-1",
            Options = new AuditRunOptions
            {
                RunSiteTopologyAudit = true,
                RunOperationalEventLogs = true,
                OperationalEventLookbackDays = 2
            },
            EdgePerformanceObservations =
            [
                new EdgePerformanceObservation(
                    SiteId: "site-1",
                    SiteName: "Main Site",
                    EdgeId: "edge-1",
                    EdgeName: "Edge Alpha",
                    EdgeRole: "Primary",
                    OnlineStatus: "ONLINE",
                    ExpectedToCarryLoad: true,
                    FindingCode: EdgePerformanceCode.LoadImbalance,
                    StatusLabel: "Overloaded",
                    IsAnomalous: true,
                    Severity: FindingSeverity.Medium,
                    ObservedConversationCount: 160,
                    SiteObservedConversationCount: 220,
                    ExpectedEdgeCount: 2,
                    ObservedSharePercent: 72.7,
                    ExpectedSharePercent: 50,
                    ShareDeltaPercent: 22.7,
                    OperationalEventCount: 240,
                    ErrorEventCount: 9,
                    ErrorRatePercent: 3.8,
                    LastEventUtc: new DateTimeOffset(2026, 04, 06, 09, 55, 00, TimeSpan.Zero),
                    Issue: "Edge Alpha is carrying materially more observed traffic than expected.",
                    RecommendedAction: "Review edge and trunk affinity.")
            ]
        };

        var bytes = await new ExcelReportService().GenerateAsync(
            report,
            CancellationToken.None,
            scopeOptions: new ExcelWorkbookScopeOptions
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
                IncludeEdgePerformance = true,
                IncludePromptHygiene = false,
                IncludeChangeAdjacency = false,
                IncludeFlappingDetection = false,
                IncludeHotSpot = false,
                IncludeFindingLifecycle = false,
                IncludeHistoricalDrift = false
            });

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        var sheet = workbook.Worksheet("Edge_Performance");
        Assert.NotNull(sheet);

        var values = sheet.CellsUsed()
            .Select(cell => cell.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        AssertContains(values, "Edge Performance & Distribution");
        AssertContains(values, "Edge Alpha");
        AssertContains(values, "Overloaded");
        AssertContains(values, "Review edge and trunk affinity.");
    }

    [Fact]
    public async Task GenerateAsync_SummarySheet_IncludesExecutiveDashboardSections()
    {
        var report = new AuditReportData
        {
            GeneratedAt = new DateTimeOffset(2026, 04, 01, 14, 30, 00, TimeSpan.Zero),
            RunStartedAtUtc = new DateTimeOffset(2026, 04, 01, 14, 25, 00, TimeSpan.Zero),
            RunCompletedAtUtc = new DateTimeOffset(2026, 04, 01, 14, 30, 00, TimeSpan.Zero),
            OrgRegion = "us-east-1",
            Options = new AuditRunOptions
            {
                RunUserTelephonyAudit = true,
                RunQueueServiceabilityAudit = true,
                RunSiteTopologyAudit = true
            },
            QueueServiceabilityFindings =
            [
                new QueueServiceabilityFinding(
                    QueueId: "queue-1",
                    QueueName: "Queue Alpha",
                    TotalMembersOnRecord: 12,
                    MembersChecked: 12,
                    ActiveMemberCount: 0,
                    InactiveMemberCount: 12,
                    UnresolvableMemberCount: 0,
                    FindingCode: QueueServiceabilityCode.AllInactive,
                    Issue: "All members are inactive.",
                    Severity: FindingSeverity.High,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Restore active queue members.")
            ],
            SiteTopologyFindings =
            [
                new SiteTopologyFinding(
                    FindingCode: SiteTopologyCode.EdgeOffline,
                    ObjectType: "Edge",
                    ObjectId: "edge-1",
                    ObjectName: "Edge Alpha",
                    SiteId: "site-1",
                    SiteName: "Primary Site",
                    EdgeId: "edge-1",
                    EdgeName: "Edge Alpha",
                    TrunkState: "DOWN",
                    Issue: "Primary edge is offline.",
                    Severity: FindingSeverity.Critical,
                    Category: FindingCategory.EscalateToGenesysCare,
                    RecommendedAction: "Escalate persistent edge outage.")
            ],
            EdgePerformanceObservations =
            [
                new EdgePerformanceObservation(
                    SiteId: "site-1",
                    SiteName: "Primary Site",
                    EdgeId: "edge-2",
                    EdgeName: "Edge Beta",
                    EdgeRole: "Primary",
                    OnlineStatus: "ONLINE",
                    ExpectedToCarryLoad: true,
                    FindingCode: EdgePerformanceCode.NoObservedTraffic,
                    StatusLabel: "No Observed Traffic",
                    IsAnomalous: true,
                    Severity: FindingSeverity.High,
                    ObservedConversationCount: 0,
                    SiteObservedConversationCount: 220,
                    ExpectedEdgeCount: 2,
                    ObservedSharePercent: 0,
                    ExpectedSharePercent: 50,
                    ShareDeltaPercent: -50,
                    OperationalEventCount: 0,
                    ErrorEventCount: 0,
                    ErrorRatePercent: 0,
                    LastEventUtc: null,
                    Issue: "Edge Beta is online but has no observed conversations while peer edges carry traffic.",
                    RecommendedAction: "Review load distribution and edge membership.")
            ],
            HotSpotFindings =
            [
                new HotSpotFinding(
                    Rank: 1,
                    ObjectId: "queue-1",
                    ObjectName: "Queue Alpha",
                    ObjectType: "Queue",
                    TotalFindingCount: 4,
                    DistinctDomainCount: 3,
                    AffectedDomains: ["Queue Serviceability", "Routing Bindings", "Historical Drift"],
                    Issue: "Queue Alpha is impacted across multiple audit domains.",
                    Severity: FindingSeverity.High,
                    RecommendedAction: "Investigate Queue Alpha first.")
            ],
            FindingLifecycleWasComputed = true,
            FindingLifecycleFindings =
            [
                new FindingLifecycleFinding(
                    LifecycleStatus: FindingLifecycleStatus.New,
                    Domain: "Queue Serviceability",
                    FindingType: QueueServiceabilityCode.AllInactive,
                    FindingKey: "queue-serviceability|queue-1|all-inactive",
                    ObjectId: "queue-1",
                    ObjectName: "Queue Alpha",
                    Issue: "Queue Alpha lost active membership.",
                    Severity: FindingSeverity.High,
                    FirstSeenUtc: new DateTimeOffset(2026, 04, 01, 14, 30, 00, TimeSpan.Zero),
                    LastSeenUtc: new DateTimeOffset(2026, 04, 01, 14, 30, 00, TimeSpan.Zero),
                    ObservationCount: 1)
            ],
            HistoricalDriftWasComputed = true,
            HistoricalDriftFindings =
            [
                new HistoricalDriftFinding(
                    ChangeType: HistoricalDriftChangeType.Changed,
                    Domain: "Telephony Ownership",
                    RelationshipType: "UserTelephonyBinding",
                    RelationshipKey: "telephony-user|user-1",
                    ObjectType: "User",
                    ObjectId: "user-1",
                    ObjectName: "Operator 1",
                    PreviousValue: "station=station-a",
                    CurrentValue: "station=station-b",
                    Issue: "Telephony ownership drift after cutover.",
                    Severity: FindingSeverity.High,
                    RecommendedAction: "Review the telephony reassignment history.")
            ]
        };

        var carePacket = new CareEvidencePacket
        {
            GeneratedUtc = report.GeneratedAt,
            OrgRegion = report.OrgRegion,
            AuditDurationSeconds = 300,
            Summary = new CareEvidenceSummary
            {
                TotalFindingsInRun = 3,
                CriticalCount = 1,
                HighCount = 2,
                EscalationCandidateCount = 1,
                ReadyForCareCount = 1,
                NeedsReviewCount = 0,
                MonitorCount = 0
            },
            EscalationCandidates =
            [
                new CareEscalationCandidate
                {
                    CandidateId = "cand-1",
                    Domain = "Site Topology",
                    FindingCode = SiteTopologyCode.EdgeOffline,
                    Severity = "Critical",
                    Category = FindingCategory.EscalateToGenesysCare.ToString(),
                    Confidence = "High",
                    SuspectedOwner = "Telephony Platform",
                    ProbableCauseCategory = "Platform defect suspected",
                    BlastRadius = "Inbound and outbound calling",
                    SupportReadiness = "Ready",
                    SupportReadinessScore = 96,
                    AffectedObjectId = "edge-1",
                    AffectedObjectName = "Edge Alpha",
                    AffectedObjectType = "Edge",
                    DependencyChain = "Site Primary Site -> Edge Edge Alpha -> Hosted Telephony Resources",
                    ApiSurfaces = ["/api/v2/telephony/providers/edges"],
                    EvidenceChain =
                    [
                        "GET /api/v2/telephony/providers/edges/sites returned the site inventory.",
                        "GET /api/v2/telephony/providers/edges returned the edge status.",
                        "Comparison result: Edge Alpha is offline while queues and trunks still reference it."
                    ],
                    WhyThisMatters = "Anything hosted on the offline edge can degrade at once, turning a single infrastructure issue into a wider calling outage.",
                    RecentChangeContext = "No matching administrative change in the last 24 hours.",
                    QualificationNotes = ["Persistent outage", "Cross-API corroboration"],
                    EvidenceSummary = "Edge Alpha is offline while queues and trunks still reference it.",
                    SuggestedCaseText = "Open a Care case for the persistent edge outage.",
                    RecommendedAction = "Open a Genesys Care case with topology evidence.",
                    WorkbookSheet = "Site_Topology"
                }
            ]
        };

        var bytes = await new ExcelReportService().GenerateAsync(
            report,
            CancellationToken.None,
            scopeOptions: SummaryOnlyScope(),
            carePacket: carePacket);

        using var stream = new MemoryStream(bytes);
        using var workbook = new XLWorkbook(stream);

        Assert.NotNull(workbook.Worksheet("Summary"));
        Assert.NotNull(workbook.Worksheet("Relationship_Explainability"));

        var summaryValues = workbook
            .Worksheet("Summary")
            .CellsUsed()
            .Select(cell => cell.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        AssertContains(summaryValues, "Executive Summary & Triage Dashboard");
        AssertContains(summaryValues, "Triage Overview");
        AssertContains(summaryValues, "Open Case Recommended");
        AssertContains(summaryValues, "Domain Health");
        AssertContains(summaryValues, "Top Impacted Objects");
        AssertContains(summaryValues, "Queue Alpha");
        AssertContains(summaryValues, "Escalation Overview");
        AssertContains(summaryValues, "Platform defect suspected");
        AssertContains(summaryValues, "Audit Inventory");
        AssertContains(summaryValues, "Historical Drift");
        AssertContains(summaryValues, "Edge Performance & Distribution");

        var explainabilityValues = workbook
            .Worksheet("Relationship_Explainability")
            .CellsUsed()
            .Select(cell => cell.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToList();

        AssertContains(explainabilityValues, "Dependency Chain");
        AssertContains(explainabilityValues, "Evidence Chain");
        AssertContains(explainabilityValues, "Why This Matters");
        AssertContains(explainabilityValues, "Hosted Telephony Resources");
        AssertContains(explainabilityValues, "Comparison result");
    }

    private static ExcelWorkbookScopeOptions SummaryOnlyScope() => new()
    {
        IncludeSummary = true,
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
        IncludeHistoricalDrift = false
    };

    private static void AssertContains(IReadOnlyCollection<string> values, string expectedSubstring)
        => Assert.Contains(values, value => value.Contains(expectedSubstring, StringComparison.Ordinal));
}
