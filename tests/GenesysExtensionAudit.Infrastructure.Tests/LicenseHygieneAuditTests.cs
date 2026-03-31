using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

/// <summary>
/// Unit tests for <see cref="LicenseHygieneAnalyzer"/>:
/// Audit 1 — Stale License Usage,
/// Audit 2 — License Over-Provisioning,
/// Audit 3 — Role & Group Overlap.
/// </summary>
public sealed class LicenseHygieneAuditTests
{
    // ─── Helpers ────────────────────────────────────────────────────────────

    private static LicenseHygieneAnalyzer.UserRecord User(
        string id,
        string? name = null,
        string state = "active",
        DateTimeOffset? tokenLastIssued = null)
        => new(id, name ?? $"Synthetic User {id}", $"synthetic-{id}@example.invalid", state, tokenLastIssued);

    private static LicenseHygieneAnalyzer.LicenseAssignment License(
        string userId,
        params string[] licenses)
        => new(userId, licenses);

    private static LicenseHygieneAnalyzer.UserRoleSubjects RoleSubjects(
        string userId,
        IReadOnlyList<LicenseHygieneAnalyzer.RoleGrant> directGrants,
        IReadOnlyList<LicenseHygieneAnalyzer.GroupRoleSubject> groupSubjects)
        => new(userId, directGrants, groupSubjects);

    private static LicenseHygieneAnalyzer.RoleGrant Grant(
        string roleId, string roleName, string divisionId = "div1", string divisionName = "Home")
        => new(roleId, roleName, divisionId, divisionName);

    private static LicenseHygieneAnalyzer.GroupRoleSubject GroupSubject(
        string groupId, string groupName, params LicenseHygieneAnalyzer.RoleGrant[] grants)
        => new(groupId, groupName, grants);

    private static readonly DateTimeOffset RecentLogin = DateTimeOffset.UtcNow.AddDays(-10);
    private static readonly DateTimeOffset StaleLogin = DateTimeOffset.UtcNow.AddDays(-90);

    // ─── Audit 1: Stale License Usage ────────────────────────────────────────

