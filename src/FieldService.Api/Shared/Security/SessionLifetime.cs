namespace FieldService.Api.Shared.Security;

public sealed record SessionLifetime(TimeSpan Idle, TimeSpan Absolute)
{
    public DateTimeOffset IdleExpiry(DateTimeOffset now, DateTimeOffset absoluteExpiresAt)
    {
        var idle = now + Idle;
        return idle < absoluteExpiresAt ? idle : absoluteExpiresAt;
    }
}
