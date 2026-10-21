using FieldService.Api.Shared.Observability;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Security;

public sealed class AuthOptionsValidator(ILogger<AuthOptionsValidator> logger) : IValidateOptions<AuthOptions>
{
    public ValidateOptionsResult Validate(string? name, AuthOptions options)
    {
        var failures = new List<string>();
        var accessTokenKey = Check(failures, AuthOptions.AccessTokenKeyName, options.AccessTokenKey);
        var socketTicketKey = Check(failures, AuthOptions.SocketTicketKeyName, options.SocketTicketKey);

        if (accessTokenKey is not null && socketTicketKey is not null && accessTokenKey.AsSpan().SequenceEqual(socketTicketKey))
        {
            Fail(failures, AuthOptions.SocketTicketKeyName, $"must differ from {AuthOptions.AccessTokenKeyName}");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private byte[]? Check(List<string> failures, string key, string? value)
    {
        if (SecretKeyRules.IsMissing(value))
        {
            Fail(failures, key, SecretKeyRules.MissingRule);
            return null;
        }

        if (!SecretKeyRules.TryDecodeKey(value, out var bytes))
        {
            Fail(failures, key, SecretKeyRules.KeyFormatRule);
            return null;
        }

        if (SecretKeyRules.WeakKeyRule(bytes) is { } rule)
        {
            Fail(failures, key, rule);
            return null;
        }

        return bytes;
    }

    private void Fail(List<string> failures, string key, string rule)
    {
        ConfigurationLog.ConfigInvalid(logger, key, rule);
        failures.Add($"{key}: {rule}.");
    }
}
