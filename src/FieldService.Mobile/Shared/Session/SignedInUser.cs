using FieldService.Mobile.Features.SignIn;

namespace FieldService.Mobile.Shared.Session;

public sealed record SignedInUser(Guid Id, string DisplayName, UserRole Role, string? Email = null)
{
    public override string ToString() => $"{nameof(SignedInUser)} {Id}";
}
