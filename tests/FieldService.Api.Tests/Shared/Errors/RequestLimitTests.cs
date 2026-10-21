using System.Net;
using System.Text;
using FieldService.Api.Domain;
using FieldService.Api.Features.Auth;
using FieldService.Api.Tests.Features.Auth;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Shared.Errors;

[Collection(DatabaseCollection.Name)]
public sealed class RequestLimitTests(DatabaseFixture fixture) : DatabaseTest(fixture)
{
    [Theory]
    [InlineData(ApiClient.SignInPath)]
    [InlineData(ApiClient.RefreshPath)]
    [InlineData(ApiClient.SignOutPath)]
    public async Task AnAuthBodyOneByteOver16KilobytesGets413OnKestrel(string path)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync(path, SignInTests.SignInBodyOfLength((int)AuthEndpoints.RequestBodyLimit + 1));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "request.too-large");
    }

    [Fact]
    public async Task AJobBodyOneByteOverOneMegabyteGets413OnKestrel()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var client = factory.CreateApiClient();
        client.AccessToken = TestTokens.For(factory, UserRole.Administrator);
        var body = $$"""{"title":"{{new string('a', 1024 * 1024)}}"}""";

        using var response = await client.PostAsync("/api/jobs", new StringContent(body, Encoding.UTF8, "application/json"));

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.RequestEntityTooLarge, "request.too-large");
    }

    [Theory]
    [InlineData("text/plain", "email=a")]
    [InlineData("application/x-www-form-urlencoded", "email=a&client=Web")]
    [InlineData("multipart/form-data; boundary=x", "--x--")]
    public async Task ASignInBodyThatIsNotJsonGets415(string mediaType, string content)
    {
        using var client = Fixture.Factory.CreateApiClient();
        var body = new StringContent(content, Encoding.UTF8);
        body.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse(mediaType);

        using var response = await client.PostAsync(ApiClient.SignInPath, body);

        using var problem = await ApiClient.AssertProblemAsync(response, HttpStatusCode.UnsupportedMediaType, "request.unsupported-media-type");
    }

    [Theory]
    [InlineData(ApiClient.RefreshPath, HttpStatusCode.Unauthorized)]
    [InlineData(ApiClient.SignOutPath, HttpStatusCode.NoContent)]
    public async Task RefreshAndSignOutWithNoBodyAndNoContentTypePassTheMediaTypeRuleOnKestrel(string path, HttpStatusCode expected)
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var client = factory.CreateApiClient();

        using var response = await client.PostAsync(path, content: null);

        Assert.Null(response.RequestMessage!.Content);
        Assert.Equal(expected, response.StatusCode);
    }

    [Fact]
    public async Task AWebRefreshAndSignOutWithNoBodySucceedOnKestrel()
    {
        await using var factory = Fixture.CreateFactory(factory => factory.UseKestrel(0));
        using var signedIn = await new TestData(Fixture, factory.Clock.GetUtcNow()).SignInAsync(factory, SessionClient.Web, CancellationToken);

        using var refresh = await signedIn.RefreshAsync();
        var next = await signedIn.NextTokenAsync(refresh);
        using var signOut = await signedIn.SignOutAsync(next);

        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, signOut.StatusCode);
    }
}
