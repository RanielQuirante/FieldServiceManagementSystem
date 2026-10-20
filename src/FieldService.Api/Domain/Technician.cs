namespace FieldService.Api.Domain;

public sealed class Technician
{
    private Technician()
    {
    }

    public Guid UserId { get; private set; }

    public Trade Trade { get; private set; }

    public string PhoneNumber { get; private set; } = string.Empty;

    public bool IsOnShift { get; private set; }

    public User User { get; private set; } = null!;

    public static Technician Create(User user, Trade trade, string phoneNumber, bool isOnShift)
    {
        return new Technician
        {
            UserId = user.Id,
            User = user,
            Trade = trade,
            PhoneNumber = phoneNumber,
            IsOnShift = isOnShift,
        };
    }
}
