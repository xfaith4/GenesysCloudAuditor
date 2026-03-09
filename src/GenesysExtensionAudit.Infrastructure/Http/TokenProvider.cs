using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using GenesysExtensionAudit.Infrastructure.Configuration;
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
    private readonly IOptionsMonitor<GenesysRegionOptions> _regionOptions;
    private readonly IOptionsMonitor<GenesysOAuthOptions> _oauthOptions;
    private readonly ILogger<TokenProvider> _logger;
    private readonly IUserSettingsService? _userSettings;
    private readonly SemaphoreSlim _lock = new(1, 1);

    private string? _cachedToken;
    private DateTimeOffset _expiresAtUtc = DateTimeOffset.MinValue;

    public TokenProvider(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<GenesysRegionOptions> regionOptions,
        IOptionsMonitor<GenesysOAuthOptions> oauthOptions,
        ILogger<TokenProvider> logger,
        IUserSettingsService? userSettings = null)
    {
        _httpClientFactory = httpClientFactory;
        _regionOptions = regionOptions;
        _oauthOptions = oauthOptions;
        _logger = logger;
        _userSettings = userSettings;
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
        var oauth = _oauthOptions.CurrentValue;
        var region = _regionOptions.CurrentValue;

        var mode = NormalizeAuthMode(oauth.AuthMode);
        if (mode == "pkce")
            return await FetchPkceFirstTokenAsync(region, oauth, ct).ConfigureAwait(false);

        if (mode == "auto")
        {
            if (CanUsePkce(oauth))
                return await FetchPkceFirstTokenAsync(region, oauth, ct).ConfigureAwait(false);

            if (!HasClientCredentials(oauth))
                throw new InvalidOperationException(
                    "No usable OAuth configuration was found. " +
                    "Configure PKCE in Settings and sign in, or provide GenesysOAuth:ClientId and GenesysOAuth:ClientSecret.");
        }

        return await FetchClientCredentialsTokenAsync(region, oauth, ct).ConfigureAwait(false);
    }

    private async Task<string> FetchPkceFirstTokenAsync(
        GenesysRegionOptions region,
        GenesysOAuthOptions oauth,
        CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(oauth.PkceAccessToken) &&
            oauth.PkceAccessTokenExpiresAtUtc is not null &&
            DateTimeOffset.UtcNow < oauth.PkceAccessTokenExpiresAtUtc.Value.AddSeconds(-60))
        {
            var expiresAt = oauth.PkceAccessTokenExpiresAtUtc.Value.AddSeconds(-60);
            _cachedToken = oauth.PkceAccessToken;
            _expiresAtUtc = expiresAt;
            return _cachedToken;
        }

        if (!CanRefreshPkce(oauth))
        {
            if (HasClientCredentials(oauth))
            {
                _logger.LogWarning("PKCE token unavailable; falling back to client credentials flow.");
                return await FetchClientCredentialsTokenAsync(region, oauth, ct).ConfigureAwait(false);
            }

            throw new InvalidOperationException(
                "PKCE is selected but no valid access/refresh token is available. " +
                "Open Settings and complete Genesys Cloud PKCE sign-in.");
        }

        return await RefreshPkceTokenAsync(region, oauth, ct).ConfigureAwait(false);
    }

    private async Task<string> RefreshPkceTokenAsync(
        GenesysRegionOptions region,
        GenesysOAuthOptions oauth,
        CancellationToken ct)
    {
        var http = _httpClientFactory.CreateClient("GenesysAuth");
        var tokenUrl = $"{region.AuthBaseUrl}/oauth/token";
        var pkceClientId = string.IsNullOrWhiteSpace(oauth.PkceClientId) ? oauth.ClientId : oauth.PkceClientId;

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "refresh_token"),
            new KeyValuePair<string, string>("client_id", pkceClientId),
            new KeyValuePair<string, string>("refresh_token", oauth.PkceRefreshToken)
        });

        _logger.LogDebug("Refreshing PKCE OAuth token from {TokenUrl}", tokenUrl);

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            if (HasClientCredentials(oauth))
            {
                _logger.LogWarning("PKCE refresh failed ({StatusCode}); falling back to client credentials.",
                    (int)response.StatusCode);
                return await FetchClientCredentialsTokenAsync(region, oauth, ct).ConfigureAwait(false);
            }

            response.EnsureSuccessStatusCode();
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var token = doc.RootElement.GetProperty("access_token").GetString()
            ?? throw new InvalidOperationException("OAuth response missing access_token.");

        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var exp)
            ? exp.GetInt32()
            : 3600;

        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var refresh)
            ? refresh.GetString()
            : oauth.PkceRefreshToken;

        _cachedToken = token;
        _expiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn - 60); // 60s safety margin
        _logger.LogInformation("PKCE OAuth token refreshed, expires in {ExpiresIn}s", expiresIn);

        PersistPkceTokens(oauth, token, refreshToken ?? string.Empty, expiresIn);
        return token;
    }

    private async Task<string> FetchClientCredentialsTokenAsync(
        GenesysRegionOptions region,
        GenesysOAuthOptions oauth,
        CancellationToken ct)
    {

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

    private void PersistPkceTokens(GenesysOAuthOptions oauth, string accessToken, string refreshToken, int expiresIn)
    {
        if (_userSettings is null)
            return;

        try
        {
            _userSettings.SaveGenesysOAuthSettings(new GenesysOAuthOptions
            {
                AuthMode = oauth.AuthMode,
                ClientId = oauth.ClientId,
                ClientSecret = oauth.ClientSecret,
                PkceClientId = oauth.PkceClientId,
                PkceRedirectUri = oauth.PkceRedirectUri,
                PkceScope = oauth.PkceScope,
                PkceAccessToken = accessToken,
                PkceRefreshToken = refreshToken,
                PkceAccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to persist refreshed PKCE tokens.");
        }
    }

    private static bool HasClientCredentials(GenesysOAuthOptions oauth)
        => !string.IsNullOrWhiteSpace(oauth.ClientId) && !string.IsNullOrWhiteSpace(oauth.ClientSecret);

    private static bool CanUsePkce(GenesysOAuthOptions oauth)
        => !string.IsNullOrWhiteSpace(oauth.PkceAccessToken) || CanRefreshPkce(oauth);

    private static bool CanRefreshPkce(GenesysOAuthOptions oauth)
    {
        var pkceClientId = string.IsNullOrWhiteSpace(oauth.PkceClientId) ? oauth.ClientId : oauth.PkceClientId;
        return !string.IsNullOrWhiteSpace(pkceClientId) &&
               !string.IsNullOrWhiteSpace(oauth.PkceRefreshToken);
    }

    private static string NormalizeAuthMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode))
            return "auto";

        return mode.Trim().ToLowerInvariant() switch
        {
            "pkce" => "pkce",
            "clientcredentials" => "client_credentials",
            "client_credentials" => "client_credentials",
            _ => "auto"
        };
    }
}

/// <summary>
/// Binds to the "GenesysOAuth" configuration section.
/// Use user-secrets or environment variables — never commit credentials.
/// </summary>
public sealed class GenesysOAuthOptions
{
    /// <summary>
    /// Supported values: auto, pkce, client_credentials.
    /// "auto" prefers PKCE when available, then falls back to client credentials.
    /// </summary>
    public string AuthMode { get; set; } = "auto";

    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;

    public string PkceClientId { get; set; } = string.Empty;
    public string PkceRedirectUri { get; set; } = "http://127.0.0.1:45731/callback";
    public string PkceScope { get; set; } = string.Empty;
    public string PkceAccessToken { get; set; } = string.Empty;
    public string PkceRefreshToken { get; set; } = string.Empty;
    public DateTimeOffset? PkceAccessTokenExpiresAtUtc { get; set; }
}
