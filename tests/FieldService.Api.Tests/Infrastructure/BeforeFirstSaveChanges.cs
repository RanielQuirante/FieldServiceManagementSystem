using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class BeforeFirstSaveChanges(Func<Task> action) : SaveChangesInterceptor
{
    private int _ran;

    public bool Ran => Volatile.Read(ref _ran) == 1;

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (Interlocked.Exchange(ref _ran, 1) == 0)
        {
            await action();
        }

        return result;
    }
}
