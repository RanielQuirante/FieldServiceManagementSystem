using FieldService.Mobile.Features.Inspection;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Connectivity;
using FieldService.Mobile.Shared.Navigation;
using FieldService.Mobile.Shared.Screens;
using FieldService.Mobile.Shared.Session;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Maui.Accessibility;
using Microsoft.Maui.ApplicationModel;
using Microsoft.Maui.ApplicationModel.Communication;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Devices.Sensors;
using Microsoft.Maui.Networking;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Testing;

public sealed class JobsHarness
{
    private NetworkAccess _access = NetworkAccess.Internet;

    public JobsHarness(
        StubHttpMessageHandler stub,
        bool dialerSupported = true,
        bool twoPane = false,
        ISessionService? session = null,
        INavigator? navigator = null,
        DevicePlatform? platform = null)
    {
        Stub = stub;
        Clock = new FakeTimeProvider(JobDtoBuilder.Now);
        Clock.SetLocalTimeZone(JobDtoBuilder.Zone);
        Connectivity = Substitute.For<IConnectivity>();
        Connectivity.NetworkAccess.Returns(_ => _access);
        Network = new NetworkStatus(Connectivity);
        Session = session ?? SignedInAsMarco();
        Navigator = navigator ?? Substitute.For<INavigator>();
        Startup = new AppStartup(Session, Navigator, Clock, Logs.CreateLogger<AppStartup>());
        Map = Substitute.For<IMap>();
        Map.TryOpenAsync(Arg.Any<Placemark>(), Arg.Any<MapLaunchOptions>()).Returns(true);
        Dialer = Substitute.For<IPhoneDialer>();
        Dialer.IsSupported.Returns(dialerSupported);
        Device = Substitute.For<IDeviceInfo>();
        Device.Platform.Returns(platform ?? DevicePlatform.Android);
        Launcher = Substitute.For<ILauncher>();
        ScreenReader = Substitute.For<ISemanticScreenReader>();
        Layout = Substitute.For<IWindowLayout>();
        Layout.IsTwoPane.Returns(twoPane);
        Jobs = new JobsApiClient(TestSession.CreateClient(stub, Logs, Session));
        Inspections = new InspectionApiClient(TestSession.CreateClient(stub, Logs, Session));
        Store = new InspectionStore(Session);
    }

    public InspectionApiClient Inspections { get; }

    public InspectionStore Store { get; }

    public InspectionViewModel CreateInspection() =>
        new(Inspections, Store, Session, Navigator, ScreenReader, Layout, Network, Clock, Logs.CreateLogger<InspectionViewModel>());

    public ReviewAndSubmitViewModel CreateReview() =>
        new(Inspections, Jobs, Store, Session, Navigator, Layout, Network, Clock, Logs.CreateLogger<ReviewAndSubmitViewModel>());

    public StubHttpMessageHandler Stub { get; }

    public AppStartup Startup { get; }

    public CapturingLoggerProvider Logs { get; } = new();

    public FakeTimeProvider Clock { get; }

    public IConnectivity Connectivity { get; }

    public NetworkStatus Network { get; }

    public ISessionService Session { get; }

    public INavigator Navigator { get; }

    public IMap Map { get; }

    public IPhoneDialer Dialer { get; }

    public IDeviceInfo Device { get; }

    public ILauncher Launcher { get; }

    public ISemanticScreenReader ScreenReader { get; }

    public IWindowLayout Layout { get; }

    public JobsApiClient Jobs { get; }

    public JobDetailsViewModel CreateDetails() =>
        new(Jobs, Store, Session, Map, Dialer, Device, Launcher, Navigator, ScreenReader, Network, Clock, Logs.CreateLogger<JobDetailsViewModel>());

    public MyJobsViewModel CreateMyJobs() =>
        new(Jobs, Session, Navigator, Startup, Layout, CreateDetails, ScreenReader, Network, Clock, Logs.CreateLogger<MyJobsViewModel>());

    public void SetOnline(bool online)
    {
        _access = online ? NetworkAccess.Internet : NetworkAccess.None;
        Connectivity.ConnectivityChanged += Raise.EventWith(new ConnectivityChangedEventArgs(_access, []));
    }

    public static IDictionary<string, object> JobQuery(Guid jobId) =>
        new Dictionary<string, object> { [Routes.JobIdParameter] = jobId };

    public void EndSession() => Session.SessionEnded += Raise.Event();

    private static ISessionService SignedInAsMarco()
    {
        var session = Substitute.For<ISessionService>();
        var accessToken = "access-" + Guid.NewGuid().ToString("N");
        session.User.Returns(new SignedInUser(JobDtoBuilder.Marco, "Marco Reyes", FieldService.Mobile.Features.SignIn.UserRole.Technician, "marco.reyes@fieldservice.example"));
        session.AccessToken.Returns(accessToken);
        session.Credential.Returns(new SessionCredential(1, JobDtoBuilder.Marco, accessToken));
        return session;
    }
}
