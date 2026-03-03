using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

public sealed class OutboundEventsPageDto
{
    [JsonPropertyName("entities")]
    public List<OutboundEventDto>? Entities { get; init; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; init; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("pageCount")]
    public int? PageCount { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }

    [JsonPropertyName("nextUri")]
    public string? NextUri { get; init; }
}

public sealed class OutboundEventDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset? Timestamp { get; init; }

    [JsonPropertyName("level")]
    public string? Level { get; init; }

    [JsonPropertyName("category")]
    public string? Category { get; init; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; init; }

    [JsonPropertyName("eventMessage")]
    public OutboundEventMessageDto? EventMessage { get; init; }
}

public sealed class OutboundEventMessageDto
{
    [JsonPropertyName("code")]
    public string? Code { get; init; }

    [JsonPropertyName("message")]
    public string? Message { get; init; }
}
