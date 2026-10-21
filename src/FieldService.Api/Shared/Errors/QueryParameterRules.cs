namespace FieldService.Api.Shared.Errors;

public sealed class QueryParameterRules(IReadOnlyList<QueryParameterRule> parameters)
{
    public IReadOnlyList<QueryParameterRule> Parameters { get; } = parameters;
}
