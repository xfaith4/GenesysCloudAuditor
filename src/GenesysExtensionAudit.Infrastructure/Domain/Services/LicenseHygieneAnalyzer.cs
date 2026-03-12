using System;
using System.Collections.Generic;
using System.Linq;
using GenesysExtensionAudit.Infrastructure.Reporting;

namespace GenesysExtensionAudit.Infrastructure.Domain.Services
{
    /// <summary>
    /// Pure domain logic for Phase 1 Identity & License Hygiene audits.
    /// All methods are stateless and testable without infrastructure dependencies.
    /// </summary>
    public sealed class LicenseHygieneAnalyzer
    {
        // ─── Input models ─────────────────────────────────────────────────────

        /// <summary>Minimal user record required for license hygiene analysis.</summary>
        public sealed record UserRecord(
            string UserId,
            string? UserName,
            string? Email,
            string? State,
            DateTimeOffset? TokenLastIssuedDate);

        /// <summary>License assignment record for a single user.</summary>
        public sealed record LicenseAssignment(
            string UserId,
            IReadOnlyList<string> Licenses);

        /// <summary>
        /// A role grant: a role assigned in a specific division to a subject.
        /// Used to describe both direct (user) and group-inherited role grants.
        /// </summary>
        public sealed record RoleGrant(
            string RoleId,
            string? RoleName,
            string? DivisionId,
            string? DivisionName);

        /// <summary>
        /// All role subjects resolved for a single user, separating direct assignments
        /// from group-inherited ones.
        /// </summary>
        public sealed record UserRoleSubjects(
            string UserId,
            IReadOnlyList<RoleGrant> DirectGrants,
            IReadOnlyList<GroupRoleSubject> GroupSubjects);

        /// <summary>
        /// Roles inherited by a user through a specific group membership.
        /// </summary>
        public sealed record GroupRoleSubject(
            string GroupId,
            string? GroupName,
            IReadOnlyList<RoleGrant> Grants);

        // ─── Audit 1: Stale License Usage ────────────────────────────────────

        /// <summary>
        /// Flags users who have a billable license assigned but have not logged in
        /// (i.e. no OAuth token was issued) within the specified threshold period.
        /// </summary>
        /// <param name="users">All user records fetched from the org.</param>
        /// <param name="licenseAssignments">License assignments from GET /api/v2/license/users.</param>
        /// <param name="thresholdDays">Days of inactivity to flag (default 60).</param>
        public IReadOnlyList<StaleLicenseFinding> AnalyzeStaleLicenses(
            IEnumerable<UserRecord> users,
            IEnumerable<LicenseAssignment> licenseAssignments,
            int thresholdDays = 60)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Abs(thresholdDays));
            var findings = new List<StaleLicenseFinding>();

            // Index license assignments by user ID for O(1) lookup
            var licenseByUserId = (licenseAssignments ?? Enumerable.Empty<LicenseAssignment>())
                .Where(la => la.UserId is not null)
                .ToDictionary(la => la.UserId, la => la, StringComparer.OrdinalIgnoreCase);

            foreach (var user in users ?? Enumerable.Empty<UserRecord>())
            {
                if (user.UserId is null) continue;

                // Only flag users who actually have a license
                if (!licenseByUserId.TryGetValue(user.UserId, out var la)) continue;
                var activeLicenses = (la.Licenses ?? [])
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();
                if (activeLicenses.Count == 0) continue;

                // Check inactivity: token never issued OR issued longer than threshold ago
                var tokenDate = user.TokenLastIssuedDate;
                if (tokenDate.HasValue && tokenDate.Value >= cutoff)
                    continue;

                var days = tokenDate.HasValue
                    ? (int)(DateTimeOffset.UtcNow - tokenDate.Value).TotalDays
                    : (int?)null;

                var issue = tokenDate.HasValue
                    ? $"User has {activeLicenses.Count} license(s) but last login was {days} days ago (threshold: {thresholdDays}). License may be wasted."
                    : $"User has {activeLicenses.Count} license(s) but has never logged in (no OAuth token record). License may be wasted.";

                findings.Add(new StaleLicenseFinding(
                    UserId: user.UserId,
                    UserName: user.UserName,
                    Email: user.Email,
                    State: user.State,
                    AssignedLicenses: activeLicenses,
                    TokenLastIssuedDate: tokenDate,
                    DaysSinceLogin: days,
                    Issue: issue));
            }

