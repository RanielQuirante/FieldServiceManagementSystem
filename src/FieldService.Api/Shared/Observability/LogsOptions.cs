namespace FieldService.Api.Shared.Observability;

public sealed class LogsOptions
{
    public const string SectionName = "Logs";

    public const string TextConsoleFormat = "Text";

    public const string JsonConsoleFormat = "Json";

    public string? Directory { get; set; }

    public bool FileEnabled { get; set; }

    public int RetainedFileCountLimit { get; set; }

    public long FileSizeLimitBytes { get; set; }

    public string? ConsoleFormat { get; set; }

    public bool UsesJsonConsole =>
        string.Equals(ConsoleFormat, JsonConsoleFormat, StringComparison.OrdinalIgnoreCase);
}
