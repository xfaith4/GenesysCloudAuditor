using GenesysExtensionAudit.Application;
using GenesysExtensionAudit.Infrastructure.Domain.Services;
using GenesysExtensionAudit.Infrastructure.Reporting;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class AuditLogSignalsAnalyzerTests
{
    private static AuditLogFinding AuditLog(
        string? serviceName = "authorization",
        string? action = "UPDATE",
        string? entityType = "Role",
        string? entityName = "Supervisor",
        string? entityId = "entity-1",
        string? userName = "admin@example.invalid",
        string? userEmail = "admin@example.invalid",
        string? userId = "user-1",
        string? clientId = null,
        DateTimeOffset? timestamp = null)
        => new(
            AuditId: Guid.NewGuid().ToString(),
            TimestampUtc: timestamp ?? DateTimeOffset.UtcNow.AddMinutes(-10),
            ServiceName: serviceName,
            Action: action,
            UserName: userName,
            UserEmail: userEmail,
            EntityType: entityType,
            EntityName: entityName,
            UserId: userId,
            ClientId: clientId,
            EntityId: entityId,
            CorrelationId: null,
            Level: "INFO");

    [Fact]
    public void Analyze_EmptyLogs_ReturnsEmpty()
    {
        var findings = new AuditLogSignalsAnalyzer().Analyze([]);

        Assert.Empty(findings);
    }

    [Fact]
    public void Analyze_DivisionEvent_ProducesDivisionScopeSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "directory", entityType: "Division", entityName: "Sales East", entityId: "division-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.DivisionScopeChange, finding.FindingCode);
        Assert.Equal("Security / Scope", finding.SignalCategory);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.ChangeReviewRequired, finding.Category);
    }

    [Fact]
    public void Analyze_OAuthClientEvent_ProducesOAuthSignal()
    {
        var logs = new[]
        {
            AuditLog(
                serviceName: "oauth",
                entityType: "OAuthClient",
                entityName: "Genesys Automation",
                entityId: "client-42",
                userName: null,
                userEmail: null,
                userId: null,
                clientId: "oauth-client-42")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.OAuthClientChange, finding.FindingCode);
        Assert.Equal("Security / OAuth", finding.SignalCategory);
        Assert.Equal("oauth-client-42", finding.ClientId);
    }

    [Fact]
    public void Analyze_AccessControlEvent_ProducesAccessControlSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "authorization", action: "ASSIGN", entityType: "Permission", entityName: "Queue Edit")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.AccessControlChange, finding.FindingCode);
        Assert.Equal("Security / Access Control", finding.SignalCategory);
    }

    [Fact]
    public void Analyze_QueueMembershipChurn_ProducesSignal()
    {
        var now = DateTimeOffset.UtcNow;
        var logs = new[]
        {
            AuditLog(serviceName: "routing", action: "ADD", entityType: "QueueMembership", entityName: "Service Desk", entityId: "queue-1", timestamp: now.AddMinutes(-20)),
            AuditLog(serviceName: "routing", action: "REMOVE", entityType: "QueueMembership", entityName: "Service Desk", entityId: "queue-1", timestamp: now.AddMinutes(-10)),
            AuditLog(serviceName: "routing", action: "ADD", entityType: "QueueMembership", entityName: "Service Desk", entityId: "queue-1", timestamp: now.AddMinutes(-5))
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.QueueMembershipChurn, finding.FindingCode);
        Assert.Equal("Routing / Queue Membership", finding.SignalCategory);
        Assert.Equal(3, finding.EventCount);
    }

    [Fact]
    public void Analyze_SingleQueueMembershipChange_DoesNotProduceSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "routing", action: "ADD", entityType: "QueueMembership", entityName: "Service Desk", entityId: "queue-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        Assert.Empty(findings);
    }

    [Fact]
    public void Analyze_FlowPublishBurst_ProducesSignal()
    {
        var now = DateTimeOffset.UtcNow;
        var logs = new[]
        {
            AuditLog(serviceName: "architect", action: "PUBLISH", entityType: "Flow", entityName: "Main IVR", entityId: "flow-1", timestamp: now.AddMinutes(-30)),
            AuditLog(serviceName: "architect", action: "PUBLISH", entityType: "Flow", entityName: "Main IVR", entityId: "flow-1", timestamp: now.AddMinutes(-15))
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.FlowPublicationChange, finding.FindingCode);
        Assert.Equal("Routing / Flow Publication", finding.SignalCategory);
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
    }

    [Fact]
    public void Analyze_SingleFlowRollback_ProducesHighSeveritySignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "architect", action: "ROLLBACK", entityType: "Flow", entityName: "Main IVR", entityId: "flow-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.FlowPublicationChange, finding.FindingCode);
        Assert.Equal(FindingSeverity.High, finding.Severity);
    }

    [Fact]
    public void Analyze_RepeatedMatchingEvents_GroupsIntoSingleSignal()
    {
        var now = DateTimeOffset.UtcNow;
        var logs = new[]
        {
            AuditLog(entityId: "role-1", entityName: "Supervisor", timestamp: now.AddMinutes(-20)),
            AuditLog(entityId: "role-1", entityName: "Supervisor", timestamp: now.AddMinutes(-10)),
            AuditLog(entityId: "role-1", entityName: "Supervisor", timestamp: now.AddMinutes(-5))
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(3, finding.EventCount);
        Assert.Equal(now.AddMinutes(-20).ToString("O"), finding.FirstEventUtc?.ToString("O"));
        Assert.Equal(now.AddMinutes(-5).ToString("O"), finding.LastEventUtc?.ToString("O"));
    }

    // ─── AdminRoleGrantRevoke ────────────────────────────────────────────────

    [Fact]
    public void Analyze_RoleGrantAction_ProducesAdminRoleGrantRevokeSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "authorization", action: "GRANT", entityType: "Role", entityName: "Master Administrator", entityId: "role-admin-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.AdminRoleGrantRevoke, finding.FindingCode);
        Assert.Equal("Security / Privileged Access", finding.SignalCategory);
        Assert.Equal(FindingSeverity.High, finding.Severity);
        Assert.Equal(FindingCategory.ChangeReviewRequired, finding.Category);
        Assert.Contains("grant", finding.Issue, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Analyze_RoleRevokeAction_ProducesAdminRoleGrantRevokeSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "authorization", action: "REVOKE", entityType: "Role", entityName: "Supervisor", entityId: "role-2")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.AdminRoleGrantRevoke, finding.FindingCode);
    }

    [Fact]
    public void Analyze_RoleGrantRevoke_TakesPriorityOverGenericAccessControl()
    {
        // A GRANT action on a Role entity should be AdminRoleGrantRevoke, not AccessControlChange.
        var logs = new[]
        {
            AuditLog(serviceName: "authorization", action: "GRANT", entityType: "Role", entityName: "Agent Role", entityId: "role-3")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.AdminRoleGrantRevoke, finding.FindingCode);
        Assert.NotEqual(AuditLogSignalCode.AccessControlChange, finding.FindingCode);
    }

    [Fact]
    public void Analyze_RoleUpdate_ProducesAccessControlSignal_NotAdminRoleGrantRevoke()
    {
        // An UPDATE on a Role entity (not a grant/revoke) should still be AccessControlChange.
        var logs = new[]
        {
            AuditLog(serviceName: "authorization", action: "UPDATE", entityType: "Role", entityName: "Supervisor", entityId: "role-4")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.AccessControlChange, finding.FindingCode);
    }

    // ─── PlatformConfigChange ────────────────────────────────────────────────

    [Fact]
    public void Analyze_SiteUpdateEvent_ProducesPlatformConfigChangeSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "telephony", action: "UPDATE", entityType: "Site", entityName: "London Office", entityId: "site-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.PlatformConfigChange, finding.FindingCode);
        Assert.Equal("Infrastructure / Platform Config", finding.SignalCategory);
        Assert.Equal(FindingSeverity.Medium, finding.Severity);
        Assert.Equal(FindingCategory.ChangeReviewRequired, finding.Category);
    }

    [Fact]
    public void Analyze_EdgeDeleteEvent_ProducesHighSeverityPlatformConfigChangeSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "telephony", action: "DELETE", entityType: "Edge", entityName: "Edge-01", entityId: "edge-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.PlatformConfigChange, finding.FindingCode);
        Assert.Equal(FindingSeverity.High, finding.Severity);
    }

    [Fact]
    public void Analyze_TrunkUpdateEvent_ProducesPlatformConfigChangeSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "telephony", action: "UPDATE", entityType: "Trunk", entityName: "PSTN Trunk", entityId: "trunk-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.PlatformConfigChange, finding.FindingCode);
    }

    [Fact]
    public void Analyze_IvrUpdateEvent_ProducesPlatformConfigChangeSignal()
    {
        var logs = new[]
        {
            AuditLog(serviceName: "routing", action: "UPDATE", entityType: "IVR", entityName: "Main IVR Config", entityId: "ivr-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.PlatformConfigChange, finding.FindingCode);
    }

    [Fact]
    public void Analyze_PlatformConfigChange_MultipleUpdatesToSameEntity_GroupedIntoSingleSignal()
    {
        var now = DateTimeOffset.UtcNow;
        var logs = new[]
        {
            AuditLog(serviceName: "telephony", action: "UPDATE", entityType: "Site", entityName: "HQ Site", entityId: "site-2", timestamp: now.AddMinutes(-30)),
            AuditLog(serviceName: "telephony", action: "UPDATE", entityType: "Site", entityName: "HQ Site", entityId: "site-2", timestamp: now.AddMinutes(-10))
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        var finding = Assert.Single(findings);
        Assert.Equal(AuditLogSignalCode.PlatformConfigChange, finding.FindingCode);
        Assert.Equal(2, finding.EventCount);
    }

    [Fact]
    public void Analyze_QueueEventDoesNotProducePlatformConfigChange()
    {
        // Queue updates should not fire PlatformConfigChange — they have their own code path.
        var logs = new[]
        {
            AuditLog(serviceName: "routing", action: "UPDATE", entityType: "Queue", entityName: "Sales Queue", entityId: "queue-1")
        };

        var findings = new AuditLogSignalsAnalyzer().Analyze(logs);

        Assert.DoesNotContain(findings, f => f.FindingCode == AuditLogSignalCode.PlatformConfigChange);
    }

    // ─── SentinelRuleRegistry ────────────────────────────────────────────────

    [Fact]
    public void SentinelRuleRegistry_ReturnsMetadataForAllSignalCodes()
    {
        var allCodes = new[]
        {
            AuditLogSignalCode.AccessControlChange,
            AuditLogSignalCode.AdminRoleGrantRevoke,
            AuditLogSignalCode.DivisionScopeChange,
            AuditLogSignalCode.OAuthClientChange,
            AuditLogSignalCode.QueueMembershipChurn,
            AuditLogSignalCode.FlowPublicationChange,
            AuditLogSignalCode.PlatformConfigChange
        };

        foreach (var code in allCodes)
        {
            var meta = SentinelRuleRegistry.GetMetadata(code);
            Assert.NotNull(meta);
            Assert.False(string.IsNullOrWhiteSpace(meta.RuleId), $"RuleId must not be empty for code {code}");
            Assert.False(string.IsNullOrWhiteSpace(meta.ProvenanceBasis), $"ProvenanceBasis must not be empty for code {code}");
            Assert.False(string.IsNullOrWhiteSpace(meta.ProvenanceSource), $"ProvenanceSource must not be empty for code {code}");
        }
    }

    [Fact]
    public void SentinelRuleRegistry_GetAll_ReturnsAllRegisteredRules()
    {
        var all = SentinelRuleRegistry.GetAll();

        Assert.Equal(7, all.Count);
        Assert.All(all, m => Assert.False(string.IsNullOrWhiteSpace(m.RuleId)));
    }

    [Fact]
    public void SentinelRuleRegistry_UnknownCode_ReturnsNull()
    {
        var meta = SentinelRuleRegistry.GetMetadata("UNKNOWN_CODE_XYZ");

        Assert.Null(meta);
    }

    [Fact]
    public void SentinelRuleRegistry_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(SentinelRuleRegistry.GetMetadata(null));
        Assert.Null(SentinelRuleRegistry.GetMetadata(string.Empty));
        Assert.Null(SentinelRuleRegistry.GetMetadata("  "));
    }
}
