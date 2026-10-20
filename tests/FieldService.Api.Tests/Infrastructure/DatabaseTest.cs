namespace FieldService.Api.Tests.Infrastructure;

public abstract class DatabaseTest(DatabaseFixture fixture) : IAsyncLifetime
{
    protected DatabaseFixture Fixture { get; } = fixture;

    protected static CancellationToken CancellationToken => TestContext.Current.CancellationToken;

    public virtual async ValueTask InitializeAsync()
    {
        await Fixture.ResetAsync(CancellationToken);
    }

    public virtual ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        return ValueTask.CompletedTask;
    }
}
