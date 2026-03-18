using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using Microsoft.Extensions.Logging;

namespace GenesysExtensionAudit.Services;

public interface IAuditLogCatalogCache
{
    /// <summary>Returns a flat sorted list of service names.</summary>
    Task<IReadOnlyList<string>> GetOrRefreshAsync(bool forceRefresh, CancellationToken ct);

    /// <summary>
    /// Returns the full structured service catalog — service names with their associated
    /// entity types and actions. Used to populate dependent filter dropdowns in the UI.
    /// </summary>
    Task<IReadOnlyList<AuditServiceInfo>> GetOrRefreshCatalogAsync(bool forceRefresh, CancellationToken ct);

    Task WarmAsync(CancellationToken ct);
}

public sealed class AuditLogCatalogCache : IAuditLogCatalogCache
{
    private readonly IGenesysAuditLogsClient _auditLogsClient;
    private readonly ILogger<AuditLogCatalogCache> _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyList<AuditServiceInfo> _cachedCatalog = [];
    private bool _isLoaded;

    public AuditLogCatalogCache(
        IGenesysAuditLogsClient auditLogsClient,
        ILogger<AuditLogCatalogCache> logger)
    {
        _auditLogsClient = auditLogsClient ?? throw new ArgumentNullException(nameof(auditLogsClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<IReadOnlyList<string>> GetOrRefreshAsync(bool forceRefresh, CancellationToken ct)
    {
        var catalog = await GetOrRefreshCatalogAsync(forceRefresh, ct).ConfigureAwait(false);
        return catalog.Select(s => s.ServiceName).ToList();
    }

    public Task<IReadOnlyList<AuditServiceInfo>> GetOrRefreshCatalogAsync(bool forceRefresh, CancellationToken ct)
        => LoadCoreAsync(forceRefresh, ct);

    public async Task WarmAsync(CancellationToken ct)
    {
        try
        {
            await LoadCoreAsync(forceRefresh: false, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Audit-log catalog warm-up failed.");
        }
    }

    private async Task<IReadOnlyList<AuditServiceInfo>> LoadCoreAsync(bool forceRefresh, CancellationToken ct)
    {
        if (!forceRefresh && _isLoaded)
            return _cachedCatalog;

        await _gate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (!forceRefresh && _isLoaded)
                return _cachedCatalog;

            var catalog = await _auditLogsClient.GetServiceCatalogAsync(ct).ConfigureAwait(false);

            _cachedCatalog = catalog;
            _isLoaded = true;
            return _cachedCatalog;
        }
        finally
        {
            _gate.Release();
        }
    }
}
