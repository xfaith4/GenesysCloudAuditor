using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Domain.Models;
using GenesysExtensionAudit.Domain.Paging;
using GenesysExtensionAudit.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using Microsoft.Extensions.Logging;
using UserDto = GenesysExtensionAudit.Infrastructure.Genesys.Dtos.GenesysUserDto;

namespace GenesysExtensionAudit.Infrastructure.Application;

/// <summary>
/// Orchestrates a full audit run:
///   1. Fetch all users (paginated)
///   2. Fetch all Edge extensions (paginated)
///   3. Map DTOs → domain records
///   4. Analyze (cross-reference) → AuditFindings
/// Lives in Infrastructure (not Core) so it can depend on concrete clients
/// without creating a circular project reference.
/// Registered under the IAuditRunner interface from Core.
/// </summary>
public sealed class AuditRunner : IAuditRunner
{
    private readonly IGenesysUsersClient _usersClient;
    private readonly IGenesysExtensionsClient _extensionsClient;
    private readonly IPaginator _paginator;
    private readonly IAuditAnalyzer _analyzer;
    private readonly IExtensionNormalizer _normalizer;
    private readonly ILogger<AuditRunner> _logger;

    public AuditRunner(
        IGenesysUsersClient usersClient,
        IGenesysExtensionsClient extensionsClient,
        IPaginator paginator,
        IAuditAnalyzer analyzer,
        IExtensionNormalizer normalizer,
        ILogger<AuditRunner> logger)
    {
        _usersClient = usersClient ?? throw new ArgumentNullException(nameof(usersClient));
        _extensionsClient = extensionsClient ?? throw new ArgumentNullException(nameof(extensionsClient));
        _paginator = paginator ?? throw new ArgumentNullException(nameof(paginator));
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
        _normalizer = normalizer ?? throw new ArgumentNullException(nameof(normalizer));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task RunAsync(
        AuditRunOptions options,
        IProgress<AuditProgress> progress,
        CancellationToken ct)
    {
        var pageSize = Math.Clamp(options.PageSize, 1, 500);

        // ── Phase 1: Fetch users ──────────────────────────────────────────
        progress.Report(new AuditProgress
        {
            Percent = 0,
            Status = "Running audit...",
            Message = "Fetching users..."
        });

        _logger.LogInformation("Audit started. PageSize={PageSize}, IncludeInactive={IncludeInactive}",
            pageSize, options.IncludeInactiveUsers);

        var userDtos = await _paginator.FetchAllAsync(
            pageNumber => _usersClient.GetUsersPageAsync(pageNumber, pageSize, options.IncludeInactiveUsers, ct),
            ct).ConfigureAwait(false);

        progress.Report(new AuditProgress
        {
            Percent = 40,
            Message = $"Fetched {userDtos.Count} users. Fetching extensions..."
        });

        // ── Phase 2: Fetch extensions ─────────────────────────────────────
        var extDtos = await _paginator.FetchAllAsync(
            pageNumber => _extensionsClient.GetExtensionsPageAsync(pageNumber, pageSize, ct),
            ct).ConfigureAwait(false);

        progress.Report(new AuditProgress
        {
            Percent = 70,
            Message = $"Fetched {extDtos.Count} extensions. Analyzing..."
        });

        // ── Phase 3: Map DTOs → domain records ───────────────────────────
        var userRecords = userDtos
            .Where(u => u.Id is not null)
            .Select(u =>
            {
                var rawExt = ExtractWorkPhoneExtension(u);
                return new UserProfileExtensionRecord(
                    UserId: u.Id!,
                    DisplayName: u.Name,
                    State: u.State,
                    RawExtension: rawExt,
                    NormalizedExtension: _normalizer.Normalize(rawExt));
            })
            .ToList();

        var assignedRecords = extDtos
            .Where(e => e.Id is not null)
            .Select(e => new AssignedExtensionRecord(
                ExtensionId: e.Id!,
                RawExtension: e.Extension,
                NormalizedExtension: _normalizer.Normalize(e.Extension),
                AssignedToType: e.AssignedTo?.Type,
                AssignedToId: e.AssignedTo?.Id))
            .ToList();

        // ── Phase 4: Analyze ──────────────────────────────────────────────
        var findings = _analyzer.Analyze(userRecords, assignedRecords);

        _logger.LogInformation(
            "Audit complete. Users={Users}, Extensions={Extensions}, " +
            "DupProfiles={DupProfiles}, DupAssigned={DupAssigned}, ProfileOnly={ProfileOnly}",
            userRecords.Count, assignedRecords.Count,
            findings.DuplicateProfileExtensions.Count,
            findings.DuplicateAssignedExtensions.Count,
            findings.ProfileOnlyExtensions.Count);

        progress.Report(new AuditProgress
        {
            Percent = 100,
            Status = "Audit completed successfully.",
            Message = $"Users: {userRecords.Count}  |  Extensions: {assignedRecords.Count}  |  " +
                      $"Dup profiles: {findings.DuplicateProfileExtensions.Count}  |  " +
                      $"Profile-only: {findings.ProfileOnlyExtensions.Count}"
        });
    }

    /// <summary>
    /// Extracts the work-phone extension from a user's primaryContactInfo.
    /// Prefers a dedicated "extension" field on a work PHONE entry;
    /// falls back to the address field if the extension field is absent.
    /// </summary>
    private static string? ExtractWorkPhoneExtension(UserDto user)
    {
        if (user.PrimaryContactInfo is null or { Count: 0 }) return null;

        // Prefer work + PHONE entry; any PHONE entry as fallback
        var candidates = user.PrimaryContactInfo
            .Where(ci => string.Equals(ci.MediaType, "PHONE", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(ci => string.Equals(ci.Type, "work", StringComparison.OrdinalIgnoreCase) ? 1 : 0);

        foreach (var ci in candidates)
        {
            if (!string.IsNullOrWhiteSpace(ci.Extension))
                return ci.Extension.Trim();
        }

        return null;
    }
}
