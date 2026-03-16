using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysEdgesClient
{
    Task<PagedResult<EdgeDto>> GetEdgesPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysEdgesClient : GenesysCloudApiClient, IGenesysEdgesClient
{
    public GenesysEdgesClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysEdgesClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<EdgeDto>> GetEdgesPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/telephony/providers/edges?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<EdgesPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<EdgeDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
