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
        AuditRunOptions? options = null,
        IReadOnlyList<QueueServiceabilityFinding>? queueServiceabilityFindings = null,
        IReadOnlyList<SiteTopologyFinding>? siteTopologyFindings = null,
        IReadOnlyList<IvrFlowBindingFinding>? ivrFlowBindingFindings = null,
        IReadOnlyList<AuditRelationshipSnapshot>? relationshipSnapshots = null)
        => new()
        {
            GeneratedAt = generatedAt,
            RunStartedAtUtc = generatedAt.AddMinutes(-5),
            RunCompletedAtUtc = generatedAt,
            OrgRegion = "us-east-1",
            Options = options ?? new AuditRunOptions(),
            QueueServiceabilityFindings = queueServiceabilityFindings ?? [],
            SiteTopologyFindings = siteTopologyFindings ?? [],
            IvrFlowBindingFindings = ivrFlowBindingFindings ?? [],
            RelationshipSnapshots = relationshipSnapshots ?? []
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

    private static AuditRelationshipSnapshot TelephonyRelationship(string userId, string userName, string stationId)
        => new(
            Domain: "Telephony Ownership",
            RelationshipType: "UserTelephonyBinding",
            RelationshipKey: $"telephony-user|{userId}",
            ObjectType: "User",
            ObjectId: userId,
            ObjectName: userName,
            NormalizedValue: $"profileExt=1001;station={stationId};dids=13175550100;locations=loc-1",
            DisplayValue: $"Profile Extension=1001; Station={stationId}; DIDs=+13175550100; Locations=HQ");

    private static AuditRelationshipSnapshot DidRelationship(string didId, string ownerId)
        => new(
            Domain: "Telephony Ownership",
            RelationshipType: "DidOwnership",
            RelationshipKey: $"telephony-did|{didId}",
            ObjectType: "Did",
            ObjectId: didId,
            ObjectName: "+13175550100",
            NormalizedValue: $"number=13175550100;owner=USER:{ownerId};pool=pool-1",
            DisplayValue: $"Number=+13175550100; Owner=USER:{ownerId}; Pool=pool-1");

    private static AuditRelationshipSnapshot RoutingRelationship(string ivrId, string ivrName, string openFlowId)
        => new(
            Domain: "Routing Bindings",
            RelationshipType: "IvrRoutingBinding",
            RelationshipKey: $"routing-ivr|{ivrId}",
            ObjectType: "IVR",
            ObjectId: ivrId,
            ObjectName: ivrName,
            NormalizedValue: $"dnis=13175550100;schedule=sg-1;open={openFlowId};closed=flow-closed;holiday=flow-holiday",
            DisplayValue: $"DNIS=+13175550100; Schedule Group=Business Hours (sg-1); Open=Main Flow ({openFlowId}); Closed=Closed Flow (flow-closed); Holiday=Holiday Flow (flow-holiday)");

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
    public void Compare_SkippedAudit_DoesNotResolvePreviousFindingFromSkippedDomain()
    {
        var service = CreateService();

        var run1 = ReportAt(
            Run1,
            options: new AuditRunOptions { RunSiteTopologyAudit = true },
            siteTopologyFindings: [SiteFinding("edge-1", "Edge One")]);
        var previous = service.Compare(run1, null).Snapshot;

        var run2 = ReportAt(
            Run2,
            options: new AuditRunOptions { RunSiteTopologyAudit = false, RunFlowDependencyAudit = true },
            ivrFlowBindingFindings: [IvrFinding("ivr-1", "Main IVR")]);

        var result = service.Compare(run2, previous);

        Assert.DoesNotContain(result.LifecycleFindings, f => f.LifecycleStatus == FindingLifecycleStatus.Resolved && f.Domain == "Site Topology");
        var @new = Assert.Single(result.LifecycleFindings);
        Assert.Equal(FindingLifecycleStatus.New, @new.LifecycleStatus);
        Assert.Equal("IVR Flow Dependency", @new.Domain);
    }

    [Fact]
    public void Compare_LegacySnapshotWithoutRelationshipDomains_DoesNotEmitDrift()
    {
        var report = ReportAt(
            Run2,
            options: new AuditRunOptions { RunUserTelephonyAudit = true },
            relationshipSnapshots: [TelephonyRelationship("user-1", "Alice", "station-2")]);

        var previous = new AuditSnapshotPacket
        {
            SnapshotVersion = "1.0",
            GeneratedUtc = Run1,
            OrgRegion = "us-east-1",
            FindingCount = 0,
            Findings = []
        };

        var result = CreateService().Compare(report, previous);

        Assert.False(result.HistoricalDriftWasComputed);
        Assert.Empty(result.HistoricalDriftFindings);
        Assert.Single(result.Snapshot.Relationships);
    }

    [Fact]
    public void Compare_Relationships_ClassifiesChangedAddedAndRemovedDrift()
    {
        var service = CreateService();

        var previous = new AuditSnapshotPacket
        {
            SnapshotVersion = "2.0",
            GeneratedUtc = Run1,
            OrgRegion = "us-east-1",
            FindingCount = 0,
            CapturedFindingDomains = [],
            Findings = [],
            RelationshipCount = 3,
            CapturedRelationshipDomains = ["Telephony Ownership", "Routing Bindings"],
            Relationships =
            new[]
            {
                TelephonyRelationship("user-1", "Alice", "station-1"),
                DidRelationship("did-1", "user-1"),
                RoutingRelationship("ivr-1", "Main IVR", "flow-open-v1")
            }.Select(r => new AuditSnapshotRelationship
            {
                Domain = r.Domain,
                RelationshipType = r.RelationshipType,
                RelationshipKey = r.RelationshipKey,
                ObjectType = r.ObjectType,
                ObjectId = r.ObjectId,
                ObjectName = r.ObjectName,
                NormalizedValue = r.NormalizedValue,
                DisplayValue = r.DisplayValue
            }).ToList()
        };

        var report = ReportAt(
            Run2,
            options: new AuditRunOptions { RunUserTelephonyAudit = true, RunFlowDependencyAudit = true },
            relationshipSnapshots:
            [
                TelephonyRelationship("user-1", "Alice", "station-2"),
                RoutingRelationship("ivr-1", "Main IVR", "flow-open-v1"),
                RoutingRelationship("ivr-2", "Backup IVR", "flow-open-v2")
            ]);

        var result = service.Compare(report, previous);

        Assert.True(result.HistoricalDriftWasComputed);
        Assert.Equal(3, result.HistoricalDriftFindings.Count);

        var changed = Assert.Single(result.HistoricalDriftFindings, f => f.ChangeType == HistoricalDriftChangeType.Changed);
        Assert.Equal("telephony-user|user-1", changed.RelationshipKey);
        Assert.Contains("station-1", changed.PreviousValue);
        Assert.Contains("station-2", changed.CurrentValue);

        var added = Assert.Single(result.HistoricalDriftFindings, f => f.ChangeType == HistoricalDriftChangeType.Added);
        Assert.Equal("routing-ivr|ivr-2", added.RelationshipKey);

        var removed = Assert.Single(result.HistoricalDriftFindings, f => f.ChangeType == HistoricalDriftChangeType.Removed);
        Assert.Equal("telephony-did|did-1", removed.RelationshipKey);
        Assert.Null(removed.CurrentValue);
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
