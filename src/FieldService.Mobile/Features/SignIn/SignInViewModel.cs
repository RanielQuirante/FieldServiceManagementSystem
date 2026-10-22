using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FieldService.Mobile.Shared.Connectivity;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Shared.Session;

namespace FieldService.Mobile.Features.SignIn;

public sealed partial class SignInViewModel : ObservableObject
{
    public const int MaxCredentialLength = 256;
    public const string WrongApplicationCode = "auth.wrong-application";
    public const string SessionEndedTitle = "Your session ended.";
    public const string SessionEndedMessage = "Sign in again to continue.";

    private readonly ISessionService _session;
    private readonly INavigator _navigator;
    private readonly NetworkStatus _network;
    private readonly AppStartup _startup;
    private readonly SynchronizationContext? _context;

    public SignInViewModel(ISessionService session, INavigator navigator, NetworkStatus network, AppStartup startup)
    {
        _session = session;
        _navigator = navigator;
        _network = network;
        _startup = startup;
        _context = SynchronizationContext.Current;
        Email = string.Empty;
        Password = string.Empty;
        session.SessionEnded += OnSessionEnded;
    }

    [ObservableProperty]
    public partial string Email { get; set; }

    [ObservableProperty]
    public partial string Password { get; set; }

    [ObservableProperty]
    public partial string? EmailError { get; set; }

    [ObservableProperty]
    public partial string? PasswordError { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasFailure), nameof(ShowsSessionNotice))]
    public partial SignInFailure? Failure { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial int EmailFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int PasswordFocusRequest { get; private set; }

    [ObservableProperty]
    public partial int PasswordHideRequest { get; private set; }

    public bool HasFailure => Failure is not null;

    public bool ShowsSessionNotice => Failure is null && _session.EndReason == SessionEndReason.Expired;

    private static SignInFailure Unreachable { get; } =
        new("FieldService could not be reached.", ScreenViewModel.CheckConnectionMessage, null);

    private static string? ValidateEmail(string email)
    {
        if (email.Length == 0)
        {
            return "Enter your email address.";
        }

        var at = email.IndexOf('@', StringComparison.Ordinal);
        var complete = at > 0 && at == email.LastIndexOf('@') && at < email.Length - 1;
        return complete ? null : "Enter a complete email address, such as name@fieldservice.example.";
    }

    private static string? ValidatePassword(string password) =>
        password.Length == 0 ? "Enter your password." : null;

    [RelayCommand]
    private async Task AppearingAsync()
    {
        OnPropertyChanged(nameof(ShowsSessionNotice));
        if (!_startup.IsComplete)
        {
            await _startup.Completed;
            OnPropertyChanged(nameof(ShowsSessionNotice));
        }
    }

    [RelayCommand]
    private void NavigatedTo() => ForgetForm();

    [RelayCommand]
    private void Loaded() => _startup.ReportShown(Routes.SignIn);

    [RelayCommand]
    private async Task SignInAsync(CancellationToken cancellationToken)
    {
        Failure = null;
        var email = Email.Trim();
        EmailError = ValidateEmail(email);
        PasswordError = ValidatePassword(Password);
        if (EmailError is not null || PasswordError is not null)
        {
            FocusFirstInvalidField();
            return;
        }

        var password = Password;
        IsBusy = true;
        try
        {
            if (!_network.IsOnline)
            {
                Failure = Unreachable;
                return;
            }

            await _session.SignInAsync(email, password, cancellationToken);
            await _navigator.GoToAsync(Routes.Jobs);
        }
        catch (ApiException exception)
        {
            Failure = Describe(exception);
            if (Failure is null)
            {
                FocusFirstInvalidField();
            }
        }
        catch (HttpRequestException)
        {
            Failure = Unreachable;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Failure = Unreachable;
        }
        finally
        {
            Password = string.Empty;
            IsBusy = false;
            OnPropertyChanged(nameof(ShowsSessionNotice));
        }
    }

    private void OnSessionEnded(object? sender, EventArgs e)
    {
        if (_context is { } context && !ReferenceEquals(SynchronizationContext.Current, context))
        {
            context.Post(_ => ForgetForm(), null);
            return;
        }

        ForgetForm();
    }

    private void ForgetForm()
    {
        Email = string.Empty;
        Password = string.Empty;
        EmailError = null;
        PasswordError = null;
        Failure = null;
        PasswordHideRequest++;
        OnPropertyChanged(nameof(ShowsSessionNotice));
    }

    private void FocusFirstInvalidField()
    {
        if (EmailError is not null)
        {
            EmailFocusRequest++;
        }
        else if (PasswordError is not null)
        {
            PasswordFocusRequest++;
        }
    }

    private SignInFailure? Describe(ApiException exception)
    {
        if (exception.StatusCode == HttpStatusCode.Unauthorized)
        {
            return new SignInFailure("The email or password is incorrect.", null, null);
        }

        if (exception.Code == WrongApplicationCode)
        {
            return new SignInFailure("This is a dispatcher account.", "Dispatchers sign in from FieldService Admin.", null);
        }

        if (exception.StatusCode == HttpStatusCode.TooManyRequests)
        {
            return new SignInFailure("Too many attempts.", ScreenViewModel.RetryWait(exception.RetryAfter), null);
        }

        if (exception.Code == ApiProblem.ValidationFailedCode && exception.Problem is { } problem)
        {
            EmailError = problem.FirstError("email");
            PasswordError = problem.FirstError("password");
            var others = problem.ErrorsExcept("email", "password");
            if (others.Count == 0 && (EmailError is not null || PasswordError is not null))
            {
                return null;
            }

            return new SignInFailure("Check the sign-in details.", others.Count > 0 ? string.Join(' ', others) : null, null);
        }

        return new SignInFailure("Sign-in could not be completed.", "Try again in a moment.", exception.CorrelationId);
    }
}
