using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="FlappingDetectionAnalyzer"/> (Phase 2.2 — Flapping and Instability Detection).
/// </summary>
public sealed class FlappingDetectionAnalyzerTests
{
    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static AuditLogFinding AuditLog(
        string entityId,
        string? entityName = null,
        string? entityType = "Queue",
        string? action = "UPDATE",
        DateTimeOffset? timestamp = null)
        => new(
            AuditId: Guid.NewGuid().ToString(),
            TimestampUtc: timestamp ?? DateTimeOffset.UtcNow.AddMinutes(-30),
            ServiceName: "routing",
            Action: action,
            UserName: "admin@example.com",
            UserEmail: "admin@example.com",
            EntityType: entityType,
            EntityName: entityName ?? $"Object {entityId}",
            UserId: null,
            ClientId: null,
            EntityId: entityId,
            CorrelationId: null,
            Level: null);

    private static IReadOnlyList<AuditLogFinding> FlappingLogs(
        string entityId,
        string entityType,
        string[] actions,
        DateTimeOffset? referenceTime = null)
    {
        var now = referenceTime ?? DateTimeOffset.UtcNow;
        return actions
            .Select((action, i) => AuditLog(entityId, entityType: entityType, action: action,
                timestamp: now.AddMinutes(-i * 10)))
            .ToList();
    }

    // ─── No data ─────────────────────────────────────────────────────────────

    [Fact]
    public void FlappingDetection_EmptyAuditLog_ReturnsEmpty()
    {
        var findings = new FlappingDetectionAnalyzer().Analyze([]);

        Assert.Empty(findings);
    }

    [Fact]
    public void FlappingDetection_AllLogsOutsideWindow_ReturnsEmpty()
    {
        var logs = new[]
        {
            AuditLog("q-1", timestamp: DateTimeOffset.UtcNow.AddDays(-3)),
            AuditLog("q-1", timestamp: DateTimeOffset.UtcNow.AddDays(-3)),
            AuditLog("q-1", timestamp: DateTimeOffset.UtcNow.AddDays(-3)),
            AuditLog("q-1", timestamp: DateTimeOffset.UtcNow.AddDays(-3)),
        };

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, windowMinutes: 60);

