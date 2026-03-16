using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysSitesClient
{
    Task<PagedResult<SiteDto>> GetSitesPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysSitesClient : GenesysCloudApiClient, IGenesysSitesClient
{
    public GenesysSitesClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysSitesClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<SiteDto>> GetSitesPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/telephony/providers/edges/sites?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<SitesPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<SiteDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
