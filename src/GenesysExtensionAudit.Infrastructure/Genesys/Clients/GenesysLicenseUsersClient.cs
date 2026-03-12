using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysLicenseUsersClient
{
    Task<PagedResult<LicenseUserDto>> GetLicenseUsersPageAsync(
        int pageNumber,
        int pageSize,
        CancellationToken ct);
}

/// <summary>
/// Fetches user license assignments from GET /api/v2/license/users.
/// Returns which Genesys Cloud license products are assigned to each user.
/// </summary>
public sealed class GenesysLicenseUsersClient : GenesysCloudApiClient, IGenesysLicenseUsersClient
{
    public GenesysLicenseUsersClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysLicenseUsersClient> logger)
        : base(http, tokenProvider, regionOptions, logger)
    {
    }

    public async Task<PagedResult<LicenseUserDto>> GetLicenseUsersPageAsync(
        int pageNumber,
        int pageSize,
        CancellationToken ct)
    {
        var path = $"/api/v2/license/users?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<LicenseUsersPageDto>(path, ct).ConfigureAwait(false);

        return new PagedResult<LicenseUserDto>(
            Items: dto.Entities ?? [],
            PageNumber: dto.PageNumber ?? pageNumber,
            PageSize: dto.PageSize ?? pageSize,
            PageCount: dto.PageCount,
            Total: dto.Total);
    }
}
