using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Page wrapper for GET /api/v2/license/users
/// </summary>
public sealed class LicenseUsersPageDto
{
    [JsonPropertyName("entities")]
    public List<LicenseUserDto>? Entities { get; init; }

    [JsonPropertyName("pageSize")]
    public int? PageSize { get; init; }

    [JsonPropertyName("pageNumber")]
    public int? PageNumber { get; init; }

    [JsonPropertyName("pageCount")]
    public int? PageCount { get; init; }

    [JsonPropertyName("total")]
    public int? Total { get; init; }
}

/// <summary>
/// A single user's license assignment record returned by GET /api/v2/license/users.
/// </summary>
public sealed class LicenseUserDto
{
    /// <summary>Genesys Cloud user ID.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// License product names assigned to this user.
    /// The Genesys API returns an array of license product-name strings,
    /// e.g. ["PureCloud 3", "PureCloud 1 WFO Digital"].
    /// </summary>
    [JsonPropertyName("licenses")]
    public List<string>? Licenses { get; init; }
}
