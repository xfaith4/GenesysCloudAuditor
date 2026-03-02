namespace GenesysExtensionAudit.Application;

public interface IAuditRunner
{
    /// <summary>
    /// Fetches all users and extensions from Genesys Cloud, runs cross-reference analysis,
    /// and reports progress via <paramref name="progress"/>.
    /// </summary>
    Task RunAsync(AuditRunOptions options, IProgress<AuditProgress> progress, CancellationToken ct);
}
