using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class EdgePerformanceAnalyzerTests
{
    [Fact]
    public void Analyze_BalancedPrimaryEdges_ReturnsBalancedObservations()
    {
        var site = Site("site-1", primaryEdgeIds: ["edge-1", "edge-2"]);
        IReadOnlyList<EdgeDto> edges =
        [
            Edge("edge-1", "Edge One", "site-1"),
            Edge("edge-2", "Edge Two", "site-1")
        ];

        var events = EventsForEdge("edge-1", "Edge One", 60)
            .Concat(EventsForEdge("edge-2", "Edge Two", 60, startOffset: 1000))
            .ToList();

        var observations = new EdgePerformanceAnalyzer().Analyze([site], edges, events);

        Assert.Equal(2, observations.Count);
        Assert.All(observations, observation =>
        {
            Assert.False(observation.IsAnomalous);
            Assert.Equal(EdgePerformanceCode.Balanced, observation.FindingCode);
            Assert.Equal("Balanced", observation.StatusLabel);
        });
    }

    [Fact]
    public void Analyze_PrimaryEdgeWithNoObservedTraffic_FlagsHighSeverityAnomaly()
    {
        var site = Site("site-1", primaryEdgeIds: ["edge-1", "edge-2", "edge-3"]);
        IReadOnlyList<EdgeDto> edges =
        [
            Edge("edge-1", "Edge One", "site-1"),
            Edge("edge-2", "Edge Two", "site-1"),
            Edge("edge-3", "Edge Three", "site-1")
        ];

        var events = EventsForEdge("edge-1", "Edge One", 90)
            .Concat(EventsForEdge("edge-2", "Edge Two", 90, startOffset: 1000))
            .ToList();

        var observations = new EdgePerformanceAnalyzer().Analyze([site], edges, events);
        var edgeThree = Assert.Single(observations, observation => observation.EdgeId == "edge-3");

        Assert.True(edgeThree.IsAnomalous);
        Assert.Equal(EdgePerformanceCode.NoObservedTraffic, edgeThree.FindingCode);
        Assert.Equal(FindingSeverity.High, edgeThree.Severity);
        Assert.Equal("No Observed Traffic", edgeThree.StatusLabel);
    }

    [Fact]
    public void Analyze_SecondaryEdgeCarryingTraffic_FlagsUnexpectedSecondaryLoad()
    {
        var site = Site(
            "site-1",
            primaryEdgeIds: ["edge-1", "edge-2"],
            secondaryEdgeIds: ["edge-3"]);
        IReadOnlyList<EdgeDto> edges =
        [
            Edge("edge-1", "Edge One", "site-1"),
            Edge("edge-2", "Edge Two", "site-1"),
            Edge("edge-3", "Edge Three", "site-1")
        ];

        var events = EventsForEdge("edge-1", "Edge One", 80)
            .Concat(EventsForEdge("edge-2", "Edge Two", 75, startOffset: 1000))
            .Concat(EventsForEdge("edge-3", "Edge Three", 30, startOffset: 2000))
            .ToList();

        var observations = new EdgePerformanceAnalyzer().Analyze([site], edges, events);
        var secondary = Assert.Single(observations, observation => observation.EdgeId == "edge-3");

        Assert.True(secondary.IsAnomalous);
        Assert.Equal(EdgePerformanceCode.SecondaryCarryingTraffic, secondary.FindingCode);
        Assert.Equal("Unexpected Secondary Load", secondary.StatusLabel);
        Assert.Equal("Secondary", secondary.EdgeRole);
    }

    private static SiteDto Site(
        string id,
        IReadOnlyList<string>? primaryEdgeIds = null,
        IReadOnlyList<string>? secondaryEdgeIds = null)
        => new()
        {
            Id = id,
            Name = $"Site {id}",
            PrimaryEdges = primaryEdgeIds?.Select(edgeId => new SiteEdgeRefDto { Id = edgeId, Name = $"Edge {edgeId}" }).ToList(),
            SecondaryEdges = secondaryEdgeIds?.Select(edgeId => new SiteEdgeRefDto { Id = edgeId, Name = $"Edge {edgeId}" }).ToList()
        };

    private static EdgeDto Edge(string id, string name, string siteId, string onlineStatus = "ONLINE")
        => new()
        {
            Id = id,
            Name = name,
            OnlineStatus = onlineStatus,
            Site = new EdgeSiteRefDto { Id = siteId, Name = $"Site {siteId}" }
        };

    private static IReadOnlyList<OperationalEventFinding> EventsForEdge(
        string edgeId,
        string edgeName,
        int conversationCount,
        int startOffset = 0)
        => Enumerable.Range(0, conversationCount)
            .Select(index => new OperationalEventFinding(
                TimestampUtc: new DateTimeOffset(2026, 04, 05, 12, 00, 00, TimeSpan.Zero).AddMinutes(startOffset + index),
                EventDefinitionId: "op-edge",
                EventDefinitionName: "edge-observation",
                EntityId: edgeId,
                EntityName: edgeName,
                CurrentValue: null,
                PreviousValue: null,
                ErrorCode: null,
                ConversationId: $"conv-{edgeId}-{startOffset + index}"))
            .ToList();
}
