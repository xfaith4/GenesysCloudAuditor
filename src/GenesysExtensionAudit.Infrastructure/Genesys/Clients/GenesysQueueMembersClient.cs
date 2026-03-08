using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysQueueMembersClient
{
    /// <summary>
    /// Fetches one page of queue members for the given queue.
    /// Returns member DTOs; the caller cross-references user IDs against the full user collection.
    /// </summary>
    Task<QueueMembersPageDto> GetQueueMembersPageAsync(
        string queueId, int pageNumber, int pageSize, CancellationToken ct);
}

public sealed class GenesysQueueMembersClient : GenesysCloudApiClient, IGenesysQueueMembersClient
{
    public GenesysQueueMembersClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysQueueMembersClient> logger)
        : base(http, tokenProvider, regionOptions, logger) { }

    public async Task<QueueMembersPageDto> GetQueueMembersPageAsync(
        string queueId, int pageNumber, int pageSize, CancellationToken ct)
    {
        var path = $"/api/v2/routing/queues/{Uri.EscapeDataString(queueId)}/members" +
                   $"?pageSize={pageSize}&pageNumber={pageNumber}&joined=true";

        return await GetJsonAsync<QueueMembersPageDto>(path, ct).ConfigureAwait(false);
    }
}
