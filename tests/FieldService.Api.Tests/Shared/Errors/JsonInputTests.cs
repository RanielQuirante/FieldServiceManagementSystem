using System.Net;
using System.Text;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Shared.Errors;

[Collection(DatabaseCollection.Name)]
public sealed class JsonInputTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    private static readonly string _sentinel = $"sentinel{Guid.NewGuid():N}";

    public static TheoryData<string, string> MalformedBodies => new()
    {
        { $$"""{"email":"{{_sentinel}}@fieldservice.example","password":"{{_sentinel}}","client":"Web",""", "body" },
        { $$"""{"email":"{{_sentinel}}@fieldservice.example","password":{{_sentinel}},"client":"Web"}""", "password" },
        { $$"""{"email":"a@fieldservice.example","password":"p","client":"{{_sentinel}}"}""", "client" },
        { """{"email":"a@fieldservice.example","password":"p","client":1}""", "client" },
        { """{"email":"a@fieldservice.example","password":7,"client":"Web"}""", "password" },
        { """{"email":"a@fieldservice.example","email":"b@fieldservice.example","password":"p","client":"Web"}""", "email" },
        { """["email"]""", "body" },
        { "\"just a string\"", "body" },
    };

    [Theory]
    [MemberData(nameof(MalformedBodies))]
    public async Task ABindingFailureBecomesAFieldErrorAndTheSubmittedValueIsNeverLoggedOrEchoed(string json, string key)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.Settings["Serilog:MinimumLevel:Default"] = "Debug");
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync(ApiClient.SignInPath, new StringContent(json, Encoding.UTF8, "application/json"));

        var body = await response.Content.ReadAsStringAsync(CancellationToken);
        await ApiClient.AssertFieldErrorsAsync(response, key);
        Assert.DoesNotContain(_sentinel, body, StringComparison.Ordinal);
        Assert.All(factory.LogSink.Events, logEvent =>
            Assert.DoesNotContain(_sentinel, CapturingLogSink.Text(logEvent), StringComparison.Ordinal));
    }

    [Fact]
    public async Task ADocumentNestedDeeperThan32LevelsGets400()
    {
        using var client = Fixture.Factory.CreateApiClient();
        var nested = $"{string.Concat(Enumerable.Repeat("{\"a\":", 33))}1{new string('}', 33)}";
        var json = $$"""{"email":"a@fieldservice.example","password":"p","client":"Web","extra":{{nested}}}""";

        using var response = await client.PostAsync(ApiClient.SignInPath, new StringContent(json, Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.BadRequest, "request.validation-failed");
    }

    [Fact]
    public async Task AnUnknownMemberIsIgnored()
    {
        using var client = Fixture.Factory.CreateApiClient();
        var json = $$"""{"email":"nobody-{{Guid.NewGuid():N}}@fieldservice.example","password":"p","client":"Web","rememberMe":true}""";

        using var response = await client.PostAsync(ApiClient.SignInPath, new StringContent(json, Encoding.UTF8, "application/json"));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.Unauthorized, "auth.invalid-credentials");
    }
}
