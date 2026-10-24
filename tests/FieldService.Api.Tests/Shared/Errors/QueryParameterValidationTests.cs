using System.Globalization;
using FieldService.Api.Domain;
using FieldService.Api.Features.Dashboard.GetDashboardSummary;
using FieldService.Api.Features.Jobs.GetJobs;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Tests.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace FieldService.Api.Tests.Shared.Errors;

[Collection(DatabaseCollection.Name)]
public sealed class QueryParameterValidationTests(DatabaseFixture fixture)
{
    private const string StatusMessage =
        "The value must be one of Unassigned, Assigned, InProgress, OnHold, Completed or Cancelled.";

    [Theory]
    [InlineData("Unassigned", null)]
    [InlineData("Cancelled", null)]
    [InlineData("1", StatusMessage)]
    [InlineData("99", StatusMessage)]
    [InlineData("-1", StatusMessage)]
    [InlineData("assigned", StatusMessage)]
    [InlineData("Assigned,OnHold", StatusMessage)]
    [InlineData(" Assigned", StatusMessage)]
    [InlineData("Assigned ", StatusMessage)]
    [InlineData("", QueryParameterRule.EmptyValueMessage)]
    [InlineData(null, null)]
    public void AnEnumerationAcceptsOnlyTheExactNameOfAMember(string? value, string? expected)
    {
        var rule = QueryParameterRule.For("status", typeof(JobStatus?));

        Assert.Equal(expected, rule.Check(value));
    }

    [Theory]
    [InlineData("01a15415-3b40-79a4-859f-2cf627f64d02", null)]
    [InlineData("01A15415-3B40-79A4-859F-2CF627F64D02", null)]
    [InlineData("abc", QueryParameterRule.GuidMessage)]
    [InlineData("1042", QueryParameterRule.GuidMessage)]
    [InlineData("", QueryParameterRule.EmptyValueMessage)]
    public void AUuidMustParseAsAGuid(string value, string? expected)
    {
        Assert.Equal(expected, QueryParameterRule.For("technicianId", typeof(Guid?)).Check(value));
    }

    [Theory]
    [InlineData("1", null)]
    [InlineData("-5", null)]
    [InlineData("10000", null)]
    [InlineData("1.5", QueryParameterRule.IntegerMessage)]
    [InlineData(" 2", QueryParameterRule.IntegerMessage)]
    [InlineData("2 ", QueryParameterRule.IntegerMessage)]
    [InlineData("0x10", QueryParameterRule.IntegerMessage)]
    [InlineData("99999999999", QueryParameterRule.IntegerMessage)]
    [InlineData("", QueryParameterRule.EmptyValueMessage)]
    public void AnIntegerMustBeAWholeNumberInRange(string value, string? expected)
    {
        Assert.Equal(expected, QueryParameterRule.For("page", typeof(int?)).Check(value));
    }

    [Theory]
    [InlineData("%_\\ anything")]
    [InlineData("")]
    public void AStringAcceptsAnyValueIncludingAnEmptyOne(string value)
    {
        Assert.Null(QueryParameterRule.For("search", typeof(string)).Check(value));
    }

