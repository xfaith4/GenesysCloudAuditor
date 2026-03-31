using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GenesysExtensionAudit.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Reporting;

public interface IElasticAuditExportService
{
    Task<ElasticExportResult> ExportAsync(
        AuditReportData report,
        CareEvidencePacket carePacket,
        AuditSnapshotPacket snapshot,
        CancellationToken ct);
}

public sealed class ElasticExportResult
{
    public bool Attempted { get; init; }
    public bool Succeeded { get; init; }
    public int DocumentsAttempted { get; init; }
    public int DocumentsSucceeded { get; init; }
    public int DocumentsFailed { get; init; }
    public HttpStatusCode? StatusCode { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? ResponseDetails { get; init; }
}

public sealed class ElasticAuditExportService : IElasticAuditExportService
{
    private const string RuleVersion = "1.0";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly IOptionsMonitor<ElasticExportOptions> _optionsMonitor;
    private readonly ILogger<ElasticAuditExportService> _logger;

    public ElasticAuditExportService(
        HttpClient httpClient,
        IOptionsMonitor<ElasticExportOptions> optionsMonitor,
        ILogger<ElasticAuditExportService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _optionsMonitor = optionsMonitor ?? throw new ArgumentNullException(nameof(optionsMonitor));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ElasticExportResult> ExportAsync(
        AuditReportData report,
        CareEvidencePacket carePacket,
        AuditSnapshotPacket snapshot,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(carePacket);
        ArgumentNullException.ThrowIfNull(snapshot);

        var options = _optionsMonitor.CurrentValue;
        if (!options.TryValidate(out var validationError))
        {
            return new ElasticExportResult
            {
                Attempted = false,
                Succeeded = false,
                Message = validationError
            };
        }

        var token = Environment.GetEnvironmentVariable(options.TokenEnvironmentVariableName);
        if (string.IsNullOrWhiteSpace(token))
        {
            return new ElasticExportResult
            {
                Attempted = false,
                Succeeded = false,
                Message = $"Elastic export token environment variable '{options.TokenEnvironmentVariableName}' is not set."
            };
        }

        var documents = BuildDocuments(report, carePacket, snapshot, options.IncludeRunSummaryDocument);
        var payload = BuildBulkPayload(documents);
        var requestUri = BuildBulkUri(options.EndpointUri, options.IndexName);

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/x-ndjson")
        };
        request.Headers.Authorization = AuthenticationHeaderValue.Parse(BuildAuthorizationValue(token));

        try
        {
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            var responseText = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return new ElasticExportResult
                {
                    Attempted = true,
                    Succeeded = false,
                    DocumentsAttempted = documents.Count,
                    DocumentsFailed = documents.Count,
                    StatusCode = response.StatusCode,
                    Message = $"Elastic export failed with HTTP {(int)response.StatusCode}.",
                    ResponseDetails = Truncate(SanitizeResponseDetails(responseText), 600)
                };
            }

            var parsed = ParseBulkResponse(responseText, documents.Count);
            _logger.LogInformation(
                "Elastic export completed. Index={Index} Attempted={Attempted} Succeeded={Succeeded} Failed={Failed}",
                options.IndexName,
                parsed.DocumentsAttempted,
                parsed.DocumentsSucceeded,
                parsed.DocumentsFailed);

            return new ElasticExportResult
            {
                Attempted = true,
                Succeeded = parsed.Succeeded,
                DocumentsAttempted = parsed.DocumentsAttempted,
                DocumentsSucceeded = parsed.DocumentsSucceeded,
                DocumentsFailed = parsed.DocumentsFailed,
                StatusCode = parsed.StatusCode,
                ResponseDetails = parsed.ResponseDetails,
                Message = parsed.DocumentsFailed == 0
                    ? $"Elastic export succeeded ({parsed.DocumentsSucceeded}/{parsed.DocumentsAttempted} documents indexed)."
                    : $"Elastic export completed with partial failures ({parsed.DocumentsSucceeded}/{parsed.DocumentsAttempted} documents indexed)."
            };
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning(ex, "Elastic export request timed out.");
            return new ElasticExportResult
            {
                Attempted = true,
                Succeeded = false,
                DocumentsAttempted = documents.Count,
                DocumentsFailed = documents.Count,
                Message = $"Elastic export request timed out: {SanitizeExceptionMessage(ex.Message)}"
            };
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Elastic export request failed.");
            return new ElasticExportResult
            {
                Attempted = true,
                Succeeded = false,
                DocumentsAttempted = documents.Count,
                DocumentsFailed = documents.Count,
                Message = $"Elastic export request failed: {SanitizeExceptionMessage(ex.Message)}"
            };
        }
    }

