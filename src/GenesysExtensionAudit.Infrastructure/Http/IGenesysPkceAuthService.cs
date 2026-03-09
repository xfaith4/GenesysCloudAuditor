namespace GenesysExtensionAudit.Infrastructure.Http;

public interface IGenesysPkceAuthService
{
    Task<GenesysPkceAuthResult> AuthenticateAsync(CancellationToken ct);

    bool IsRedirectPortAvailable(string redirectUri, out string message);
}
