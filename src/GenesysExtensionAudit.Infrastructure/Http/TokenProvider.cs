using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Http;

/// <summary>
/// OAuth Client Credentials token provider for Genesys Cloud.
/// Caches the access token until it expires (with a 60-second safety margin).
///
/// CONFIGURATION REQUIRED: Set GenesysOAuth:ClientId and GenesysOAuth:ClientSecret
/// via appsettings.json, user-secrets, or environment variables before use.
/// </summary>
public sealed class TokenProvider : ITokenProvider
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptions<GenesysRegionOptions> _regionOptions;
    private readonly IOptions<GenesysOAuthOptions> _oauthOptions;
    private readonly ILogger<TokenProvider> _logger;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    public TokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptions<GenesysRegionOptions> regionOptions,
        IOptions<GenesysOAuthOptions> oauthOptions,
        ILogger<TokenProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _regionOptions = regionOptions;
        _oauthOptions = oauthOptions;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken ct)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
            return _cachedToken;

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Double-check after acquiring lock
            if (_cachedToken is not null && DateTimeOffset.UtcNow < _expiresAtUtc)
                return _cachedToken;

            return await FetchTokenAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task ForceRefreshAsync(CancellationToken ct)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _cachedToken = null;
            _expiresAtUtc = DateTimeOffset.MinValue;
            await FetchTokenAsync(ct).ConfigureAwait(false);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<string> FetchTokenAsync(CancellationToken ct)
    {
        var oauth = _oauthOptions.Value;
        var region = _regionOptions.Value;

        if (string.IsNullOrWhiteSpace(oauth.ClientId) || string.IsNullOrWhiteSpace(oauth.ClientSecret))
            throw new InvalidOperationException(
                "GenesysOAuth:ClientId and GenesysOAuth:ClientSecret must be configured before running an audit. " +
                "Use user-secrets (dev) or environment variables (prod).");

        var http = _httpClientFactory.CreateClient("GenesysAuth");
        var tokenUrl = $"{region.AuthBaseUrl}/oauth/token";

        var credentials = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{oauth.ClientId}:{oauth.ClientSecret}"));

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials")
        });

        _logger.LogDebug("Fetching OAuth token from {TokenUrl}", tokenUrl);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var token = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("OAuth response missing access_token.");

        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp)
            ? exp.GetInt32()
            : 3600;

        _cachedToken = token;
        _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60); // 60s safety margin

        _logger.LogInformation("OAuth token acquired, expires in {ExpiresIn}s", expiresIn);

        return token;
    }
}

/// <summary>
/// Binds to the "GenesysOAuth" configuration section.
/// Use user-secrets or environment variables — never commit credentials.
/// </summary>
public sealed class GenesysOAuthOptions
{
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
}
