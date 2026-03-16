using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/telephony/providers/edges
/// </summary>
public sealed class EdgesPageDto
{
    [JsonPropertyName("entities")]
    public List<EdgeDto>? Entities { get; init; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; init; }

    [JsonPropertyName("pageCount")]
    public int? PageCount { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }
}

/// <summary>
/// A Genesys Cloud Edge device from GET /api/v2/telephony/providers/edges.
/// Edges bridge the PSTN and carrier infrastructure to the Genesys Cloud platform.
/// </summary>
public sealed class EdgeDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The site this edge belongs to.</summary>
    [JsonPropertyName("site")]
    public EdgeSiteRefDto? Site { get; init; }

    /// <summary>
    /// Online status reported by the edge: ONLINE, OFFLINE, or UNKNOWN.
    /// OFFLINE or UNKNOWN means the edge cannot carry traffic.
    /// </summary>
    [JsonPropertyName("onlineStatus")]
    public string? OnlineStatus { get; init; }

    /// <summary>
    /// Software status: CURRENT, UPDATE_REQUIRED, UPDATING, etc.
    /// UPDATE_REQUIRED may degrade call quality or block new features.
    /// </summary>
    [JsonPropertyName("softwareStatus")]
    public string? SoftwareStatus { get; init; }

    /// <summary>
    /// Platform-facing status code: ACTIVE, INACTIVE, HYBRID.
    /// </summary>
    [JsonPropertyName("statusCode")]
    public string? StatusCode { get; init; }

    /// <summary>Whether the edge is managed by Genesys or customer-managed.</summary>
    [JsonPropertyName("managed")]
    public bool? Managed { get; init; }
}

public sealed class EdgeSiteRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
