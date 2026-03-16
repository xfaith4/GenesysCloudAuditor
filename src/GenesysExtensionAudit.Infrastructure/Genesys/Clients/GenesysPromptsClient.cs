using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysPromptsClient
{
    Task<PagedResult<PromptDto>> GetPromptsPageAsync(int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysPromptsClient : GenesysCloudApiClient, IGenesysPromptsClient
{
    public GenesysPromptsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysPromptsClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<PagedResult<PromptDto>> GetPromptsPageAsync(
        int pageNumber, int pageSize, CancellationToken ct)
    {
        // includeMediaUris=true ensures PromptResourceDto.MediaUri is populated.
        var path = $"/api/v2/architect/prompts?pageSize={pageSize}&pageNumber={pageNumber}&includeMediaUris=true";
        var dto = await GetJsonAsync<PromptsPageDto>(path, ct).ConfigureAwait(false);
        return new PagedResult<PromptDto>(
            dto.Entities ?? [],
            dto.PageNumber ?? pageNumber,
            dto.PageSize ?? pageSize,
            dto.PageCount,
            dto.Total);
    }
}
