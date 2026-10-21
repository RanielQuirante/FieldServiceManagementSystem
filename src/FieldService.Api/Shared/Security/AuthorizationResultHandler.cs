using System.Security.Claims;
using FieldService.Api.Shared.Http;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace FieldService.Api.Shared.Security;

public sealed class AuthorizationResultHandler(ILogger<AuthorizationResultHandler> logger)
    : IAuthorizationMiddlewareResultHandler
{
    private const string FallbackPolicyName = "fallback";

    private readonly AuthorizationMiddlewareResultHandler _inner = new();

    public Task HandleAsync(
        RequestDelegate next,
        HttpContext context,
        AuthorizationPolicy policy,
        PolicyAuthorizationResult authorizeResult)
    {
        if (context.GetEndpoint() is not RouteEndpoint endpoint)
        {
            return next(context);
        }

        if (authorizeResult.Forbidden)
        {
            var names = endpoint.Metadata
                .GetOrderedMetadata<IAuthorizeData>()
                .Select(data => data.Policy)
                .OfType<string>()
                .ToArray();
            SecurityLog.Forbidden(
                logger,
                names.Length == 0 ? FallbackPolicyName : string.Join(",", names),
                context.User.FindFirstValue(AccessTokenClaims.Subject),
                context.User.FindFirstValue(AccessTokenClaims.Role),
                RouteTemplates.Of(endpoint));
        }

        return _inner.HandleAsync(next, context, policy, authorizeResult);
    }
}
