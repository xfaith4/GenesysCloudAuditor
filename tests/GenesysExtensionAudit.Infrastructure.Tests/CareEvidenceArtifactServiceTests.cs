using System.Text;
using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class CareEvidenceArtifactServiceTests
{
    [Fact]
    public void BuildJson_IncludesExplainabilityFields()
    {
        var packet = SamplePacket();

        var json = Encoding.UTF8.GetString(new CareEvidenceArtifactService().BuildJson(packet));

        Assert.Contains("\"dependencyChain\"", json, StringComparison.Ordinal);
        Assert.Contains("\"evidenceChain\"", json, StringComparison.Ordinal);
        Assert.Contains("\"whyThisMatters\"", json, StringComparison.Ordinal);

        using var document = JsonDocument.Parse(json);
        var candidate = document.RootElement.GetProperty("escalationCandidates")[0];
        Assert.Equal("Site Main Site -> Edge Edge Alpha -> Hosted Telephony Resources", candidate.GetProperty("dependencyChain").GetString());
        Assert.Equal("Anything hosted on the offline edge can degrade at once.", candidate.GetProperty("whyThisMatters").GetString());
    }

    [Fact]
    public void BuildHtml_RendersSummaryAndEvidenceSections()
    {
        var report = new AuditReportData
        {
            GeneratedAt = new DateTimeOffset(2026, 04, 02, 15, 00, 00, TimeSpan.Zero),
            OrgRegion = "us-east-1"
        };

        var html = Encoding.UTF8.GetString(new CareEvidenceArtifactService().BuildHtml(report, SamplePacket()));

        Assert.Contains("<!DOCTYPE html>", html, StringComparison.Ordinal);
        Assert.Contains("Genesys Cloud Audit Summary", html, StringComparison.Ordinal);
        Assert.Contains("Escalation Overview", html, StringComparison.Ordinal);
        Assert.Contains("Evidence Chains", html, StringComparison.Ordinal);
        Assert.Contains("Site Main Site -&gt; Edge Edge Alpha -&gt; Hosted Telephony Resources", html, StringComparison.Ordinal);
        Assert.Contains("No recent correlated admin change", html, StringComparison.Ordinal);
    }

    private static CareEvidencePacket SamplePacket() => new()
    {
        GeneratedUtc = new DateTimeOffset(2026, 04, 02, 15, 00, 00, TimeSpan.Zero),
        OrgRegion = "us-east-1",
        AuditDurationSeconds = 123,
        Summary = new CareEvidenceSummary
        {
            TotalFindingsInRun = 3,
            CriticalCount = 1,
            HighCount = 1,
            EscalationCandidateCount = 1,
            ReadyForCareCount = 1
        },
        EscalationCandidates =
        [
            new CareEscalationCandidate
            {
                CandidateId = "cand-1",
                Domain = "Site Topology",
                FindingCode = "EDGE_OFFLINE",
                Severity = "Critical",
                Category = "EscalateToGenesysCare",
                Confidence = "High",
                SuspectedOwner = "Telephony Engineering",
                ProbableCauseCategory = "Edge or infrastructure outage",
                BlastRadius = "Inbound and outbound calling",
                SupportReadiness = "Ready",
                SupportReadinessScore = 94,
                AffectedObjectId = "edge-1",
                AffectedObjectName = "Edge Alpha",
                AffectedObjectType = "Edge",
                RelatedObjectIds = ["site-1"],
                RelatedObjectNames = ["Main Site"],
                DependencyChain = "Site Main Site -> Edge Edge Alpha -> Hosted Telephony Resources",
                ApiSurfaces = ["GET /api/v2/telephony/providers/edges/sites"],
                EvidenceChain =
                [
                    "GET /api/v2/telephony/providers/edges/sites returned the site inventory.",
                    "Comparison result: Edge Alpha is offline."
                ],
                WhyThisMatters = "Anything hosted on the offline edge can degrade at once.",
                RecentChangeContext = null,
                QualificationNotes = ["No recent correlated admin change was found in the audit-log window."],
                EvidenceSummary = "Edge Alpha is offline.",
                SuggestedCaseText = "Open a care case.",
                RecommendedAction = "Escalate to support.",
                WorkbookSheet = "Site_Topology"
            }
        ]
    };
}
