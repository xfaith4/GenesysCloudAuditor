using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="CareEvidenceExportService"/> (Phase 3 escalation intelligence).
/// </summary>
public sealed class CareEvidenceExportServiceTests
{
    private static readonly DateTimeOffset RunStartedUtc = new(2026, 03, 31, 12, 00, 00, TimeSpan.Zero);
    private static readonly DateTimeOffset RunCompletedUtc = new(2026, 03, 31, 12, 15, 00, TimeSpan.Zero);

    private static CareEvidenceExportService CreateService()
        => new(NullLogger<CareEvidenceExportService>.Instance);

    private static AuditReportData EmptyReport() => new()
    {
        GeneratedAt = RunCompletedUtc,
        RunStartedAtUtc = RunStartedUtc,
        RunCompletedAtUtc = RunCompletedUtc,
        OrgRegion = "us-east-1"
    };

    private static SiteTopologyFinding EdgeOfflineFinding(string edgeId, string edgeName)
        => new(
            FindingCode: SiteTopologyCode.EdgeOffline,
            ObjectType: "Edge",
            ObjectId: edgeId,
            ObjectName: edgeName,
            SiteId: "site-main",
            SiteName: "Main Site",
            EdgeId: edgeId,
            EdgeName: edgeName,
            TrunkState: null,
            Issue: "Edge is offline",
            Severity: FindingSeverity.Critical,
            Category: FindingCategory.EscalateToGenesysCare,
            RecommendedAction: "Check edge connectivity.");

    private static IvrFlowBindingFinding MissingOpenHoursFlow(string ivrId, string ivrName)
        => new(
            IvrId: ivrId,
            IvrName: ivrName,
            Dnis: ["+13175550100"],
            BindingSlot: "OpenHours",
            BoundFlowId: null,
            BoundFlowName: null,
            FlowDaysSincePublished: null,
            FindingCode: IvrBindingCode.NoOpenHoursFlow,
            Issue: "Inbound calls during open hours have no route.",
            Severity: FindingSeverity.Critical,
            Category: FindingCategory.LocalConfigFix,
            RecommendedAction: "Bind an active flow.");

    private static ChangeAdjacencyFinding ChangeFor(string objectId, string objectName, string relatedFindingType)
        => new(
            FindingCode: ChangeAdjacencyCode.ChangeBeforeFinding,
            AffectedObjectType: "IVR",
            AffectedObjectId: objectId,
            AffectedObjectName: objectName,
            ChangeTimestamp: RunCompletedUtc.AddMinutes(-5),
            ChangeAction: "UPDATE",
            ChangedBy: "audit-bot@example.invalid",
            ChangeCount: 1,
            RelatedFindingType: relatedFindingType,
            Issue: "Recent change preceded finding",
            Severity: FindingSeverity.Info,
            RecommendedAction: "Review recent change.");

    [Fact]
    public void BuildPacket_IncludesSiteTopologyCandidates_WithReadyForCareMetadata()
    {
        var report = new AuditReportData
        {
            GeneratedAt = RunCompletedUtc,
            RunStartedAtUtc = RunStartedUtc,
            RunCompletedAtUtc = RunCompletedUtc,
            OrgRegion = "us-east-1",
            SiteTopologyFindings = [EdgeOfflineFinding("edge-1", "Edge One")]
        };

        var packet = CreateService().BuildPacket(report);

        var candidate = Assert.Single(packet.EscalationCandidates);
        Assert.Equal("Site Topology", candidate.Domain);
        Assert.Equal("High", candidate.Confidence);
        Assert.Equal("Ready", candidate.SupportReadiness);
        Assert.True(candidate.SupportReadinessScore >= 70);
        Assert.Equal("Telephony Engineering", candidate.SuspectedOwner);
        Assert.True(string.Join(" ", candidate.QualificationNotes).Contains("API surfaces", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(1, packet.Summary.ReadyForCareCount);
        Assert.Equal(0, packet.Summary.NeedsReviewCount);
        Assert.Equal(0, packet.Summary.MonitorCount);
    }

    [Fact]
    public void BuildPacket_RecentChangeContext_DowngradesSupportReadiness()
    {
        var withoutChange = new AuditReportData
        {
            GeneratedAt = RunCompletedUtc,
            RunStartedAtUtc = RunStartedUtc,
            RunCompletedAtUtc = RunCompletedUtc,
            OrgRegion = "us-east-1",
            IvrFlowBindingFindings = [MissingOpenHoursFlow("ivr-1", "Main IVR")]
        };

        var withChange = new AuditReportData
        {
            GeneratedAt = RunCompletedUtc,
            RunStartedAtUtc = RunStartedUtc,
            RunCompletedAtUtc = RunCompletedUtc,
            OrgRegion = "us-east-1",
            IvrFlowBindingFindings = [MissingOpenHoursFlow("ivr-1", "Main IVR")],
            ChangeAdjacencyFindings = [ChangeFor("ivr-1", "Main IVR", "IVR Flow Dependency")]
        };

        var basePacket = CreateService().BuildPacket(withoutChange);
        var changedPacket = CreateService().BuildPacket(withChange);

        var baseCandidate = Assert.Single(basePacket.EscalationCandidates);
        var changedCandidate = Assert.Single(changedPacket.EscalationCandidates);

        Assert.Equal("NeedsReview", baseCandidate.SupportReadiness);
        Assert.Equal("Monitor", changedCandidate.SupportReadiness);
        Assert.True(changedCandidate.SupportReadinessScore < baseCandidate.SupportReadinessScore);
        Assert.NotNull(changedCandidate.RecentChangeContext);
        Assert.True(string.Join(" ", changedCandidate.QualificationNotes).Contains("plausible local cause", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void BuildPacket_SummaryCountsIncludeAllSeverityBackedFindings()
    {
        var report = new AuditReportData
        {
            GeneratedAt = RunCompletedUtc,
            RunStartedAtUtc = RunStartedUtc,
            RunCompletedAtUtc = RunCompletedUtc,
            OrgRegion = "us-east-1",
            SiteTopologyFindings = [EdgeOfflineFinding("edge-1", "Edge One")],
            IvrFlowBindingFindings = [MissingOpenHoursFlow("ivr-1", "Main IVR")],
            PromptHygieneFindings =
            [
                new PromptHygieneFinding(
                    PromptId: "prompt-1",
                    PromptName: "Main Greeting",
                    Description: null,
                    IsSystemPrompt: false,
                    ResourceCount: 0,
                    AffectedLanguages: "en-us",
                    FindingCode: PromptHygieneCode.NoResources,
                    Issue: "Prompt has no resources.",
                    Severity: FindingSeverity.Medium,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Upload audio.")
            ]
        };

        var packet = CreateService().BuildPacket(report);

        Assert.Equal(3, packet.Summary.TotalFindingsInRun);
        Assert.Equal(2, packet.Summary.CriticalCount);
        Assert.Equal(1, packet.Summary.MediumCount);
        Assert.Equal(2, packet.Summary.EscalationCandidateCount);
    }
}
