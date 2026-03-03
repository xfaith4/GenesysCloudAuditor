using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Genesys.Clients;

public interface IGenesysOperationalEventsClient
{
    Task<OperationalEventsQueryResponseDto> QueryEventsAsync(
        OperationalEventsQueryRequestDto request,
        int pageSize,
        string? afterCursor,
        CancellationToken ct);
}

public sealed class GenesysOperationalEventsClient : GenesysCloudApiClient, IGenesysOperationalEventsClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public GenesysOperationalEventsClient(
        HttpClient http,
        ITokenProvider tokenProvider,
        IOptions<GenesysRegionOptions> regionOptions,
        ILogger<GenesysOperationalEventsClient> logger)
        : base(http, tokenProvider, regionOptions, logger)
    {
    }

    public Task<OperationalEventsQueryResponseDto> QueryEventsAsync(
        OperationalEventsQueryRequestDto request,
        int pageSize,
        string? afterCursor,
        CancellationToken ct)
    {
        var boundedPageSize = Math.Clamp(pageSize, 1, 200);
        var path = new StringBuilder("/api/v2/usage/events/query")
            .Append("?pageSize=").Append(boundedPageSize);

        if (!string.IsNullOrWhiteSpace(afterCursor))
            path.Append("&after=").Append(Uri.EscapeDataString(afterCursor));

        using var message = new HttpRequestMessage(HttpMethod.Post, ApiUri(path.ToString()));
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        message.Content = new StringContent(
            JsonSerializer.Serialize(request, JsonOptions),
            Encoding.UTF8,
            "application/json");

        return SendJsonAsync<OperationalEventsQueryResponseDto>(message, ct);
    }
}
