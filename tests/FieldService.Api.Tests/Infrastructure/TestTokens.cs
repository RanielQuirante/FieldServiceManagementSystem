using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using Microsoft.Extensions.DependencyInjection;

namespace FieldService.Api.Tests.Infrastructure;

public static class TestTokens
{
    public static AccessToken Issue(ApiFactory factory, Guid userId, UserRole role, Guid? sessionId = null)
    {
        return factory.Services
            .GetRequiredService<AccessTokenIssuer>()
            .Issue(userId, role, sessionId ?? Guid.CreateVersion7());
    }

    public static string For(ApiFactory factory, User user, Guid? sessionId = null)
    {
        return Issue(factory, user.Id, user.Role, sessionId).Value;
    }

    public static string For(ApiFactory factory, UserRole role)
    {
        return Issue(factory, Guid.CreateVersion7(), role).Value;
    }
}
