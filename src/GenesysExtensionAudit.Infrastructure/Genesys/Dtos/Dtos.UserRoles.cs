using System.Text.Json.Serialization;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Dtos;

/// <summary>
/// Response wrapper for GET /api/v2/users/{userId}/roles (AuthzSubjectEntityListing).
/// Contains subjects with type USER (direct assignments) and GROUP (inherited roles).
/// </summary>
public sealed class UserRolesResponseDto
{
    [JsonPropertyName("entities")]
    public List<AuthzSubjectDto>? Entities { get; init; }
}

/// <summary>
/// A subject that holds role grants for a user — either the user themselves (type=USER)
/// or a group they belong to (type=GROUP).
/// </summary>
public sealed class AuthzSubjectDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }

    /// <summary>"USER" for direct assignments, "GROUP" for group-inherited roles.</summary>
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("grants")]
    public List<AuthzGrantDto>? Grants { get; init; }
}

/// <summary>
/// A single role grant: a role assigned in a specific division to a subject.
/// </summary>
public sealed class AuthzGrantDto
{
    [JsonPropertyName("subjectId")]
    public string? SubjectId { get; init; }

    [JsonPropertyName("division")]
    public AuthzDivisionRefDto? Division { get; init; }

    [JsonPropertyName("role")]
    public AuthzRoleRefDto? Role { get; init; }

    /// <summary>
    /// True when the grant was made on a parent division and applies here by inheritance.
    /// </summary>
    [JsonPropertyName("grantMadeIndirectly")]
    public bool? GrantMadeIndirectly { get; init; }
}

public sealed class AuthzRoleRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}

public sealed class AuthzDivisionRefDto
{
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonPropertyName("name")]
    public string? Name { get; init; }
}
