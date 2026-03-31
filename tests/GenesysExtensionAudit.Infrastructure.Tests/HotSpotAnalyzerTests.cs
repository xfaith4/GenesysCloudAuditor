using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="HotSpotAnalyzer"/> (Phase 2.3 — Hot Spot Ranking).
/// </summary>
public sealed class HotSpotAnalyzerTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AuditReportData EmptyReport() => new();

    private static QueueServiceabilityFinding QueueServiceabilityFinding(string queueId, string queueName)
        => new(
            QueueId: queueId,
            QueueName: queueName,
            TotalMembersOnRecord: 5,
            MembersChecked: 5,
            ActiveMemberCount: 0,
            InactiveMemberCount: 5,
            UnresolvableMemberCount: 0,
            FindingCode: QueueServiceabilityCode.AllInactive,
            Issue: "All members inactive",
            Severity: FindingSeverity.High,
            Category: FindingCategory.LocalConfigFix,
            RecommendedAction: "Review queue membership.");

    private static QueueFinding QueueFinding(string queueId, string queueName)
        => new(
            QueueId: queueId,
            QueueName: queueName,
            Description: null,
            MemberCount: 0,
            Issue: "Empty queue");

    private static FlowFinding FlowFinding(string flowId, string flowName)
        => new(
            FlowId: flowId,
            FlowName: flowName,
            FlowType: "INBOUNDCALL",
            IsPublished: false,
            PublishedDate: null,
            DateModified: null,
            DaysSincePublished: null,
            Issue: "Never published");

    private static InactiveUserFinding InactiveUserFinding(string userId, string userName)
        => new(
            UserId: userId,
            UserName: userName,
            Email: $"{userName}@example.invalid",
            State: "inactive",
            TokenLastIssuedDate: DateTimeOffset.UtcNow.AddDays(-120),
            DaysSinceLogin: 120,
            Issue: "User inactive");

    private static StaleLicenseFinding StaleLicenseFinding(string userId, string userName)
        => new(
            UserId: userId,
            UserName: userName,
            Email: $"{userName}@example.invalid",
            State: "active",
            AssignedLicenses: ["PureCloud 3"],
            TokenLastIssuedDate: DateTimeOffset.UtcNow.AddDays(-90),
            DaysSinceLogin: 90,
            Issue: "License stale");

    private static SiteTopologyFinding SiteTopologyFinding(string objectId, string objectName)
        => new(
            FindingCode: SiteTopologyCode.EdgeOffline,
            ObjectType: "Edge",
            ObjectId: objectId,
            ObjectName: objectName,
            SiteId: "site-1",
            SiteName: "Main Site",
            EdgeId: objectId,
            EdgeName: objectName,
            TrunkState: null,
            Issue: "Edge offline",
            Severity: FindingSeverity.Critical,
            Category: FindingCategory.EscalateToGenesysCare,
            RecommendedAction: "Check edge.");

    private static ChangeAdjacencyFinding ChangeAdjacencyFinding(string objectId, string objectName, string objectType)
        => new(
            FindingCode: ChangeAdjacencyCode.ChangeBeforeFinding,
            AffectedObjectType: objectType,
            AffectedObjectId: objectId,
            AffectedObjectName: objectName,
            ChangeTimestamp: DateTimeOffset.UtcNow.AddMinutes(-30),
            ChangeAction: "UPDATE",
            ChangedBy: "audit-bot@example.invalid",
            ChangeCount: 1,
            RelatedFindingType: "Queue Serviceability",
            Issue: "Change preceded finding",
            Severity: FindingSeverity.Info,
            RecommendedAction: "Review change.");

    // ─── No findings / empty ─────────────────────────────────────────────────

    [Fact]
    public void HotSpot_EmptyReport_ReturnsEmpty()
    {
        var findings = new HotSpotAnalyzer().Analyze(EmptyReport());

        Assert.Empty(findings);
    }

    [Fact]
    public void HotSpot_SingleDomainOnly_ReturnsEmptyWithDefaultThreshold()
    {
        // A queue only appears in one domain — not a hot spot at default minDistinctDomains=2
        var report = new AuditReportData
        {
            QueueServiceabilityFindings = [QueueServiceabilityFinding("q-1", "ServiceDesk")]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Empty(findings);
    }

    [Fact]
    public void HotSpot_SingleDomainWithMinOne_ReturnsEntry()
    {
        var report = new AuditReportData
        {
            QueueServiceabilityFindings = [QueueServiceabilityFinding("q-1", "ServiceDesk")]
        };

        var findings = new HotSpotAnalyzer().Analyze(report, minDistinctDomains: 1);

        Assert.Single(findings);
        Assert.Equal("q-1", findings[0].ObjectId);
    }

    // ─── Cross-domain detection ───────────────────────────────────────────────

    [Fact]
    public void HotSpot_QueueInTwoDomains_FlagsAsHotSpot()
    {
        var queueId = "queue-support";
        var report = new AuditReportData
        {
            QueueServiceabilityFindings = [QueueServiceabilityFinding(queueId, "Support")],
            QueueFindings = [QueueFinding(queueId, "Support")]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(queueId, f.ObjectId);
        Assert.Equal(2, f.TotalFindingCount);
        Assert.Equal(2, f.DistinctDomainCount);
        Assert.Contains("Queue Serviceability", f.AffectedDomains);
        Assert.Contains("Queue Hygiene", f.AffectedDomains);
    }

    [Fact]
    public void HotSpot_UserInThreeDomains_FlagsAsHotSpot()
    {
        var userId = "user-alice";
        var report = new AuditReportData
        {
            InactiveUserFindings = [InactiveUserFinding(userId, "alice")],
            StaleLicenseFindings = [StaleLicenseFinding(userId, "alice")],
            LicenseOverProvisioningFindings =
            [
                new LicenseOverProvisioningFinding(
                    UserId: userId,
                    UserName: "alice",
                    Email: "alice@example.invalid",
                    State: "active",
                    AllAssignedLicenses: ["PureCloud 3"],
                    OverProvisionedLicenses: ["PureCloud 3"],
                    TokenLastIssuedDate: null,
                    DaysSinceLogin: null,
                    Issue: "Over-provisioned",
                    RecommendedAction: "Downgrade license.")
            ]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(userId, f.ObjectId);
        Assert.Equal(3, f.TotalFindingCount);
        Assert.Equal(3, f.DistinctDomainCount);
    }

    // ─── Ranking ─────────────────────────────────────────────────────────────

    [Fact]
    public void HotSpot_MultipleObjects_RankedByTotalFindingCountDescending()
    {
        var userId = "user-bob";
        var queueId = "queue-main";

        // User appears in 3 domains, queue in 2
        var report = new AuditReportData
        {
            InactiveUserFindings = [InactiveUserFinding(userId, "bob")],
            StaleLicenseFindings = [StaleLicenseFinding(userId, "bob")],
            LicenseOverProvisioningFindings =
            [
                new LicenseOverProvisioningFinding(userId, "bob", "bob@example.invalid", "active",
                    ["PureCloud 3"], ["PureCloud 3"], null, null, "Over-provisioned", "Downgrade license.")
            ],
            QueueServiceabilityFindings = [QueueServiceabilityFinding(queueId, "Main")],
            QueueFindings = [QueueFinding(queueId, "Main")]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Equal(2, findings.Count);
        // User (3 appearances) ranked #1
        Assert.Equal(1, findings[0].Rank);
        Assert.Equal(userId, findings[0].ObjectId);
        // Queue (2 appearances) ranked #2
        Assert.Equal(2, findings[1].Rank);
        Assert.Equal(queueId, findings[1].ObjectId);
    }

    [Fact]
    public void HotSpot_AffectedDomainsAreSortedAlphabetically()
    {
        var queueId = "q-sorted";
        var report = new AuditReportData
        {
            QueueServiceabilityFindings = [QueueServiceabilityFinding(queueId, "Sorted")],
            QueueFindings = [QueueFinding(queueId, "Sorted")],
            ChangeAdjacencyFindings = [ChangeAdjacencyFinding(queueId, "Sorted", "Queue")]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Single(findings);
        var domains = findings[0].AffectedDomains.ToList();
        Assert.Equal(domains.OrderBy(d => d).ToList(), domains);
    }

    // ─── Severity ────────────────────────────────────────────────────────────

    [Fact]
    public void HotSpot_FivePlusFindings_SeverityIsHigh()
    {
        // Create a queue that appears in 5+ findings by reusing ID across collections
        var queueId = "q-heavy";
        var report = new AuditReportData
        {
            QueueServiceabilityFindings = [QueueServiceabilityFinding(queueId, "Heavy")],
            QueueFindings = [QueueFinding(queueId, "Heavy")],
            // ChangeAdjacency can reference the same queue multiple times
            ChangeAdjacencyFindings =
            [
                ChangeAdjacencyFinding(queueId, "Heavy", "Queue"),
                ChangeAdjacencyFinding(queueId, "Heavy", "Queue"),
                ChangeAdjacencyFinding(queueId, "Heavy", "Queue"),
            ]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Single(findings);
        // Total finding count = 5 (2 from queue domains + 3 from change adjacency)
        Assert.True(findings[0].TotalFindingCount >= 5);
        Assert.Equal(FindingSeverity.High, findings[0].Severity);
    }

    [Fact]
    public void HotSpot_TwoDomains_SeverityIsLow()
    {
        var queueId = "q-two";
        var report = new AuditReportData
        {
            QueueServiceabilityFindings = [QueueServiceabilityFinding(queueId, "Two")],
            QueueFindings = [QueueFinding(queueId, "Two")]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Single(findings);
        Assert.Equal(FindingSeverity.Low, findings[0].Severity);
    }

    // ─── Flapping detection integration ──────────────────────────────────────

    [Fact]
    public void HotSpot_FlappingFindingContributes_ToHotSpotRanking()
    {
        var objectId = "edge-flap";
        var report = new AuditReportData
        {
            SiteTopologyFindings = [SiteTopologyFinding(objectId, "FlappingEdge")],
            FlappingDetectionFindings =
            [
                new FlappingFinding(
                    FindingCode: FlappingCode.ResourceOscillation,
                    AffectedObjectType: "Edge",
                    AffectedObjectId: objectId,
                    AffectedObjectName: "FlappingEdge",
                    FirstChangeUtc: DateTimeOffset.UtcNow.AddHours(-2),
                    LastChangeUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
                    ChangeCount: 6,
                    DistinctActionCount: 2,
                    ObservedActions: ["CREATE", "DELETE"],
                    Issue: "Edge oscillating",
                    Severity: FindingSeverity.High,
                    RecommendedAction: "Investigate edge")
            ]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Single(findings);
        Assert.Equal(objectId, findings[0].ObjectId);
        Assert.Contains("Site Topology", findings[0].AffectedDomains);
        Assert.Contains("Flapping Detection", findings[0].AffectedDomains);
    }

    // ─── Issue and recommended action text ───────────────────────────────────

    [Fact]
    public void HotSpot_IssueTextContainsObjectNameAndDomainCount()
    {
        var queueId = "q-issue-test";
        var report = new AuditReportData
        {
            QueueServiceabilityFindings = [QueueServiceabilityFinding(queueId, "IssueQueue")],
            QueueFindings = [QueueFinding(queueId, "IssueQueue")]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Single(findings);
        Assert.Contains("IssueQueue", findings[0].Issue);
        Assert.Contains("2", findings[0].Issue);
    }

    [Fact]
    public void HotSpot_RecommendedActionMentionsInvestigation()
    {
        var queueId = "q-advice";
        var report = new AuditReportData
        {
            QueueServiceabilityFindings = [QueueServiceabilityFinding(queueId, "AdviceQueue")],
            QueueFindings = [QueueFinding(queueId, "AdviceQueue")]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        Assert.Single(findings);
        Assert.Contains("Investigate", findings[0].RecommendedAction, StringComparison.OrdinalIgnoreCase);
    }

    // ─── No-ID / name-only objects ────────────────────────────────────────────

    [Fact]
    public void HotSpot_ObjectWithNoIdButName_IndexedByName()
    {
        // A finding with no ID but with a name should still be indexed
        var report = new AuditReportData
        {
            QueueFindings =
            [
                new QueueFinding(QueueId: "  ", QueueName: "NameOnlyQueue", Description: null, MemberCount: 0, Issue: "Empty")
            ],
            QueueServiceabilityFindings =
            [
                new QueueServiceabilityFinding(
                    QueueId: "  ",
                    QueueName: "NameOnlyQueue",
                    TotalMembersOnRecord: 0,
                    MembersChecked: 0,
                    ActiveMemberCount: 0,
                    InactiveMemberCount: 0,
                    UnresolvableMemberCount: 0,
                    FindingCode: QueueServiceabilityCode.AllInactive,
                    Issue: "Inactive",
                    Severity: FindingSeverity.High,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Fix")
            ]
        };

        var findings = new HotSpotAnalyzer().Analyze(report);

        // Both findings share the same name and should resolve to one hot spot
        Assert.Single(findings);
        Assert.Equal("NameOnlyQueue", findings[0].ObjectName);
    }
}
