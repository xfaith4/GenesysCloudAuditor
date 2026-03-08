using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysIvrsClient
{
    Task<PagedResult<IvrDto>> GetIvrsPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysIvrsClient : GenesysCloudApiClient, IGenesysIvrsClient
{
    public GenesysIvrsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysIvrsClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<IvrDto>> GetIvrsPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/architect/ivrs?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<IvrsPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<IvrDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
