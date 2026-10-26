using CommunityToolkit.Mvvm.ComponentModel;

namespace FieldService.Mobile.Shared.Connectivity;

public sealed partial class NetworkStatus : ObservableObject
{
    public NetworkStatus(IConnectivity connectivity, IDeviceInfo deviceInfo)
    {
        ArgumentNullException.ThrowIfNull(connectivity);
        ArgumentNullException.ThrowIfNull(deviceInfo);

        var platform = deviceInfo.Platform;
        IsOnline = IsReachable(connectivity.NetworkAccess, platform);
        connectivity.ConnectivityChanged += (_, args) => IsOnline = IsReachable(args.NetworkAccess, platform);
    }

    [ObservableProperty]
    public partial bool IsOnline { get; private set; }

    public static bool IsReachable(NetworkAccess access, DevicePlatform platform) =>
        platform == DevicePlatform.WinUI
            ? access is NetworkAccess.Internet or NetworkAccess.ConstrainedInternet
            : access != NetworkAccess.None;
}
