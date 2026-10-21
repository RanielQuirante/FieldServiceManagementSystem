using FieldService.Api.Domain;

namespace FieldService.Api.Shared.Security;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    public const string AccessTokenKeyName = "Auth:AccessTokenKey";

    public const string SocketTicketKeyName = "Auth:SocketTicketKey";

    public const string Issuer = "fieldservice-api";

    public const string AccessTokenAudience = "fieldservice-api";

    public static readonly TimeSpan AccessTokenLifetime = TimeSpan.FromMinutes(15);

    public static readonly TimeSpan ClockSkew = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan RefreshGraceWindow = TimeSpan.FromSeconds(30);

    public string? AccessTokenKey { get; set; }

    public string? SocketTicketKey { get; set; }

    public static SessionLifetime SessionLifetimeFor(SessionClient client) => client switch
    {
        SessionClient.Web => new SessionLifetime(TimeSpan.FromMinutes(60), TimeSpan.FromHours(12)),
        SessionClient.Mobile => new SessionLifetime(TimeSpan.FromDays(7), TimeSpan.FromDays(30)),
        _ => throw new ArgumentOutOfRangeException(nameof(client), client, "The session client is not supported."),
    };
}
