using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/telephony/providers/edges/trunks
/// </summary>
public sealed class TrunksPageDto
{
    [JsonPropertyName("entities")]
    public List<TrunkDto>? Entities { get; init; }

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
/// A trunk from GET /api/v2/telephony/providers/edges/trunks.
/// Trunks carry PSTN and carrier signalling between an edge and the external network.
/// </summary>
public sealed class TrunkDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>The edge device this trunk is hosted on.</summary>
    [JsonPropertyName("edge")]
    public TrunkEdgeRefDto? Edge { get; init; }

    /// <summary>
    /// Current operational state of the trunk: UP, DOWN, UNKNOWN.
    /// DOWN or UNKNOWN means the trunk cannot carry calls.
    /// </summary>
    [JsonPropertyName("trunkState")]
    public string? TrunkState { get; init; }

    /// <summary>
    /// Whether this trunk is administratively in service.
    /// A trunk can be UP but out of service if recently de-provisioned.
    /// </summary>
    [JsonPropertyName("inService")]
    public bool? InService { get; init; }

    /// <summary>Whether the trunk is enabled (admin toggle).</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>
    /// Trunk type: EXTERNAL (BYOC PSTN), MANAGED (Genesys Cloud Voice),
    /// HYBRID, PHONE (for individual phones).
    /// </summary>
    [JsonPropertyName("trunkType")]
    public string? TrunkType { get; init; }

    /// <summary>Trunk base settings reference (configuration template).</summary>
    [JsonPropertyName("trunkBase")]
    public TrunkBaseRefDto? TrunkBase { get; init; }
}

public sealed class TrunkEdgeRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class TrunkBaseRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
