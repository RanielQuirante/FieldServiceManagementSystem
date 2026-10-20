using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Maui.Networking;
using Microsoft.Maui.Storage;
using NSubstitute;

namespace FieldService.Mobile.Tests.Spikes;

public sealed class NetTargetSeamTests
{
    private const string RefreshTokenKey = "fieldservice.refresh-token";

    [Fact]
    public async Task AViewModelReadsSecureStorageAndConnectivityThroughSubstitutes()
    {
        var secureStorage = Substitute.For<ISecureStorage>();
        var connectivity = Substitute.For<IConnectivity>();
        secureStorage.GetAsync(RefreshTokenKey).Returns(Guid.NewGuid().ToString("N"));
        connectivity.NetworkAccess.Returns(NetworkAccess.Internet);
        var viewModel = new SessionProbeViewModel(secureStorage, connectivity);
        var changed = new List<string?>();
        viewModel.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.True(viewModel.IsOnline);
        Assert.True(viewModel.HasStoredSession);
        Assert.Equal([nameof(SessionProbeViewModel.IsOnline), nameof(SessionProbeViewModel.HasStoredSession)], changed);
        await secureStorage.Received(1).GetAsync(RefreshTokenKey);
    }

    [Fact]
    public async Task AViewModelReportsOfflineWithoutAStoredSession()
    {
        var secureStorage = Substitute.For<ISecureStorage>();
        var connectivity = Substitute.For<IConnectivity>();
        secureStorage.GetAsync(RefreshTokenKey).Returns((string?)null);
        connectivity.NetworkAccess.Returns(NetworkAccess.None);
        var viewModel = new SessionProbeViewModel(secureStorage, connectivity);

        await viewModel.LoadCommand.ExecuteAsync(null);

        Assert.False(viewModel.IsOnline);
        Assert.False(viewModel.HasStoredSession);
    }

    [Theory]
    [InlineData("FieldService.Mobile.MainApplication")]
    [InlineData("FieldService.Mobile.MainActivity")]
    [InlineData("FieldService.Mobile.WinUI.App")]
    public void TheNet10AppAssemblyLeavesOutPlatformsCode(string typeName)
    {
        Assert.Null(typeof(MauiProgram).Assembly.GetType(typeName));
    }

    private sealed class SessionProbeViewModel : ObservableObject
    {
        private readonly ISecureStorage _secureStorage;
        private readonly IConnectivity _connectivity;
        private bool _isOnline;
        private bool _hasStoredSession;

        public SessionProbeViewModel(ISecureStorage secureStorage, IConnectivity connectivity)
        {
            _secureStorage = secureStorage;
            _connectivity = connectivity;
            LoadCommand = new AsyncRelayCommand(LoadAsync);
        }

        public IAsyncRelayCommand LoadCommand { get; }

        public bool IsOnline
        {
            get => _isOnline;
            private set => SetProperty(ref _isOnline, value);
        }

        public bool HasStoredSession
        {
            get => _hasStoredSession;
            private set => SetProperty(ref _hasStoredSession, value);
        }

        private async Task LoadAsync(CancellationToken cancellationToken)
        {
            IsOnline = _connectivity.NetworkAccess == NetworkAccess.Internet;
            var refreshToken = await _secureStorage.GetAsync(RefreshTokenKey);
            cancellationToken.ThrowIfCancellationRequested();
            HasStoredSession = !string.IsNullOrEmpty(refreshToken);
        }
    }
}
