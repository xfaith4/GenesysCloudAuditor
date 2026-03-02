// File: src/GenesysExtensionAudit.Infrastructure/Genesys/Dtos/Dtos.Extensions.cs
using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/telephony/providers/edges/extensions
/// </summary>
public sealed class EdgeExtensionsPageDto
{
    [JsonPropertyName("entities")]
    public List<EdgeExtensionEntityDto>? Entities { get; init; }

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
/// Minimal Edge extension entity shape for audit comparison.
/// </summary>
public sealed class EdgeExtensionEntityDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>The numeric/string extension value (often digits).</summary>
    [JsonPropertyName("extension")]
    public string? Extension { get; init; }

    /// <summary>
    /// The entity this extension is assigned to (user, group, etc.).
    /// Populated from the "assignedTo" field in the Genesys response.
    /// </summary>
    [JsonPropertyName("assignedTo")]
    public AssignedToDto? AssignedTo { get; init; }
}

/// <summary>
/// Describes the entity that an extension is assigned to.
/// </summary>
public sealed class AssignedToDto
{
    /// <summary>E.g. "USER", "GROUP", "STATION".</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    /// <summary>The ID of the assigned entity.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }
}
