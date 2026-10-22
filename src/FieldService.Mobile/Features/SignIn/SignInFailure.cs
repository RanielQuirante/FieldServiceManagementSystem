namespace FieldService.Mobile.Features.SignIn;

public sealed record SignInFailure(string Title, string? Message, string? Reference)
{
    public bool HasMessage => !string.IsNullOrEmpty(Message);

    public bool HasReference => !string.IsNullOrEmpty(Reference);
}
