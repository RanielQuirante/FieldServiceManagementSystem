using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using FieldService.Api.Tests.Infrastructure;

namespace FieldService.Api.Tests.Contracts;

public sealed class SignatureVectorTests
{
    private static readonly string[] _members = ["secret", "id", "timestamp", "body", "signature"];

    [Fact]
    public void TheVectorHoldsItsFiveMembersInTheDocumentedOrderWithTheTimestampAsDigits()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(VectorPath));
        var root = document.RootElement;

        Assert.Equal(_members, root.EnumerateObject().Select(member => member.Name));
        Assert.All(root.EnumerateObject(), member => Assert.Equal(JsonValueKind.String, member.Value.ValueKind));
        Assert.Matches("^[0-9]{1,12}$", root.GetProperty("timestamp").GetString());
        Assert.StartsWith(SecretKeyRules.WebhookSecretPrefix, root.GetProperty("secret").GetString(), StringComparison.Ordinal);
        Assert.StartsWith($"{WebhookSigner.SignatureVersion},", root.GetProperty("signature").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public void TheApiSignerReproducesTheVectorSignatureFromItsSecretIdTimestampAndBody()
    {
        var vector = Read();

        var signature = WebhookSigner.FromSecret(vector.Secret)
            .Sign(vector.Id, long.Parse(vector.Timestamp, CultureInfo.InvariantCulture), Encoding.UTF8.GetBytes(vector.Body));

        Assert.Equal(vector.Signature, signature);
    }

    [Fact]
    public void AnIndependentHmacOverTheVectorIdTimestampAndBodyEqualsTheVectorSignature()
    {
        var vector = Read();
        var key = Convert.FromBase64String(vector.Secret[SecretKeyRules.WebhookSecretPrefix.Length..]);

        var mac = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes($"{vector.Id}.{vector.Timestamp}.{vector.Body}"));

        Assert.Equal(32, key.Length);
        Assert.Equal($"v1,{Convert.ToBase64String(mac)}", vector.Signature);
    }

    [Fact]
    public void TheVectorBodyIsTheStatusChangedFixtureAsTheApiSerializerWritesItAndSigningThoseBytesGivesTheVectorSignature()
    {
        var vector = Read();
        var fixture = EventFixtureTests.Read(EventFixtureTests.StatusChangedFixture);

        var body = JobEventSerializer.Serialize(fixture.ToJobEvent());
        var signature = WebhookSigner.FromSecret(vector.Secret)
            .Sign(fixture.Id.ToString(), long.Parse(vector.Timestamp, CultureInfo.InvariantCulture), body);

        Assert.Equal(Encoding.UTF8.GetBytes(vector.Body), body);
        Assert.Equal(EventFixtureTests.Compact(EventFixtureTests.StatusChangedFixture), Encoding.UTF8.GetBytes(vector.Body));
        Assert.Equal(fixture.Id.ToString(), vector.Id);
        Assert.Equal(vector.Signature, signature);
    }

    [Fact]
    public void OneChangedCharacterInTheIdTimestampOrBodyOrAnotherSecretGivesAnotherSignature()
    {
        var vector = Read();
        var signer = WebhookSigner.FromSecret(vector.Secret);
        var timestamp = long.Parse(vector.Timestamp, CultureInfo.InvariantCulture);
        var body = Encoding.UTF8.GetBytes(vector.Body);
        var changedBody = (byte[])body.Clone();
        changedBody[body.Length / 2] ^= 0x01;
        var otherSecret = $"{SecretKeyRules.WebhookSecretPrefix}{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}";

        string[] signatures =
        [
            signer.Sign(ChangeCharacter(vector.Id), timestamp, body),
            signer.Sign(vector.Id, timestamp + 1, body),
            signer.Sign(vector.Id, timestamp, changedBody),
            WebhookSigner.FromSecret(otherSecret).Sign(vector.Id, timestamp, body),
        ];

        Assert.All(signatures, signature => Assert.NotEqual(vector.Signature, signature));
        Assert.Equal(signatures.Length, signatures.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void TheVectorSecretIsAPublishedTestKeyThatTheApisStartupRulesRefuse()
    {
        var vector = Read();

        Assert.True(SecretKeyRules.TryDecodeWebhookSecret(vector.Secret, out var key));
        Assert.Equal(SecretKeyRules.PublishedKeyRule, SecretKeyRules.WeakKeyRule(key));
    }

    public static string VectorPath => Path.Combine(RepositoryPaths.Root, "docs", "contracts", "events", "signature-test-vector.json");

    private static string ChangeCharacter(string text)
    {
        var characters = text.ToCharArray();
        var index = characters.Length - 1;
        characters[index] = characters[index] == '0' ? '1' : '0';
        return new string(characters);
    }

    private static Vector Read()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(VectorPath));
        var root = document.RootElement;
        return new Vector(
            root.GetProperty("secret").GetString()!,
            root.GetProperty("id").GetString()!,
            root.GetProperty("timestamp").GetString()!,
            root.GetProperty("body").GetString()!,
            root.GetProperty("signature").GetString()!);
    }

    private sealed record Vector(string Secret, string Id, string Timestamp, string Body, string Signature)
    {
        public override string ToString() => $"signature test vector {Id}";
    }
}
