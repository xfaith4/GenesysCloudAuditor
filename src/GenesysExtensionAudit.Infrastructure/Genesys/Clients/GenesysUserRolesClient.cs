using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysUserRolesClient
{
    /// <summary>
    /// Returns the role subjects for a single user, including both direct (USER) and
    /// group-inherited (GROUP) grants. Maps to GET /api/v2/users/{userId}/roles.
    /// </summary>
    Task<UserRolesResponseDto> GetUserRolesAsync(string userId, CancellationToken ct);
}

/// <summary>
/// Fetches the role subjects for a single Genesys Cloud user.
/// Each response contains subjects of type "USER" (direct assignments) and
/// "GROUP" (inherited through group membership), each with their granted roles.
/// </summary>
public sealed class GenesysUserRolesClient : GenesysCloudApiClient, IGenesysUserRolesClient
{
    public GenesysUserRolesClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysUserRolesClient> logger)
        : base(http, tokenProvider, regionOptions, logger)
    {
    }

    public async Task<UserRolesResponseDto> GetUserRolesAsync(string userId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(userId))
            throw new ArgumentException("User ID cannot be null or empty.", nameof(userId));

        var path = $"/api/v2/users/{Uri.EscapeDataString(userId)}/roles";
        return await GetJsonAsync<UserRolesResponseDto>(path, ct).ConfigureAwait(false);
    }
}
