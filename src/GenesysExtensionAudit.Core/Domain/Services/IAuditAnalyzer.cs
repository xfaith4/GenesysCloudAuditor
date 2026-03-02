using GenesysExtensionAudit.Domain.Models;

namespace GenesysExtensionAudit.Domain.Services;

/// <summary>
/// Computes audit findings by cross-referencing normalized user profile
/// extension values against the Edge telephony assignment list.
/// </summary>
public interface IAuditAnalyzer
{
    AuditFindings Analyze(
        IReadOnlyList<UserProfileExtensionRecord> userProfileExtensions,
        IReadOnlyList<AssignedExtensionRecord> assignedExtensions);
}
