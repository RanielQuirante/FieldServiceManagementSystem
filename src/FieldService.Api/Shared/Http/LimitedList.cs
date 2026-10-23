using System.Collections.ObjectModel;
using FieldService.Api.Shared.Errors;

namespace FieldService.Api.Shared.Http;

public interface ICollectionLimit
{
    static abstract int Max { get; }

    static abstract string Message { get; }
}

public sealed class LimitedList<T, TLimit> : Collection<T>
    where TLimit : ICollectionLimit
{
    protected override void InsertItem(int index, T item)
    {
        if (Count >= TLimit.Max)
        {
            throw new CollectionLimitException(TLimit.Message);
        }

        base.InsertItem(index, item);
    }
}
