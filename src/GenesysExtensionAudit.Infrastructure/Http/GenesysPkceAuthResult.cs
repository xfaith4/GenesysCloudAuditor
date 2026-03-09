namespace GenesysExtensionAudit.Infrastructure.Http;

public sealed class GenesysPkceAuthResult
{
    public bool Success { get; init; }
    public string Message { get; init; } = string.Empty;
    public string AccessToken { get; init; } = string.Empty;
    public string RefreshToken { get; init; } = string.Empty;
    public DateTimeOffset AccessTokenExpiresAtUtc { get; init; }
}
