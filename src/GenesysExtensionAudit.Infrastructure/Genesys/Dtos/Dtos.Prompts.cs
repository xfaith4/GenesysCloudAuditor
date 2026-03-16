using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/architect/prompts
/// </summary>
public sealed class PromptsPageDto
{
    [JsonPropertyName("entities")]
    public List<PromptDto>? Entities { get; init; }

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
/// Represents a single architect prompt from GET /api/v2/architect/prompts.
/// Each prompt can have one resource per language. A resource with no media URI
/// and no TTS string cannot produce audio — calls referencing it play silence.
/// </summary>
public sealed class PromptDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>True if this is a Genesys system prompt (vs. customer-created).</summary>
    [JsonPropertyName("systemPrompt")]
    public bool? SystemPrompt { get; init; }

    /// <summary>
    /// Language-specific audio resources.
    /// Populated when the API is called with includeMediaUris=true.
    /// </summary>
    [JsonPropertyName("resources")]
    public List<PromptResourceDto>? Resources { get; init; }
}

/// <summary>
/// A single language variant of a prompt resource.
/// </summary>
public sealed class PromptResourceDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>BCP-47 language tag, e.g. "en-us".</summary>
    [JsonPropertyName("language")]
    public string? Language { get; init; }

    /// <summary>URI of the transcoded audio file. Null if audio has never been uploaded.</summary>
    [JsonPropertyName("mediaUri")]
    public string? MediaUri { get; init; }

    /// <summary>TTS fallback string. If set, the platform can synthesise audio even without a media file.</summary>
    [JsonPropertyName("ttsString")]
    public string? TtsString { get; init; }

    /// <summary>Upload/transcode state: "transcoded", "uploaded", "notApplicable", etc.</summary>
    [JsonPropertyName("uploadStatus")]
    public string? UploadStatus { get; init; }
}
