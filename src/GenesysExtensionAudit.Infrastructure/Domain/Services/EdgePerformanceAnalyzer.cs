using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Derives per-edge operational load observations from already-fetched site, edge,
/// and operational-event data. This does not create a new API dependency; it turns
/// existing telemetry into an edge-distribution view that is useful for high-volume,
/// multi-edge sites.
/// </summary>
public sealed class EdgePerformanceAnalyzer
{
    private const int MinimumSiteConversationSample = 50;
    private const int MinimumConversationsPerExpectedEdge = 25;
    private const double UnderutilizedRatioThreshold = 0.35d;
    private const double OverloadedRatioThreshold = 1.65d;
    private const int SecondaryTrafficConversationThreshold = 25;
    private const double SecondaryTrafficShareThresholdPercent = 5d;
    private const int ElevatedErrorMinimumEvents = 20;
    private const int ElevatedErrorMinimumCount = 5;
    private const double ElevatedErrorRateThresholdPercent = 5d;

    public IReadOnlyList<EdgePerformanceObservation> Analyze(
        IReadOnlyList<SiteDto> sites,
        IReadOnlyList<EdgeDto> edges,
        IReadOnlyList<OperationalEventFinding> operationalEvents)
    {
        if (edges.Count == 0)
            return [];

        var siteById = sites
            .Where(site => !string.IsNullOrWhiteSpace(site.Id))
            .ToDictionary(site => site.Id!, site => site, StringComparer.OrdinalIgnoreCase);

        var edgeById = edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.Id))
            .ToDictionary(edge => edge.Id!, edge => edge, StringComparer.OrdinalIgnoreCase);

        var uniqueEdgeIdsByName = edges
            .Where(edge => !string.IsNullOrWhiteSpace(edge.Name) && !string.IsNullOrWhiteSpace(edge.Id))
            .GroupBy(edge => edge.Name!, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .ToDictionary(group => group.Key, group => group.First().Id!, StringComparer.OrdinalIgnoreCase);

        var eventStatsByEdgeId = BuildEventStats(edgeById, uniqueEdgeIdsByName, operationalEvents);
        var onlineEdgeIdsBySite = BuildOnlineEdgeIdsBySite(edges);
        var primaryEdgeIdsBySite = BuildSiteEdgeMap(sites, site => site.PrimaryEdges);
        var secondaryEdgeIdsBySite = BuildSiteEdgeMap(sites, site => site.SecondaryEdges);
        var expectedLoadEdgeIdsBySite = BuildExpectedLoadEdgeIdsBySite(
            edges,
            onlineEdgeIdsBySite,
            primaryEdgeIdsBySite,
            secondaryEdgeIdsBySite);

        var observations = new List<EdgePerformanceObservation>(edges.Count);

        foreach (var edge in edges.Where(edge => !string.IsNullOrWhiteSpace(edge.Id)))
        {
            var edgeId = edge.Id!;
            var siteId = edge.Site?.Id;
            siteById.TryGetValue(siteId ?? string.Empty, out var site);

            var edgeRole = DetermineEdgeRole(edgeId, siteId, primaryEdgeIdsBySite, secondaryEdgeIdsBySite);
            var isOnline = string.Equals(edge.OnlineStatus, "ONLINE", StringComparison.OrdinalIgnoreCase);
            var expectedLoadEdgeIds = siteId is not null && expectedLoadEdgeIdsBySite.TryGetValue(siteId, out var expectedIds)
                ? expectedIds
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var expectedToCarryLoad = expectedLoadEdgeIds.Contains(edgeId);
            var expectedEdgeCount = expectedLoadEdgeIds.Count;

            eventStatsByEdgeId.TryGetValue(edgeId, out var edgeStats);
            var observedConversationCount = edgeStats?.DistinctConversationCount ?? 0;
            var operationalEventCount = edgeStats?.OperationalEventCount ?? 0;
            var errorEventCount = edgeStats?.ErrorEventCount ?? 0;
            var lastEventUtc = edgeStats?.LastEventUtc;
            var errorRatePercent = operationalEventCount == 0
                ? 0d
                : (double)errorEventCount / operationalEventCount * 100d;

            var siteObservedConversationCount = siteId is not null
                ? edges.Where(candidate => string.Equals(candidate.Site?.Id, siteId, StringComparison.OrdinalIgnoreCase))
                    .Sum(candidate => eventStatsByEdgeId.TryGetValue(candidate.Id ?? string.Empty, out var stats) ? stats.DistinctConversationCount : 0)
                : observedConversationCount;

            var observedSharePercent = siteObservedConversationCount == 0
                ? 0d
                : (double)observedConversationCount / siteObservedConversationCount * 100d;
            var expectedSharePercent = expectedToCarryLoad && expectedEdgeCount > 0
                ? 100d / expectedEdgeCount
                : 0d;
            var shareDeltaPercent = observedSharePercent - expectedSharePercent;

            var sampleFloor = Math.Max(MinimumSiteConversationSample, expectedEdgeCount * MinimumConversationsPerExpectedEdge);
            var hasSufficientSample = siteObservedConversationCount >= sampleFloor;

            var status = BuildStatus(
                edgeRole,
                isOnline,
                expectedToCarryLoad,
                expectedEdgeCount,
                observedConversationCount,
                siteObservedConversationCount,
                observedSharePercent,
                expectedSharePercent,
                operationalEventCount,
                errorEventCount,
                errorRatePercent,
                hasSufficientSample,
                site?.Name ?? edge.Site?.Name,
                edge.Name ?? edgeId);

            observations.Add(new EdgePerformanceObservation(
                SiteId: siteId,
                SiteName: site?.Name ?? edge.Site?.Name,
                EdgeId: edgeId,
                EdgeName: edge.Name,
                EdgeRole: edgeRole,
                OnlineStatus: edge.OnlineStatus,
                ExpectedToCarryLoad: expectedToCarryLoad,
                FindingCode: status.FindingCode,
                StatusLabel: status.StatusLabel,
                IsAnomalous: status.IsAnomalous,
                Severity: status.Severity,
                ObservedConversationCount: observedConversationCount,
                SiteObservedConversationCount: siteObservedConversationCount,
                ExpectedEdgeCount: expectedEdgeCount,
                ObservedSharePercent: observedSharePercent,
                ExpectedSharePercent: expectedSharePercent,
                ShareDeltaPercent: shareDeltaPercent,
                OperationalEventCount: operationalEventCount,
                ErrorEventCount: errorEventCount,
                ErrorRatePercent: errorRatePercent,
                LastEventUtc: lastEventUtc,
                Issue: status.Issue,
                RecommendedAction: status.RecommendedAction));
        }

        return observations
            .OrderByDescending(observation => observation.IsAnomalous)
            .ThenBy(observation => observation.SiteName ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observation => observation.EdgeRole, StringComparer.OrdinalIgnoreCase)
            .ThenBy(observation => observation.EdgeName ?? observation.EdgeId, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static Dictionary<string, EdgeEventStats> BuildEventStats(
        IReadOnlyDictionary<string, EdgeDto> edgeById,
        IReadOnlyDictionary<string, string> uniqueEdgeIdsByName,
        IReadOnlyList<OperationalEventFinding> operationalEvents)
    {
        var stats = new Dictionary<string, EdgeEventStats>(StringComparer.OrdinalIgnoreCase);

        foreach (var operationalEvent in operationalEvents)
        {
            var edgeId = ResolveEdgeId(edgeById, uniqueEdgeIdsByName, operationalEvent);
            if (edgeId is null)
                continue;

            if (!stats.TryGetValue(edgeId, out var entry))
            {
                entry = new EdgeEventStats();
                stats[edgeId] = entry;
            }

            entry.OperationalEventCount++;
            if (!string.IsNullOrWhiteSpace(operationalEvent.ErrorCode))
                entry.ErrorEventCount++;
            if (!string.IsNullOrWhiteSpace(operationalEvent.ConversationId))
                entry.ConversationIds.Add(operationalEvent.ConversationId!);

            if (operationalEvent.TimestampUtc is { } timestamp &&
                (!entry.LastEventUtc.HasValue || timestamp > entry.LastEventUtc.Value))
            {
                entry.LastEventUtc = timestamp;
            }
        }

        return stats;
    }

    private static string? ResolveEdgeId(
        IReadOnlyDictionary<string, EdgeDto> edgeById,
        IReadOnlyDictionary<string, string> uniqueEdgeIdsByName,
        OperationalEventFinding operationalEvent)
    {
        if (!string.IsNullOrWhiteSpace(operationalEvent.EntityId) &&
            edgeById.ContainsKey(operationalEvent.EntityId))
        {
            return operationalEvent.EntityId;
        }

        if (!string.IsNullOrWhiteSpace(operationalEvent.EntityName) &&
            uniqueEdgeIdsByName.TryGetValue(operationalEvent.EntityName, out var edgeId))
        {
            return edgeId;
        }

        return null;
    }

    private static Dictionary<string, HashSet<string>> BuildOnlineEdgeIdsBySite(IReadOnlyList<EdgeDto> edges)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var edge in edges.Where(edge =>
                     !string.IsNullOrWhiteSpace(edge.Id) &&
                     !string.IsNullOrWhiteSpace(edge.Site?.Id) &&
                     string.Equals(edge.OnlineStatus, "ONLINE", StringComparison.OrdinalIgnoreCase)))
        {
            if (!map.TryGetValue(edge.Site!.Id!, out var set))
            {
                set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                map[edge.Site.Id!] = set;
            }

            set.Add(edge.Id!);
        }

        return map;
    }

    private static Dictionary<string, HashSet<string>> BuildSiteEdgeMap(
        IReadOnlyList<SiteDto> sites,
        Func<SiteDto, IReadOnlyList<SiteEdgeRefDto>?> selector)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var site in sites.Where(site => !string.IsNullOrWhiteSpace(site.Id)))
        {
            var edgeIds = selector(site)?
                .Where(edge => !string.IsNullOrWhiteSpace(edge.Id))
                .Select(edge => edge.Id!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            if (edgeIds is { Count: > 0 })
                map[site.Id!] = edgeIds;
        }

        return map;
    }

    private static Dictionary<string, HashSet<string>> BuildExpectedLoadEdgeIdsBySite(
        IReadOnlyList<EdgeDto> edges,
        IReadOnlyDictionary<string, HashSet<string>> onlineEdgeIdsBySite,
        IReadOnlyDictionary<string, HashSet<string>> primaryEdgeIdsBySite,
        IReadOnlyDictionary<string, HashSet<string>> secondaryEdgeIdsBySite)
    {
        var map = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        foreach (var grouping in edges
                     .Where(edge => !string.IsNullOrWhiteSpace(edge.Id) && !string.IsNullOrWhiteSpace(edge.Site?.Id))
                     .GroupBy(edge => edge.Site!.Id!, StringComparer.OrdinalIgnoreCase))
        {
            if (!onlineEdgeIdsBySite.TryGetValue(grouping.Key, out var onlineEdgeIds) || onlineEdgeIds.Count == 0)
                continue;

            primaryEdgeIdsBySite.TryGetValue(grouping.Key, out var primaryIds);
            secondaryEdgeIdsBySite.TryGetValue(grouping.Key, out var secondaryIds);

            var expectedIds = primaryIds is { Count: > 0 }
                ? onlineEdgeIds.Where(primaryIds.Contains).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (expectedIds.Count == 0)
            {
                expectedIds = onlineEdgeIds
                    .Where(edgeId => secondaryIds is null || !secondaryIds.Contains(edgeId))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            if (expectedIds.Count == 0)
                expectedIds = onlineEdgeIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

            map[grouping.Key] = expectedIds;
        }

        return map;
    }

    private static string DetermineEdgeRole(
        string edgeId,
        string? siteId,
        IReadOnlyDictionary<string, HashSet<string>> primaryEdgeIdsBySite,
        IReadOnlyDictionary<string, HashSet<string>> secondaryEdgeIdsBySite)
    {
        if (string.IsNullOrWhiteSpace(siteId))
            return "Unassigned";

        if (primaryEdgeIdsBySite.TryGetValue(siteId, out var primaryIds) && primaryIds.Contains(edgeId))
            return "Primary";

        if (secondaryEdgeIdsBySite.TryGetValue(siteId, out var secondaryIds) && secondaryIds.Contains(edgeId))
            return "Secondary";

        return "Assigned";
    }

    private static EdgeStatus BuildStatus(
        string edgeRole,
        bool isOnline,
        bool expectedToCarryLoad,
        int expectedEdgeCount,
        int observedConversationCount,
        int siteObservedConversationCount,
        double observedSharePercent,
        double expectedSharePercent,
        int operationalEventCount,
        int errorEventCount,
        double errorRatePercent,
        bool hasSufficientSample,
        string? siteName,
        string edgeName)
    {
        var siteLabel = string.IsNullOrWhiteSpace(siteName) ? "the site" : $"site '{siteName}'";

        if (!isOnline)
        {
            return new EdgeStatus(
                EdgePerformanceCode.Unavailable,
                "Unavailable",
                false,
                FindingSeverity.Info,
                $"Edge '{edgeName}' is not online, so distribution analysis is informational only in this view. Topology findings cover the availability risk separately.",
                "Review the topology sheet for availability findings before drawing traffic-distribution conclusions.");
        }

        if (siteObservedConversationCount == 0)
        {
            return new EdgeStatus(
                EdgePerformanceCode.InsufficientData,
                "No Matched Traffic",
                false,
                FindingSeverity.Info,
                $"No operational events matched edge '{edgeName}' during the current lookback window, so this run cannot judge load distribution for {siteLabel}.",
                "Confirm the operational-event lookback window and verify whether the tenant is emitting edge-attributable operational events.");
        }

        if (!hasSufficientSample || expectedEdgeCount <= 0)
        {
            return new EdgeStatus(
                EdgePerformanceCode.InsufficientData,
                "Insufficient Data",
                false,
                FindingSeverity.Info,
                $"Observed traffic for {siteLabel} is below the minimum sample size required to judge whether edge '{edgeName}' is balanced.",
                "Increase the operational-event lookback window or compare a higher-volume period before treating this as a load-distribution issue.");
        }

        if (!expectedToCarryLoad)
        {
            if (observedConversationCount >= SecondaryTrafficConversationThreshold ||
                observedSharePercent >= SecondaryTrafficShareThresholdPercent)
            {
                return BuildStatusWithErrorOverlay(
                    new EdgeStatus(
                        EdgePerformanceCode.SecondaryCarryingTraffic,
                        "Unexpected Secondary Load",
                        true,
                        FindingSeverity.Medium,
                        $"Secondary edge '{edgeName}' is carrying {observedConversationCount} observed conversation(s), {observedSharePercent:0.0}% of the site's observed edge traffic, while primary edges remain online.",
                        "Confirm whether the site is intentionally in failover or spilling traffic to secondary edges. If not, review edge-grouping, trunk placement, and failover settings."),
                    operationalEventCount,
                    errorEventCount,
                    errorRatePercent);
            }

            return BuildStatusWithErrorOverlay(
                new EdgeStatus(
                    EdgePerformanceCode.Balanced,
                    "Standby / Minimal Load",
                    false,
                    FindingSeverity.Info,
                    $"Secondary edge '{edgeName}' is carrying little or no observed traffic, which is expected while primary edges remain online.",
                    "No action required unless the site was expected to be in failover."),
                operationalEventCount,
                errorEventCount,
                errorRatePercent);
        }

        if (observedConversationCount == 0)
        {
            return BuildStatusWithErrorOverlay(
                new EdgeStatus(
                    EdgePerformanceCode.NoObservedTraffic,
                    "No Observed Traffic",
                    true,
                    FindingSeverity.High,
                    $"Edge '{edgeName}' is online and expected to carry load, but no observed conversations were matched to it while peers in {siteLabel} carried traffic.",
                    "Check edge assignment, trunk affinity, and failover state. If the edge should be live, compare recent changes and routing policies for traffic pinning."),
                operationalEventCount,
                errorEventCount,
                errorRatePercent);
        }

        var underloaded = observedSharePercent < expectedSharePercent * UnderutilizedRatioThreshold;
        var overloaded = observedSharePercent > expectedSharePercent * OverloadedRatioThreshold;

        if (underloaded || overloaded)
        {
            var statusLabel = underloaded ? "Underutilized" : "Overloaded";
            var issue =
                $"Edge '{edgeName}' is carrying {observedSharePercent:0.0}% of the site's observed edge traffic. " +
                $"Based on {expectedEdgeCount} expected load-bearing edge(s), the baseline share is {expectedSharePercent:0.0}%.";

            var action = underloaded
                ? "Review whether calls are pinning to other edges, whether this edge recently rejoined service, or whether trunk/route affinity excludes it."
                : "Review whether calls are pinning to this edge, whether peer edges are partially unavailable, or whether trunk/route affinity is over-concentrating load.";

            return BuildStatusWithErrorOverlay(
                new EdgeStatus(
                    EdgePerformanceCode.LoadImbalance,
                    statusLabel,
                    true,
                    FindingSeverity.Medium,
                    issue,
                    action),
                operationalEventCount,
                errorEventCount,
                errorRatePercent);
        }

        return BuildStatusWithErrorOverlay(
            new EdgeStatus(
                EdgePerformanceCode.Balanced,
                "Balanced",
                false,
                FindingSeverity.Info,
                $"Edge '{edgeName}' is carrying {observedSharePercent:0.0}% of observed edge traffic versus an expected baseline of {expectedSharePercent:0.0}%.",
                "No action required; the observed distribution is within the current heuristic tolerance band."),
            operationalEventCount,
            errorEventCount,
            errorRatePercent);
    }

    private static EdgeStatus BuildStatusWithErrorOverlay(
        EdgeStatus baseline,
        int operationalEventCount,
        int errorEventCount,
        double errorRatePercent)
    {
        var elevatedErrors =
            operationalEventCount >= ElevatedErrorMinimumEvents &&
            errorEventCount >= ElevatedErrorMinimumCount &&
            errorRatePercent >= ElevatedErrorRateThresholdPercent;

        if (!elevatedErrors)
            return baseline;

        var issueSuffix = $" The edge also has {errorEventCount} error-coded operational event(s) out of {operationalEventCount} total ({errorRatePercent:0.0}%).";
        var actionSuffix = " Review the error-coded operational events for edge-specific failures or carrier-side instability.";

        if (baseline.IsAnomalous)
        {
            return baseline with
            {
                Severity = FindingSeverity.High,
                StatusLabel = $"{baseline.StatusLabel} + Elevated Errors",
                Issue = baseline.Issue + issueSuffix,
                RecommendedAction = baseline.RecommendedAction + " " + actionSuffix
            };
        }

        return baseline with
        {
            FindingCode = EdgePerformanceCode.ElevatedErrorRate,
            StatusLabel = "Elevated Errors",
            IsAnomalous = true,
            Severity = FindingSeverity.Medium,
            Issue = $"Edge has an elevated error-coded operational-event rate: {errorEventCount} of {operationalEventCount} event(s) ({errorRatePercent:0.0}%).",
            RecommendedAction = "Inspect the edge's operational events for repeated error codes, then compare with trunk and topology status for corroboration."
        };
    }

    private sealed class EdgeEventStats
    {
        public int OperationalEventCount { get; set; }
        public int ErrorEventCount { get; set; }
        public HashSet<string> ConversationIds { get; } = new(StringComparer.OrdinalIgnoreCase);
        public DateTimeOffset? LastEventUtc { get; set; }
        public int DistinctConversationCount => ConversationIds.Count;
    }

    private sealed record EdgeStatus(
        string FindingCode,
        string StatusLabel,
        bool IsAnomalous,
        FindingSeverity Severity,
        string Issue,
        string RecommendedAction);
}
