using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GenesysExtensionAudit.Infrastructure.Http;

/// <summary>
/// DelegatingHandler that logs HTTP request timing and response status.
/// Does not log Authorization header values.
/// </summary>
public sealed class HttpLoggingHandler : DelegatingHandler
{
    private readonly ILogger<HttpLoggingHandler> _logger;

    public HttpLoggingHandler(ILogger<HttpLoggingHandler> logger)
        => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var sw = Stopwatch.StartNew();
        HttpResponseMessage? response = null;

        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            sw.Stop();

            _logger.LogInformation(
                "HTTP {Method} {Path} → {StatusCode} in {ElapsedMs}ms",
                request.Method.Method,
                request.RequestUri?.GetLeftPart(UriPartial.Path),
                (int)response.StatusCode,
                sw.ElapsedMilliseconds);

            return response;
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            sw.Stop();
            _logger.LogWarning(
                ex,
                "HTTP {Method} {Path} failed after {ElapsedMs}ms",
                request.Method.Method,
                request.RequestUri?.GetLeftPart(UriPartial.Path),
                sw.ElapsedMilliseconds);
            throw;
        }
    }
}
