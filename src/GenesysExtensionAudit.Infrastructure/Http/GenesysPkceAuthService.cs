using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Http;

/// <summary>
/// Implements OAuth 2.0 Authorization Code with PKCE using a loopback redirect URI.
/// </summary>
public sealed class GenesysPkceAuthService : IGenesysPkceAuthService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IOptionsMonitor<GenesysRegionOptions> _regionOptions;
    private readonly IOptionsMonitor<GenesysOAuthOptions> _oauthOptions;
    private readonly ILogger<GenesysPkceAuthService> _logger;

    public GenesysPkceAuthService(
        IHttpClientFactory httpClientFactory,
        IOptionsMonitor<GenesysRegionOptions> regionOptions,
        IOptionsMonitor<GenesysOAuthOptions> oauthOptions,
        ILogger<GenesysPkceAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _regionOptions = regionOptions;
        _oauthOptions = oauthOptions;
        _logger = logger;
    }

    public bool IsRedirectPortAvailable(string redirectUri, out string message)
    {
        if (!TryValidateRedirectUri(redirectUri, out var redirect, out message))
            return false;

        var ip = redirect.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.Parse(redirect.Host);

        try
        {
            using var probe = new TcpListener(ip, redirect.Port);
            probe.Start();
            message = $"Port {redirect.Port} is available.";
            return true;
        }
        catch (SocketException)
        {
            message = $"Port {redirect.Port} is already in use. Pick a different loopback redirect port.";
            return false;
        }
        catch (Exception ex)
        {
            message = $"Port check failed: {ex.Message}";
            return false;
        }
    }

    public async Task<GenesysPkceAuthResult> AuthenticateAsync(CancellationToken ct)
    {
        var region = _regionOptions.CurrentValue;
        var oauth = _oauthOptions.CurrentValue;

        var clientId = string.IsNullOrWhiteSpace(oauth.PkceClientId) ? oauth.ClientId : oauth.PkceClientId;
        if (string.IsNullOrWhiteSpace(clientId))
        {
            return new GenesysPkceAuthResult
            {
                Success = false,
                Message = "PKCE Client ID is required."
            };
        }

        if (!TryValidateRedirectUri(oauth.PkceRedirectUri, out var redirectUri, out var validationError))
        {
            return new GenesysPkceAuthResult
            {
                Success = false,
                Message = validationError
            };
        }

        var ip = redirectUri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            ? IPAddress.Loopback
            : IPAddress.Parse(redirectUri.Host);

        using var listener = new TcpListener(ip, redirectUri.Port);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            return new GenesysPkceAuthResult
            {
                Success = false,
                Message = $"Redirect port {redirectUri.Port} is unavailable. Update PkceRedirectUri and retry."
            };
        }

        var state = CreateBase64UrlRandom(24);
        var codeVerifier = CreateBase64UrlRandom(64);
        var codeChallenge = CreateCodeChallenge(codeVerifier);

        var authorizeUrl = BuildAuthorizeUrl(
            region.AuthBaseUrl,
            clientId,
            redirectUri,
            codeChallenge,
            state,
            oauth.PkceScope);
        _logger.LogInformation("Starting Genesys PKCE auth flow against {AuthBaseUrl}", region.AuthBaseUrl);

        OpenSystemBrowser(authorizeUrl);

        string? code = null;
        string? responseState = null;
        string? oauthError = null;

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromMinutes(2));

        try
        {
            var acceptTask = listener.AcceptTcpClientAsync(timeoutCts.Token).AsTask();
            using var client = await acceptTask.ConfigureAwait(false);
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, detectEncodingFromByteOrderMarks: false, leaveOpen: true);

            var requestLine = await reader.ReadLineAsync(timeoutCts.Token).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(requestLine))
                throw new InvalidOperationException("Empty loopback callback request.");

            var requestTarget = ExtractRequestTarget(requestLine);
            var callbackUri = new Uri($"http://{redirectUri.Host}:{redirectUri.Port}{requestTarget}", UriKind.Absolute);
            var query = ParseQuery(callbackUri.Query);

            query.TryGetValue("code", out code);
            query.TryGetValue("state", out responseState);
            query.TryGetValue("error", out oauthError);

            var html = oauthError is null
                ? "<html><body><h3>Genesys Cloud authentication complete.</h3><p>You can close this tab and return to the app.</p></body></html>"
                : $"<html><body><h3>Genesys Cloud authentication failed.</h3><p>{WebUtility.HtmlEncode(oauthError)}</p></body></html>";

            await WriteHttpResponseAsync(stream, html, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return new GenesysPkceAuthResult
            {
                Success = false,
                Message = "PKCE sign-in timed out after 2 minutes."
            };
        }
        finally
        {
            listener.Stop();
        }

        if (!string.IsNullOrWhiteSpace(oauthError))
        {
            return new GenesysPkceAuthResult
            {
                Success = false,
                Message = $"Authorization failed: {oauthError}"
            };
        }

        if (string.IsNullOrWhiteSpace(code) || !string.Equals(state, responseState, StringComparison.Ordinal))
        {
            return new GenesysPkceAuthResult
            {
                Success = false,
                Message = "Invalid authorization response (missing code or state mismatch)."
            };
        }

        return await ExchangeCodeForTokenAsync(region.AuthBaseUrl, clientId, redirectUri.ToString(), code, codeVerifier, ct)
            .ConfigureAwait(false);
    }

    private async Task<GenesysPkceAuthResult> ExchangeCodeForTokenAsync(
        string authBaseUrl,
        string clientId,
        string redirectUri,
        string code,
        string codeVerifier,
        CancellationToken ct)
    {
        var tokenUrl = $"{authBaseUrl}/oauth/token";
        var http = _httpClientFactory.CreateClient("GenesysAuth");

        using var request = new HttpRequestMessage(HttpMethod.Post, tokenUrl);
        request.Content = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "authorization_code"),
            new KeyValuePair<string, string>("client_id", clientId),
            new KeyValuePair<string, string>("redirect_uri", redirectUri),
            new KeyValuePair<string, string>("code", code),
            new KeyValuePair<string, string>("code_verifier", codeVerifier)
        });

        using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogWarning("PKCE token exchange failed with {StatusCode}: {Body}", (int)response.StatusCode, body);
            return new GenesysPkceAuthResult
            {
                Success = false,
                Message = $"Token exchange failed ({(int)response.StatusCode}). Verify OAuth client type, redirect URI, and region."
            };
        }

        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var accessToken = doc.RootElement.TryGetProperty("access_token", out var accessProp)
            ? accessProp.GetString() ?? string.Empty
            : string.Empty;

        var refreshToken = doc.RootElement.TryGetProperty("refresh_token", out var refreshProp)
            ? refreshProp.GetString() ?? string.Empty
            : string.Empty;

        var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var expProp)
            ? expProp.GetInt32()
            : 3600;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return new GenesysPkceAuthResult
            {
                Success = false,
                Message = "OAuth response missing access_token."
            };
        }

        return new GenesysPkceAuthResult
        {
            Success = true,
            Message = "PKCE authentication succeeded.",
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            AccessTokenExpiresAtUtc = DateTimeOffset.UtcNow.AddSeconds(expiresIn)
        };
    }

    private static bool TryValidateRedirectUri(string redirectUri, out Uri uri, out string message)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var parsedUri))
        {
            uri = null!;
            message = "PkceRedirectUri must be a valid absolute URI.";
            return false;
        }

        uri = parsedUri;

        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase))
        {
            message = "PkceRedirectUri must use http:// loopback.";
            return false;
        }

        var host = uri.Host.ToLowerInvariant();
        if (host is not ("127.0.0.1" or "localhost"))
        {
            message = "PkceRedirectUri host must be 127.0.0.1 or localhost.";
            return false;
        }

        if (uri.Port <= 0)
        {
            message = "PkceRedirectUri must include an explicit port.";
            return false;
        }

        message = string.Empty;
        return true;
    }

    private static void OpenSystemBrowser(string url)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }

    private static string CreateCodeChallenge(string codeVerifier)
    {
        var bytes = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return ToBase64Url(bytes);
    }

    private static string CreateBase64UrlRandom(int bytesLength)
    {
        var bytes = RandomNumberGenerator.GetBytes(bytesLength);
        return ToBase64Url(bytes);
    }

    private static string ToBase64Url(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string BuildAuthorizeUrl(
        string authBaseUrl,
        string clientId,
        Uri redirectUri,
        string codeChallenge,
        string state,
        string? scope)
    {
        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", clientId),
            new("redirect_uri", redirectUri.ToString()),
            new("code_challenge", codeChallenge),
            new("code_challenge_method", "S256"),
            new("state", state)
        };

        var normalizedScope = NormalizeScope(scope);
        if (!string.IsNullOrWhiteSpace(normalizedScope))
            query.Add(new KeyValuePair<string, string>("scope", normalizedScope));

        var q = string.Join("&", query.Select(kv =>
            $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"));

        return $"{authBaseUrl}/oauth/authorize?{q}";
    }

    private static string NormalizeScope(string? scope)
    {
        if (string.IsNullOrWhiteSpace(scope))
            return string.Empty;

        return string.Join(' ', scope
            .Split([' ', ',', '\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal));
    }

    private static string ExtractRequestTarget(string requestLine)
    {
        var parts = requestLine.Split(' ');
        if (parts.Length < 2)
            throw new InvalidOperationException("Invalid HTTP request line.");

        return parts[1];
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(query))
            return result;

        var trimmed = query.TrimStart('?');
        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = pair.IndexOf('=');
            if (idx < 0)
            {
                result[Uri.UnescapeDataString(pair)] = string.Empty;
                continue;
            }

            var key = Uri.UnescapeDataString(pair[..idx]);
            var value = Uri.UnescapeDataString(pair[(idx + 1)..]);
            result[key] = value;
        }

        return result;
    }

    private static async Task WriteHttpResponseAsync(NetworkStream stream, string body, CancellationToken ct)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header =
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/html; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n";

        var headerBytes = Encoding.ASCII.GetBytes(header);
        await stream.WriteAsync(headerBytes, ct).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
