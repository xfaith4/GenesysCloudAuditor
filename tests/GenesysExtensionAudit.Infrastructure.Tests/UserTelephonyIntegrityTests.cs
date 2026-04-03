using System.Reflection;
using GenesysExtensionAudit.Infrastructure.Application;
using GenesysExtensionAudit.Infrastructure.Genesys.Dtos;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class UserTelephonyIntegrityTests
{
    [Fact]
    public void AnalyzeUserTelephonyIntegrity_FlagsMissingOwnerWhenInactiveUsersIncluded()
    {
        var findings = Analyze(
            users: [],
            extensions:
            [
                new EdgeExtensionEntityDto
                {
                    Id = "ext-1",
                    Extension = "4101",
                    AssignedTo = new AssignedToDto { Type = "USER", Id = "ghost-user" }
                }
            ],
            dids: [],
            includeInactiveUsers: true);

        var finding = Assert.Single(findings);
        Assert.Equal(TelephonyIntegrityCode.GhostTelephonyAssignment, finding.FindingCode);
        Assert.Equal("ghost-user", finding.UserId);
        Assert.Equal("4101", finding.ProfileExtensionRaw);
        Assert.Contains("not returned by the user inventory", finding.Issue);
    }

    [Fact]
    public void AnalyzeUserTelephonyIntegrity_FlagsDidAssignedToInactiveUser()
    {
        var findings = Analyze(
            users:
            [
                new GenesysUserDto
                {
                    Id = "u-inactive",
                    Name = "Inactive Agent",
                    State = "inactive",
                    PrimaryContactInfo =
                    [
                        new GenesysPrimaryContactInfoDto
                        {
                            Type = "work",
                            MediaType = "PHONE",
                            Address = "+13175550123",
                            Extension = "4101"
                        }
                    ],
                    Station = new GenesysStationRefDto { Id = "st-1", Name = "Desk 12" }
                }
            ],
            extensions: [],
            dids:
            [
                new DidDto
                {
                    Id = "did-1",
                    PhoneNumber = "+13175550123",
                    Owner = new DidOwnerDto { Type = "User", Id = "u-inactive" }
                }
            ],
            includeInactiveUsers: true);

        var finding = Assert.Single(findings, f => f.FindingCode == TelephonyIntegrityCode.GhostTelephonyAssignment);
        Assert.Equal(TelephonyIntegrityCode.GhostTelephonyAssignment, finding.FindingCode);
        Assert.Equal("u-inactive", finding.UserId);
        Assert.Equal("+13175550123", finding.RelatedDidNumber);
        Assert.Equal("st-1", finding.StationId);
        Assert.Contains("inactive user", finding.Issue);
    }

    [Fact]
    public void AnalyzeUserTelephonyIntegrity_DoesNotFlagMissingOwnerWhenInactiveUsersExcluded()
    {
        var findings = Analyze(
            users: [],
            extensions:
            [
                new EdgeExtensionEntityDto
                {
                    Id = "ext-1",
                    Extension = "4101",
                    AssignedTo = new AssignedToDto { Type = "USER", Id = "possibly-inactive-user" }
                }
            ],
            dids: [],
            includeInactiveUsers: false);

        Assert.Empty(findings);
    }

    private static IReadOnlyList<UserTelephonyIntegrityFinding> Analyze(
        IReadOnlyList<GenesysUserDto> users,
        IReadOnlyList<EdgeExtensionEntityDto> extensions,
        IReadOnlyList<DidDto> dids,
        bool includeInactiveUsers)
    {
        var method = typeof(AuditOrchestrator).GetMethod(
            "AnalyzeUserTelephonyIntegrity",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var findings = method!.Invoke(null, [users, extensions, dids, includeInactiveUsers]) as IReadOnlyList<UserTelephonyIntegrityFinding>;
        Assert.NotNull(findings);
        return findings!;
    }
}
