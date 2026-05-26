using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Metadata record for a sentinel rule: the documented basis, provenance source,
/// rule identifier, and operational context that justifies emitting the signal.
/// Each rule maps to one or more <see cref="AuditLogSignalCode"/> constants.
/// </summary>
public sealed record SentinelRuleMetadata(
    /// <summary>Stable unique rule identifier used for cross-reference and export.</summary>
    string RuleId,
    /// <summary>Human-readable rule name for triage and operator display.</summary>
    string RuleName,
    /// <summary>Audit domain this rule primarily covers (e.g. Security, Telephony, Routing).</summary>
    string Domain,
    /// <summary>Display category for grouping signals in the sentinel worksheet.</summary>
    string SignalCategory,
    /// <summary>Default severity if the analyzer does not escalate based on volume or pattern.</summary>
    FindingSeverity DefaultSeverity,
    /// <summary>Name of the documentation, guide, or internal standard that justifies this rule.</summary>
    string ProvenanceSource,
    /// <summary>Plain-language summary of the documented principle or expected state this rule enforces.</summary>
    string ProvenanceBasis,
    /// <summary>Recommended review cadence (e.g. "Per Change / On Detection", "Weekly").</summary>
    string ReviewCadence,
    /// <summary>Role or team responsible for reviewing and acting on signals from this rule.</summary>
    string OwnerRole);

