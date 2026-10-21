using System.Net;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;
using Serilog.Events;

namespace FieldService.Api.Tests.Shared.Security;

[Collection(DatabaseCollection.Name)]
public sealed class AuthorizationResultHandlerTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    [Theory]
    [InlineData("/api/jobs")]
    [InlineData("/api/jobs/")]
    [InlineData("/api/checklist-templates")]
    public async Task AForbiddenRequestLogsThePolicyTheUserTheRoleAndTheRouteWithoutATrailingSlash(string path)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings["Serilog:MinimumLevel:Default"] = "Information");
        using var client = factory.CreateApiClient();
        var userId = Guid.CreateVersion7();
        client.AccessToken = TestTokens.Issue(factory, userId, UserRole.Technician).Value;

        using var response = await client.GetAsync(path);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Forbidden, "auth.forbidden");
        var forbidden = Assert.Single(factory.LogSink.Named(SecurityLog.ForbiddenEvent));
        Assert.Equal(LogEventLevel.Information, forbidden.Level);
        Assert.Equal(Policies.Administrator, CapturingLogSink.Scalar(forbidden, "Policy"));
        Assert.Equal(userId.ToString(), CapturingLogSink.Scalar(forbidden, "UserId"));
        Assert.Equal(nameof(UserRole.Technician), CapturingLogSink.Scalar(forbidden, "Role"));
        Assert.Equal(path.TrimEnd('/'), CapturingLogSink.Scalar(forbidden, "RoutePattern"));
    }
}
