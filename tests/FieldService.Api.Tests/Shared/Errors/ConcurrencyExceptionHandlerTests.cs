using System.Text.Json;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FieldService.Api.Tests.Shared.Errors;

public sealed class ConcurrencyExceptionHandlerTests
{
    public static TheoryData<string, string> MappedSqlStates => new()
    {
        { PostgresErrorCodes.UniqueViolation, ErrorCodes.ConcurrencyConflict },
        { PostgresErrorCodes.LockNotAvailable, ErrorCodes.ConcurrencyConflict },
        { PostgresErrorCodes.ObjectNotInPrerequisiteState, ErrorCodes.InspectionLocked },
    };

    public static TheoryData<string> UnmappedSqlStates =>
    [
        PostgresErrorCodes.CheckViolation,
        PostgresErrorCodes.ForeignKeyViolation,
        PostgresErrorCodes.InsufficientPrivilege,
        PostgresErrorCodes.QueryCanceled,
    ];

    [Theory]
    [MemberData(nameof(MappedSqlStates))]
    public async Task SaveFailureWithMappedSqlStateBecomesConflictProblem(string sqlState, string code)
    {
        var exception = new DbUpdateException("save failed", new PostgresException("probe", "ERROR", "ERROR", sqlState));

        var outcome = await HandleAsync(exception);

        AssertConflict(outcome, code);
    }

    [Fact]
    public async Task StaleVersionBecomesConcurrencyConflictProblem()
    {
        var exception = new DbUpdateConcurrencyException("stale version");

        var outcome = await HandleAsync(exception);

        AssertConflict(outcome, ErrorCodes.ConcurrencyConflict);
    }

    [Theory]
    [MemberData(nameof(UnmappedSqlStates))]
    public async Task SaveFailureWithAnyOtherSqlStateIsLeftToTheNextHandler(string sqlState)
    {
        var exception = new DbUpdateException("save failed", new PostgresException("probe", "ERROR", "ERROR", sqlState));

        var outcome = await HandleAsync(exception);

        AssertNotHandled(outcome);
    }

    [Fact]
    public async Task ExceptionOtherThanASaveFailureIsLeftToTheNextHandler()
    {
        var outcome = await HandleAsync(new InvalidOperationException("not a save failure"));

        AssertNotHandled(outcome);
    }

    private static void AssertConflict(HandlerOutcome outcome, string code)
    {
        Assert.True(outcome.Handled);
        Assert.Equal(StatusCodes.Status409Conflict, outcome.StatusCode);
        Assert.StartsWith("application/problem+json", outcome.ContentType);
        using var problem = JsonDocument.Parse(outcome.Body);
        Assert.Equal(StatusCodes.Status409Conflict, problem.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(code, problem.RootElement.GetProperty(ProblemDetailsSetup.CodeMember).GetString());
        Assert.Equal(ErrorCodes.TitleOf(code), problem.RootElement.GetProperty("title").GetString());
        Assert.False(problem.RootElement.TryGetProperty("detail", out _));
    }

    private static void AssertNotHandled(HandlerOutcome outcome)
    {
        Assert.False(outcome.Handled);
        Assert.Equal(StatusCodes.Status200OK, outcome.StatusCode);
        Assert.Empty(outcome.Body);
    }

    private static async Task<HandlerOutcome> HandleAsync(Exception exception)
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .ConfigureHttpJsonOptions(options => HttpRegistration.ConfigureJson(options.SerializerOptions))
            .AddErrorHandling()
            .BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = HttpMethods.Put;
        context.Request.Path = "/api/jobs/probe/status";
        using var body = new MemoryStream();
        context.Response.Body = body;
        var handler = new ConcurrencyExceptionHandler(services.GetRequiredService<IProblemDetailsService>());

        var handled = await handler.TryHandleAsync(context, exception, TestContext.Current.CancellationToken);

        return new HandlerOutcome(
            handled,
            context.Response.StatusCode,
            context.Response.ContentType,
            System.Text.Encoding.UTF8.GetString(body.ToArray()));
    }

    private sealed record HandlerOutcome(bool Handled, int StatusCode, string? ContentType, string Body);
}
