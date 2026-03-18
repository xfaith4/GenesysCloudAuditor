using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services;

/// <summary>
/// Phase 2.3 — Hot Spot Ranking.
/// Identifies objects that appear as the subject of findings across two or more distinct
/// audit domains, surfacing the highest-priority investigation targets in a single ranked list.
///
/// Objects that appear in multiple domains (e.g. the same user in "Queue Serviceability",
/// "Inactive User", and "License Over-Provisioning") are likely contributing to cascading
/// failures and should be prioritised for remediation.
///
/// Operates entirely on the collected <see cref="AuditReportData"/> — no additional API
/// calls are made.
/// </summary>
public sealed class HotSpotAnalyzer
{
    /// <summary>
    /// Ranks objects that appear across multiple audit domains in the provided report.
    /// </summary>
    /// <param name="report">The completed audit report to inspect.</param>
    /// <param name="minDistinctDomains">
    /// Minimum number of distinct audit domains an object must appear in to be ranked
    /// as a hot spot. Default: 2.
    /// </param>
    public IReadOnlyList<HotSpotFinding> Analyze(
        AuditReportData report,
        int minDistinctDomains = 2)
    {
        // Key → accumulated entry describing all domains this object appears in
        var entries = new Dictionary<string, HotSpotEntry>(StringComparer.OrdinalIgnoreCase);

        void Register(string? id, string? name, string? type, string domain)
        {
            var key = !string.IsNullOrWhiteSpace(id) ? id!
                    : !string.IsNullOrWhiteSpace(name) ? name!
                    : null;
            if (key is null) return;

            if (!entries.TryGetValue(key, out var entry))
            {
                entry = new HotSpotEntry(
                    ObjectId: !string.IsNullOrWhiteSpace(id) ? id : null,
                    ObjectName: !string.IsNullOrWhiteSpace(name) ? name : null,
                    ObjectType: type);
                entries[key] = entry;
            }

            entry.AddDomain(domain);
        }

        // ── Gather all object references from every finding collection ────────

        foreach (var f in report.QueueServiceabilityFindings)
            Register(f.QueueId, f.QueueName, "Queue", "Queue Serviceability");

        foreach (var f in report.QueueFindings)
            Register(f.QueueId, f.QueueName, "Queue", "Queue Hygiene");

        foreach (var f in report.FlowFindings)
            Register(f.FlowId, f.FlowName, "Flow", "Stale Flow");

        foreach (var f in report.IvrFlowBindingFindings)
            Register(f.IvrId, f.IvrName, "IVR", "IVR Flow Binding");

        foreach (var f in report.UserTelephonyIntegrityFindings)
            Register(f.UserId, f.UserName, "User", "User Telephony Integrity");

        foreach (var f in report.SiteTopologyFindings)
            Register(f.ObjectId, f.ObjectName, f.ObjectType, "Site Topology");

        foreach (var f in report.GroupFindings)
            Register(f.GroupId, f.GroupName, "Group", "Group Hygiene");

        foreach (var f in report.DidFindings)
            Register(f.DidId, f.PhoneNumber, "DID", "DID Mismatch");

        foreach (var f in report.InactiveUserFindings)
            Register(f.UserId, f.UserName, "User", "Inactive User");

        foreach (var f in report.PromptHygieneFindings)
            Register(f.PromptId, f.PromptName, "Prompt", "Prompt Hygiene");

        foreach (var f in report.StaleLicenseFindings)
            Register(f.UserId, f.UserName, "User", "Stale License");

        foreach (var f in report.LicenseOverProvisioningFindings)
            Register(f.UserId, f.UserName, "User", "License Over-Provisioning");

        foreach (var f in report.RoleGroupOverlapFindings)
            Register(f.UserId, f.UserName, "User", "Role & Group Overlap");

        foreach (var f in report.ChangeAdjacencyFindings)
            Register(f.AffectedObjectId, f.AffectedObjectName, f.AffectedObjectType, "Change Adjacency");

        foreach (var f in report.FlappingDetectionFindings)
            Register(f.AffectedObjectId, f.AffectedObjectName, f.AffectedObjectType, "Flapping Detection");

        // ── Rank hot spots ────────────────────────────────────────────────────

        var threshold = Math.Max(1, minDistinctDomains);
        var ranked = entries.Values
            .Where(e => e.DistinctDomainCount >= threshold)
            .OrderByDescending(e => e.TotalFindingCount)
            .ThenByDescending(e => e.DistinctDomainCount)
            .ThenBy(e => e.ObjectName ?? e.ObjectId ?? string.Empty, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return ranked
            .Select((entry, index) => BuildFinding(entry, rank: index + 1))
            .ToList();
    }

    private static HotSpotFinding BuildFinding(HotSpotEntry entry, int rank)
    {
        var domains = entry.Domains
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var objectLabel = entry.ObjectName ?? entry.ObjectId ?? "unknown";
        var typeLabel = entry.ObjectType ?? "object";
        var domainList = string.Join(", ", domains);

        var severity = entry.TotalFindingCount >= 5 || entry.DistinctDomainCount >= 4
            ? FindingSeverity.High
            : entry.DistinctDomainCount >= 3 || entry.TotalFindingCount >= 3
            ? FindingSeverity.Medium
            : FindingSeverity.Low;

        var issue =
            $"{typeLabel} '{objectLabel}' appears in {entry.TotalFindingCount} finding(s) " +
            $"across {entry.DistinctDomainCount} distinct audit domain(s): {domainList}. " +
            "This concentration of anomalies indicates a high-priority investigation target that may " +
            "be contributing to cascading failures across multiple areas.";

        var recommendedAction =
            $"Investigate '{objectLabel}' holistically across all flagged domains. " +
            "Resolve the highest-severity finding first, then re-run the audit to confirm " +
            "that the fix has not introduced regressions in the remaining domains. " +
            "If contradictory API state persists after remediation, escalate to Genesys Care.";

        return new HotSpotFinding(
            Rank: rank,
            ObjectId: entry.ObjectId,
            ObjectName: entry.ObjectName,
            ObjectType: entry.ObjectType,
            TotalFindingCount: entry.TotalFindingCount,
            DistinctDomainCount: entry.DistinctDomainCount,
            AffectedDomains: domains,
            Issue: issue,
            Severity: severity,
            RecommendedAction: recommendedAction);
    }

    // ─── Internal accumulator ─────────────────────────────────────────────────

    private sealed class HotSpotEntry
    {
        private readonly List<string> _domains = [];

        public HotSpotEntry(string? ObjectId, string? ObjectName, string? ObjectType)
        {
            this.ObjectId = ObjectId;
            this.ObjectName = ObjectName;
            this.ObjectType = ObjectType;
        }

        public string? ObjectId { get; }
        public string? ObjectName { get; }
        public string? ObjectType { get; }

        /// <summary>All domain labels accumulated (may contain duplicates if an object appears multiple times in the same domain).</summary>
        public IReadOnlyList<string> Domains => _domains;

        public int TotalFindingCount => _domains.Count;
        public int DistinctDomainCount => _domains.Distinct(StringComparer.OrdinalIgnoreCase).Count();

        public void AddDomain(string domain) => _domains.Add(domain);
    }
}
