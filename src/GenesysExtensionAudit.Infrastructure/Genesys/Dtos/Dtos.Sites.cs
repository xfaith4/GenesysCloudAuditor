using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/telephony/providers/edges/sites
/// </summary>
public sealed class SitesPageDto
{
    [JsonPropertyName("entities")]
    public List<SiteDto>? Entities { get; init; }

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
/// A telephony site from GET /api/v2/telephony/providers/edges/sites.
/// A site groups edges and stations under a single location/timezone configuration.
/// </summary>
public sealed class SiteDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }

    /// <summary>Edges designated as primary for this site.</summary>
    [JsonPropertyName("primaryEdges")]
    public List<SiteEdgeRefDto>? PrimaryEdges { get; init; }

    /// <summary>Edges designated as secondary/standby for this site.</summary>
    [JsonPropertyName("secondaryEdges")]
    public List<SiteEdgeRefDto>? SecondaryEdges { get; init; }

    [JsonPropertyName("location")]
    public SiteLocationRefDto? Location { get; init; }

    /// <summary>MediaRegions where this site is homed.</summary>
    [JsonPropertyName("mediaRegions")]
    public List<string>? MediaRegions { get; init; }

    /// <summary>RTCP settings, can be used to detect configuration completeness.</summary>
    [JsonPropertyName("rtcpSettings")]
    public SiteRtcpSettingsDto? RtcpSettings { get; init; }
}

public sealed class SiteEdgeRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class SiteLocationRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class SiteRtcpSettingsDto
{
    [JsonPropertyName("retentionTimeDays")]
    public int? RetentionTimeDays { get; init; }
}
