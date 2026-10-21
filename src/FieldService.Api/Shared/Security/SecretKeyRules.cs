namespace FieldService.Api.Shared.Security;

public static class SecretKeyRules
{
    public const int MinimumKeyBytes = 32;

    public const string WebhookSecretPrefix = "whsec_";

    public const string MissingRule = "must be set";

    public const string KeyFormatRule = "must be standard base64 of 32 or more random bytes";

    public const string PublishedKeyRule = "must not be one of the published test keys";

    public const string PatternedKeyRule = "must not be a patterned key whose bytes are all equal or rise by a constant step";

    private static readonly byte[][] _publishedTestKeys = [ByteRun(0x00), ByteRun(0x20), ByteRun(0x40)];

    public static bool IsMissing(string? value) => string.IsNullOrWhiteSpace(value);

    public static bool TryDecodeKey(string? value, out byte[] bytes)
    {
        bytes = [];
        if (IsMissing(value))
        {
            return false;
        }

        var buffer = new byte[value!.Length];
        if (!Convert.TryFromBase64String(value, buffer, out var written) || written < MinimumKeyBytes)
        {
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

    public static bool TryDecodeWebhookSecret(string? value, out byte[] bytes)
    {
        bytes = [];
        if (IsMissing(value) || !value!.StartsWith(WebhookSecretPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return TryDecodeKey(value[WebhookSecretPrefix.Length..], out bytes);
    }

    public static string? WeakKeyRule(byte[] bytes)
    {
        if (_publishedTestKeys.Any(published => published.AsSpan().SequenceEqual(bytes)))
        {
            return PublishedKeyRule;
        }

        return IsPatterned(bytes) ? PatternedKeyRule : null;
    }

    public static bool IsPatterned(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 2)
        {
            return true;
        }

        var step = (byte)(bytes[1] - bytes[0]);
        for (var index = 2; index < bytes.Length; index++)
        {
            if ((byte)(bytes[index] - bytes[index - 1]) != step)
            {
                return false;
            }
        }

        return true;
    }

    private static byte[] ByteRun(int first)
    {
        var run = new byte[MinimumKeyBytes];
        for (var index = 0; index < run.Length; index++)
        {
            run[index] = (byte)(first + index);
        }

        return run;
    }
}
