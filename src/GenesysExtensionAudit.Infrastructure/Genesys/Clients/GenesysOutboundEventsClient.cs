using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysOutboundEventsClient
{
    Task<PagedResult<OutboundEventDto>> GetOutboundEventsPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysOutboundEventsClient : GenesysCloudApiClient, IGenesysOutboundEventsClient
{
    public GenesysOutboundEventsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysOutboundEventsClient> logger)
        : base(http, tokenProvider, regionOptions, logger)
    {
    }

    public async Task<PagedResult<OutboundEventDto>> GetOutboundEventsPageAsync(
        int pageNumber,
        int pageSize,
        CancellationToken ct)
    {
        var path = $"/api/v2/outbound/events?pageSize={pageSize}&pageNumber={pageNumber}";
        var dto = await GetJsonAsync<OutboundEventsPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<OutboundEventDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
