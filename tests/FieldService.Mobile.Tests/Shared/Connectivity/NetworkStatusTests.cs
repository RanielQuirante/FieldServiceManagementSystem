using FieldService.Mobile.Shared.Connectivity;
using Microsoft.Maui.Devices;
using Microsoft.Maui.Networking;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Connectivity;

public sealed class NetworkStatusTests
{
    [Theory]
    [InlineData(NetworkAccess.None, false)]
    [InlineData(NetworkAccess.Unknown, false)]
    [InlineData(NetworkAccess.Local, false)]
    [InlineData(NetworkAccess.ConstrainedInternet, true)]
    [InlineData(NetworkAccess.Internet, true)]
    public void OnWindowsOnlyAnInternetConnectionCountsAsOnline(NetworkAccess access, bool online) =>
        Assert.Equal(online, NetworkStatus.IsReachable(access, DevicePlatform.WinUI));

    [Theory]
    [InlineData(NetworkAccess.None, false)]
    [InlineData(NetworkAccess.Unknown, true)]
    [InlineData(NetworkAccess.Local, true)]
    [InlineData(NetworkAccess.ConstrainedInternet, true)]
    [InlineData(NetworkAccess.Internet, true)]
    public void OnAndroidOnlyNoNetworkCountsAsOffline(NetworkAccess access, bool online) =>
        Assert.Equal(online, NetworkStatus.IsReachable(access, DevicePlatform.Android));

    [Fact]
    public void OnWindowsAReleasedAddressTakesTheAppOfflineAndARenewBringsItBack()
    {
        var connectivity = Substitute.For<IConnectivity>();
        connectivity.NetworkAccess.Returns(NetworkAccess.Internet);
        var device = Substitute.For<IDeviceInfo>();
        device.Platform.Returns(DevicePlatform.WinUI);
        var status = new NetworkStatus(connectivity, device);
        Assert.True(status.IsOnline);

        connectivity.ConnectivityChanged += Raise.EventWith(new ConnectivityChangedEventArgs(NetworkAccess.Local, [ConnectionProfile.Ethernet]));
        Assert.False(status.IsOnline);

        connectivity.ConnectivityChanged += Raise.EventWith(new ConnectivityChangedEventArgs(NetworkAccess.Unknown, []));
        Assert.False(status.IsOnline);

        connectivity.ConnectivityChanged += Raise.EventWith(new ConnectivityChangedEventArgs(NetworkAccess.Internet, [ConnectionProfile.Ethernet]));
        Assert.True(status.IsOnline);
    }
}
