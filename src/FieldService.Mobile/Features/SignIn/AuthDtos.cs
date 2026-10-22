namespace FieldService.Mobile.Features.SignIn;

public enum SessionClient
{
    Unknown,
    Web,
    Mobile,
}

public enum UserRole
{
    Unknown,
    Administrator,
    Technician,
}

public sealed record SignInRequest(string Email, string Password, SessionClient Client)
{
    public override string ToString() => nameof(SignInRequest);
}

public sealed record RefreshSessionRequest(string? RefreshToken)
{
    public override string ToString() => nameof(RefreshSessionRequest);
}

public sealed record SignOutRequest(string? RefreshToken)
{
    public override string ToString() => nameof(SignOutRequest);
}

public sealed record SessionUser(Guid Id, string DisplayName, UserRole Role)
{
    public override string ToString() => $"{nameof(SessionUser)} {Id}";
}

public sealed record SessionResponse(string AccessToken, DateTimeOffset AccessTokenExpiresAt, SessionUser User, string? RefreshToken)
{
    public override string ToString() => nameof(SessionResponse);
}
