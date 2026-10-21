using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.Security;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Realtime;

public sealed class NotificationsOptionsValidator(
    IHostEnvironment environment,
    IConfiguration configuration,
    ILogger<NotificationsOptionsValidator> logger) : IValidateOptions<NotificationsOptions>
{
    private const string WebhookSecretFormatRule = "must be whsec_ followed by standard base64 of 32 or more random bytes";

    public ValidateOptionsResult Validate(string? name, NotificationsOptions options)
    {
        var failures = new List<string>();

        if (options.IsEnabled)
        {
            if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUrl)
                || (baseUrl.Scheme != Uri.UriSchemeHttp && baseUrl.Scheme != Uri.UriSchemeHttps))
            {
                Fail(failures, NotificationsOptions.BaseUrlName, "must be an absolute http or https URL");
            }
            else if (environment.IsProduction() && baseUrl.Scheme != Uri.UriSchemeHttps)
            {
                Fail(failures, NotificationsOptions.BaseUrlName, "must use https in Production");
            }

            if (SecretKeyRules.IsMissing(options.WebhookSecret))
            {
                Fail(failures, NotificationsOptions.WebhookSecretName, $"must be set when {NotificationsOptions.BaseUrlName} is set");
            }
        }

        if (!SecretKeyRules.IsMissing(options.WebhookSecret))
        {
            CheckSecret(failures, options.WebhookSecret);
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private void CheckSecret(List<string> failures, string? secret)
    {
        if (!SecretKeyRules.TryDecodeWebhookSecret(secret, out var bytes))
        {
            Fail(failures, NotificationsOptions.WebhookSecretName, WebhookSecretFormatRule);
            return;
        }

        if (SecretKeyRules.WeakKeyRule(bytes) is { } rule)
        {
            Fail(failures, NotificationsOptions.WebhookSecretName, rule);
            return;
        }

        foreach (var keyName in new[] { AuthOptions.AccessTokenKeyName, AuthOptions.SocketTicketKeyName })
        {
            if (SecretKeyRules.TryDecodeKey(configuration[keyName], out var key) && key.AsSpan().SequenceEqual(bytes))
            {
                Fail(failures, NotificationsOptions.WebhookSecretName, $"must differ from {keyName}");
            }
        }
    }

    private void Fail(List<string> failures, string key, string rule)
    {
        ConfigurationLog.ConfigInvalid(logger, key, rule);
        failures.Add($"{key}: {rule}.");
    }
}
