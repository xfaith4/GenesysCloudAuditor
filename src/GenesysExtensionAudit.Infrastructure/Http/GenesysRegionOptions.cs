namespace GenesysExtensionAudit.Infrastructure.Http;

/// <summary>
/// Binds to the "Genesys" configuration section in appsettings.json.
/// </summary>
public sealed class GenesysRegionOptions
{
    /// <summary>Genesys Cloud API domain, e.g. "mypurecloud.com" or "usw2.pure.cloud".</summary>
    public string Region { get; set; } = "mypurecloud.com";

    /// <summary>Records per page when calling paginated Genesys endpoints (1–500).</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>When true, includes inactive users in the audit.</summary>
    public bool IncludeInactive { get; set; } = false;

    /// <summary>Token-bucket rate limit for outbound API calls.</summary>
    public int MaxRequestsPerSecond { get; set; } = 3;

    public string ApiBaseUrl => $"https://api.{Region}";
    public string AuthBaseUrl => $"https://login.{Region}";
}
