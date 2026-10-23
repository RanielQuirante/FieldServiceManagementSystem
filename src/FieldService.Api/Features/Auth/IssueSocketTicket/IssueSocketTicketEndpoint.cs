using FieldService.Api.Domain;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace FieldService.Api.Features.Auth.IssueSocketTicket;

public static class IssueSocketTicketEndpoint
{
    public static void Map(RouteGroupBuilder auth) =>
        auth.MapPost("/socket-tickets", HandleAsync)
            .WithName("IssueSocketTicket")
            .WithSummary("Issue a socket ticket")
            .WithDescription("Issues a ticket for one connection attempt to the notification service's Socket.IO endpoint. The ticket is valid for 60 seconds and is accepted once; every call issues a new ticket. Send no body. The access token's session must still be live and its user active; a revoked or expired session, or an inactive user, returns 401 auth.session-expired.")
            .RequireAuthorization(Policies.Administrator)
            .RequireRateLimiting(RateLimitPolicies.SocketTickets)
            .WithMetadata(new RequestSizeLimitAttribute(AuthEndpoints.RequestBodyLimit))
            .ProducesProblem(StatusCodes.Status401Unauthorized);

    public static async Task<Results<Ok<SocketTicketResponse>, ProblemHttpResult>> HandleAsync(
        HttpContext http,
        CurrentUser user,
        FieldServiceDbContext db,
        SocketTicketIssuer tickets,
        TimeProvider time,
        ILoggerFactory loggers,
        CancellationToken cancellationToken)
    {
        var now = UtcInstant.Truncate(time.GetUtcNow());
        var sessionIsLive = await db.UserSessions
            .AsNoTracking()
            .Where(session => session.Id == user.SessionId
                && session.UserId == user.Id
                && session.RevokedAt == null
                && session.IdleExpiresAt > now
                && session.AbsoluteExpiresAt > now)
            .Join(
                db.Users.Where(entry => entry.IsActive),
                session => session.UserId,
                entry => entry.Id,
                (session, _) => session.Id)
            .AnyAsync(cancellationToken);
        if (!sessionIsLive)
        {
            return Problems.Of(http, ErrorCodes.SessionExpired);
        }

        var ticket = tickets.Issue(user.Id, user.Role);
        SecurityLog.SocketTicketIssued(loggers.CreateLogger(typeof(IssueSocketTicketEndpoint)), user.Id, ticket.TicketId);
        return TypedResults.Ok(new SocketTicketResponse(ticket.Value, ticket.ExpiresAt));
    }
}
