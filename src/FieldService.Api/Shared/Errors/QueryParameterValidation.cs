using System.Reflection;

namespace FieldService.Api.Shared.Errors;

public static class QueryParameterValidation
{
    public static RouteHandlerBuilder ValidateQueryParameters(this RouteHandlerBuilder builder)
    {
        builder.Add(endpoint =>
        {
            var handler = endpoint.Metadata.OfType<MethodInfo>().FirstOrDefault()
                ?? throw new InvalidOperationException(
                    $"{endpoint.DisplayName} has no handler method, so its query parameters are unknown.");
            var rules = new QueryParameterRules(QueryParameterRule.ListFor(handler));
            endpoint.Metadata.Add(rules);

            var next = endpoint.RequestDelegate
                ?? throw new InvalidOperationException($"{endpoint.DisplayName} has no request delegate.");
            endpoint.RequestDelegate = context => InvokeAsync(context, rules, next);
        });

        return builder;
    }

    public static Dictionary<string, string[]> Validate(IQueryCollection query, QueryParameterRules rules)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        foreach (var rule in rules.Parameters)
        {
            if (!query.TryGetValue(rule.Name, out var values) || values.Count == 0)
            {
                continue;
            }

            if (values.Count > 1)
            {
                errors[rule.Name] = [QueryParameterRule.RepeatedValueMessage];
                continue;
            }

            if (rule.Check(values[0]) is { } message)
            {
                errors[rule.Name] = [message];
            }
        }

        return errors;
    }

    private static async Task InvokeAsync(HttpContext context, QueryParameterRules rules, RequestDelegate next)
    {
        var errors = Validate(context.Request.Query, rules);
        if (errors.Count == 0)
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.RequestServices.GetRequiredService<IProblemDetailsService>().TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = context,
            ProblemDetails = new HttpValidationProblemDetails(errors)
            {
                Status = StatusCodes.Status400BadRequest,
            },
        });
    }
}