    [Theory]
    [InlineData("2026-10-19T04:00:00.000Z", null)]
    [InlineData("2026-10-19T04:00:00Z", null)]
    [InlineData("2026-10-19T04:00:00.1Z", null)]
    [InlineData("2026-10-19T04:00:00.1234567Z", null)]
    [InlineData("2026-10-19T00:00:00-04:00", null)]
    [InlineData("2026-10-19T09:30:00+05:30", null)]
    [InlineData("2026-10-19T00:00:00+00:00", null)]
    [InlineData("2026-10-19t04:00:00z", null)]
    [InlineData("2026-10-19T04:00:00", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T04:00:00.000", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T04:00Z", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T04:00:00.12345678Z", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T04:00:00.Z", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T00:00:00 04:00", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T00:00:00+0400", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19 04:00:00Z", QueryParameterRule.InstantMessage)]
    [InlineData(" 2026-10-19T04:00:00Z", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T04:00:00Z ", QueryParameterRule.InstantMessage)]
    [InlineData("2026-02-30T04:00:00Z", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T24:00:00Z", QueryParameterRule.InstantMessage)]
    [InlineData("2026-10-19T04:00:60Z", QueryParameterRule.InstantMessage)]
    [InlineData("10/19/2026 04:00:00 +00:00", QueryParameterRule.InstantMessage)]
    [InlineData("1792420920", QueryParameterRule.InstantMessage)]
    [InlineData("today", QueryParameterRule.InstantMessage)]
    [InlineData("", QueryParameterRule.EmptyValueMessage)]
    [InlineData(null, null)]
    public void AnInstantMustBeRfc3339WithZOrANumericOffsetAndUpToSevenFractionalDigits(string? value, string? expected)
    {
        var rule = QueryParameterRule.For("from", typeof(DateTimeOffset?));

        Assert.Equal(typeof(DateTimeOffset), rule.ValueType);
        Assert.Equal(expected, rule.Check(value));
    }

    [Theory]
    [InlineData("2026-10-19T04:00:00.000Z")]
    [InlineData("2026-10-19T00:00:00-04:00")]
    [InlineData("2026-10-19T09:30:00.1234567+05:30")]
    [InlineData("2026-10-19t04:00:00z")]
    public void EveryInstantTheRuleAcceptsBindsToTheSameMomentAsTheJsonConverterReads(string value)
    {
        Assert.Null(QueryParameterRule.For("to", typeof(DateTimeOffset?)).Check(value));
        Assert.True(UtcInstantJsonConverter.TryParse(value, out var fromJson));

        var bound = DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal | DateTimeStyles.AllowWhiteSpaces);

        Assert.Equal(fromJson, UtcInstant.Truncate(bound));
    }

    [Theory]
    [InlineData(typeof(long))]
    [InlineData(typeof(DateTime?))]
    [InlineData(typeof(bool))]
    public void ATypeWithoutARuleFailsWhenTheEndpointIsBuilt(Type type)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => QueryParameterRule.For("from", type));

        Assert.Contains("from", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetDashboardSummaryDeclaresFromAndToAsInstantsByTheirWireNames()
    {
        var rules = QueryParameterRule.ListFor(
            typeof(GetDashboardSummaryEndpoint).GetMethod(nameof(GetDashboardSummaryEndpoint.HandleAsync))!);

        Assert.Equal(["from", "to"], rules.Select(rule => rule.Name));
        Assert.Equal([typeof(DateTimeOffset), typeof(DateTimeOffset)], rules.Select(rule => rule.ValueType));
    }

    [Fact]
    public void GetJobsDeclaresItsSixQueryParametersByTheirWireNames()
    {
        var rules = QueryParameterRule.ListFor(typeof(GetJobsEndpoint).GetMethod(nameof(GetJobsEndpoint.HandleAsync))!);

        Assert.Equal(["search", "status", "technicianId", "priority", "page", "pageSize"], rules.Select(rule => rule.Name));
        Assert.Equal(
            [typeof(string), typeof(JobStatus), typeof(Guid), typeof(JobPriority), typeof(int), typeof(int)],
            rules.Select(rule => rule.ValueType));
    }

    [Fact]
    public void ARepeatedParameterAndEveryBadValueAreReportedTogether()
    {
        var rules = new QueryParameterRules(QueryParameterRule.ListFor(typeof(GetJobsEndpoint).GetMethod(nameof(GetJobsEndpoint.HandleAsync))!));
        var query = new QueryCollection(new Dictionary<string, StringValues>
        {
            ["status"] = new(["Assigned", "OnHold"]),
            ["technicianId"] = "abc",
            ["page"] = "2",
            ["unknown"] = "ignored",
        });

        var errors = QueryParameterValidation.Validate(query, rules);

        Assert.Equal(["status", "technicianId"], errors.Keys.Order(StringComparer.Ordinal));
        Assert.Equal([QueryParameterRule.RepeatedValueMessage], errors["status"]);
        Assert.Equal([QueryParameterRule.GuidMessage], errors["technicianId"]);
    }

    [Fact]
    public void EveryEndpointWithQueryParametersValidatesExactlyThoseParameters()
    {
        var descriptions = fixture.Factory.Services
            .GetRequiredService<IApiDescriptionGroupCollectionProvider>()
            .ApiDescriptionGroups.Items
            .SelectMany(group => group.Items)
            .ToList();
        var withQuery = descriptions
            .Where(description => description.ParameterDescriptions.Any(parameter => parameter.Source == BindingSource.Query))
            .ToList();

        Assert.NotEmpty(withQuery);
        Assert.All(withQuery, description =>
        {
            var rules = description.ActionDescriptor.EndpointMetadata.OfType<QueryParameterRules>().SingleOrDefault();
            Assert.True(rules is not null, $"{description.HttpMethod} {description.RelativePath} does not validate its query parameters.");
            Assert.Equal(
                description.ParameterDescriptions
                    .Where(parameter => parameter.Source == BindingSource.Query)
                    .Select(parameter => parameter.Name)
                    .Order(StringComparer.Ordinal),
                rules.Parameters.Select(rule => rule.Name).Order(StringComparer.Ordinal));
        });
    }
}
