using System.Net;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Http;

public sealed class CorrelationIdHandlerTests
{
    [Fact]
    public async Task EveryRequestCarriesANewCorrelationId()
    {
        var stub = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var client = TestSession.CreateClient(stub, new CapturingLoggerProvider(), authorize: null);

        using var first = await client.GetAsync("api/test", TestContext.Current.CancellationToken);
        using var second = await client.GetAsync("api/test", TestContext.Current.CancellationToken);

        var ids = stub.Requests.Select(request => request.CorrelationId).ToList();
        Assert.Equal(2, ids.Count);
        Assert.All(ids, id => Assert.True(Guid.TryParseExact(id, "D", out _)));
        Assert.NotEqual(ids[0], ids[1]);
    }

    [Fact]
    public async Task ReplacesACorrelationIdTheCallerAlreadySet()
    {
        var stub = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)));
        using var client = TestSession.CreateClient(stub, new CapturingLoggerProvider(), authorize: null);
        using var request = new HttpRequestMessage(HttpMethod.Get, "api/test");
        request.Headers.TryAddWithoutValidation(CorrelationIdHandler.HeaderName, "reused-id");

        using var response = await client.SendAsync(request, TestContext.Current.CancellationToken);

        var id = Assert.Single(stub.Requests).CorrelationId;
        Assert.NotEqual("reused-id", id);
        Assert.True(Guid.TryParseExact(id, "D", out _));
    }

    [Fact]
    public async Task TheApiExceptionExposesTheRequestCorrelationIdAsTheReference_WhenTheResponseHasNoProblem()
    {
        var stub = new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway)));
        var auth = new AuthApiClient(TestSession.CreateClient(stub, new CapturingLoggerProvider(), authorize: null));

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            auth.SignInAsync(new SignInRequest("aisha.karim@fieldservice.example", TestSession.NewPassword(), SessionClient.Mobile), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadGateway, exception.StatusCode);
        Assert.Null(exception.Problem);
        Assert.Equal(Assert.Single(stub.Requests).CorrelationId, exception.CorrelationId);
    }

    [Fact]
    public async Task TheApiExceptionPrefersTheCorrelationIdOfTheProblem()
    {
        var problemId = Guid.NewGuid().ToString("D");
        var stub = StubHttpMessageHandler.Sequence(() => ProblemResponses.ServerError(problemId));
        var auth = new AuthApiClient(TestSession.CreateClient(stub, new CapturingLoggerProvider(), authorize: null));

        var exception = await Assert.ThrowsAsync<ApiException>(() =>
            auth.SignInAsync(new SignInRequest("aisha.karim@fieldservice.example", TestSession.NewPassword(), SessionClient.Mobile), CancellationToken.None));

        Assert.Equal(problemId, exception.CorrelationId);
        Assert.Equal("server.error", exception.Code);
    }

    [Fact]
    public async Task TheApiExceptionReadsTheResponseHeader_WhenTheProblemHasNoCorrelationId()
    {
        var headerId = Guid.NewGuid().ToString("D");
        var stub = new StubHttpMessageHandler((_, _) =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            response.Headers.TryAddWithoutValidation(CorrelationIdHandler.HeaderName, headerId);
            return Task.FromResult(response);
        });
        var auth = new AuthApiClient(TestSession.CreateClient(stub, new CapturingLoggerProvider(), authorize: null));

        var exception = await Assert.ThrowsAsync<ApiException>(() => auth.RefreshAsync(TestSession.NewRefreshToken(), CancellationToken.None));

        Assert.Equal(headerId, exception.CorrelationId);
    }
}
