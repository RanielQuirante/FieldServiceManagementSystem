namespace FieldService.Api.Domain;

public sealed class UserSession
{
    private readonly List<RefreshToken> _refreshTokens = [];

    private UserSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public SessionClient Client { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastRefreshedAt { get; private set; }

    public DateTimeOffset IdleExpiresAt { get; private set; }

    public DateTimeOffset AbsoluteExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public SessionRevocationReason? RevokedReason { get; private set; }

    public IReadOnlyList<RefreshToken> RefreshTokens => _refreshTokens;

    public static UserSession Create(
        Guid userId,
        SessionClient client,
        DateTimeOffset now,
        DateTimeOffset idleExpiresAt,
        DateTimeOffset absoluteExpiresAt)
    {
        return new UserSession
        {
            Id = Guid.CreateVersion7(now),
            UserId = userId,
            Client = client,
            CreatedAt = now,
            IdleExpiresAt = idleExpiresAt,
            AbsoluteExpiresAt = absoluteExpiresAt,
        };
    }
}
