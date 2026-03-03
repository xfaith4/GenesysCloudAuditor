using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

public sealed class OperationalEventsQueryRequestDto
{
    [JsonPropertyName("interval")]
    public string Interval { get; init; } = string.Empty;

    [JsonPropertyName("eventDefinitionIds")]
    public IReadOnlyList<string>? EventDefinitionIds { get; init; }

    [JsonPropertyName("searchTerm")]
    public string? SearchTerm { get; init; }

    [JsonPropertyName("sortOrder")]
    public string? SortOrder { get; init; } = "DESC";
}

public sealed class OperationalEventsQueryResponseDto
{
    [JsonPropertyName("entities")]
    public List<OperationalEventDto>? Entities { get; init; }

    [JsonPropertyName("nextUri")]
    public string? NextUri { get; init; }

    [JsonPropertyName("selfUri")]
    public string? SelfUri { get; init; }

    [JsonPropertyName("previousUri")]
    public string? PreviousUri { get; init; }
}

public sealed class OperationalEventDto
{
    [JsonPropertyName("eventDefinition")]
    public AddressableEntityRefDto? EventDefinition { get; init; }

    [JsonPropertyName("entityId")]
    public string? EntityId { get; init; }

    [JsonPropertyName("entityToken")]
    public string? EntityToken { get; init; }

    [JsonPropertyName("entityName")]
    public string? EntityName { get; init; }

    [JsonPropertyName("previousValue")]
    public string? PreviousValue { get; init; }

    [JsonPropertyName("currentValue")]
    public string? CurrentValue { get; init; }

    [JsonPropertyName("errorCode")]
    public string? ErrorCode { get; init; }

    [JsonPropertyName("parentEntityId")]
    public string? ParentEntityId { get; init; }

    [JsonPropertyName("conversation")]
    public AddressableEntityRefDto? Conversation { get; init; }

    [JsonPropertyName("dateCreated")]
    public DateTimeOffset? DateCreated { get; init; }

    [JsonPropertyName("entityVersion")]
    public string? EntityVersion { get; init; }
}

public sealed class AddressableEntityRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("selfUri")]
    public string? SelfUri { get; init; }
}
