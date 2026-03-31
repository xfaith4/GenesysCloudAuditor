using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="AuditSnapshotService"/> (Phase 4.1 snapshot persistence and lifecycle classification).
/// </summary>
public sealed class AuditSnapshotServiceTests
{
    private static readonly DateTimeOffset Run1 = new(2026, 04, 01, 10, 00, 00, TimeSpan.Zero);
    private static readonly DateTimeOffset Run2 = new(2026, 04, 02, 10, 00, 00, TimeSpan.Zero);

    private static AuditSnapshotService CreateService() => new();

    private static AuditReportData ReportAt(
        DateTimeOffset generatedAt,
        IReadOnlyList<QueueServiceabilityFinding>? queueServiceabilityFindings = null,
        IReadOnlyList<SiteTopologyFinding>? siteTopologyFindings = null,
        IReadOnlyList<IvrFlowBindingFinding>? ivrFlowBindingFindings = null)
        => new()
        {
            GeneratedAt = generatedAt,
            RunStartedAtUtc = generatedAt.AddMinutes(-5),
            RunCompletedAtUtc = generatedAt,
            OrgRegion = "us-east-1",
            QueueServiceabilityFindings = queueServiceabilityFindings ?? [],
            SiteTopologyFindings = siteTopologyFindings ?? [],
            IvrFlowBindingFindings = ivrFlowBindingFindings ?? []
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

    private static SiteTopologyFinding SiteFinding(string edgeId, string edgeName)
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
            Issue: "Edge offline",
            Severity: FindingSeverity.Critical,
            Category: FindingCategory.EscalateToGenesysCare,
            RecommendedAction: "Investigate edge.");

    private static IvrFlowBindingFinding IvrFinding(string ivrId, string ivrName)
        => new(
            IvrId: ivrId,
            IvrName: ivrName,
            Dnis: ["+13175550100"],
            BindingSlot: "OpenHours",
            BoundFlowId: null,
            BoundFlowName: null,
            FlowDaysSincePublished: null,
            FindingCode: IvrBindingCode.NoOpenHoursFlow,
            Issue: "No open-hours flow binding.",
            Severity: FindingSeverity.Critical,
            Category: FindingCategory.LocalConfigFix,
            RecommendedAction: "Assign a flow.");

    [Fact]
    public void Compare_NoPreviousSnapshot_MarksAllFindingsAsNew()
    {
        var report = ReportAt(
            Run1,
            queueServiceabilityFindings: [QueueFinding("queue-1", "Support")],
            siteTopologyFindings: [SiteFinding("edge-1", "Edge One")]);

        var result = CreateService().Compare(report, previousSnapshot: null);

        Assert.Equal(2, result.Snapshot.FindingCount);
        Assert.Equal(2, result.LifecycleFindings.Count);
        Assert.All(result.LifecycleFindings, f => Assert.Equal(FindingLifecycleStatus.New, f.LifecycleStatus));
        Assert.All(result.LifecycleFindings, f => Assert.Equal(1, f.ObservationCount));
    }

    [Fact]
    public void Compare_PreviousSnapshot_ClassifiesRecurrentAndResolvedFindings()
    {
        var service = CreateService();

        var run1 = ReportAt(
            Run1,
            queueServiceabilityFindings: [QueueFinding("queue-1", "Support")],
            siteTopologyFindings: [SiteFinding("edge-1", "Edge One")]);
        var previous = service.Compare(run1, null).Snapshot;

        var run2 = ReportAt(
            Run2,
            queueServiceabilityFindings: [QueueFinding("queue-1", "Support")],
            ivrFlowBindingFindings: [IvrFinding("ivr-1", "Main IVR")]);

        var result = service.Compare(run2, previous);

        Assert.Equal(2, result.Snapshot.FindingCount);
        Assert.Equal(3, result.LifecycleFindings.Count);

        var recurrent = Assert.Single(result.LifecycleFindings, f => f.LifecycleStatus == FindingLifecycleStatus.Recurrent);
        Assert.Equal("queue-1", recurrent.ObjectId);
        Assert.Equal(2, recurrent.ObservationCount);
        Assert.Equal(Run1, recurrent.FirstSeenUtc);
        Assert.Equal(Run2, recurrent.LastSeenUtc);

        var resolved = Assert.Single(result.LifecycleFindings, f => f.LifecycleStatus == FindingLifecycleStatus.Resolved);
        Assert.Equal("edge-1", resolved.ObjectId);
        Assert.Equal(1, resolved.ObservationCount);
        Assert.Equal(Run1, resolved.LastSeenUtc);

        var @new = Assert.Single(result.LifecycleFindings, f => f.LifecycleStatus == FindingLifecycleStatus.New);
        Assert.Equal("ivr-1", @new.ObjectId);
    }

    [Fact]
    public async Task SaveAndLoadLatestAsync_RoundTripsSnapshot()
    {
        var service = CreateService();
        var report = ReportAt(Run1, queueServiceabilityFindings: [QueueFinding("queue-1", "Support")]);
        var snapshot = service.Compare(report, null).Snapshot;

        var root = Path.Combine(Path.GetTempPath(), "genesys-audit-snapshot-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            var savedPath = await service.SaveSnapshotAsync(snapshot, root, "GenesysAudit", CancellationToken.None);
            var loaded = await service.LoadLatestAsync(root, "GenesysAudit", CancellationToken.None);

            Assert.Equal(savedPath, loaded.Path);
            Assert.NotNull(loaded.Snapshot);
            Assert.Equal(snapshot.GeneratedUtc, loaded.Snapshot!.GeneratedUtc);
            Assert.Single(loaded.Snapshot.Findings);
            Assert.Equal("queue-1", loaded.Snapshot.Findings[0].ObjectId);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }
}
