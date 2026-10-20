namespace FieldService.Api.Domain;

public sealed class User
{
    private User()
    {
    }

    public Guid Id { get; private set; }

    public string Email { get; private set; } = string.Empty;

    public string DisplayName { get; private set; } = string.Empty;

    public string PasswordHash { get; private set; } = string.Empty;

    public UserRole Role { get; private set; }

    public bool IsActive { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? LastSignedInAt { get; private set; }

    public Technician? Technician { get; private set; }

    public static User Create(
        string email,
        string displayName,
        string passwordHash,
        UserRole role,
        DateTimeOffset now)
    {
        return new User
        {
            Id = Guid.CreateVersion7(now),
            Email = email.ToLowerInvariant(),
            DisplayName = displayName,
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now,
        };
    }
}
