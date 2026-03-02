namespace GenesysExtensionAudit.Domain.Models;

/// <summary>
/// Domain record representing a single user's work-phone extension as read from their Genesys Cloud profile.
/// </summary>
public sealed record UserProfileExtensionRecord(
    string UserId,
    string? DisplayName,
    string? State,
    string? RawExtension,
    string? NormalizedExtension);
