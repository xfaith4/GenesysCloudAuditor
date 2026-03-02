using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysExtensionsClient
{
    Task<PagedResult<EdgeExtensionEntityDto>> GetExtensionsPageAsync(
        int pageNumber,
        int pageSize,
        CancellationToken ct);
}

public sealed class GenesysExtensionsClient : GenesysCloudApiClient, IGenesysExtensionsClient
{
    public GenesysExtensionsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysExtensionsClient> logger)
        : base(http, tokenProvider, regionOptions, logger)
    {
    }

    public async Task<PagedResult<EdgeExtensionEntityDto>> GetExtensionsPageAsync(
        int pageNumber,
        int pageSize,
        CancellationToken ct)
    {
        var path = $"/api/v2/telephony/providers/edges/extensions?pageSize={pageSize}&pageNumber={pageNumber}";

        var dto = await GetJsonAsync<EdgeExtensionsPageDto>(path, ct).ConfigureAwait(false);

        return new PagedResult<EdgeExtensionEntityDto>(
            Items: dto.Entities ?? [],
            PageNumber: dto.PageNumber ?? pageNumber,
            PageSize: dto.PageSize ?? pageSize,
            PageCount: dto.PageCount,
            Total: dto.Total);
    }
}
