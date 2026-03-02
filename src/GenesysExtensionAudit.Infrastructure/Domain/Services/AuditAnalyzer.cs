using GenesysExtensionAudit.Domain.Models;
using GenesysExtensionAudit.Domain.Services;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Cross-references normalized user profile extension values against
/// the Edge telephony assignment list to produce audit findings.
/// </summary>
public sealed class AuditAnalyzer : IAuditAnalyzer
{
    public AuditFindings Analyze(
        IReadOnlyList<UserProfileExtensionRecord> userProfileExtensions,
        IReadOnlyList<AssignedExtensionRecord> assignedExtensions)
    {
        // Index profiles by normalized extension
        var profilesByExt = userProfileExtensions
            .Where(u => u.NormalizedExtension is not null)
            .GroupBy(u => u.NormalizedExtension!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<UserProfileExtensionRecord>)g.ToList(), StringComparer.Ordinal);

        // Index assignments by normalized extension
        var assignedByExt = assignedExtensions
            .Where(a => a.NormalizedExtension is not null)
            .GroupBy(a => a.NormalizedExtension!, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<AssignedExtensionRecord>)g.ToList(), StringComparer.Ordinal);

        // (1) Duplicate profile extensions: same normalized ext on multiple distinct users
        var dupProfiles = profilesByExt
            .Where(kvp => kvp.Value.Select(u => u.UserId).Distinct(StringComparer.OrdinalIgnoreCase).Count() >= 2)
            .Select(kvp => new DuplicateProfileExtension(
                kvp.Key,
                kvp.Value.OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(f => f.NormalizedExtension, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // (2) Duplicate assigned extensions: same normalized ext on multiple assignments
        var dupAssigned = assignedByExt
            .Where(kvp => kvp.Value.Count >= 2)
            .Select(kvp => new DuplicateAssignedExtension(
                kvp.Key,
                kvp.Value.OrderBy(a => a.AssignedToId, StringComparer.OrdinalIgnoreCase).ToList()))
            .OrderBy(f => f.NormalizedExtension, StringComparer.OrdinalIgnoreCase)
            .ToList();

        // (3) Profile-only extensions: set on a profile but not in telephony assignments
        var profileOnly = profilesByExt
            .Where(kvp => !assignedByExt.ContainsKey(kvp.Key))
            .SelectMany(kvp => kvp.Value.Select(u => new UnassignedProfileExtension(kvp.Key, u)))
            .OrderBy(f => f.NormalizedExtension, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.User.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new AuditFindings(
            TotalUsersConsidered: userProfileExtensions.Count,
            TotalAssignmentsConsidered: assignedExtensions.Count,
            DuplicateProfileExtensions: dupProfiles,
            DuplicateAssignedExtensions: dupAssigned,
            ProfileOnlyExtensions: profileOnly);
    }
}
