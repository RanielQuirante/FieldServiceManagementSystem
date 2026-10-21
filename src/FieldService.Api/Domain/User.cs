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
        var at = UtcInstant.Truncate(now);
        return new User
        {
            Id = Guid.CreateVersion7(at),
            Email = DomainText.Required(email, nameof(email)).ToLowerInvariant(),
            DisplayName = DomainText.Required(displayName, nameof(displayName)),
            PasswordHash = passwordHash,
            Role = role,
            IsActive = true,
            CreatedAt = at,
            UpdatedAt = at,
        };
    }

    public void ReplacePasswordHash(string passwordHash, DateTimeOffset now)
    {
        PasswordHash = passwordHash;
        UpdatedAt = UtcInstant.Truncate(now);
    }

    public void Deactivate(DateTimeOffset now)
    {
        if (!IsActive)
        {
            return;
        }

        IsActive = false;
        UpdatedAt = UtcInstant.Truncate(now);
    }

    public void Activate(DateTimeOffset now)
    {
        if (IsActive)
        {
            return;
        }

        IsActive = true;
        UpdatedAt = UtcInstant.Truncate(now);
    }
}