    [Fact]
    public void StaleLicense_NoFindings_WhenUserLoggedInRecently()
    {
        var users = new[] { User("u1", tokenLastIssued: RecentLogin) };
        var licenses = new[] { License("u1", "PureCloud 3") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeStaleLicenses(users, licenses, thresholdDays: 60);

        Assert.Empty(findings);
    }

    [Fact]
    public void StaleLicense_FindingReported_WhenLicensedUserHasNotLoggedIn()
    {
        var users = new[] { User("u1", tokenLastIssued: StaleLogin) };
        var licenses = new[] { License("u1", "PureCloud 3") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeStaleLicenses(users, licenses, thresholdDays: 60);

        Assert.Single(findings);
        var finding = findings[0];
        Assert.Equal("u1", finding.UserId);
        Assert.Contains("PureCloud 3", finding.AssignedLicenses);
        Assert.NotNull(finding.DaysSinceLogin);
        Assert.True(finding.DaysSinceLogin > 60);
    }

    [Fact]
    public void StaleLicense_NoFinding_WhenUserHasNoLicense()
    {
        var users = new[] { User("u1", tokenLastIssued: StaleLogin) };
        var licenses = Array.Empty<LicenseHygieneAnalyzer.LicenseAssignment>();

        var findings = new LicenseHygieneAnalyzer().AnalyzeStaleLicenses(users, licenses, thresholdDays: 60);

        Assert.Empty(findings);
    }

    [Fact]
    public void StaleLicense_FindingReported_WhenUserNeverLoggedIn()
    {
        var users = new[] { User("u1", tokenLastIssued: null) };
        var licenses = new[] { License("u1", "PureCloud 1") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeStaleLicenses(users, licenses, thresholdDays: 60);

        Assert.Single(findings);
        Assert.Null(findings[0].DaysSinceLogin);
        Assert.Null(findings[0].TokenLastIssuedDate);
        Assert.Contains("never logged in", findings[0].Issue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaleLicense_FindingReported_WhenMultipleStaleUsers()
    {
        var users = new[]
        {
            User("u1", tokenLastIssued: StaleLogin),
            User("u2", tokenLastIssued: RecentLogin),
            User("u3", tokenLastIssued: StaleLogin),
        };
        var licenses = new[]
        {
            License("u1", "PureCloud 2"),
            License("u2", "PureCloud 2"),
            License("u3", "PureCloud 2"),
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeStaleLicenses(users, licenses, thresholdDays: 60);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.NotEqual("u2", f.UserId));
    }

    [Fact]
    public void StaleLicense_JustUnderThreshold_IsNotFlagged()
    {
        // User logged in 59 days ago — within the threshold window
        var users = new[] { User("u1", tokenLastIssued: DateTimeOffset.UtcNow.AddDays(-59)) };
        var licenses = new[] { License("u1", "PureCloud 1") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeStaleLicenses(users, licenses, thresholdDays: 60);

        Assert.Empty(findings);
    }

    [Fact]
    public void StaleLicense_LicenseWithEmptyString_IsIgnored()
    {
        // A license entry that is just whitespace should not count
        var users = new[] { User("u1", tokenLastIssued: StaleLogin) };
        var licenses = new[] { License("u1", "  ", "") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeStaleLicenses(users, licenses, thresholdDays: 60);

        Assert.Empty(findings);
    }

    // ─── Audit 2: License Over-Provisioning ──────────────────────────────────

    [Fact]
    public void LicenseOverProvisioning_NoFindings_WhenOnlyLowTierLicense()
    {
        var users = new[] { User("u1", tokenLastIssued: StaleLogin) };
        var licenses = new[] { License("u1", "PureCloud 1") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeLicenseOverProvisioning(users, licenses);

        Assert.Empty(findings);
    }

    [Fact]
    public void LicenseOverProvisioning_FindingReported_WhenCx3UserNeverLogged()
    {
        var users = new[] { User("u1", tokenLastIssued: null) };
        var licenses = new[] { License("u1", "PureCloud 3") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeLicenseOverProvisioning(users, licenses);

        Assert.Single(findings);
        Assert.Equal("u1", findings[0].UserId);
        Assert.Contains("PureCloud 3", findings[0].OverProvisionedLicenses);
    }

    [Fact]
    public void LicenseOverProvisioning_FindingReported_WhenWfmLicenseIsUnused()
    {
        var users = new[] { User("u1", tokenLastIssued: StaleLogin) };
        var licenses = new[] { License("u1", "PureCloud 1", "PureCloud 1 WFO Digital") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeLicenseOverProvisioning(users, licenses);

        Assert.Single(findings);
        Assert.Contains("WFO", findings[0].OverProvisionedLicenses[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LicenseOverProvisioning_NoFinding_WhenCx3UserActivelyLogging()
    {
        var users = new[] { User("u1", tokenLastIssued: RecentLogin) };
        var licenses = new[] { License("u1", "PureCloud 3") };

        var findings = new LicenseHygieneAnalyzer().AnalyzeLicenseOverProvisioning(users, licenses);

        Assert.Empty(findings);
    }

    [Fact]
    public void LicenseOverProvisioning_IsPremiumLicense_MatchesCx3Fragments()
    {
        Assert.True(LicenseHygieneAnalyzer.IsPremiumLicense("PureCloud 3"));
        Assert.True(LicenseHygieneAnalyzer.IsPremiumLicense("Genesys Cloud CX 3 Digital"));
        Assert.True(LicenseHygieneAnalyzer.IsPremiumLicense("PureCloud 1 WFO Digital"));
        Assert.True(LicenseHygieneAnalyzer.IsPremiumLicense("Outbound Dialer Add-On"));
        Assert.True(LicenseHygieneAnalyzer.IsPremiumLicense("WFM Workforce Management"));
        Assert.False(LicenseHygieneAnalyzer.IsPremiumLicense("PureCloud 1"));
        Assert.False(LicenseHygieneAnalyzer.IsPremiumLicense("PureCloud 2"));
        Assert.False(LicenseHygieneAnalyzer.IsPremiumLicense(""));
    }

    // ─── Audit 3: Role & Group Overlap ───────────────────────────────────────

    [Fact]
    public void RoleGroupOverlap_NoFindings_WhenNoGroupSubjects()
    {
        var subjects = new[]
        {
            RoleSubjects("u1",
                directGrants: [Grant("role-admin", "Admin")],
                groupSubjects: [])
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeRoleGroupOverlap(subjects);

        Assert.Empty(findings);
    }

    [Fact]
    public void RoleGroupOverlap_NoFindings_WhenGroupHasDifferentRole()
    {
        var subjects = new[]
        {
            RoleSubjects("u1",
                directGrants: [Grant("role-admin", "Admin", "div1")],
                groupSubjects: [GroupSubject("g1", "Agents", Grant("role-agent", "Agent", "div1"))])
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeRoleGroupOverlap(subjects);

        Assert.Empty(findings);
    }

    [Fact]
    public void RoleGroupOverlap_FindingReported_WhenDirectRoleAlsoCoveredByGroup()
    {
        var subjects = new[]
        {
            RoleSubjects("u1",
                directGrants: [Grant("role-admin", "Admin", "div1")],
                groupSubjects: [GroupSubject("g1", "IT Admins", Grant("role-admin", "Admin", "div1"))])
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeRoleGroupOverlap(subjects);

        Assert.Single(findings);
        var f = findings[0];
        Assert.Equal("u1", f.UserId);
        Assert.Equal("role-admin", f.RoleId);
        Assert.Equal("g1", f.GroupId);
        Assert.Equal("IT Admins", f.GroupName);
    }

    [Fact]
    public void RoleGroupOverlap_NoFinding_WhenSameRoleButDifferentDivision()
    {
        // Same role, different division → NOT an overlap
        var subjects = new[]
        {
            RoleSubjects("u1",
                directGrants: [Grant("role-admin", "Admin", "div1")],
                groupSubjects: [GroupSubject("g1", "IT Admins", Grant("role-admin", "Admin", "div2"))])
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeRoleGroupOverlap(subjects);

        Assert.Empty(findings);
    }

    [Fact]
    public void RoleGroupOverlap_MultipleFindingsPerUser_WhenSeveralOverlaps()
    {
        var subjects = new[]
        {
            RoleSubjects("u1",
                directGrants:
                [
                    Grant("role-admin", "Admin", "div1"),
                    Grant("role-agent", "Agent", "div1"),
                ],
                groupSubjects:
                [
                    GroupSubject("g1", "IT Admins",
                        Grant("role-admin", "Admin", "div1"),
                        Grant("role-agent", "Agent", "div1"))
                ])
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeRoleGroupOverlap(subjects);

        Assert.Equal(2, findings.Count);
        Assert.All(findings, f => Assert.Equal("u1", f.UserId));
    }

    [Fact]
    public void RoleGroupOverlap_NoFinding_WhenNoDirectGrants()
    {
        var subjects = new[]
        {
            RoleSubjects("u1",
                directGrants: [],
                groupSubjects: [GroupSubject("g1", "IT Admins", Grant("role-admin", "Admin", "div1"))])
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeRoleGroupOverlap(subjects);

        Assert.Empty(findings);
    }

    [Fact]
    public void RoleGroupOverlap_EnrichesWithUserMeta_WhenLookupProvided()
    {
        var subjects = new[]
        {
            RoleSubjects("u1",
                directGrants: [Grant("role-admin", "Admin", "div1")],
                groupSubjects: [GroupSubject("g1", "IT Admins", Grant("role-admin", "Admin", "div1"))])
        };

        var lookup = new Dictionary<string, (string? Name, string? Email, string? State)>(StringComparer.OrdinalIgnoreCase)
        {
            ["u1"] = ("Synthetic User U1", "synthetic-u1@example.invalid", "active")
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeRoleGroupOverlap(subjects, lookup);

        Assert.Single(findings);
        Assert.Equal("Synthetic User U1", findings[0].UserName);
        Assert.Equal("synthetic-u1@example.invalid", findings[0].Email);
        Assert.Equal("active", findings[0].UserState);
    }

    [Fact]
    public void RoleGroupOverlap_FindingWithCorrectIssueAndRecommendedAction()
    {
        var subjects = new[]
        {
            RoleSubjects("u1",
                directGrants: [Grant("role-admin", "Admin", "div1", "Home Division")],
                groupSubjects: [GroupSubject("g1", "IT Admins", Grant("role-admin", "Admin", "div1", "Home Division"))])
        };

        var findings = new LicenseHygieneAnalyzer().AnalyzeRoleGroupOverlap(subjects);

        Assert.Single(findings);
        Assert.Contains("Admin", findings[0].Issue);
        Assert.Contains("IT Admins", findings[0].Issue);
        Assert.Contains("redundant", findings[0].Issue, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Remove", findings[0].RecommendedAction, StringComparison.OrdinalIgnoreCase);
    }
}