    private static Uri BuildBulkUri(string endpointUri, string indexName)
    {
        var baseUri = endpointUri.Trim().TrimEnd('/');
        return new Uri($"{baseUri}/{indexName.Trim()}/_bulk", UriKind.Absolute);
    }

    private static string BuildAuthorizationValue(string token)
    {
        var trimmed = token.Trim();
        if (trimmed.StartsWith("ApiKey ", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        return $"ApiKey {trimmed}";
    }

    private static List<ElasticDocumentEnvelope> BuildDocuments(
        AuditReportData report,
        CareEvidencePacket carePacket,
        AuditSnapshotPacket snapshot,
        bool includeRunSummaryDocument)
    {
        var runId = report.RunId;
        var candidateMap = carePacket.EscalationCandidates
            .GroupBy(BuildCandidateLookupKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        var documents = snapshot.Findings
            .Select(finding =>
            {
                candidateMap.TryGetValue(BuildCandidateLookupKey(finding), out var candidate);
                candidate ??= carePacket.EscalationCandidates.FirstOrDefault(c => CandidateMatchesFinding(c, finding));

                var impactedObjectIds = new List<string>();
                if (!string.IsNullOrWhiteSpace(finding.ObjectId))
                    impactedObjectIds.Add(finding.ObjectId);
                if (candidate is not null)
                {
                    impactedObjectIds.AddRange(candidate.RelatedObjectIds.Where(id => !string.IsNullOrWhiteSpace(id)));
                }

                var impactedObjectNames = new List<string>();
                if (!string.IsNullOrWhiteSpace(finding.ObjectName))
                    impactedObjectNames.Add(finding.ObjectName);
                if (candidate is not null)
                {
                    impactedObjectNames.AddRange(candidate.RelatedObjectNames.Where(name => !string.IsNullOrWhiteSpace(name)));
                }

                var document = new ElasticFindingDocument
                {
                    DocumentType = "finding",
                    RunId = runId,
                    GeneratedUtc = report.GeneratedAt,
                    OrganizationIdentifier = report.OrgRegion,
                    Region = report.OrgRegion,
                    FindingId = finding.FindingKey,
                    FindingType = finding.FindingType,
                    Domain = finding.Domain,
                    Severity = finding.Severity,
                    Confidence = candidate?.Confidence,
                    ProbableCauseCategory = candidate?.ProbableCauseCategory,
                    RecommendedOwner = candidate?.SuspectedOwner,
                    RecommendedNextAction = candidate?.RecommendedAction,
                    ImpactedObjectIds = impactedObjectIds.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    ImpactedObjectNames = impactedObjectNames.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                    EvidenceSummary = candidate?.EvidenceSummary ?? finding.Issue,
                    SupportEscalationEligible = candidate is not null,
                    SupportReadiness = candidate?.SupportReadiness,
                    RuleId = finding.FindingType,
                    RuleVersion = RuleVersion,
                    WorkbookSheet = candidate?.WorkbookSheet,
                    ObservationCount = finding.ObservationCount,
                    FirstSeenUtc = finding.FirstSeenUtc,
                    LastSeenUtc = finding.LastSeenUtc
                };

                return new ElasticDocumentEnvelope(BuildDocumentId(runId, "finding", finding.FindingKey), document);
            })
            .ToList();

        if (includeRunSummaryDocument)
        {
            var summary = new ElasticRunSummaryDocument
            {
                DocumentType = "run-summary",
                RunId = runId,
                GeneratedUtc = report.GeneratedAt,
                OrganizationIdentifier = report.OrgRegion,
                Region = report.OrgRegion,
                ActiveFindingCount = snapshot.FindingCount,
                EscalationCandidateCount = carePacket.Summary.EscalationCandidateCount,
                ReadyForCareCount = carePacket.Summary.ReadyForCareCount,
                NeedsReviewCount = carePacket.Summary.NeedsReviewCount,
                MonitorCount = carePacket.Summary.MonitorCount,
                CriticalCount = carePacket.Summary.CriticalCount,
                HighCount = carePacket.Summary.HighCount,
                MediumCount = carePacket.Summary.MediumCount,
                InformationalCount = carePacket.Summary.InformationalCount,
                CapturedFindingDomains = snapshot.CapturedFindingDomains,
                RelationshipCount = snapshot.RelationshipCount,
                CapturedRelationshipDomains = snapshot.CapturedRelationshipDomains
            };

            documents.Add(new ElasticDocumentEnvelope(BuildDocumentId(runId, "summary", "run-summary"), summary));
        }

        return documents;
    }

    private static string BuildBulkPayload(IReadOnlyList<ElasticDocumentEnvelope> documents)
    {
        var builder = new StringBuilder();
        foreach (var document in documents)
        {
            builder.Append("{\"index\":{\"_id\":\"")
                .Append(document.DocumentId)
                .AppendLine("\"}}");
            builder.AppendLine(JsonSerializer.Serialize(document.Body, JsonOptions));
        }

        return builder.ToString();
    }

    private static ElasticExportResult ParseBulkResponse(string responseText, int attemptedCount)
    {
        try
        {
            using var document = JsonDocument.Parse(responseText);
            var root = document.RootElement;
            var hasErrors = root.TryGetProperty("errors", out var errorsEl) && errorsEl.GetBoolean();
            var succeeded = 0;
            var failed = 0;

            if (root.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in itemsEl.EnumerateArray())
                {
                    var operation = item.EnumerateObject().FirstOrDefault().Value;
                    if (operation.ValueKind != JsonValueKind.Object || !operation.TryGetProperty("status", out var statusEl))
                    {
                        failed++;
                        continue;
                    }

                    var status = statusEl.GetInt32();
                    if (status is >= 200 and < 300)
                        succeeded++;
                    else
                        failed++;
                }
            }
            else
            {
                succeeded = attemptedCount;
            }

            return new ElasticExportResult
            {
                Succeeded = !hasErrors && failed == 0,
                DocumentsAttempted = attemptedCount,
                DocumentsSucceeded = succeeded,
                DocumentsFailed = failed,
                ResponseDetails = hasErrors ? Truncate(SanitizeResponseDetails(responseText), 600) : null
            };
        }
        catch
        {
            return new ElasticExportResult
            {
                Succeeded = true,
                DocumentsAttempted = attemptedCount,
                DocumentsSucceeded = attemptedCount,
                DocumentsFailed = 0
            };
        }
    }

    private static bool CandidateMatchesFinding(CareEscalationCandidate candidate, AuditSnapshotFinding finding)
    {
        if (!string.Equals(candidate.FindingCode, finding.FindingType, StringComparison.OrdinalIgnoreCase))
            return false;

        if (!string.IsNullOrWhiteSpace(candidate.AffectedObjectId) && !string.IsNullOrWhiteSpace(finding.ObjectId))
            return string.Equals(candidate.AffectedObjectId, finding.ObjectId, StringComparison.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(candidate.AffectedObjectName) && !string.IsNullOrWhiteSpace(finding.ObjectName))
            return string.Equals(candidate.AffectedObjectName, finding.ObjectName, StringComparison.OrdinalIgnoreCase);

        return NormalizeForLookup(candidate.Domain) == NormalizeForLookup(finding.Domain);
    }

    private static string BuildCandidateLookupKey(CareEscalationCandidate candidate)
        => string.Join("|",
            NormalizeForLookup(candidate.Domain),
            NormalizeForLookup(candidate.FindingCode),
            NormalizeForLookup(candidate.AffectedObjectId ?? candidate.AffectedObjectName));

    private static string BuildCandidateLookupKey(AuditSnapshotFinding finding)
        => string.Join("|",
            NormalizeForLookup(finding.Domain),
            NormalizeForLookup(finding.FindingType),
            NormalizeForLookup(finding.ObjectId ?? finding.ObjectName));

    private static string NormalizeForLookup(string? value)
        => new((value ?? string.Empty)
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());

    private static string BuildDocumentId(string runId, string kind, string sourceKey)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceKey))).ToLowerInvariant();
        return $"{runId}-{kind}-{hash}";
    }

    private static string SanitizeExceptionMessage(string message)
        => Truncate(message.Replace('\r', ' ').Replace('\n', ' ').Trim(), 400);

    private static string SanitizeResponseDetails(string responseText)
        => Truncate(responseText.Replace('\r', ' ').Replace('\n', ' ').Trim(), 600);

    private static string Truncate(string? text, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var trimmed = text.Trim();
        return trimmed.Length <= maxLength ? trimmed : $"{trimmed[..maxLength]}...";
    }

    private sealed record ElasticDocumentEnvelope(string DocumentId, object Body);

    private sealed class ElasticFindingDocument
    {
        public string DocumentType { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; init; }
        public string OrganizationIdentifier { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public string FindingId { get; init; } = string.Empty;
        public string FindingType { get; init; } = string.Empty;
        public string Domain { get; init; } = string.Empty;
        public string Severity { get; init; } = string.Empty;
        public string? Confidence { get; init; }
        public string? ProbableCauseCategory { get; init; }
        public string? RecommendedOwner { get; init; }
        public string? RecommendedNextAction { get; init; }
        public IReadOnlyList<string> ImpactedObjectIds { get; init; } = [];
        public IReadOnlyList<string> ImpactedObjectNames { get; init; } = [];
        public string EvidenceSummary { get; init; } = string.Empty;
        public bool SupportEscalationEligible { get; init; }
        public string? SupportReadiness { get; init; }
        public string RuleId { get; init; } = string.Empty;
        public string RuleVersion { get; init; } = string.Empty;
        public string? WorkbookSheet { get; init; }
        public int ObservationCount { get; init; }
        public DateTimeOffset FirstSeenUtc { get; init; }
        public DateTimeOffset LastSeenUtc { get; init; }
    }

    private sealed class ElasticRunSummaryDocument
    {
        public string DocumentType { get; init; } = string.Empty;
        public string RunId { get; init; } = string.Empty;
        public DateTimeOffset GeneratedUtc { get; init; }
        public string OrganizationIdentifier { get; init; } = string.Empty;
        public string Region { get; init; } = string.Empty;
        public int ActiveFindingCount { get; init; }
        public int EscalationCandidateCount { get; init; }
        public int ReadyForCareCount { get; init; }
        public int NeedsReviewCount { get; init; }
        public int MonitorCount { get; init; }
        public int CriticalCount { get; init; }
        public int HighCount { get; init; }
        public int MediumCount { get; init; }
        public int InformationalCount { get; init; }
        public IReadOnlyList<string> CapturedFindingDomains { get; init; } = [];
        public int RelationshipCount { get; init; }
        public IReadOnlyList<string> CapturedRelationshipDomains { get; init; } = [];
    }
}