            return findings
                .OrderByDescending(f => f.DaysSinceLogin ?? int.MaxValue)
                .ThenBy(f => f.UserName ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        // ─── Audit 2: License Over-Provisioning ──────────────────────────────

        /// <summary>
        /// License name fragments that indicate a CX3-tier or premium license
        /// which includes WEM and Outbound features.
        /// </summary>
        public static readonly string[] Cx3LicenseFragments =
        [
            "PureCloud 3",
            "Genesys Cloud CX 3",
            "CX3",
            "WFM",
            "WFO",
            "Quality",
            "Outbound",
            "Dialer",
            "Campaign"
        ];

        /// <summary>
        /// Flags users assigned to a CX3-tier (WEM/Outbound) license who have no record
        /// of recent activity, indicating the premium features are likely unused.
        /// </summary>
        /// <param name="users">All user records fetched from the org.</param>
        /// <param name="licenseAssignments">License assignments from GET /api/v2/license/users.</param>
        /// <param name="inactivityThresholdDays">
        /// Days since last login to treat as "no recent usage" (default 60).
        /// </param>
        public IReadOnlyList<LicenseOverProvisioningFinding> AnalyzeLicenseOverProvisioning(
            IEnumerable<UserRecord> users,
            IEnumerable<LicenseAssignment> licenseAssignments,
            int inactivityThresholdDays = 60)
        {
            var cutoff = DateTimeOffset.UtcNow.AddDays(-Math.Abs(inactivityThresholdDays));
            var findings = new List<LicenseOverProvisioningFinding>();

            var licenseByUserId = (licenseAssignments ?? Enumerable.Empty<LicenseAssignment>())
                .Where(la => la.UserId is not null)
                .ToDictionary(la => la.UserId, la => la, StringComparer.OrdinalIgnoreCase);

            foreach (var user in users ?? Enumerable.Empty<UserRecord>())
            {
                if (user.UserId is null) continue;

                if (!licenseByUserId.TryGetValue(user.UserId, out var la)) continue;
                var allLicenses = (la.Licenses ?? [])
                    .Where(l => !string.IsNullOrWhiteSpace(l))
                    .ToList();

                // Identify premium (CX3/WEM/Outbound) licenses
                var premiumLicenses = allLicenses
                    .Where(IsPremiumLicense)
                    .ToList();

                if (premiumLicenses.Count == 0) continue;

                // Check for recent activity — if the user has been active recently,
                // we cannot conclude over-provisioning without actual feature telemetry.
                var tokenDate = user.TokenLastIssuedDate;
                if (tokenDate.HasValue && tokenDate.Value >= cutoff)
                    continue;

                var days = tokenDate.HasValue
                    ? (int)(DateTimeOffset.UtcNow - tokenDate.Value).TotalDays
                    : (int?)null;

                var licenseList = string.Join(", ", premiumLicenses);
                var issue = tokenDate.HasValue
                    ? $"User holds premium license(s) [{licenseList}] associated with WEM/Outbound features, " +
                      $"but last login was {days} days ago (threshold: {inactivityThresholdDays}). " +
                      "No evidence of feature usage — consider downgrading to a lower tier."
                    : $"User holds premium license(s) [{licenseList}] associated with WEM/Outbound features " +
                      "but has never logged in. Consider removing or reassigning these licenses.";

                findings.Add(new LicenseOverProvisioningFinding(
                    UserId: user.UserId,
                    UserName: user.UserName,
                    Email: user.Email,
                    State: user.State,
                    AllAssignedLicenses: allLicenses,
                    OverProvisionedLicenses: premiumLicenses,
                    TokenLastIssuedDate: tokenDate,
                    DaysSinceLogin: days,
                    Issue: issue,
                    RecommendedAction: "Review user's feature utilization. If WEM or Outbound features are not in use, " +
                                       "downgrade the license tier to CX1 or CX2 to reduce licensing costs."));
            }

            return findings
                .OrderByDescending(f => f.DaysSinceLogin ?? int.MaxValue)
                .ThenBy(f => f.UserName ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Returns true if the license name indicates a premium CX3/WEM/Outbound tier.</summary>
        public static bool IsPremiumLicense(string licenseName)
            => Cx3LicenseFragments.Any(frag =>
                licenseName.Contains(frag, StringComparison.OrdinalIgnoreCase));

        // ─── Audit 3: Role & Group Overlap ───────────────────────────────────

        /// <summary>
        /// Flags direct user role assignments that are already covered by a group-inherited
        /// role in the same division. The direct assignment is redundant and can be removed
        /// to simplify access management.
        /// </summary>
        /// <param name="userRoleSubjects">
        /// Pre-fetched role subjects for each user, separating USER (direct) from GROUP (inherited) grants.
        /// </param>
        /// <param name="userLookup">
        /// Optional lookup of user metadata (name, email, state) indexed by user ID.
        /// </param>
        public IReadOnlyList<RoleGroupOverlapFinding> AnalyzeRoleGroupOverlap(
            IEnumerable<UserRoleSubjects> userRoleSubjects,
            IReadOnlyDictionary<string, (string? Name, string? Email, string? State)>? userLookup = null)
        {
            var findings = new List<RoleGroupOverlapFinding>();

            foreach (var subjects in userRoleSubjects ?? Enumerable.Empty<UserRoleSubjects>())
            {
                if (subjects.UserId is null) continue;

                (string? Name, string? Email, string? State) meta = (null, null, null);
                userLookup?.TryGetValue(subjects.UserId, out meta);

                // Build a lookup of all group-granted roles: (roleId, divisionId) → (groupId, groupName)
                var groupGrantedRoles = new Dictionary<(string RoleId, string DivisionId), List<(string GroupId, string? GroupName)>>(
                    EqualityComparer<(string, string)>.Default);

                foreach (var groupSubject in subjects.GroupSubjects ?? [])
                {
                    if (groupSubject.GroupId is null) continue;

                    foreach (var grant in groupSubject.Grants ?? [])
                    {
                        if (grant.RoleId is null) continue;
                        var key = (
                            RoleId: grant.RoleId,
                            DivisionId: grant.DivisionId ?? string.Empty);

                        if (!groupGrantedRoles.TryGetValue(key, out var groups))
                        {
                            groups = new List<(string, string?)>();
                            groupGrantedRoles[key] = groups;
                        }
                        groups.Add((groupSubject.GroupId, groupSubject.GroupName));
                    }
                }

                // For each direct (USER) grant, check if it's already covered by a group
                foreach (var directGrant in subjects.DirectGrants ?? [])
                {
                    if (directGrant.RoleId is null) continue;

                    var key = (
                        RoleId: directGrant.RoleId,
                        DivisionId: directGrant.DivisionId ?? string.Empty);

                    if (!groupGrantedRoles.TryGetValue(key, out var coveringGroups)) continue;

                    // Emit one finding per covering group (usually just one, but handle multiple)
                    foreach (var (groupId, groupName) in coveringGroups)
                    {
                        findings.Add(new RoleGroupOverlapFinding(
                            UserId: subjects.UserId,
                            UserName: meta.Name,
                            Email: meta.Email,
                            UserState: meta.State,
                            RoleId: directGrant.RoleId,
                            RoleName: directGrant.RoleName,
                            DivisionId: directGrant.DivisionId,
                            DivisionName: directGrant.DivisionName,
                            GroupId: groupId,
                            GroupName: groupName,
                            Issue: $"Role '{directGrant.RoleName ?? directGrant.RoleId}' in division '{directGrant.DivisionName ?? directGrant.DivisionId ?? "Home"}' " +
                                   $"is directly assigned to this user but is already inherited through group '{groupName ?? groupId}'. " +
                                   "The direct assignment is redundant.",
                            RecommendedAction: $"Remove the direct role assignment for '{directGrant.RoleName ?? directGrant.RoleId}' from this user. " +
                                               $"Access is already provided via group '{groupName ?? groupId}'."));
                    }
                }
            }

            return findings
                .OrderBy(f => f.UserName ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.RoleName ?? "", StringComparer.OrdinalIgnoreCase)
                .ThenBy(f => f.DivisionName ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }
}
