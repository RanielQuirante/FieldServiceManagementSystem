namespace FieldService.Api.Domain;

public sealed class RefreshToken
{
    private RefreshToken()
    {
    }

    public Guid Id { get; private set; }

    public Guid SessionId { get; private set; }

    public byte[] TokenHash { get; private set; } = [];

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? ConsumedAt { get; private set; }

    public Guid? ReplacedById { get; private set; }

    public static RefreshToken Create(Guid sessionId, byte[] tokenHash, DateTimeOffset now)
    {
        return new RefreshToken
        {
            Id = Guid.CreateVersion7(now),
            SessionId = sessionId,
            TokenHash = tokenHash,
            CreatedAt = now,
        };
    }
}
