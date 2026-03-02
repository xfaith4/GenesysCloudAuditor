using System.Net.Http.Headers;

namespace GenesysExtensionAudit.Infrastructure.Http;

/// <summary>
/// DelegatingHandler that attaches a Bearer token to every outbound request.
/// Must be placed AFTER RateLimitHandler and HttpLoggingHandler in the pipeline.
/// </summary>
public sealed class OAuthBearerHandler : DelegatingHandler
{
    private readonly ITokenProvider _tokenProvider;

    public OAuthBearerHandler(ITokenProvider tokenProvider)
        => _tokenProvider = tokenProvider ?? throw new ArgumentNullException(nameof(tokenProvider));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
