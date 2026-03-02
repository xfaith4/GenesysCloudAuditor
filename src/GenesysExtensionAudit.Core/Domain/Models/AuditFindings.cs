namespace GenesysExtensionAudit.Domain.Models;

/// <summary>Multiple user profiles share the same normalized extension.</summary>
public sealed record DuplicateProfileExtension(
    string NormalizedExtension,
    IReadOnlyList<UserProfileExtensionRecord> Users);

/// <summary>Multiple telephony assignments share the same normalized extension.</summary>
public sealed record DuplicateAssignedExtension(
    string NormalizedExtension,
    IReadOnlyList<AssignedExtensionRecord> Assignments);

/// <summary>A user profile has an extension value that has no corresponding telephony assignment.</summary>
public sealed record UnassignedProfileExtension(
    string NormalizedExtension,
    UserProfileExtensionRecord User);

/// <summary>
/// Aggregated output from a completed audit run.
/// </summary>
public sealed record AuditFindings(
    int TotalUsersConsidered,
    int TotalAssignmentsConsidered,
    IReadOnlyList<DuplicateProfileExtension> DuplicateProfileExtensions,
    IReadOnlyList<DuplicateAssignedExtension> DuplicateAssignedExtensions,
    IReadOnlyList<UnassignedProfileExtension> ProfileOnlyExtensions);
