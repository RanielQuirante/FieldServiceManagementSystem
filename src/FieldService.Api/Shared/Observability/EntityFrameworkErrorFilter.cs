using Serilog.Core;
using Serilog.Events;

namespace FieldService.Api.Shared.Observability;

public static class EntityFrameworkErrorFilter
{
    public const string Category = "Microsoft.EntityFrameworkCore";

    public static bool IsExcluded(LogEvent logEvent) =>
        logEvent.Level >= LogEventLevel.Error
        && logEvent.Properties.TryGetValue(Constants.SourceContextPropertyName, out var source)
        && source is ScalarValue { Value: string category }
        && (category == Category || category.StartsWith(Category + ".", StringComparison.Ordinal));
}
