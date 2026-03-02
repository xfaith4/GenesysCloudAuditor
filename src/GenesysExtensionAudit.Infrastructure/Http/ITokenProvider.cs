namespace GenesysExtensionAudit.Infrastructure.Http;

public interface ITokenProvider
{
    Task<string> GetAccessTokenAsync(CancellationToken ct);
    Task ForceRefreshAsync(CancellationToken ct);
}
