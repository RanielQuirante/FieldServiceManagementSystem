namespace FieldService.Mobile.Shared.Session;

public interface ISessionService
{
    event EventHandler? SessionEnded;

    SignedInUser? User { get; }

    string? AccessToken { get; }

    SessionCredential Credential { get; }

    SessionEndReason EndReason { get; }

    Task<SignedInUser> SignInAsync(string email, string password, CancellationToken cancellationToken);

    Task<string> TryRestoreAsync(CancellationToken cancellationToken);

    Task<string?> RefreshAccessTokenAsync(SessionCredential rejected, CancellationToken cancellationToken);

    Task SignOutAsync(CancellationToken cancellationToken);
}
