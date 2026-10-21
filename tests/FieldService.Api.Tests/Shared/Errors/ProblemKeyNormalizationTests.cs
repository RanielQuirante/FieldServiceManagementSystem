using FieldService.Api.Shared.Errors;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace FieldService.Api.Tests.Shared.Errors;

public sealed class ProblemKeyNormalizationTests
{
    [Theory]
    [InlineData("$.priority", "priority")]
    [InlineData("$.Priority", "priority")]
    [InlineData("$.answers[2].note", "answers[2].note")]
    [InlineData("$.answers[0].readings[1].value", "answers[0].readings[1].value")]
    [InlineData("$.Answers[0].Readings[1].ReadingFieldId", "answers[0].readings[1].readingFieldId")]
    [InlineData("$", "body")]
    [InlineData("", "body")]
    [InlineData(null, "body")]
    public void AJsonPathBecomesACamelCaseKeyWithoutTheRootMarker(string? path, string expected)
    {
        Assert.Equal(expected, ProblemKeyNormalizer.FromJsonPath(path));
    }

    [Fact]
    public void ValidationProblemKeysWrittenByTheFrameworkAreNormalizedAndMerged()
    {
        var context = new DefaultHttpContext();
        context.Request.Path = "/api/jobs";
        var problem = new HttpValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["SiteAddress"] = ["Enter the site address."],
            ["ScheduledEnd"] = ["The scheduled end must be after the scheduled start."],
            ["scheduledEnd"] = ["Another rule."],
            ["Answers[2].Note"] = ["Add a note."],
        })
        {
            Status = StatusCodes.Status400BadRequest,
        };

        ProblemDetailsSetup.Complete(problem, context);

        Assert.Equal(["answers[2].note", "scheduledEnd", "siteAddress"], problem.Errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(2, problem.Errors["scheduledEnd"].Length);
        Assert.Equal("request.validation-failed", problem.Extensions[ProblemDetailsSetup.CodeMember]);
        Assert.Equal("/api/jobs", problem.Instance);
    }
}
