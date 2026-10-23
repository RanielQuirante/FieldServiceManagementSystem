using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using FieldService.Api.Shared.Security;

namespace FieldService.Api.Shared.Realtime;

public sealed class WebhookSigner
{
    public const string SignatureVersion = "v1";

    private readonly byte[] _key;

    public WebhookSigner(ReadOnlySpan<byte> key)
    {
        _key = key.ToArray();
    }

    public static WebhookSigner FromSecret(string? secret) =>
        SecretKeyRules.TryDecodeWebhookSecret(secret, out var key)
            ? new WebhookSigner(key)
            : throw new InvalidOperationException($"{NotificationsOptions.WebhookSecretName} is not a valid webhook secret.");

    public string Sign(string webhookId, long timestamp, ReadOnlySpan<byte> body)
    {
        using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, _key);
        hmac.AppendData(Encoding.UTF8.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{webhookId}.{timestamp}.")));
        hmac.AppendData(body);
        return $"{SignatureVersion},{Convert.ToBase64String(hmac.GetHashAndReset())}";
    }
}
