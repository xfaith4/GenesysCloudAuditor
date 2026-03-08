namespace GenesysExtensionAudit.Application;

/// <summary>
/// Severity scale for audit findings.
/// Maps to the action model defined in ROADMAP.md Phase 3.1.
/// </summary>
public enum FindingSeverity
{
    /// <summary>Definite known-bad state; immediate attention required.</summary>
    Critical,

    /// <summary>Likely broken or causing degraded service; should be prioritized.</summary>
    High,

    /// <summary>Hygiene issue or potential problem; review recommended.</summary>
    Medium,

    /// <summary>Low-risk informational issue.</summary>
    Low,

    /// <summary>Informational export; not a problem signal.</summary>
    Info
}

/// <summary>
/// Confidence level that the finding reflects a real problem versus noise.
/// </summary>
public enum FindingConfidence
{
    /// <summary>Multiple API sources agree; high certainty this is a real problem.</summary>
    High,

    /// <summary>Single source or partially corroborated across API surfaces.</summary>
    Medium,

    /// <summary>Possible false positive; further investigation recommended before acting.</summary>
    Low
}

/// <summary>
/// Recommended ownership and remediation category for a finding.
/// Determines who should act and what kind of action is appropriate.
/// </summary>
public enum FindingCategory
{
    /// <summary>
    /// Likely resolvable directly by the tenant admin or engineering team.
    /// No platform involvement expected.
    /// </summary>
    LocalConfigFix,

    /// <summary>
    /// A recent configuration change may be the root cause.
    /// Review recent changes; rollback may be warranted.
    /// </summary>
    ChangeReviewRequired,

    /// <summary>
    /// Evidence is weak or may be transient. Continue observation.
    /// Re-run the audit after the next expected change cycle.
    /// </summary>
    MonitorRerun,

    /// <summary>
    /// Evidence suggests a probable platform-side defect or unresolvable inconsistent state.
    /// Escalate to Genesys Care with the evidence packet.
    /// </summary>
    EscalateToGenesysCare
}
