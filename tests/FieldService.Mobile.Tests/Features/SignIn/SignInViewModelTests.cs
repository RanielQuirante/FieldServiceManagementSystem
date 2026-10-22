using System.Net;
using System.Xml.Linq;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared.Connectivity;
using FieldService.Mobile.Shared.Controls;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Maui.Networking;
using NSubstitute;

namespace FieldService.Mobile.Tests.Features.SignIn;

public sealed class SignInViewModelTests
{
    private const string Email = "aisha.karim@fieldservice.example";
    private static readonly Guid _technicianId = Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e");

    [Fact]
    public void SignInIsEnabledFromTheStart_WhileBothFieldsAreEmpty()
    {
        var (viewModel, _) = Create(StubHttpMessageHandler.Sequence());

        Assert.True(viewModel.SignInCommand.CanExecute(null));
        Assert.Null(viewModel.EmailError);
        Assert.Null(viewModel.PasswordError);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("   ", "")]
    public async Task PressingSignInWithEmptyFieldsShowsBothFieldErrorsFocusesEmailAndSendsNothing(string email, string password)
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence());
        viewModel.Email = email;
        viewModel.Password = password;

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal("Enter your email address.", viewModel.EmailError);
        Assert.Equal("Enter your password.", viewModel.PasswordError);
        Assert.Equal(1, viewModel.EmailFocusRequest);
        Assert.Equal(0, viewModel.PasswordFocusRequest);
        Assert.Null(viewModel.Failure);
        Assert.Empty(harness.Stub.Requests);
    }

    [Fact]
    public async Task PressingSignInWithOnlyThePasswordEmptyFocusesThePassword()
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence());
        viewModel.Email = Email;

        await viewModel.SignInCommand.ExecuteAsync(null);
        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Null(viewModel.EmailError);
        Assert.Equal("Enter your password.", viewModel.PasswordError);
        Assert.Equal(0, viewModel.EmailFocusRequest);
        Assert.Equal(2, viewModel.PasswordFocusRequest);
        Assert.Empty(harness.Stub.Requests);
    }

    [Fact]
    public async Task ShowsTheSessionEndedNotice_AfterAnExpiredSessionUntilAnErrorReplacesIt()
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(ProblemResponses.SessionExpired, ProblemResponses.InvalidCredentials));
        Assert.False(viewModel.ShowsSessionNotice);
        harness.StoreRefreshToken(TestSession.NewRefreshToken());
        await harness.Session.TryRestoreAsync(CancellationToken.None);

        viewModel.AppearingCommand.Execute(null);
        Assert.True(viewModel.ShowsSessionNotice);

        Fill(viewModel);
        await viewModel.SignInCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.Failure);
        Assert.False(viewModel.ShowsSessionNotice);
    }

    [Fact]
    public async Task ShowsTheSessionEndedNotice_WhenAStoredSessionIsRejectedAfterThePageAppeared()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(ProblemResponses.SessionExpired));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());
        var connectivity = Substitute.For<IConnectivity>();
        connectivity.NetworkAccess.Returns(NetworkAccess.Internet);
        var startup = Startup(harness);
        var viewModel = new SignInViewModel(harness.Session, harness.Navigator, new NetworkStatus(connectivity), startup);
        viewModel.LoadedCommand.Execute(null);
        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);
        Assert.False(viewModel.ShowsSessionNotice);

        await startup.RunAsync(CancellationToken.None);
        await appearing;

        Assert.True(viewModel.ShowsSessionNotice);
    }

    [Fact]
    public async Task ShowsNoNotice_AfterASignOutTheTechnicianChose()
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(ProblemResponses.NoContent));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());
        await harness.Session.SignOutAsync(CancellationToken.None);

        viewModel.AppearingCommand.Execute(null);

        Assert.False(viewModel.ShowsSessionNotice);
    }

    [Fact]
    public async Task SignInIsDisabledAndBusy_WhileTheRequestRuns()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (viewModel, _) = Create(new StubHttpMessageHandler((_, _) => release.Task));
        Fill(viewModel);

        var running = viewModel.SignInCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsBusy);
        Assert.False(viewModel.SignInCommand.CanExecute(null));
        release.SetResult(ProblemResponses.InvalidCredentials());
        await running;
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task SignInStoresTheRefreshTokenKeepsTheAccessTokenInMemoryAndGoesToJobs()
    {
        var refreshToken = TestSession.NewRefreshToken();
        var accessToken = TestSession.NewAccessToken();
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(accessToken, refreshToken, _technicianId, "Aisha Karim")));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Failure);
        await harness.Storage.Received(1).SetAsync(TokenStore.RefreshTokenKey, refreshToken);
        await harness.Storage.DidNotReceive().SetAsync(Arg.Any<string>(), accessToken);
        Assert.Equal(accessToken, harness.Session.AccessToken);
        await harness.Navigator.Received(1).GoToAsync(Routes.Jobs);
        Assert.Equal(string.Empty, viewModel.Password);
    }

    [Fact]
    public async Task SendsTheTrimmedEmailAndTheMobileClient()
    {
        var password = TestSession.NewPassword();
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(ProblemResponses.InvalidCredentials));
        viewModel.Email = "  " + Email + " ";
        viewModel.Password = password;

        await viewModel.SignInCommand.ExecuteAsync(null);

        var request = Assert.Single(harness.Stub.Requests);
        Assert.Equal("/api/auth/sign-in", request.Path);
        Assert.Null(request.Authorization);
        var body = ProblemResponses.ReadBody(request);
        Assert.Equal(Email, body.GetProperty("email").GetString());
        Assert.Equal(password, body.GetProperty("password").GetString());
        Assert.Equal("Mobile", body.GetProperty("client").GetString());
    }

    [Fact]
    public async Task ShowsTheIncorrectCredentialsMessageAndClearsThePassword_WhenTheApiAnswers401()
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(ProblemResponses.InvalidCredentials));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(new SignInFailure("The email or password is incorrect.", null, null), viewModel.Failure);
        Assert.True(viewModel.HasFailure);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Equal(Email, viewModel.Email);
        await harness.Navigator.DidNotReceive().GoToAsync(Arg.Any<string>());
        await harness.Storage.DidNotReceive().SetAsync(Arg.Any<string>(), Arg.Any<string>());
    }

    [Fact]
    public async Task ShowsTheDispatcherMessage_WhenAnAdministratorSignsIn()
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(ProblemResponses.WrongApplication));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(
            new SignInFailure("This is a dispatcher account.", "Dispatchers sign in from FieldService Admin.", null),
            viewModel.Failure);
        Assert.Null(harness.Session.AccessToken);
        Assert.Equal(string.Empty, viewModel.Password);
    }

    [Theory]
    [InlineData(42, "Try again in 42 seconds.")]
    [InlineData(1, "Try again in 1 second.")]
    [InlineData(null, "Try again in 60 seconds.")]
    public async Task ShowsTheRetryWait_WhenSignInIsRateLimited(int? retryAfterSeconds, string message)
    {
        TimeSpan? retryAfter = retryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;
        var (viewModel, _) = Create(StubHttpMessageHandler.Sequence(() => ProblemResponses.RateLimited(retryAfter)));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(new SignInFailure("Too many attempts.", message, null), viewModel.Failure);
    }

    [Fact]
    public async Task ShowsTheUnreachableMessageWithoutARequest_WhenTheDeviceIsOffline()
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(), NetworkAccess.None);
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(
            new SignInFailure("FieldService could not be reached.", "Check your connection, then try again.", null),
            viewModel.Failure);
        Assert.Empty(harness.Stub.Requests);
        Assert.Equal(string.Empty, viewModel.Password);
    }

    [Fact]
    public async Task SignsIn_WhenTheConnectionComesBack()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim")));
        var connectivity = Substitute.For<IConnectivity>();
        connectivity.NetworkAccess.Returns(NetworkAccess.None);
        var network = new NetworkStatus(connectivity);
        var viewModel = new SignInViewModel(harness.Session, harness.Navigator, network, CompletedStartup(harness));
        Fill(viewModel);
        await viewModel.SignInCommand.ExecuteAsync(null);
        Assert.False(network.IsOnline);

        connectivity.ConnectivityChanged += Raise.EventWith(new ConnectivityChangedEventArgs(NetworkAccess.Internet, []));
        viewModel.Password = TestSession.NewPassword();
        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.True(network.IsOnline);
        Assert.Null(viewModel.Failure);
        await harness.Navigator.Received(1).GoToAsync(Routes.Jobs);
    }

    [Fact]
    public async Task ShowsTheUnreachableMessage_WhenTheApiCannotBeReached()
    {
        var (viewModel, _) = Create(StubHttpMessageHandler.Throwing(new HttpRequestException("Connection refused")));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal("FieldService could not be reached.", viewModel.Failure?.Title);
        Assert.False(viewModel.IsBusy);
    }

    [Fact]
    public async Task ShowsTheUnreachableMessage_WhenTheRequestTimesOut()
    {
        var (viewModel, _) = Create(StubHttpMessageHandler.Throwing(new TaskCanceledException("timeout")));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal("FieldService could not be reached.", viewModel.Failure?.Title);
    }

    [Fact]
    public async Task ShowsTheCorrelationReference_WhenTheServerFails()
    {
        var correlationId = Guid.NewGuid().ToString("D");
        var (viewModel, _) = Create(StubHttpMessageHandler.Sequence(() => ProblemResponses.ServerError(correlationId)));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(new SignInFailure("Sign-in could not be completed.", "Try again in a moment.", correlationId), viewModel.Failure);
        Assert.True(viewModel.Failure!.HasReference);
    }

    [Fact]
    public async Task ShowsTheRequestCorrelationId_WhenTheServerFailsWithoutAProblem()
    {
        var (viewModel, harness) = Create(new StubHttpMessageHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadGateway))));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(Assert.Single(harness.Stub.Requests).CorrelationId, viewModel.Failure?.Reference);
    }

    [Fact]
    public async Task ShowsServerFieldErrorsOnTheirFields_WhenTheApiRejectsTheRequest()
    {
        var (viewModel, _) = Create(StubHttpMessageHandler.Sequence(() => ProblemResponses.ValidationFailed(
            new Dictionary<string, string[]> { ["email"] = ["Enter an email address."] })));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal("Enter an email address.", viewModel.EmailError);
        Assert.Null(viewModel.PasswordError);
        Assert.Null(viewModel.Failure);
    }

    [Fact]
    public async Task ShowsOtherServerErrorsAsAFormMessage()
    {
        var (viewModel, _) = Create(StubHttpMessageHandler.Sequence(() => ProblemResponses.ValidationFailed(
            new Dictionary<string, string[]> { ["client"] = ["Choose Web or Mobile."] })));
        Fill(viewModel);

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal(new SignInFailure("Check the sign-in details.", "Choose Web or Mobile.", null), viewModel.Failure);
    }

    [Theory]
    [InlineData("aisha.karim")]
    [InlineData("@fieldservice.example")]
    [InlineData("aisha.karim@")]
    [InlineData("aisha@karim@fieldservice.example")]
    public async Task RejectsAnIncompleteEmailWithoutARequestAndKeepsThePassword(string email)
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence());
        var password = TestSession.NewPassword();
        viewModel.Email = email;
        viewModel.Password = password;

        await viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Equal("Enter a complete email address, such as name@fieldservice.example.", viewModel.EmailError);
        Assert.Empty(harness.Stub.Requests);
        Assert.Equal(password, viewModel.Password);
    }

    [Fact]
    public async Task ClearsAnEarlierFailure_WhenTheNextAttemptStarts()
    {
        var release = new TaskCompletionSource<HttpResponseMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var (viewModel, _) = Create(new StubHttpMessageHandler((_, _) =>
            Interlocked.Increment(ref calls) == 1 ? Task.FromResult(ProblemResponses.InvalidCredentials()) : release.Task));
        Fill(viewModel);
        await viewModel.SignInCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.Failure);

        viewModel.Password = TestSession.NewPassword();
        var running = viewModel.SignInCommand.ExecuteAsync(null);

        Assert.Null(viewModel.Failure);
        release.SetResult(ProblemResponses.InvalidCredentials());
        await running;
    }

    [Fact]
    public async Task ThePasswordAndTokensNeverReachALog()
    {
        var logs = new CapturingLoggerProvider();
        var refreshToken = TestSession.NewRefreshToken();
        var accessToken = TestSession.NewAccessToken();
        var (viewModel, _) = Create(
            StubHttpMessageHandler.Sequence(
                ProblemResponses.InvalidCredentials,
                () => ProblemResponses.Session(accessToken, refreshToken, _technicianId, "Aisha Karim")),
            logs: logs);
        var firstPassword = TestSession.NewPassword();
        var secondPassword = TestSession.NewPassword();

        viewModel.Email = Email;
        viewModel.Password = firstPassword;
        await viewModel.SignInCommand.ExecuteAsync(null);
        viewModel.Password = secondPassword;
        await viewModel.SignInCommand.ExecuteAsync(null);

        var text = logs.AllText();
        Assert.NotEmpty(logs.Entries);
        Assert.DoesNotContain(firstPassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain(secondPassword, text, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, text, StringComparison.Ordinal);
        Assert.DoesNotContain(Email, text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AfterAnExpiryTheCachedSignInShowsAnEmptyFormAndKeepsTheSessionEndedNotice()
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"),
            ProblemResponses.SessionExpired));
        Fill(viewModel);
        await viewModel.SignInCommand.ExecuteAsync(null);
        Assert.Equal(Email, viewModel.Email);
        var hideRequests = viewModel.PasswordHideRequest;

        await harness.Session.RefreshAccessTokenAsync(harness.Session.Credential, CancellationToken.None);
        await WaitFor(() => viewModel.Email.Length == 0);
        viewModel.AppearingCommand.Execute(null);

        AssertEmptyForm(viewModel);
        Assert.Equal(hideRequests + 1, viewModel.PasswordHideRequest);
        Assert.Equal(SessionEndReason.Expired, harness.Session.EndReason);
        Assert.True(viewModel.ShowsSessionNotice);
    }

    [Fact]
    public async Task AfterARevocationFoundAtStartupTheSignInShowsAnEmptyFormAndTheSessionEndedNotice()
    {
        var harness = TestSession.Create(StubHttpMessageHandler.Sequence(
            () => ProblemResponses.Problem(HttpStatusCode.Unauthorized, "auth.session-revoked", "The session has been revoked.")));
        harness.StoreRefreshToken(TestSession.NewRefreshToken());
        var connectivity = Substitute.For<IConnectivity>();
        connectivity.NetworkAccess.Returns(NetworkAccess.Internet);
        var startup = Startup(harness);
        var viewModel = new SignInViewModel(harness.Session, harness.Navigator, new NetworkStatus(connectivity), startup);
        viewModel.Email = Email;
        viewModel.LoadedCommand.Execute(null);
        var appearing = viewModel.AppearingCommand.ExecuteAsync(null);

        await startup.RunAsync(CancellationToken.None);
        await appearing;
        await WaitFor(() => viewModel.Email.Length == 0);

        AssertEmptyForm(viewModel);
        Assert.Equal(1, viewModel.PasswordHideRequest);
        Assert.True(viewModel.ShowsSessionNotice);
    }

    [Fact]
    public async Task AfterASignOutTheCachedSignInShowsAnEmptyFormWithoutTheNoticeOrTheEarlierFailure()
    {
        var (viewModel, harness) = Create(StubHttpMessageHandler.Sequence(
            ProblemResponses.InvalidCredentials,
            () => ProblemResponses.Session(TestSession.NewAccessToken(), TestSession.NewRefreshToken(), _technicianId, "Aisha Karim"),
            ProblemResponses.NoContent));
        Fill(viewModel);
        await viewModel.SignInCommand.ExecuteAsync(null);
        Assert.NotNull(viewModel.Failure);
        Fill(viewModel);
        await viewModel.SignInCommand.ExecuteAsync(null);
        viewModel.Failure = new SignInFailure("Sign-in could not be completed.", "Try again in a moment.", Guid.NewGuid().ToString("D"));
        viewModel.EmailError = "Enter a complete email address, such as name@fieldservice.example.";
        viewModel.PasswordError = "Enter your password.";

        await harness.Session.SignOutAsync(CancellationToken.None);
        await WaitFor(() => viewModel.Email.Length == 0);
        viewModel.AppearingCommand.Execute(null);

        AssertEmptyForm(viewModel);
        Assert.Equal(SessionEndReason.SignedOut, harness.Session.EndReason);
        Assert.False(viewModel.ShowsSessionNotice);
    }

    [Fact]
    public async Task TheEndOfASessionSignalledFromAnotherThreadClearsTheFormOnTheScreensThread()
    {
        var session = Substitute.For<ISessionService>();
        session.EndReason.Returns(SessionEndReason.Expired);
        var connectivity = Substitute.For<IConnectivity>();
        connectivity.NetworkAccess.Returns(NetworkAccess.Internet);
        var startup = new AppStartup(session, Substitute.For<INavigator>(), new FakeTimeProvider(), new CapturingLoggerProvider().CreateLogger<AppStartup>());
        var screen = new QueuedSynchronizationContext();
        var viewModel = QueuedSynchronizationContext.CreateOn(screen, () => new SignInViewModel(session, Substitute.For<INavigator>(), new NetworkStatus(connectivity), startup));
        Fill(viewModel);

        await Task.Run(() => session.SessionEnded += Raise.Event(), TestContext.Current.CancellationToken);

        Assert.Equal(Email, viewModel.Email);
        Assert.Equal(1, screen.RunPending());
        AssertEmptyForm(viewModel);
        Assert.True(viewModel.ShowsSessionNotice);
    }

    [Theory]
    [InlineData(SessionEndReason.Expired, true)]
    [InlineData(SessionEndReason.SignedOut, false)]
    public void ANavigationToSignInClearsTheFormEvenWithoutTheEndOfSessionSignal(SessionEndReason reason, bool notice)
    {
        var session = Substitute.For<ISessionService>();
        session.EndReason.Returns(reason);
        var connectivity = Substitute.For<IConnectivity>();
        connectivity.NetworkAccess.Returns(NetworkAccess.Internet);
        var startup = new AppStartup(session, Substitute.For<INavigator>(), new FakeTimeProvider(), new CapturingLoggerProvider().CreateLogger<AppStartup>());
        var viewModel = new SignInViewModel(session, Substitute.For<INavigator>(), new NetworkStatus(connectivity), startup);
        Fill(viewModel);
        viewModel.EmailError = "Enter a complete email address, such as name@fieldservice.example.";
        viewModel.Failure = new SignInFailure("The email or password is incorrect.", null, null);

        viewModel.NavigatedToCommand.Execute(null);

        AssertEmptyForm(viewModel);
        Assert.Equal(1, viewModel.PasswordHideRequest);
        Assert.Equal(notice, viewModel.ShowsSessionNotice);
    }

    [Fact]
    public void TheSignInPageHandsEveryNavigationToItsViewModel()
    {
        var method = typeof(SignInPage).GetMethod(
            "OnNavigatedTo",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
            [typeof(Microsoft.Maui.Controls.NavigatedToEventArgs)]);
        var source = File.ReadAllText(Path.Combine(RepositoryPaths.MobileApp, "Features", "SignIn", "SignInPage.xaml.cs"));

        Assert.Equal(typeof(SignInPage), method?.DeclaringType);
        Assert.Contains("_viewModel.NavigatedToCommand.Execute(null);", source, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true, 0, 1, false, true)]
    [InlineData(true, 3, 4, false, true)]
    [InlineData(true, 4, 4, false, false)]
    [InlineData(false, 0, 1, false, false)]
    public void APasswordShownBeforeTheSessionEndedIsHiddenAgain(bool isPassword, int previous, int requested, bool isTextHidden, bool hidden)
    {
        Assert.Equal(hidden, FormField.HidesTextAfterRequest(isPassword, previous, requested, isTextHidden));
    }

    [Fact]
    public void ThePasswordFieldOfTheSignInPageListensForTheHideRequest()
    {
        var page = XDocument.Load(Path.Combine(RepositoryPaths.MobileApp, "Features", "SignIn", "SignInPage.xaml"));
        var fields = page.Descendants().Where(element => element.Name.LocalName == "FormField").ToList();

        var password = Assert.Single(fields, field => field.Attribute("IsPassword")?.Value == "True");
        Assert.Equal("{Binding PasswordHideRequest}", password.Attribute("HidePasswordRequest")?.Value);
        Assert.Equal("{Binding Email}", fields.Single(field => field.Attribute("IsPassword") is null).Attribute("Text")?.Value);
    }

    private static void AssertEmptyForm(SignInViewModel viewModel)
    {
        Assert.Equal(string.Empty, viewModel.Email);
        Assert.Equal(string.Empty, viewModel.Password);
        Assert.Null(viewModel.EmailError);
        Assert.Null(viewModel.PasswordError);
        Assert.Null(viewModel.Failure);
        Assert.False(viewModel.HasFailure);
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 200 && !condition(); attempt++)
        {
            await Task.Delay(5);
        }

        Assert.True(condition());
    }

    private static void Fill(SignInViewModel viewModel)
    {
        viewModel.Email = Email;
        viewModel.Password = TestSession.NewPassword();
    }

    private static (SignInViewModel ViewModel, TestSession Harness) Create(
        StubHttpMessageHandler stub,
        NetworkAccess access = NetworkAccess.Internet,
        CapturingLoggerProvider? logs = null)
    {
        var harness = TestSession.Create(stub, logs);
        var connectivity = Substitute.For<IConnectivity>();
        connectivity.NetworkAccess.Returns(access);
        var viewModel = new SignInViewModel(harness.Session, harness.Navigator, new NetworkStatus(connectivity), CompletedStartup(harness));
        return (viewModel, harness);
    }

    private static AppStartup Startup(TestSession harness) =>
        new(harness.Session, harness.Navigator, new FakeTimeProvider(), harness.Logs.CreateLogger<AppStartup>());

    private static AppStartup CompletedStartup(TestSession harness)
    {
        var startup = Startup(harness);
        startup.ReportShown(Routes.SignIn);
        startup.RunAsync(CancellationToken.None).GetAwaiter().GetResult();
        return startup;
    }
}
