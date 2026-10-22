using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Shared.Navigation;

namespace FieldService.Mobile;

public partial class AppShell : Shell
{
    private readonly AppStartup _startup;

    public AppShell(AppStartup startup)
    {
        InitializeComponent();
        foreach (var (route, page) in PushedRoutes)
        {
            Routing.RegisterRoute(route, page);
        }

        _startup = startup;
        Loaded += OnLoaded;
    }

    public static IReadOnlyDictionary<string, Type> PushedRoutes { get; } = new Dictionary<string, Type>(StringComparer.Ordinal)
    {
        [Routes.JobDetailsRoute] = typeof(JobDetailsPage),
    };

    private async void OnLoaded(object? sender, EventArgs e) => await _startup.RunAsync(CancellationToken.None);
}
