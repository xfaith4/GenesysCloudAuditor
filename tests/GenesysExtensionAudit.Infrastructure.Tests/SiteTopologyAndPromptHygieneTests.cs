using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="SiteTopologyAnalyzer"/> (Phase 1.5)
/// and <see cref="PromptHygieneAnalyzer"/> (Prompt Hygiene).
/// </summary>
public sealed class SiteTopologyAndPromptHygieneTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static SiteDto Site(string id, string name = "Site A")
        => new() { Id = id, Name = name };

    private static EdgeDto Edge(
        string id,
        string? siteId = null,
        string onlineStatus = "ONLINE",
        string? siteName = null)
        => new()
        {
            Id = id,
            Name = $"Edge {id}",
            OnlineStatus = onlineStatus,
            Site = siteId is null ? null : new EdgeSiteRefDto { Id = siteId, Name = siteName ?? $"Site {siteId}" }
        };

    private static TrunkDto Trunk(
        string id,
        string? edgeId = null,
        string trunkState = "UP",
        bool enabled = true,
        bool inService = true)
        => new()
        {
            Id = id,
            Name = $"Trunk {id}",
            Edge = edgeId is null ? null : new TrunkEdgeRefDto { Id = edgeId, Name = $"Edge {edgeId}" },
            TrunkState = trunkState,
            Enabled = enabled,
            InService = inService
        };

    private static PromptDto Prompt(
        string id,
        string name = "Test Prompt",
        bool isSystem = false,
        IReadOnlyList<PromptResourceDto>? resources = null)
        => new()
        {
            Id = id,
            Name = name,
            SystemPrompt = isSystem,
            Resources = resources?.ToList()
        };

    private static PromptResourceDto Resource(
        string language,
        string? mediaUri = null,
        string? ttsString = null)
        => new() { Language = language, MediaUri = mediaUri, TtsString = ttsString };

    // ─── SiteTopologyAnalyzer: empty input ────────────────────────────────

    [Fact]
    public void SiteTopology_EmptyInput_ReturnsNoFindings()
    {
        var findings = new SiteTopologyAnalyzer().Analyze([], [], []);

        Assert.Empty(findings);
    }

    // ─── SiteTopologyAnalyzer: healthy topology ───────────────────────────

    [Fact]
    public void SiteTopology_AllOnlineEdgesWithSite_ReturnsNoFindings()
    {
        var site = Site("site-1");
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "ONLINE");
        var trunk = Trunk("t-1", edgeId: "e-1", trunkState: "UP");

        var findings = new SiteTopologyAnalyzer().Analyze([site], [edge], [trunk]);

        Assert.Empty(findings);
    }

    // ─── SiteTopologyAnalyzer: EdgeOrphanedSite ───────────────────────────

    [Fact]
    public void SiteTopology_EdgeReferencesDeletedSite_FlagsOrphanedEdge()
    {
        var edge = Edge("e-1", siteId: "site-deleted");

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], []);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(SiteTopologyCode.EdgeOrphanedSite, f.FindingCode);
        Assert.Equal("Edge", f.ObjectType);
        Assert.Equal("e-1", f.ObjectId);
        Assert.Equal(FindingSeverity.High, f.Severity);
    }

    [Fact]
    public void SiteTopology_EdgeWithNoSiteRef_NotFlaggedAsOrphaned()
    {
        // Edge with no site reference — not orphaned, just unassigned
        var edge = Edge("e-1", siteId: null, onlineStatus: "ONLINE");

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], []);

        Assert.DoesNotContain(findings, f => f.FindingCode == SiteTopologyCode.EdgeOrphanedSite);
    }

    // ─── SiteTopologyAnalyzer: EdgeOffline ────────────────────────────────

    [Fact]
    public void SiteTopology_EdgeOffline_FlagsAsCritical()
    {
        var site = Site("site-1");
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "OFFLINE");

        var findings = new SiteTopologyAnalyzer().Analyze([site], [edge], []);

        Assert.Contains(findings, f =>
            f.FindingCode == SiteTopologyCode.EdgeOffline &&
            f.Severity == FindingSeverity.Critical);
    }

    [Fact]
    public void SiteTopology_EdgeUnknownStatus_FlaggedAsOffline()
    {
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "UNKNOWN");

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], []);

        Assert.Contains(findings, f => f.FindingCode == SiteTopologyCode.EdgeOffline);
    }

    // ─── SiteTopologyAnalyzer: SiteNoActiveEdges ─────────────────────────

    [Fact]
    public void SiteTopology_SiteWithAllOfflineEdges_FlagsNoActiveEdges()
    {
        var site = Site("site-1");
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "OFFLINE");

        var findings = new SiteTopologyAnalyzer().Analyze([site], [edge], []);

        Assert.Contains(findings, f =>
            f.FindingCode == SiteTopologyCode.SiteNoActiveEdges &&
            f.SiteId == "site-1" &&
            f.Severity == FindingSeverity.Critical);
    }

    [Fact]
    public void SiteTopology_SiteWithNoEdgesAtAll_NotFlaggedForNoActiveEdges()
    {
        // Pure cloud site with no edges — should NOT trigger SiteNoActiveEdges
        var site = Site("site-cloud");

        var findings = new SiteTopologyAnalyzer().Analyze([site], [], []);

        Assert.DoesNotContain(findings, f => f.FindingCode == SiteTopologyCode.SiteNoActiveEdges);
    }

    [Fact]
    public void SiteTopology_SiteWithOneOnlineOneOfflineEdge_NotFlaggedForNoActiveEdges()
    {
        var site = Site("site-1");
        var edgeOnline = Edge("e-online", siteId: "site-1", onlineStatus: "ONLINE");
        var edgeOffline = Edge("e-offline", siteId: "site-1", onlineStatus: "OFFLINE");

        var findings = new SiteTopologyAnalyzer().Analyze([site], [edgeOnline, edgeOffline], []);

        Assert.DoesNotContain(findings, f => f.FindingCode == SiteTopologyCode.SiteNoActiveEdges);
        // But the offline edge itself should be flagged
        Assert.Contains(findings, f => f.FindingCode == SiteTopologyCode.EdgeOffline);
    }

    // ─── SiteTopologyAnalyzer: TrunkEdgeOffline ──────────────────────────

    [Fact]
    public void SiteTopology_TrunkOnOfflineEdge_FlagsAsCritical()
    {
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "OFFLINE");
        var trunk = Trunk("t-1", edgeId: "e-1", trunkState: "UP");

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], [trunk]);

        Assert.Contains(findings, f =>
            f.FindingCode == SiteTopologyCode.TrunkEdgeOffline &&
            f.ObjectId == "t-1" &&
            f.Severity == FindingSeverity.Critical);
    }

    [Fact]
    public void SiteTopology_TrunkOnOnlineEdge_NotFlaggedForOfflineEdge()
    {
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "ONLINE");
        var trunk = Trunk("t-1", edgeId: "e-1", trunkState: "UP");

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], [trunk]);

        Assert.DoesNotContain(findings, f => f.FindingCode == SiteTopologyCode.TrunkEdgeOffline);
    }

    // ─── SiteTopologyAnalyzer: TrunkOutOfService ─────────────────────────

    [Fact]
    public void SiteTopology_TrunkDisabled_FlaggedAsOutOfService()
    {
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "ONLINE");
        var trunk = Trunk("t-1", edgeId: "e-1", enabled: false);

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], [trunk]);

        Assert.Contains(findings, f =>
            f.FindingCode == SiteTopologyCode.TrunkOutOfService &&
            f.Severity == FindingSeverity.High);
    }

    [Fact]
    public void SiteTopology_TrunkNotInService_FlaggedAsOutOfService()
    {
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "ONLINE");
        var trunk = Trunk("t-1", edgeId: "e-1", inService: false);

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], [trunk]);

        Assert.Contains(findings, f => f.FindingCode == SiteTopologyCode.TrunkOutOfService);
    }

    [Fact]
    public void SiteTopology_TrunkDisabledOnOfflineEdge_NotDoubleFlagged()
    {
        // Trunk is disabled AND on an offline edge.
        // TrunkEdgeOffline should be raised; TrunkOutOfService should NOT be raised
        // (offline-edge case takes priority).
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "OFFLINE");
        var trunk = Trunk("t-1", edgeId: "e-1", enabled: false);

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], [trunk]);

        Assert.Contains(findings, f => f.FindingCode == SiteTopologyCode.TrunkEdgeOffline);
        Assert.DoesNotContain(findings, f => f.FindingCode == SiteTopologyCode.TrunkOutOfService);
    }

    // ─── SiteTopologyAnalyzer: TrunkDown ─────────────────────────────────

    [Fact]
    public void SiteTopology_TrunkDown_FlaggedAsHigh()
    {
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "ONLINE");
        var trunk = Trunk("t-1", edgeId: "e-1", trunkState: "DOWN");

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], [trunk]);

        Assert.Contains(findings, f =>
            f.FindingCode == SiteTopologyCode.TrunkDown &&
            f.Severity == FindingSeverity.High);
    }

    [Fact]
    public void SiteTopology_TrunkDownOnOfflineEdge_NotDoubleFlagged()
    {
        // Trunk is DOWN AND on an offline edge.
        // TrunkEdgeOffline should be raised; TrunkDown should NOT be raised.
        var edge = Edge("e-1", siteId: "site-1", onlineStatus: "OFFLINE");
        var trunk = Trunk("t-1", edgeId: "e-1", trunkState: "DOWN");

        var findings = new SiteTopologyAnalyzer().Analyze([], [edge], [trunk]);

        Assert.Contains(findings, f => f.FindingCode == SiteTopologyCode.TrunkEdgeOffline);
        Assert.DoesNotContain(findings, f => f.FindingCode == SiteTopologyCode.TrunkDown);
    }

    // ─── PromptHygieneAnalyzer: no prompts ───────────────────────────────

    [Fact]
    public void PromptHygiene_EmptyInput_ReturnsNoFindings()
    {
        var findings = new PromptHygieneAnalyzer().Analyze([]);

        Assert.Empty(findings);
    }

    // ─── PromptHygieneAnalyzer: healthy prompt ────────────────────────────

    [Fact]
    public void PromptHygiene_PromptWithMediaUri_ReturnsNoFindings()
    {
        var prompt = Prompt("p-1", resources:
        [
            Resource("en-us", mediaUri: "https://example.com/audio.wav")
        ]);

        var findings = new PromptHygieneAnalyzer().Analyze([prompt]);

        Assert.Empty(findings);
    }

    [Fact]
    public void PromptHygiene_PromptWithTtsString_ReturnsNoFindings()
    {
        var prompt = Prompt("p-1", resources:
        [
            Resource("en-us", ttsString: "Welcome to our service.")
        ]);

        var findings = new PromptHygieneAnalyzer().Analyze([prompt]);

        Assert.Empty(findings);
    }

    [Fact]
    public void PromptHygiene_PromptWithMixedLanguages_OnlyFlagsEmptyOnes()
    {
        // en-us has media; fr-fr has nothing
        // Since NOT ALL resources are empty, NoPlayableMedia is not triggered
        var prompt = Prompt("p-1", resources:
        [
            Resource("en-us", mediaUri: "https://example.com/audio.wav"),
            Resource("fr-fr")
        ]);

        var findings = new PromptHygieneAnalyzer().Analyze([prompt]);

        Assert.Empty(findings);
    }

    // ─── PromptHygieneAnalyzer: NoResources ──────────────────────────────

    [Fact]
    public void PromptHygiene_PromptWithNullResources_FlagsNoResources()
    {
        var prompt = Prompt("p-1", name: "Welcome Prompt");

        var findings = new PromptHygieneAnalyzer().Analyze([prompt]);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(PromptHygieneCode.NoResources, f.FindingCode);
        Assert.Equal("p-1", f.PromptId);
        Assert.Equal(0, f.ResourceCount);
        Assert.Equal("(none)", f.AffectedLanguages);
        Assert.Equal(FindingSeverity.High, f.Severity);
    }

    [Fact]
    public void PromptHygiene_PromptWithEmptyResourcesList_FlagsNoResources()
    {
        var prompt = Prompt("p-1", resources: []);

        var findings = new PromptHygieneAnalyzer().Analyze([prompt]);

        Assert.Single(findings);
        Assert.Equal(PromptHygieneCode.NoResources, findings[0].FindingCode);
    }

    // ─── PromptHygieneAnalyzer: NoPlayableMedia ───────────────────────────

    [Fact]
    public void PromptHygiene_AllResourcesHaveNoMediaOrTts_FlagsNoPlayableMedia()
    {
        var prompt = Prompt("p-1", name: "Silent Prompt", resources:
        [
            Resource("en-us"),
            Resource("es-us")
        ]);

        var findings = new PromptHygieneAnalyzer().Analyze([prompt]);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal(PromptHygieneCode.NoPlayableMedia, f.FindingCode);
        Assert.Equal(2, f.ResourceCount);
        Assert.Contains("en-us", f.AffectedLanguages);
        Assert.Contains("es-us", f.AffectedLanguages);
        Assert.Equal(FindingSeverity.Medium, f.Severity);
    }

    [Fact]
    public void PromptHygiene_SystemPrompt_StillFlagged()
    {
        // System prompts with no resources should still be flagged
        var prompt = Prompt("sys-p-1", isSystem: true);

        var findings = new PromptHygieneAnalyzer().Analyze([prompt]);

        Assert.Single(findings);
        Assert.True(findings[0].IsSystemPrompt);
    }

    [Fact]
    public void PromptHygiene_PromptWithNullId_IsSkipped()
    {
        var prompt = new PromptDto { Id = null, Name = "No ID Prompt" };

        var findings = new PromptHygieneAnalyzer().Analyze([prompt]);

        Assert.Empty(findings);
    }

    [Fact]
    public void PromptHygiene_MultiplePromptsWithIssues_AllFlagged()
    {
        var prompts = new[]
        {
            Prompt("p-1"),                              // no resources
            Prompt("p-2", resources: [Resource("en-us")]),  // no media or TTS
            Prompt("p-3", resources: [Resource("en-us", mediaUri: "https://example.com/audio.wav")]) // healthy
        };

        var findings = new PromptHygieneAnalyzer().Analyze(prompts);

        Assert.Equal(2, findings.Count);
        Assert.Contains(findings, f => f.PromptId == "p-1" && f.FindingCode == PromptHygieneCode.NoResources);
        Assert.Contains(findings, f => f.PromptId == "p-2" && f.FindingCode == PromptHygieneCode.NoPlayableMedia);
    }
}
