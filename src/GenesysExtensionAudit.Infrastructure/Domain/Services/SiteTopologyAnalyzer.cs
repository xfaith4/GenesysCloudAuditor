using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Analyzes site–edge–trunk topology data to detect anomalies:
/// <list type="bullet">
///   <item>Sites with no online edges — all inbound PSTN traffic will fail.</item>
///   <item>Edges that are offline — stations and trunks on those edges cannot function.</item>
///   <item>Edges that reference a site not found in the site list — the edge is orphaned.</item>
///   <item>Trunks hosted on offline edges — even if the trunk state is UP, calls cannot pass.</item>
///   <item>Trunks that are administratively disabled or not in service.</item>
///   <item>Trunks reporting a DOWN or UNKNOWN operational state.</item>
/// </list>
/// Uses only already-fetched site, edge, and trunk data — no additional API calls.
/// Silently returns empty if the tenant has no edge-based telephony (cloud-only).
/// </summary>
public sealed class SiteTopologyAnalyzer
{
    /// <summary>
    /// Analyzes the provided sites, edges, and trunks for topology anomalies.
    /// Returns an empty list if all three collections are empty (cloud-only tenant).
    /// </summary>
    public IReadOnlyList<SiteTopologyFinding> Analyze(
        IReadOnlyList<SiteDto> sites,
        IReadOnlyList<EdgeDto> edges,
        IReadOnlyList<TrunkDto> trunks)
    {
        var findings = new List<SiteTopologyFinding>();

        if (sites.Count == 0 && edges.Count == 0 && trunks.Count == 0)
            return findings;

        // Build fast lookup: site ID → site
        var siteById = sites
            .Where(s => s.Id is not null)
            .ToDictionary(s => s.Id!, s => s, StringComparer.OrdinalIgnoreCase);

        // Build fast lookup: edge ID → edge
        var edgeById = edges
            .Where(e => e.Id is not null)
            .ToDictionary(e => e.Id!, e => e, StringComparer.OrdinalIgnoreCase);

        // ── Check 1: Edges that reference a site not in the site list (orphaned) ──
        foreach (var edge in edges.Where(e => e.Id is not null))
        {
            var siteRefId = edge.Site?.Id;
            if (siteRefId is not null && !siteById.ContainsKey(siteRefId))
            {
                findings.Add(new SiteTopologyFinding(
                    FindingCode: SiteTopologyCode.EdgeOrphanedSite,
                    ObjectType: "Edge",
                    ObjectId: edge.Id,
                    ObjectName: edge.Name,
                    SiteId: siteRefId,
                    SiteName: edge.Site?.Name,
                    EdgeId: edge.Id,
                    EdgeName: edge.Name,
                    TrunkState: null,
                    Issue: $"Edge '{edge.Name ?? edge.Id}' references site '{edge.Site?.Name ?? siteRefId}' " +
                           "which does not appear in the site list. The site may have been deleted.",
                    Severity: FindingSeverity.High,
                    Category: FindingCategory.LocalConfigFix,
                    RecommendedAction: "Reassign this edge to an existing site, or restore the deleted site configuration."));
            }
        }

        // ── Check 2: Edges that are offline ─────────────────────────────────────
        foreach (var edge in edges.Where(e => e.Id is not null))
        {
            var isOffline = string.Equals(edge.OnlineStatus, "OFFLINE", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(edge.OnlineStatus, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
            if (!isOffline) continue;

            siteById.TryGetValue(edge.Site?.Id ?? "", out var parentSite);

            findings.Add(new SiteTopologyFinding(
                FindingCode: SiteTopologyCode.EdgeOffline,
                ObjectType: "Edge",
                ObjectId: edge.Id,
                ObjectName: edge.Name,
                SiteId: edge.Site?.Id,
                SiteName: edge.Site?.Name ?? parentSite?.Name,
                EdgeId: edge.Id,
                EdgeName: edge.Name,
                TrunkState: null,
                Issue: $"Edge '{edge.Name ?? edge.Id}' is reporting online status '{edge.OnlineStatus}'. " +
                       "Stations and trunks hosted on this edge cannot carry calls.",
                Severity: FindingSeverity.Critical,
                Category: FindingCategory.EscalateToGenesysCare,
                RecommendedAction: "Check edge connectivity and hardware status. If the edge is expected to be online, " +
                                   "escalate to Genesys Care with this finding and the edge ID."));
        }

        // ── Check 3: Sites with no online edges ──────────────────────────────────
        // Build per-site list of online edges for quick reference
        var onlineEdgesBySite = edges
            .Where(e => e.Id is not null && e.Site?.Id is not null
                     && string.Equals(e.OnlineStatus, "ONLINE", StringComparison.OrdinalIgnoreCase))
            .GroupBy(e => e.Site!.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        var siteEdgesBySite = edges
            .Where(e => e.Id is not null && e.Site?.Id is not null)
            .GroupBy(e => e.Site!.Id!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.OrdinalIgnoreCase);

        foreach (var site in sites.Where(s => s.Id is not null))
        {
            // Only check sites that have at least one edge assigned — pure cloud sites have none
            siteEdgesBySite.TryGetValue(site.Id!, out var allSiteEdges);
            if (allSiteEdges is null || allSiteEdges.Count == 0) continue;

            onlineEdgesBySite.TryGetValue(site.Id!, out var onlineEdges);
            if ((onlineEdges?.Count ?? 0) == 0)
            {
                findings.Add(new SiteTopologyFinding(
                    FindingCode: SiteTopologyCode.SiteNoActiveEdges,
                    ObjectType: "Site",
                    ObjectId: site.Id,
                    ObjectName: site.Name,
                    SiteId: site.Id,
                    SiteName: site.Name,
                    EdgeId: null,
                    EdgeName: null,
                    TrunkState: null,
                    Issue: $"Site '{site.Name ?? site.Id}' has {allSiteEdges.Count} edge(s) assigned but none are currently online. " +
                           "All PSTN inbound and outbound traffic through this site will fail.",
                    Severity: FindingSeverity.Critical,
                    Category: FindingCategory.EscalateToGenesysCare,
                    RecommendedAction: "Investigate all edges assigned to this site. If all edges are offline, " +
                                       "escalate immediately to Genesys Care with site ID and edge IDs."));
            }
        }

        // ── Check 4: Trunks on offline edges ─────────────────────────────────────
        foreach (var trunk in trunks.Where(t => t.Id is not null && t.Edge?.Id is not null))
        {
            if (!edgeById.TryGetValue(trunk.Edge!.Id!, out var hostEdge)) continue;

            var edgeOffline = string.Equals(hostEdge.OnlineStatus, "OFFLINE", StringComparison.OrdinalIgnoreCase)
                           || string.Equals(hostEdge.OnlineStatus, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
            if (!edgeOffline) continue;

            findings.Add(new SiteTopologyFinding(
                FindingCode: SiteTopologyCode.TrunkEdgeOffline,
                ObjectType: "Trunk",
                ObjectId: trunk.Id,
                ObjectName: trunk.Name,
                SiteId: hostEdge.Site?.Id,
                SiteName: hostEdge.Site?.Name,
                EdgeId: hostEdge.Id,
                EdgeName: hostEdge.Name,
                TrunkState: trunk.TrunkState,
                Issue: $"Trunk '{trunk.Name ?? trunk.Id}' is hosted on edge '{hostEdge.Name ?? hostEdge.Id}' " +
                       $"which is currently offline ({hostEdge.OnlineStatus}). The trunk cannot carry calls regardless of its own state.",
                Severity: FindingSeverity.Critical,
                Category: FindingCategory.EscalateToGenesysCare,
                RecommendedAction: "Restore the host edge to bring this trunk back into service."));
        }

        // ── Check 5: Trunks that are disabled or out of service ───────────────────
        foreach (var trunk in trunks.Where(t => t.Id is not null))
        {
            // Only flag trunks whose host edge is online (offline-edge case already covered above)
            if (trunk.Edge?.Id is not null && edgeById.TryGetValue(trunk.Edge.Id, out var hostEdge2))
            {
                var edgeOk = string.Equals(hostEdge2.OnlineStatus, "ONLINE", StringComparison.OrdinalIgnoreCase);
                if (!edgeOk) continue;
            }

            var disabled = trunk.Enabled == false || trunk.InService == false;
            if (!disabled) continue;

            findings.Add(new SiteTopologyFinding(
                FindingCode: SiteTopologyCode.TrunkOutOfService,
                ObjectType: "Trunk",
                ObjectId: trunk.Id,
                ObjectName: trunk.Name,
                SiteId: trunk.Edge?.Id is not null && edgeById.TryGetValue(trunk.Edge.Id, out var he) ? he.Site?.Id : null,
                SiteName: trunk.Edge?.Id is not null && edgeById.TryGetValue(trunk.Edge.Id, out var he2) ? he2.Site?.Name : null,
                EdgeId: trunk.Edge?.Id,
                EdgeName: trunk.Edge?.Name,
                TrunkState: trunk.TrunkState,
                Issue: $"Trunk '{trunk.Name ?? trunk.Id}' is administratively " +
                       (trunk.Enabled == false ? "disabled" : "out of service") +
                       ". Calls cannot be routed through this trunk.",
                Severity: FindingSeverity.High,
                Category: FindingCategory.LocalConfigFix,
                RecommendedAction: "Enable or return the trunk to service if it is intentionally active, " +
                                   "or decommission it from the platform if it is no longer needed."));
        }

        // ── Check 6: Trunks that are DOWN or UNKNOWN (and enabled/in-service) ─────
        foreach (var trunk in trunks.Where(t => t.Id is not null && t.Enabled != false && t.InService != false))
        {
            var isDown = string.Equals(trunk.TrunkState, "DOWN", StringComparison.OrdinalIgnoreCase)
                      || string.Equals(trunk.TrunkState, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
            if (!isDown) continue;

            // Skip if host edge is already offline (covered above)
            if (trunk.Edge?.Id is not null && edgeById.TryGetValue(trunk.Edge.Id, out var hostEdge3))
            {
                var edgeOffline2 = string.Equals(hostEdge3.OnlineStatus, "OFFLINE", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(hostEdge3.OnlineStatus, "UNKNOWN", StringComparison.OrdinalIgnoreCase);
                if (edgeOffline2) continue;
            }

            findings.Add(new SiteTopologyFinding(
                FindingCode: SiteTopologyCode.TrunkDown,
                ObjectType: "Trunk",
                ObjectId: trunk.Id,
                ObjectName: trunk.Name,
                SiteId: trunk.Edge?.Id is not null && edgeById.TryGetValue(trunk.Edge.Id, out var heSite) ? heSite.Site?.Id : null,
                SiteName: trunk.Edge?.Id is not null && edgeById.TryGetValue(trunk.Edge.Id, out var heSite2) ? heSite2.Site?.Name : null,
                EdgeId: trunk.Edge?.Id,
                EdgeName: trunk.Edge?.Name,
                TrunkState: trunk.TrunkState,
                Issue: $"Trunk '{trunk.Name ?? trunk.Id}' is enabled and in service but reporting state '{trunk.TrunkState}'. " +
                       "Calls routed to this trunk will fail or behave unpredictably.",
                Severity: FindingSeverity.High,
                Category: FindingCategory.EscalateToGenesysCare,
                RecommendedAction: "Check carrier connectivity. If the state persists after carrier confirmation, " +
                                   "escalate to Genesys Care with trunk ID and current state."));
        }

        return findings
            .OrderByDescending(f => f.Severity)
            .ThenBy(f => f.SiteName ?? "", StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.ObjectName ?? "", StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
