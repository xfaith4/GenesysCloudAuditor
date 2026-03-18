using System.Net;
using System.Reflection;
using GenesysExtensionAudit.Infrastructure.Genesys.Clients;
using GenesysExtensionAudit.Infrastructure.Http;
using Xunit;

namespace GenesysExtensionAudit.Infrastructure.Tests;

public sealed class GenesysOAuthScopeTests
{
    [Fact]
    public void BuildAuthorizeUrl_IncludesNormalizedPkceScope()
    {
        var method = typeof(GenesysPkceAuthService).GetMethod(
            "BuildAuthorizeUrl",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var url = method!.Invoke(null, new object?[]
        {
            "https://login.usw2.pure.cloud",
            "client-id",
            new Uri("http://127.0.0.1:45731/callback"),
            "challenge",
            "state",
            " users:readonly,\ntelephony:readonly   users:readonly "
        }) as string;

        Assert.NotNull(url);

        var query = Uri.UnescapeDataString(new Uri(url!, UriKind.Absolute).Query);
        Assert.Contains("scope=users:readonly telephony:readonly", query);
    }

    [Fact]
    public void GenesysApiException_FormatsMissingScopeErrorAsActionableHint()
    {
        var ex = new GenesysApiException(
            HttpStatusCode.Forbidden,
            "Forbidden",
            "corr-123",
            """{"message":"App not authorized to use scope [telephony:readonly, telephony]","code":"app.not.authorized.for.scope","status":403}""");

        Assert.Contains("CorrelationId=corr-123", ex.Message);
        Assert.Contains("Missing Genesys OAuth scope(s): telephony:readonly, telephony.", ex.Message);
        Assert.DoesNotContain("Body=", ex.Message);
    }
}
