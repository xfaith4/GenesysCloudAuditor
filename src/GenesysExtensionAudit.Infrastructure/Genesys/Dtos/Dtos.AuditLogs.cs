using System.Text.Json;
using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

// ─── Request DTOs ────────────────────────────────────────────────────────────

/// <summary>
/// Body sent to POST /api/v2/audits/query to start an async audit-log transaction.
/// </summary>
public sealed class AuditLogsSubmitRequestDto
{
    /// <summary>ISO 8601 interval, e.g. "2025-01-01T00:00:00Z/2025-01-02T00:00:00Z".</summary>
    [JsonPropertyName("interval")]
    public string Interval { get; init; } = string.Empty;

    /// <summary>One or more service names to include. Empty list means all services.</summary>
    [JsonPropertyName("serviceName")]
    public List<string> ServiceName { get; init; } = [];

    /// <summary>Action strings to include (e.g. CREATE, UPDATE, DELETE).</summary>
    [JsonPropertyName("action")]
    public List<string> Action { get; init; } = [];

    /// <summary>
    /// Server-side filter clauses ANDed together.
    /// Each clause specifies a property/value pair.
    /// Supported properties: action, entityType, entityId, userId, clientId.
    /// Omitted (null) when no filters are configured — avoids sending an empty array.
    /// </summary>
    [JsonPropertyName("filters")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AuditLogFilterDto>? Filters { get; init; }

    /// <summary>Sort specification. Omitted when using defaults.</summary>
    [JsonPropertyName("sort")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<AuditLogSortDto>? Sort { get; init; }
}

/// <summary>
/// A single filter clause for the audit-log query ("filters" array element).
/// </summary>
public sealed class AuditLogFilterDto
{
    /// <summary>
    /// The filterable property name.
    /// Genesys-supported values: action, entityType, entityId, userId, clientId.
    /// </summary>
    [JsonPropertyName("property")]
    public string Property { get; init; } = string.Empty;

    [JsonPropertyName("value")]
    public string Value { get; init; } = string.Empty;
}

/// <summary>
/// A sort clause for the audit-log query ("sort" array element).
/// </summary>
public sealed class AuditLogSortDto
{
    /// <summary>Field to sort by: dateIssued, action, serviceName, entityType.</summary>
    [JsonPropertyName("name")]
    public string Name { get; init; } = "dateIssued";

    /// <summary>Sort direction: ASC or DESC.</summary>
    [JsonPropertyName("sortOrder")]
    public string SortOrder { get; init; } = "DESC";
}

// ─── Response DTOs ───────────────────────────────────────────────────────────

public sealed class AuditQueryStatusDto
{
    [JsonPropertyName("state")]
    public string? State { get; init; }
}

/// <summary>
/// One page of results from GET /api/v2/audits/query/{transactionId}/results.
/// Results are raw JSON elements because the audit record schema varies per service.
/// </summary>
public sealed class AuditLogsResultsPageDto
{
    [JsonPropertyName("results")]
    public List<JsonElement>? Results { get; init; }

    [JsonPropertyName("nextUri")]
    public string? NextUri { get; init; }

    [JsonPropertyName("totalHits")]
    public int? TotalHits { get; init; }
}

// ─── Service Catalog ─────────────────────────────────────────────────────────

/// <summary>
/// Structured information about one Genesys Cloud audit service,
/// populated from GET /api/v2/audits/query/servicemapping.
/// Used to drive the filter dropdowns in the audit-log configuration UI.
/// </summary>
public sealed class AuditServiceInfo
{
    /// <summary>The service name as it appears in audit records and in the POST query body.</summary>
    public string ServiceName { get; init; } = string.Empty;

    /// <summary>Entity types audited by this service (e.g. Flow, Queue, User).</summary>
    public IReadOnlyList<string> EntityTypes { get; init; } = [];

    /// <summary>Actions supported by this service (e.g. CREATE, UPDATE, DELETE, PUBLISH).</summary>
    public IReadOnlyList<string> Actions { get; init; } = [];
}
