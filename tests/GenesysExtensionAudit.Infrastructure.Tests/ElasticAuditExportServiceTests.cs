using System.Net;
using System.Net.Http.Headers;
using System.Text;
using GenesysExtensionAudit.Infrastructure.Configuration;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class ElasticAuditExportServiceTests
{
    [Fact]
    public async Task ExportAsync_MissingTokenEnvironmentVariable_ReturnsValidationFailure()
    {
        const string envVarName = "GENESYS_AUDIT_TEST_ELASTIC_TOKEN_MISSING";
        Environment.SetEnvironmentVariable(envVarName, null);

        var handler = new StubHttpMessageHandler((_, _) => throw new InvalidOperationException("HTTP should not be called."));
        var service = CreateService(
            new ElasticExportOptions
            {
                Enabled = true,
                EndpointUri = "https://elastic.example.local:9200",
                IndexName = "genesys-audit-findings",
                TokenEnvironmentVariableName = envVarName
            },
            handler);

        var result = await service.ExportAsync(SampleReport(), SampleCarePacket(), SampleSnapshot(), CancellationToken.None);

        Assert.False(result.Attempted);
        Assert.False(result.Succeeded);
        Assert.Contains(envVarName, result.Message, StringComparison.Ordinal);
        Assert.False(handler.WasCalled);
    }

    [Fact]
    public async Task ExportAsync_BuildsBulkRequest_WithDeterministicFindingAndSummaryDocuments()
    {
        const string envVarName = "GENESYS_AUDIT_TEST_ELASTIC_TOKEN_PRESENT";
        Environment.SetEnvironmentVariable(envVarName, "elastic-token-value");

        HttpRequestMessage? capturedRequest = null;
        string? capturedBody = null;
        var handler = new StubHttpMessageHandler(async (request, _) =>
        {
            capturedRequest = request;
            capturedBody = await request.Content!.ReadAsStringAsync().ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"errors\":false,\"items\":[{\"index\":{\"status\":201}},{\"index\":{\"status\":201}}]}", Encoding.UTF8, "application/json")
            };
        });

        var service = CreateService(
            new ElasticExportOptions
            {
                Enabled = true,
                EndpointUri = "https://elastic.example.local:9200",
                IndexName = "genesys-audit-findings",
                TokenEnvironmentVariableName = envVarName,
                IncludeRunSummaryDocument = true
            },
            handler);

        var result = await service.ExportAsync(SampleReport(), SampleCarePacket(), SampleSnapshot(), CancellationToken.None);

        Assert.True(result.Attempted);
        Assert.True(result.Succeeded);
        Assert.Equal(2, result.DocumentsAttempted);
        Assert.Equal(2, result.DocumentsSucceeded);
        Assert.NotNull(capturedRequest);
        Assert.Equal("https://elastic.example.local:9200/genesys-audit-findings/_bulk", capturedRequest!.RequestUri!.ToString());
        Assert.Equal("ApiKey", capturedRequest.Headers.Authorization!.Scheme);
        Assert.Equal("elastic-token-value", capturedRequest.Headers.Authorization.Parameter);
        Assert.NotNull(capturedBody);
        Assert.Contains("\"documentType\":\"finding\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"documentType\":\"run-summary\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"ruleVersion\":\"1.0\"", capturedBody, StringComparison.Ordinal);
        Assert.Contains("\"supportEscalationEligible\":true", capturedBody, StringComparison.Ordinal);
    }

    private static ElasticAuditExportService CreateService(ElasticExportOptions options, HttpMessageHandler handler)
        => new(
            new HttpClient(handler),
            new StaticOptionsMonitor<ElasticExportOptions>(options),
            NullLogger<ElasticAuditExportService>.Instance);

    private static AuditReportData SampleReport() => new()
    {
        RunId = "run-001",
        GeneratedAt = new DateTimeOffset(2026, 04, 03, 12, 00, 00, TimeSpan.Zero),
        OrgRegion = "us-east-1"
    };

    private static CareEvidencePacket SampleCarePacket() => new()
    {
        GeneratedUtc = new DateTimeOffset(2026, 04, 03, 12, 00, 00, TimeSpan.Zero),
        OrgRegion = "us-east-1",
        Summary = new CareEvidenceSummary
        {
            TotalFindingsInRun = 1,
            CriticalCount = 1,
            EscalationCandidateCount = 1,
            ReadyForCareCount = 1
        },
        EscalationCandidates =
        [
            new CareEscalationCandidate
            {
                CandidateId = "cand-1",
                Domain = "Site Topology",
                FindingCode = SiteTopologyCode.EdgeOffline,
                Severity = "Critical",
                Category = "EscalateToGenesysCare",
                Confidence = "High",
                SuspectedOwner = "Telephony Engineering",
                ProbableCauseCategory = "Edge or infrastructure outage",
                BlastRadius = "Inbound and outbound calling",
                SupportReadiness = "Ready",
                SupportReadinessScore = 95,
                AffectedObjectId = "edge-1",
                AffectedObjectName = "Edge Alpha",
                AffectedObjectType = "Edge",
                RecommendedAction = "Escalate to support.",
                EvidenceSummary = "Edge Alpha is offline.",
                WorkbookSheet = "Site_Topology"
            }
        ]
    };

    private static AuditSnapshotPacket SampleSnapshot() => new()
    {
        GeneratedUtc = new DateTimeOffset(2026, 04, 03, 12, 00, 00, TimeSpan.Zero),
        OrgRegion = "us-east-1",
        FindingCount = 1,
        CapturedFindingDomains = ["Site Topology"],
        Findings =
        [
            new AuditSnapshotFinding
            {
                FindingKey = "site-topology|edge-1|EDGE_OFFLINE",
                Domain = "Site Topology",
                FindingType = SiteTopologyCode.EdgeOffline,
                ObjectId = "edge-1",
                ObjectName = "Edge Alpha",
                Issue = "Edge Alpha is offline.",
                Severity = "Critical",
                FirstSeenUtc = new DateTimeOffset(2026, 04, 03, 12, 00, 00, TimeSpan.Zero),
                LastSeenUtc = new DateTimeOffset(2026, 04, 03, 12, 00, 00, TimeSpan.Zero),
                ObservationCount = 1
            }
        ],
        RelationshipCount = 0,
        CapturedRelationshipDomains = [],
        Relationships = []
    };

    private sealed class StaticOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
    {
        public T CurrentValue => currentValue;
        public T Get(string? name) => currentValue;
        public IDisposable? OnChange(Action<T, string?> listener) => null;
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        public bool WasCalled { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return callback(request, cancellationToken);
        }
    }
}