        Assert.Empty(findings);
    }

    [Fact]
    public void FlappingDetection_FewerThanMinChanges_ReturnsEmpty()
    {
        // 2 changes < default min of 4
        var logs = new[]
        {
            AuditLog("q-1", action: "CREATE"),
            AuditLog("q-1", action: "DELETE"),
        };

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Empty(findings);
    }

    // ─── Assignment flapping ──────────────────────────────────────────────────

    [Fact]
    public void FlappingDetection_AssignmentToggle_FlagsAssignmentFlapping()
    {
        var entityId = "user-123";
        var logs = FlappingLogs(entityId, "User",
            ["ASSIGN", "UNASSIGN", "ASSIGN", "UNASSIGN"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(FlappingCode.AssignmentFlapping, f.FindingCode);
        Assert.Equal(entityId, f.AffectedObjectId);
        Assert.Equal(4, f.ChangeCount);
        Assert.Equal(2, f.DistinctActionCount);
        Assert.Contains("ASSIGN", f.ObservedActions);
        Assert.Contains("UNASSIGN", f.ObservedActions);
    }

    [Fact]
    public void FlappingDetection_AssignmentFlapping_SeverityIsMedium()
    {
        var logs = FlappingLogs("user-abc", "User",
            ["ADD", "REMOVE", "ADD", "REMOVE"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        Assert.Equal(FindingSeverity.Medium, findings[0].Severity);
    }

    [Fact]
    public void FlappingDetection_CreateDeleteLoop_FlagsAssignmentFlapping()
    {
        var entityId = "role-xyz";
        var logs = FlappingLogs(entityId, "Role",
            ["CREATE", "DELETE", "CREATE", "DELETE"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        Assert.Equal(FlappingCode.AssignmentFlapping, findings[0].FindingCode);
    }

    // ─── Publish churn ────────────────────────────────────────────────────────

    [Fact]
    public void FlappingDetection_FlowRepublishedManyTimes_FlagsPublishChurn()
    {
        var flowId = "flow-ivr-main";
        var logs = FlappingLogs(flowId, "FLOW",
            ["PUBLISH", "PUBLISH", "PUBLISH", "PUBLISH"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(FlappingCode.PublishChurn, f.FindingCode);
        Assert.Equal(flowId, f.AffectedObjectId);
        Assert.Equal(FindingSeverity.High, f.Severity);
    }

    [Fact]
    public void FlappingDetection_FlowEntityType_CaseInsensitive_FlagsPublishChurn()
    {
        // Entity type "InboundCallFlow" should still trigger PublishChurn
        var logs = FlappingLogs("flow-1", "InboundCallFlow",
            ["UPDATE", "UPDATE", "UPDATE", "UPDATE"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        Assert.Equal(FlappingCode.PublishChurn, findings[0].FindingCode);
    }

    // ─── Resource oscillation ─────────────────────────────────────────────────

    [Fact]
    public void FlappingDetection_EdgeToggleStates_FlagsResourceOscillation()
    {
        var edgeId = "edge-001";
        var logs = FlappingLogs(edgeId, "Edge",
            ["UPDATE", "DELETE", "CREATE", "UPDATE"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(FlappingCode.ResourceOscillation, f.FindingCode);
        Assert.Equal(edgeId, f.AffectedObjectId);
        Assert.Equal(FindingSeverity.High, f.Severity);
    }

    [Fact]
    public void FlappingDetection_TrunkOscillation_FlagsResourceOscillation()
    {
        var logs = FlappingLogs("trunk-42", "Trunk",
            ["UPDATE", "CREATE", "DELETE", "UPDATE"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        Assert.Equal(FlappingCode.ResourceOscillation, findings[0].FindingCode);
    }

    [Fact]
    public void FlappingDetection_SiteOscillation_FlagsResourceOscillation()
    {
        var logs = FlappingLogs("site-main", "Site",
            ["UPDATE", "UPDATE", "DELETE", "CREATE"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        Assert.Equal(FlappingCode.ResourceOscillation, findings[0].FindingCode);
    }

    [Fact]
    public void FlappingDetection_SingleActionHighFrequency_FlagsResourceOscillation()
    {
        // Same action repeated many times — instability without a toggle pattern
        var logs = FlappingLogs("queue-service", "Queue",
            ["UPDATE", "UPDATE", "UPDATE", "UPDATE", "UPDATE"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        var f = findings[0];
        // Queue with single action type → ResourceOscillation (not AssignmentFlapping)
        Assert.Equal(FlappingCode.ResourceOscillation, f.FindingCode);
        Assert.Equal(1, f.DistinctActionCount);
    }

    // ─── Time window filtering ────────────────────────────────────────────────

    [Fact]
    public void FlappingDetection_MixedWindowEntries_OnlyRecentCountTowardFlap()
    {
        var entityId = "q-mixed";
        var now = DateTimeOffset.UtcNow;

        // 2 old logs (outside 60-min window) + 2 recent — total recent = 2, below threshold of 4
        var logs = new[]
        {
            AuditLog(entityId, action: "UPDATE", timestamp: now.AddDays(-2)),
            AuditLog(entityId, action: "UPDATE", timestamp: now.AddDays(-2)),
            AuditLog(entityId, action: "UPDATE", timestamp: now.AddMinutes(-10)),
            AuditLog(entityId, action: "UPDATE", timestamp: now.AddMinutes(-20)),
        };

        var findings = new FlappingDetectionAnalyzer()
            .Analyze(logs, windowMinutes: 60, minChangesForFlap: 4);

        Assert.Empty(findings);
    }

    [Fact]
    public void FlappingDetection_AllRecentLogs_ExceedsThreshold_ProducesFinding()
    {
        var entityId = "q-all-recent";
        var now = DateTimeOffset.UtcNow;

        var logs = Enumerable.Range(0, 4)
            .Select(i => AuditLog(entityId, action: "UPDATE",
                timestamp: now.AddMinutes(-i * 5)))
            .ToList();

        var findings = new FlappingDetectionAnalyzer()
            .Analyze(logs, windowMinutes: 60, minChangesForFlap: 4);

        Assert.Single(findings);
    }

    // ─── Output fields ────────────────────────────────────────────────────────

    [Fact]
    public void FlappingDetection_PopulatesFirstAndLastChangeTimestamps()
    {
        var entityId = "q-ts";
        var now = DateTimeOffset.UtcNow;

        var logs = new[]
        {
            AuditLog(entityId, action: "UPDATE", timestamp: now.AddMinutes(-60)),
            AuditLog(entityId, action: "DELETE", timestamp: now.AddMinutes(-40)),
            AuditLog(entityId, action: "CREATE", timestamp: now.AddMinutes(-20)),
            AuditLog(entityId, action: "UPDATE", timestamp: now.AddMinutes(-5)),
        };

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        var f = findings[0];
        // FirstChange should be the oldest event
        Assert.NotNull(f.FirstChangeUtc);
        Assert.NotNull(f.LastChangeUtc);
        Assert.True(f.FirstChangeUtc < f.LastChangeUtc);
    }

    [Fact]
    public void FlappingDetection_PopulatesObservedActions_Sorted()
    {
        var entityId = "q-actions";
        var logs = FlappingLogs(entityId, "Queue",
            ["DELETE", "CREATE", "UPDATE", "UPDATE"]);

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        var observedActions = findings[0].ObservedActions;
        // Should be sorted alphabetically
        Assert.Equal(observedActions.OrderBy(a => a).ToList(), observedActions.ToList());
    }

    // ─── Multiple objects ─────────────────────────────────────────────────────

    [Fact]
    public void FlappingDetection_MultipleFlappingObjects_AllReported()
    {
        var now = DateTimeOffset.UtcNow;

        var logs = new List<AuditLogFinding>();
        // Object 1: flow with churn
        logs.AddRange(FlappingLogs("flow-1", "Flow", ["PUBLISH", "PUBLISH", "PUBLISH", "PUBLISH"], now));
        // Object 2: edge oscillation
        logs.AddRange(FlappingLogs("edge-1", "Edge", ["UPDATE", "CREATE", "DELETE", "UPDATE"], now));

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.FindingCode == FlappingCode.PublishChurn);
        Assert.Contains(findings, f => f.FindingCode == FlappingCode.ResourceOscillation);
    }

    [Fact]
    public void FlappingDetection_ResultsOrderedBySeverityThenChangeCountDescending()
    {
        var now = DateTimeOffset.UtcNow;

        var logs = new List<AuditLogFinding>();
        // Queue (Medium severity) with 4 changes
        logs.AddRange(FlappingLogs("q-1", "Queue", ["CREATE", "DELETE", "CREATE", "DELETE"], now));
        // Flow (High severity) with 5 changes
        logs.AddRange(FlappingLogs("flow-1", "Flow", ["PUBLISH", "PUBLISH", "PUBLISH", "PUBLISH", "PUBLISH"], now));

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Equal(2, findings.Count);
        // High severity comes first (Critical=0, High=1, Medium=2 in the enum)
        Assert.Equal(FindingSeverity.High, findings[0].Severity);
        Assert.Equal(FindingSeverity.Medium, findings[1].Severity);
    }

    // ─── Match by name when ID is absent ─────────────────────────────────────

    [Fact]
    public void FlappingDetection_MatchByName_WhenEntityIdIsNull()
    {
        var now = DateTimeOffset.UtcNow;
        var logs = Enumerable.Range(0, 4)
            .Select(i => new AuditLogFinding(
                AuditId: Guid.NewGuid().ToString(),
                TimestampUtc: now.AddMinutes(-i * 5),
                ServiceName: "routing",
                Action: i % 2 == 0 ? "CREATE" : "DELETE",
                UserName: "admin",
                UserEmail: "admin@example.com",
                EntityType: "Queue",
                EntityName: "ServiceDesk",
                UserId: null,
                ClientId: null,
                EntityId: null,
                CorrelationId: null,
                Level: null))
            .ToList();

        var findings = new FlappingDetectionAnalyzer().Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findings);
        Assert.Equal("ServiceDesk", findings[0].AffectedObjectName);
        Assert.Null(findings[0].AffectedObjectId);
    }

    // ─── Custom window and threshold ─────────────────────────────────────────

    [Fact]
    public void FlappingDetection_CustomMinChanges_AffectsThreshold()
    {
        var logs = FlappingLogs("q-custom", "Queue",
            ["CREATE", "DELETE", "CREATE"]);  // 3 changes

        // With threshold 3, should flag; with threshold 4, should not
        var findingsWith3 = new FlappingDetectionAnalyzer()
            .Analyze(logs, minChangesForFlap: 3);
        var findingsWith4 = new FlappingDetectionAnalyzer()
            .Analyze(logs, minChangesForFlap: 4);

        Assert.Single(findingsWith3);
        Assert.Empty(findingsWith4);
    }
}
