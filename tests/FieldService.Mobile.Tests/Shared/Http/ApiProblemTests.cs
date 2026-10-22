using System.Net;
using System.Text;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Http;

public sealed class ApiProblemTests
{
    [Fact]
    public async Task ReadsTheCodeCorrelationIdAndFieldErrorsOfAProblem()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        using var response = ProblemResponses.Problem(
            HttpStatusCode.BadRequest,
            "request.validation-failed",
            "One or more fields are invalid.",
            correlationId,
            new Dictionary<string, string[]>
            {
                ["email"] = ["Enter an email address."],
                ["answers[2].note"] = ["Describe the fault.", "Use 1000 characters or fewer."],
            });

        var exception = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal(HttpStatusCode.BadRequest, exception.StatusCode);
        Assert.Equal("request.validation-failed", exception.Code);
        Assert.Equal(correlationId, exception.CorrelationId);
        var problem = Assert.IsType<ApiProblem>(exception.Problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal("One or more fields are invalid.", problem.Title);
        Assert.Equal("Enter an email address.", problem.FirstError("email"));
        Assert.Equal("Describe the fault.", problem.FirstError("answers[2].note"));
        Assert.Null(problem.FirstError("password"));
        Assert.Equal(["Describe the fault.", "Use 1000 characters or fewer."], problem.ErrorsExcept("email"));
    }

    [Fact]
    public async Task CarriesTheRetryAfterWaitOfARateLimit()
    {
        using var response = ProblemResponses.RateLimited(TimeSpan.FromSeconds(42));

        var exception = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.Equal("rate-limit.exceeded", exception.Code);
        Assert.Equal(TimeSpan.FromSeconds(42), exception.RetryAfter);
    }

    [Fact]
    public async Task LeavesTheProblemEmpty_WhenTheBodyIsNotJson()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadGateway)
        {
            Content = new StringContent("<html>Bad gateway</html>", Encoding.UTF8, "text/html"),
        };

        var exception = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Null(exception.Problem);
        Assert.Null(exception.Code);
    }

    [Fact]
    public async Task LeavesTheProblemEmpty_WhenTheJsonIsMalformed()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent("{\"code\":", Encoding.UTF8, ProblemResponses.ProblemMediaType),
        };

        var exception = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Null(exception.Problem);
    }

    [Fact]
    public async Task TheExceptionMessageHoldsTheStatusAndCodeOnly()
    {
        using var response = ProblemResponses.ValidationFailed(new Dictionary<string, string[]> { ["email"] = ["aisha.karim@fieldservice.example is not valid."] });

        var exception = await ApiException.FromResponseAsync(response, CancellationToken.None);

        Assert.Equal("The API answered 400 request.validation-failed.", exception.Message);
    }
}
