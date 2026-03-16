using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysTrunksClient
{
    Task<PagedResult<TrunkDto>> GetTrunksPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysTrunksClient : GenesysCloudApiClient, IGenesysTrunksClient
{
    public GenesysTrunksClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysTrunksClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<TrunkDto>> GetTrunksPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/telephony/providers/edges/trunks?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<TrunksPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<TrunkDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
