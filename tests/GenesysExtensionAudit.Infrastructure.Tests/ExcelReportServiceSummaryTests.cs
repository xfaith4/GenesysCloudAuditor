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
                    ApiSurfaces = ["/api/v2/telephony/providers/edges"],
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
