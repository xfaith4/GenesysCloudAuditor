namespace GenesysExtensionAudit.Application;

/// <summary>
/// Input parameters for a single audit run.
/// Passed from the ViewModel to IAuditRunner.
/// </summary>
public sealed class AuditRunOptions
{
    /// <summary>Records per page when paging Genesys endpoints (1–500).</summary>
    public int PageSize { get; init; } = 100;

    /// <summary>
    /// When false (default): requests /api/v2/users with &amp;state=active.
    /// When true: requests all users (active + inactive).
    /// </summary>
    public bool IncludeInactiveUsers { get; init; }
}
