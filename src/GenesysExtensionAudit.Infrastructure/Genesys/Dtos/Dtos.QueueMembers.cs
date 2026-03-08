using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/routing/queues/{queueId}/members
/// </summary>
public sealed class QueueMembersPageDto
{
    [JsonPropertyName("entities")]
    public List<QueueMemberDto>? Entities { get; init; }

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
/// A single queue member entry returned by the Genesys queue members API.
/// The user ID is exposed at the top-level "id" field; the nested "user" object
/// may provide the same ID plus name/state on some API versions.
/// </summary>
public sealed class QueueMemberDto
{
    /// <summary>User ID (top-level, present on all API versions).</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>User display name (top-level, may be present).</summary>
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>
    /// Nested user reference included by some API versions or expand parameters.
    /// </summary>
    [JsonPropertyName("user")]
    public QueueMemberUserRefDto? User { get; init; }

    /// <summary>Whether the member is actively joined to the queue.</summary>
    [JsonPropertyName("joined")]
    public bool? Joined { get; init; }

    /// <summary>Ring number for this member in the queue routing order.</summary>
    [JsonPropertyName("ringNumber")]
    public int? RingNumber { get; init; }
}

/// <summary>
/// Minimal user reference nested inside a queue member response.
/// </summary>
public sealed class QueueMemberUserRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("state")]
    public string? State { get; init; }
}