/// <summary>
/// Registry that maps every <see cref="AuditLogSignalCode"/> constant to its
/// <see cref="SentinelRuleMetadata"/> — the rule ID, documented basis, and provenance
/// that justify emitting the signal.
///
/// This registry is the implementation of the Phase 2.4 "Rule registry and metadata model"
/// and "Source/provenance tracking" roadmap items.
/// </summary>
public static class SentinelRuleRegistry
{
    private static readonly IReadOnlyDictionary<string, SentinelRuleMetadata> Registry =
        new Dictionary<string, SentinelRuleMetadata>(StringComparer.OrdinalIgnoreCase)
        {
            [AuditLogSignalCode.AccessControlChange] = new(
                RuleId: "SENTINEL-SEC-001",
                RuleName: "Access Control Change",
                Domain: "Security / Identity",
                SignalCategory: "Security / Access Control",
                DefaultSeverity: FindingSeverity.High,
                ProvenanceSource: "Genesys Cloud Admin Guide — Authorization",
                ProvenanceBasis: "Role and permission changes should be reviewed and approved through a formal change management process to prevent unauthorized scope expansion or inadvertent access reduction.",
                ReviewCadence: "Per Change / On Detection",
                OwnerRole: "Security Administrator"),

            [AuditLogSignalCode.AdminRoleGrantRevoke] = new(
                RuleId: "SENTINEL-SEC-002",
                RuleName: "Admin Role Grant or Revoke",
                Domain: "Security / Identity",
                SignalCategory: "Security / Privileged Access",
                DefaultSeverity: FindingSeverity.High,
                ProvenanceSource: "Genesys Cloud Admin Guide — Roles and Permissions — Least Privilege",
                ProvenanceBasis: "Explicit role grants and revocations directly alter administrative reach across the tenant. These changes should be infrequent, approved, and consistent with the principle of least privilege. Unexpected grants or revocations may indicate unauthorized access escalation or an automated process operating outside its intended scope.",
                ReviewCadence: "Per Change / On Detection",
                OwnerRole: "Security Administrator"),

            [AuditLogSignalCode.DivisionScopeChange] = new(
                RuleId: "SENTINEL-SEC-003",
                RuleName: "Division or Scope Change",
                Domain: "Security / Identity",
                SignalCategory: "Security / Scope",
                DefaultSeverity: FindingSeverity.High,
                ProvenanceSource: "Genesys Cloud Admin Guide — Organizations and Divisions",
                ProvenanceBasis: "Division scope changes affect routing reach, administrative visibility, and data segregation. Unreviewed scope changes can create routing gaps, expose resources across organizational boundaries, or silently remove intended access restrictions.",
                ReviewCadence: "Per Change / On Detection",
                OwnerRole: "Platform Administrator"),

            [AuditLogSignalCode.OAuthClientChange] = new(
                RuleId: "SENTINEL-SEC-004",
                RuleName: "OAuth Client Change",
                Domain: "Security / Integration",
                SignalCategory: "Security / OAuth",
                DefaultSeverity: FindingSeverity.High,
                ProvenanceSource: "Genesys Cloud Admin Guide — OAuth 2.0 Clients",
                ProvenanceBasis: "OAuth client modifications affect what APIs can be called on behalf of the organization and which credentials are trusted. Credential or permission changes should align with approved automation patterns and be reviewed before treating adjacent findings as platform-side behavior.",
                ReviewCadence: "Per Change / On Detection",
                OwnerRole: "Security Administrator"),

            [AuditLogSignalCode.QueueMembershipChurn] = new(
                RuleId: "SENTINEL-ROUTING-001",
                RuleName: "Queue Membership Churn",
                Domain: "Routing / Queue Ownership",
                SignalCategory: "Routing / Queue Membership",
                DefaultSeverity: FindingSeverity.Medium,
                ProvenanceSource: "Genesys Cloud Best Practices — Queue Design and Management",
                ProvenanceBasis: "Stable queue membership is a prerequisite for predictable routing coverage. Repeated membership changes within a short window indicate potential automation conflict, competing admin activity, or an unstable integration that can degrade service levels and skill coverage without producing obvious errors.",
                ReviewCadence: "Weekly / On Detection",
                OwnerRole: "Contact Center Administrator"),

            [AuditLogSignalCode.FlowPublicationChange] = new(
                RuleId: "SENTINEL-ROUTING-002",
                RuleName: "Flow Publication Change",
                Domain: "Routing / Architect Flows",
                SignalCategory: "Routing / Flow Publication",
                DefaultSeverity: FindingSeverity.Medium,
                ProvenanceSource: "Genesys Cloud Architect — Flow Management Best Practices",
                ProvenanceBasis: "Flow publication should be stable and change-managed. A single publish may be routine, but repeated publish, rollback, or restore activity within a short window suggests uncontrolled change, automation conflict, or a failed deployment that is affecting live routing behavior.",
                ReviewCadence: "Per Change / On Detection",
                OwnerRole: "Contact Center Engineer"),

            [AuditLogSignalCode.PlatformConfigChange] = new(
                RuleId: "SENTINEL-INFRA-001",
                RuleName: "Platform Infrastructure Configuration Change",
                Domain: "Infrastructure / Topology",
                SignalCategory: "Infrastructure / Platform Config",
                DefaultSeverity: FindingSeverity.Medium,
                ProvenanceSource: "Genesys Cloud Admin Guide — Telephony and Routing Administration",
                ProvenanceBasis: "Changes to core platform configuration objects — including sites, edges, trunks, IVR routing tables, telephony locations, and schedule groups — can cause routing disruptions or silent failures if made without adequate testing or change management review. These changes should be correlated against active topology and telephony findings.",
                ReviewCadence: "Per Change / On Detection",
                OwnerRole: "Platform Administrator"),
        };

    /// <summary>
    /// Returns the <see cref="SentinelRuleMetadata"/> for the given signal code,
    /// or <see langword="null"/> if the code is not registered.
    /// </summary>
    public static SentinelRuleMetadata? GetMetadata(string? signalCode)
    {
        if (string.IsNullOrWhiteSpace(signalCode))
            return null;

        return Registry.TryGetValue(signalCode, out var meta) ? meta : null;
    }

    /// <summary>
    /// Returns all registered sentinel rule metadata entries, ordered by rule ID.
    /// </summary>
    public static IReadOnlyList<SentinelRuleMetadata> GetAll()
        => Registry.Values
            .OrderBy(m => m.RuleId, StringComparer.OrdinalIgnoreCase)
            .ToList();
}
