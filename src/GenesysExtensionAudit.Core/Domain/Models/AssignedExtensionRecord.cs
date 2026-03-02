namespace GenesysExtensionAudit.Domain.Models;

/// <summary>
/// Domain record representing a single Edge telephony extension assignment.
/// </summary>
public sealed record AssignedExtensionRecord(
    string ExtensionId,
    string? RawExtension,
    string? NormalizedExtension,
    string? AssignedToType,
    string? AssignedToId);
