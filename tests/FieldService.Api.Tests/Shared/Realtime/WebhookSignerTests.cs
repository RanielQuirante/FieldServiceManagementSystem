using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Shared.Realtime;

public sealed class WebhookSignerTests
{
    [Fact]
    public void TheSignerReproducesTheSignatureVectorFromItsSecretIdTimestampAndBody()
    {
        var vector = SignatureVector.Load();

        var signature = WebhookSigner.FromSecret(vector.Secret).Sign(vector.Id, vector.Timestamp, vector.Body);

        Assert.Equal(vector.Signature, signature);
    }

    [Fact]
    public void TheVectorBodyIsTheCompactedStatusChangedFixture()
    {
        var vector = SignatureVector.Load();

        Assert.Equal(JobEventFixtures.Compact("job.status-changed.v1.json"), vector.Body);
    }

    [Fact]
    public void TheKeyIsTheBase64DecodedSecretAfterThePrefixAndNeverTheTextOfTheSecret()
    {
        var vector = SignatureVector.Load();
        var decoded = Convert.FromBase64String(vector.Secret[SecretKeyRules.WebhookSecretPrefix.Length..]);

        var fromDecodedBytes = new WebhookSigner(decoded).Sign(vector.Id, vector.Timestamp, vector.Body);
        var fromSecretText = new WebhookSigner(Encoding.UTF8.GetBytes(vector.Secret)).Sign(vector.Id, vector.Timestamp, vector.Body);

        Assert.Equal(32, decoded.Length);
        Assert.Equal(vector.Signature, fromDecodedBytes);
        Assert.NotEqual(vector.Signature, fromSecretText);
    }

    [Fact]
    public void TheSignatureIsV1FollowedByThePaddedBase64OfThirtyTwoBytes()
    {
        var vector = SignatureVector.Load();

        var signature = WebhookSigner.FromSecret(vector.Secret).Sign(vector.Id, vector.Timestamp, vector.Body);

        Assert.StartsWith("v1,", signature, StringComparison.Ordinal);
        Assert.Equal(32, Convert.FromBase64String(signature["v1,".Length..]).Length);
        Assert.Equal(47, signature.Length);
    }

    [Fact]
    public void OneChangedBodyByteIdOrTimestampChangesTheSignature()
    {
        var vector = SignatureVector.Load();
        var signer = WebhookSigner.FromSecret(vector.Secret);
        var changedBody = vector.Body.ToArray();
        changedBody[^2] ^= 0x01;

        string[] signatures =
        [
            signer.Sign(vector.Id, vector.Timestamp, changedBody),
            signer.Sign(Guid.CreateVersion7().ToString(), vector.Timestamp, vector.Body),
            signer.Sign(vector.Id, vector.Timestamp + 1, vector.Body),
        ];

        Assert.All(signatures, signature => Assert.NotEqual(vector.Signature, signature));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("no prefix")]
    [InlineData("sixteen bytes")]
    [InlineData("not base64")]
    public void ASecretWithoutThePrefixOrShorterThanThirtyTwoBytesIsRefusedWithoutEchoingIt(string kind)
    {
        var secret = kind switch
        {
            "missing" => null,
            "empty" => string.Empty,
            "no prefix" => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            "sixteen bytes" => SecretKeyRules.WebhookSecretPrefix + Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            _ => SecretKeyRules.WebhookSecretPrefix + "not base64",
        };

        var exception = Assert.Throws<InvalidOperationException>(() => WebhookSigner.FromSecret(secret));

        Assert.Equal("Notifications:WebhookSecret is not a valid webhook secret.", exception.Message);
    }

    private sealed record SignatureVector(string Secret, string Id, long Timestamp, byte[] Body, string Signature)
    {
        public static SignatureVector Load()
        {
            var path = Path.Combine(RepositoryPaths.EventContractsDirectory, "signature-test-vector.json");
            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            var root = document.RootElement;
            Assert.Equal(["secret", "id", "timestamp", "body", "signature"], root.EnumerateObject().Select(member => member.Name));
            Assert.Equal(JsonValueKind.String, root.GetProperty("timestamp").ValueKind);
            return new SignatureVector(
                root.GetProperty("secret").GetString()!,
                root.GetProperty("id").GetString()!,
                long.Parse(root.GetProperty("timestamp").GetString()!, NumberStyles.None, CultureInfo.InvariantCulture),
                Encoding.UTF8.GetBytes(root.GetProperty("body").GetString()!),
                root.GetProperty("signature").GetString()!);
        }
    }
}
