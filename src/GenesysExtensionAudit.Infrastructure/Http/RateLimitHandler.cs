using Microsoft.Extensions.Options;

namespace GenesysExtensionAudit.Infrastructure.Http;

/// <summary>
/// DelegatingHandler that enforces a per-second request rate limit using a
/// token-bucket approach. Configured by <see cref="GenesysRegionOptions.MaxRequestsPerSecond"/>.
/// </summary>
public sealed class RateLimitHandler : DelegatingHandler
{
    private readonly int _maxRequestsPerSecond;
    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static DateTimeOffset _lastRequestAt = DateTimeOffset.MinValue;

    public RateLimitHandler(IOptions<GenesysRegionOptions> options)
    {
        _maxRequestsPerSecond = Math.Max(1, options?.Value?.MaxRequestsPerSecond ?? 3);
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var minInterval = TimeSpan.FromSeconds(1.0 / _maxRequestsPerSecond);
            var now = DateTimeOffset.UtcNow;
            var wait = (_lastRequestAt + minInterval) - now;

            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, cancellationToken).ConfigureAwait(false);

            _lastRequestAt = DateTimeOffset.UtcNow;
        }
        finally
        {
            Gate.Release();
        }

        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }
}
