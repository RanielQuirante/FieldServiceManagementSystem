using FieldService.Api.Features.Auth.IssueSocketTicket;
using FieldService.Api.Features.Auth.RefreshSession;
using FieldService.Api.Features.Auth.SignIn;
using FieldService.Api.Features.Auth.SignOut;

namespace FieldService.Api.Features.Auth;

public static class AuthEndpoints
{
    public const string Prefix = "/api/auth";

    public const long RequestBodyLimit = 16 * 1024;

    public static void Map(IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup(Prefix).WithTags("Auth");
        auth.ProducesProblem(StatusCodes.Status403Forbidden);
        auth.ProducesProblem(StatusCodes.Status429TooManyRequests);

        SignInEndpoint.Map(auth);
        RefreshSessionEndpoint.Map(auth);
        SignOutEndpoint.Map(auth);
        IssueSocketTicketEndpoint.Map(auth);
    }
}
