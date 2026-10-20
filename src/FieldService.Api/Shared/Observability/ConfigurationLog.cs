namespace FieldService.Api.Shared.Observability;

public static partial class ConfigurationLog
{
    [LoggerMessage(
        EventId = 1001,
        EventName = "config.invalid",
        Level = LogLevel.Critical,
        Message = "Configuration key {Key} breaks the rule: {Rule}")]
    public static partial void ConfigInvalid(ILogger logger, string key, string rule);
}
