using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Observability;

public sealed class LogsOptionsValidator : IValidateOptions<LogsOptions>
{
    public ValidateOptionsResult Validate(string? name, LogsOptions options)
    {
        var failures = new List<string>();

        if (!string.Equals(options.ConsoleFormat, LogsOptions.TextConsoleFormat, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(options.ConsoleFormat, LogsOptions.JsonConsoleFormat, StringComparison.OrdinalIgnoreCase))
        {
            failures.Add("Logs:ConsoleFormat: must be Text or Json.");
        }

        if (options.FileEnabled)
        {
            if (string.IsNullOrWhiteSpace(options.Directory))
            {
                failures.Add("Logs:Directory: must be set when Logs:FileEnabled is true.");
            }

            if (options.RetainedFileCountLimit < 1)
            {
                failures.Add("Logs:RetainedFileCountLimit: must be 1 or more.");
            }

            if (options.FileSizeLimitBytes < 1)
            {
                failures.Add("Logs:FileSizeLimitBytes: must be 1 or more.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
