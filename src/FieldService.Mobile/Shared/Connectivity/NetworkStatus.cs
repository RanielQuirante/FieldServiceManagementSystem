using CommunityToolkit.Mvvm.ComponentModel;

namespace FieldService.Mobile.Shared.Connectivity;

public sealed partial class NetworkStatus : ObservableObject
{
    public NetworkStatus(IConnectivity connectivity)
    {
        ArgumentNullException.ThrowIfNull(connectivity);

        IsOnline = IsReachable(connectivity.NetworkAccess);
        connectivity.ConnectivityChanged += (_, args) => IsOnline = IsReachable(args.NetworkAccess);
    }

    [ObservableProperty]
    public partial bool IsOnline { get; private set; }

    private static bool IsReachable(NetworkAccess access) => access != NetworkAccess.None;
}
