using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="ChangeAdjacencyAnalyzer"/> and <see cref="ActiveFindingIndex"/>
/// (Phase 2.1 — Change Adjacency Marker).
/// </summary>
public sealed class ChangeAdjacencyAnalyzerTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AuditLogFinding AuditLog(
        string entityId,
        string? entityName = null,
        string? entityType = "Queue",
        string? action = "UPDATE",
        string? userName = "audit-bot@example.invalid",
        DateTimeOffset? timestamp = null)
        => new(
            AuditId: Guid.NewGuid().ToString(),
            TimestampUtc: timestamp ?? DateTimeOffset.UtcNow.AddMinutes(-30),
            ServiceName: "routing",
            Action: action,
            UserName: userName,
            UserEmail: $"{userName}",
            EntityType: entityType,
            EntityName: entityName ?? $"Object {entityId}",
            UserId: null,
            ClientId: null,
            EntityId: entityId,
            CorrelationId: null,
            Level: null);

    private static AuditReportData ReportWith(
        IReadOnlyList<QueueServiceabilityFinding>? queueFindings = null,
        IReadOnlyList<FlowFinding>? flowFindings = null,
        IReadOnlyList<SiteTopologyFinding>? siteFindings = null,
        IReadOnlyList<GroupFinding>? groupFindings = null)
        => new()
        {
            QueueServiceabilityFindings = queueFindings ?? [],
            FlowFindings = flowFindings ?? [],
            SiteTopologyFindings = siteFindings ?? [],
            GroupFindings = groupFindings ?? []
        };

    private static QueueServiceabilityFinding QueueFinding(string queueId, string queueName)
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

    private static FlowFinding StaleFlow(string flowId, string flowName)
        => new(
            FlowId: flowId,
            FlowName: flowName,
            FlowType: "INBOUNDCALL",
            IsPublished: true,
            PublishedDate: DateTime.UtcNow.AddDays(-120),
            DateModified: DateTime.UtcNow.AddDays(-120),
            DaysSincePublished: 120,
            Issue: "Not republished in 120 days");

    private static SiteTopologyFinding SiteTopoFinding(string objectId, string objectName)
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

    // ─── ActiveFindingIndex ───────────────────────────────────────────────────

    [Fact]
    public void ActiveFindingIndex_EmptyReport_IsEmpty()
    {
        var index = ActiveFindingIndex.Build(ReportWith());

        Assert.True(index.IsEmpty);
    }

    [Fact]
    public void ActiveFindingIndex_IndexesQueueById()
    {
        var report = ReportWith(queueFindings: [QueueFinding("q-123", "ServiceDesk")]);

        var index = ActiveFindingIndex.Build(report);

        Assert.False(index.IsEmpty);
        Assert.True(index.TryGetFindingType("q-123", out var label));
        Assert.Contains("Queue", label);
    }

    [Fact]
    public void ActiveFindingIndex_IndexesQueueByName()
    {
        var report = ReportWith(queueFindings: [QueueFinding("q-123", "ServiceDesk")]);

        var index = ActiveFindingIndex.Build(report);

        Assert.True(index.TryGetFindingType("ServiceDesk", out _));
    }

    [Fact]
    public void ActiveFindingIndex_LookupIsCaseInsensitive()
    {
        var report = ReportWith(queueFindings: [QueueFinding("Q-ABC", "ServiceDesk")]);

        var index = ActiveFindingIndex.Build(report);

        Assert.True(index.TryGetFindingType("q-abc", out _));
        Assert.True(index.TryGetFindingType("servicedesk", out _));
    }

    [Fact]
    public void ActiveFindingIndex_IndexesFlowFindings()
    {
        var report = ReportWith(flowFindings: [StaleFlow("flow-1", "MainFlow")]);

        var index = ActiveFindingIndex.Build(report);

        Assert.True(index.TryGetFindingType("flow-1", out _));
        Assert.True(index.TryGetFindingType("MainFlow", out _));
    }

    [Fact]
    public void ActiveFindingIndex_IndexesSiteTopologyFindings()
    {
        var report = ReportWith(siteFindings: [SiteTopoFinding("e-999", "EdgeAlpha")]);

        var index = ActiveFindingIndex.Build(report);

        Assert.True(index.TryGetFindingType("e-999", out _));
        Assert.True(index.TryGetFindingType("EdgeAlpha", out _));
    }

    // ─── ChangeAdjacencyAnalyzer: no data ────────────────────────────────────

    [Fact]
    public void ChangeAdjacency_NoAuditLogs_ReturnsEmpty()
    {
        var index = ActiveFindingIndex.Build(ReportWith(queueFindings: [QueueFinding("q-1", "Q1")]));

        var findings = new ChangeAdjacencyAnalyzer().Analyze([], index);

        Assert.Empty(findings);
    }

    [Fact]
    public void ChangeAdjacency_EmptyFindingIndex_ReturnsEmpty()
    {
        var logs = new[] { AuditLog("q-1") };
        var index = ActiveFindingIndex.Build(ReportWith());

        var findings = new ChangeAdjacencyAnalyzer().Analyze(logs, index);

        Assert.Empty(findings);
    }

    // ─── ChangeAdjacencyAnalyzer: basic correlation ───────────────────────────

    [Fact]
    public void ChangeAdjacency_AuditLogMatchesActiveFinding_ProducesAdjacencyFinding()
    {
        var queueId = "queue-servicedesk";
        var logs = new[] { AuditLog(queueId, entityType: "Queue") };
        var report = ReportWith(queueFindings: [QueueFinding(queueId, "ServiceDesk")]);
        var index = ActiveFindingIndex.Build(report);

        var findings = new ChangeAdjacencyAnalyzer().Analyze(logs, index);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(ChangeAdjacencyCode.ChangeBeforeFinding, f.FindingCode);
        Assert.Equal(queueId, f.AffectedObjectId);
        Assert.Equal(1, f.ChangeCount);
        Assert.Contains("Queue", f.RelatedFindingType);
    }

    [Fact]
    public void ChangeAdjacency_MatchByName_WhenIdNotPresent()
    {
        // Audit log has no entity ID, only entity name
        var log = new AuditLogFinding(
            AuditId: "a1",
            TimestampUtc: DateTimeOffset.UtcNow.AddMinutes(-10),
            ServiceName: "routing",
            Action: "UPDATE",
            UserName: "admin",
            UserEmail: "audit-bot@example.invalid",
            EntityType: "Queue",
            EntityName: "ServiceDesk",
            UserId: null,
            ClientId: null,
            EntityId: null,
            CorrelationId: null,
            Level: null);
        var report = ReportWith(queueFindings: [QueueFinding("q-sd", "ServiceDesk")]);
        var index = ActiveFindingIndex.Build(report);

        var findings = new ChangeAdjacencyAnalyzer().Analyze([log], index);

        Assert.Single(findings);
        Assert.Equal(ChangeAdjacencyCode.ChangeBeforeFinding, findings[0].FindingCode);
    }

    [Fact]
    public void ChangeAdjacency_NoMatchingFinding_ReturnsEmpty()
    {
        var logs = new[] { AuditLog("unrelated-object") };
        var report = ReportWith(queueFindings: [QueueFinding("queue-1", "ServiceDesk")]);
        var index = ActiveFindingIndex.Build(report);

        var findings = new ChangeAdjacencyAnalyzer().Analyze(logs, index);

        Assert.Empty(findings);
    }

    // ─── ChangeAdjacencyAnalyzer: time window filtering ──────────────────────

    [Fact]
    public void ChangeAdjacency_OldAuditLog_ExcludedByWindow()
    {
        var queueId = "q-old";
        var oldLog = AuditLog(queueId, timestamp: DateTimeOffset.UtcNow.AddDays(-2));
        var report = ReportWith(queueFindings: [QueueFinding(queueId, "OldQueue")]);
        var index = ActiveFindingIndex.Build(report);

        // Window is 60 minutes — log is 2 days old
        var findings = new ChangeAdjacencyAnalyzer().Analyze([oldLog], index, windowMinutes: 60);

        Assert.Empty(findings);
    }

    [Fact]
    public void ChangeAdjacency_RecentAuditLog_IncludedByWindow()
    {
        var queueId = "q-recent";
        var recentLog = AuditLog(queueId, timestamp: DateTimeOffset.UtcNow.AddMinutes(-30));
        var report = ReportWith(queueFindings: [QueueFinding(queueId, "RecentQueue")]);
        var index = ActiveFindingIndex.Build(report);

        var findings = new ChangeAdjacencyAnalyzer().Analyze([recentLog], index, windowMinutes: 60);

        Assert.Single(findings);
    }

    // ─── ChangeAdjacencyAnalyzer: repeated changes ────────────────────────────

    [Fact]
    public void ChangeAdjacency_ThreeOrMoreChanges_FlagsRepeatedChanges()
    {
        var queueId = "q-churn";
        var logs = Enumerable.Range(0, 3)
            .Select(i => AuditLog(queueId, timestamp: DateTimeOffset.UtcNow.AddMinutes(-i * 5)))
            .ToArray();
        var report = ReportWith(queueFindings: [QueueFinding(queueId, "ChurnQueue")]);
        var index = ActiveFindingIndex.Build(report);

        var findings = new ChangeAdjacencyAnalyzer().Analyze(logs, index);

        Assert.Single(findings);
        Assert.Equal(ChangeAdjacencyCode.RepeatedChanges, findings[0].FindingCode);
        Assert.Equal(3, findings[0].ChangeCount);
        Assert.Equal(FindingSeverity.Medium, findings[0].Severity);
    }

    [Fact]
    public void ChangeAdjacency_TwoChanges_FlagsChangeBeforeFinding()
    {
        var queueId = "q-two";
        var logs = new[]
        {
            AuditLog(queueId, timestamp: DateTimeOffset.UtcNow.AddMinutes(-10)),
            AuditLog(queueId, timestamp: DateTimeOffset.UtcNow.AddMinutes(-20))
        };
        var report = ReportWith(queueFindings: [QueueFinding(queueId, "TwoQueue")]);
        var index = ActiveFindingIndex.Build(report);

        var findings = new ChangeAdjacencyAnalyzer().Analyze(logs, index);

        Assert.Single(findings);
        // 2 changes is below the RepeatedChanges threshold of 3
        Assert.Equal(ChangeAdjacencyCode.ChangeBeforeFinding, findings[0].FindingCode);
        Assert.Equal(2, findings[0].ChangeCount);
    }

    // ─── ChangeAdjacencyAnalyzer: output fields ───────────────────────────────

    [Fact]
    public void ChangeAdjacency_PopulatesChangedByFromUserName()
    {
        var queueId = "q-1";
        var log = AuditLog(queueId, userName: "queue-owner-01@example.invalid");
        var report = ReportWith(queueFindings: [QueueFinding(queueId, "Queue1")]);
        var index = ActiveFindingIndex.Build(report);

        var findings = new ChangeAdjacencyAnalyzer().Analyze([log], index);

        Assert.Single(findings);
        Assert.Equal("queue-owner-01@example.invalid", findings[0].ChangedBy);
    }

    [Fact]
    public void ChangeAdjacency_ResultsOrderedBySeverityThenTimestamp()
    {
        var queueId1 = "q-1";
        var queueId2 = "q-2";
        var queueId3 = "q-3";

        // Three repeated changes on q-1 → RepeatedChanges (Medium)
        var logs = Enumerable.Range(0, 3)
            .Select(i => AuditLog(queueId1, timestamp: DateTimeOffset.UtcNow.AddMinutes(-i)))
            .Concat([
                // Single change on q-2 (recent) and q-3 (older)
                AuditLog(queueId2, timestamp: DateTimeOffset.UtcNow.AddMinutes(-5)),
                AuditLog(queueId3, timestamp: DateTimeOffset.UtcNow.AddMinutes(-60))
            ])
            .ToArray();

        var report = ReportWith(queueFindings:
        [
            QueueFinding(queueId1, "Q1"),
            QueueFinding(queueId2, "Q2"),
            QueueFinding(queueId3, "Q3")
        ]);
        var index = ActiveFindingIndex.Build(report);

        var findings = new ChangeAdjacencyAnalyzer().Analyze(logs, index).ToList();

        Assert.Equal(3, findings.Count);
        // Medium severity (RepeatedChanges) comes first — Medium(2) < Info(4) in FindingSeverity enum
        Assert.Equal(ChangeAdjacencyCode.RepeatedChanges, findings[0].FindingCode);
        // Then Info findings ordered by timestamp descending (q-2 more recent than q-3)
        Assert.Equal(ChangeAdjacencyCode.ChangeBeforeFinding, findings[1].FindingCode);
        Assert.Equal(ChangeAdjacencyCode.ChangeBeforeFinding, findings[2].FindingCode);
    }
}
