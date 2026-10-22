using FieldService.Mobile.Shared.Session;
using FieldService.Mobile.Tests.Shared.Testing;
using Microsoft.Maui.Storage;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace FieldService.Mobile.Tests.Shared.Session;

public sealed class TokenStoreTests
{
    [Fact]
    public void TheRefreshTokenKeyIsTheRecordedSecureStorageKey()
    {
        Assert.Equal("fieldservice.refresh-token", TokenStore.RefreshTokenKey);
    }

    [Fact]
    public async Task ReadReturnsTheRefreshTokenStoredUnderTheKey()
    {
        var refreshToken = TestSession.NewRefreshToken();
        var storage = Substitute.For<ISecureStorage>();
        storage.GetAsync(TokenStore.RefreshTokenKey).Returns(refreshToken);
        var store = new TokenStore(storage, new CapturingLoggerProvider().CreateLogger<TokenStore>());

        var value = await store.ReadAsync();

        Assert.Equal(refreshToken, value);
        storage.DidNotReceive().RemoveAll();
    }

    [Fact]
    public async Task ReadReturnsNothing_WhenNoTokenIsStored()
    {
        var storage = Substitute.For<ISecureStorage>();
        storage.GetAsync(TokenStore.RefreshTokenKey).Returns((string?)null);
        var store = new TokenStore(storage, new CapturingLoggerProvider().CreateLogger<TokenStore>());

        Assert.Null(await store.ReadAsync());
    }

    [Fact]
    public async Task ReadRemovesEverythingAndReturnsNothing_WhenSecureStorageCannotBeRead()
    {
        var logs = new CapturingLoggerProvider();
        var storage = Substitute.For<ISecureStorage>();
        storage.GetAsync(TokenStore.RefreshTokenKey).ThrowsAsync(new InvalidOperationException("keystore entry unreadable"));
        var store = new TokenStore(storage, logs.CreateLogger<TokenStore>());

        var value = await store.ReadAsync();

        Assert.Null(value);
        storage.Received(1).RemoveAll();
        var entry = Assert.Single(logs.Entries);
        Assert.Equal("session.token-read-failed", entry.EventName);
        Assert.Equal(nameof(InvalidOperationException), entry.Value("Failure"));
        Assert.DoesNotContain("keystore entry unreadable", logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task WriteStoresTheTokenUnderTheKey()
    {
        var refreshToken = TestSession.NewRefreshToken();
        var storage = Substitute.For<ISecureStorage>();
        var store = new TokenStore(storage, new CapturingLoggerProvider().CreateLogger<TokenStore>());

        var written = await store.WriteAsync(refreshToken);

        Assert.True(written);
        await storage.Received(1).SetAsync(TokenStore.RefreshTokenKey, refreshToken);
    }

    [Fact]
    public async Task WriteClearsStorage_WhenSecureStorageRejectsTheToken()
    {
        var refreshToken = TestSession.NewRefreshToken();
        var logs = new CapturingLoggerProvider();
        var storage = Substitute.For<ISecureStorage>();
        storage.SetAsync(TokenStore.RefreshTokenKey, refreshToken).ThrowsAsync(new InvalidOperationException("write failed"));
        var store = new TokenStore(storage, logs.CreateLogger<TokenStore>());

        var written = await store.WriteAsync(refreshToken);

        Assert.False(written);
        storage.Received(1).RemoveAll();
        Assert.DoesNotContain(refreshToken, logs.AllText(), StringComparison.Ordinal);
    }

    [Fact]
    public void ClearRemovesEverythingFromSecureStorage()
    {
        var storage = Substitute.For<ISecureStorage>();
        var store = new TokenStore(storage, new CapturingLoggerProvider().CreateLogger<TokenStore>());

        store.Clear();

        storage.Received(1).RemoveAll();
    }

    [Fact]
    public void ClearLogsAndContinues_WhenSecureStorageCannotBeCleared()
    {
        var logs = new CapturingLoggerProvider();
        var storage = Substitute.For<ISecureStorage>();
        storage.When(target => target.RemoveAll()).Throw(new InvalidOperationException("clear failed"));
        var store = new TokenStore(storage, logs.CreateLogger<TokenStore>());

        store.Clear();

        Assert.Equal("session.token-clear-failed", Assert.Single(logs.Entries).EventName);
    }
}
