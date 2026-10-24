using System.Buffers.Text;
using System.Globalization;
using System.Security.Cryptography;
using FieldService.Api.Features.Diagnostics.ReportClientError;
using FieldService.Api.Shared.Security;

namespace FieldService.Api.Tests.Features.Diagnostics;

public sealed class ClientErrorScrubberTests
{
    public const string Jwt = "jwt";

    public const string BearerValue = "bearer";

    public const string WebhookSecret = "whsec";

    public const string RefreshToken = "refresh";

    public const string Email = "email";

    public const string Query = "query";

    public static IReadOnlyList<string> AllKinds { get; } = [Jwt, BearerValue, WebhookSecret, RefreshToken, Email, Query];

    public static TheoryData<string> Kinds => [.. AllKinds];

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EachPatternIsReplacedWhereItStandsAndTheTextAroundItIsKept(string kind)
    {
        var sentinel = Sentinel.For(kind);

        var scrubbed = ClientErrorScrubber.Scrub($"Request failed: {sentinel.Text} (retry 2)");

        Assert.Equal($"Request failed: {sentinel.Replacement} (retry 2)", scrubbed);
        Assert.DoesNotContain(sentinel.Secret, scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Bearer ")]
    [InlineData("bearer ")]
    [InlineData("BEARER\t")]
    [InlineData("Bearer\n   ")]
    public void ABearerValueIsReplacedWhateverItsCaseAndTheWhiteSpaceBeforeIt(string prefix)
    {
        var value = $"{Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(24))}.~+/=";

        var scrubbed = ClientErrorScrubber.Scrub($"Authorization: {prefix}{value}");

        Assert.Equal($"Authorization: {ClientErrorScrubber.Redacted}", scrubbed);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Refresh failed: ", " expired")]
    [InlineData("    at refresh(", ")")]
    public void ARefreshTokenThatStartsWithAHyphenIsReplacedAtTheStartAfterASpaceAndAfterAParenthesis(string before, string after)
    {
        var token = Sentinel.RefreshTokenStartingWith('-');

        var scrubbed = ClientErrorScrubber.Scrub($"{before}{token}{after}");

        Assert.True(RefreshTokenFactory.TryHash(token, out _));
        Assert.StartsWith("-", token, StringComparison.Ordinal);
        Assert.Equal($"{before}{ClientErrorScrubber.Redacted}{after}", scrubbed);
        Assert.DoesNotContain(token[1..], scrubbed, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("Refresh failed: ", " expired")]
    [InlineData("    at refresh(", ")")]
    public void ARefreshShapedValueThatEndsWithAHyphenIsReplacedWhole(string before, string after)
    {
        var value = $"{Sentinel.RefreshShaped()[..^1]}-";

        var scrubbed = ClientErrorScrubber.Scrub($"{before}{value}{after}");

        Assert.Equal($"{before}{ClientErrorScrubber.Redacted}{after}", scrubbed);
    }

    [Theory]
    [InlineData("{0}-")]
    [InlineData("-{0}")]
    [InlineData("chunk-{0}-v2")]
    [InlineData("{0}-{0}")]
    [InlineData("{0}_")]
    [InlineData("01a15488-9680-7989-92af-ed07fc34a890-01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e")]
    public void ARunOfMoreThan43TokenCharactersIsNeverPartlyReplaced(string shape)
    {
        var run = string.Format(CultureInfo.InvariantCulture, shape, Sentinel.RefreshShaped());
        var text = $"Failed to load /assets/{run}.js";

        Assert.InRange(run.Length, 44, int.MaxValue);
        Assert.Equal(text, ClientErrorScrubber.Scrub(text));
        Assert.Equal(run, ClientErrorScrubber.Scrub(run));
    }

    [Fact]
    public void TheApiReferenceExampleIsScrubbedAsDocumented()
    {
        var scrubbed = ClientErrorScrubber.Scrub("Request to /api/jobs?search=Harbor failed for dispatcher@fieldservice.example");

        Assert.Equal("Request to /api/jobs?[redacted] failed for [redacted]", scrubbed);
    }

    [Fact]
    public void AQueryStringEndsAtWhiteSpaceOrAClosingParenthesis()
    {
        var scrubbed = ClientErrorScrubber.Scrub("GET /api/jobs?search=Harbor&page=2 failed (at https://example.test/jobs?status=OnHold) ?");

        Assert.Equal("GET /api/jobs?[redacted] failed (at https://example.test/jobs?[redacted]) ?[redacted]", scrubbed);
    }

    [Fact]
    public void EverySecretInOneStackIsRemovedAndATokenInsideAQueryStringLeavesNothingBehind()
    {
        var jwt = Sentinel.For(Jwt);
        var bearer = Sentinel.For(BearerValue);
        var secret = Sentinel.For(WebhookSecret);
        var refresh = Sentinel.For(RefreshToken);
        var email = Sentinel.For(Email);
        var stack = string.Join(
            '\n',
            "TypeError: Cannot read properties of undefined (reading 'number')",
            $"    at fetchJob (https://example.test/api/jobs?token={jwt.Secret}&x=1)",
            $"    at {bearer.Text}",
            $"    at sign({secret.Text})",
            $"    at refresh({refresh.Text})",
            $"    at notify({email.Text})");

        var scrubbed = ClientErrorScrubber.Scrub(stack);

        Assert.Equal(
            string.Join(
                '\n',
                "TypeError: Cannot read properties of undefined (reading 'number')",
                "    at fetchJob (https://example.test/api/jobs?[redacted])",
                "    at [redacted]",
                "    at sign([redacted])",
                "    at refresh([redacted])",
                "    at notify([redacted])"),
            scrubbed);
        foreach (var sentinel in new[] { jwt, bearer, secret, refresh, email })
        {
            Assert.DoesNotContain(sentinel.Secret, scrubbed, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void AJwtAfterBearerIsRemovedWholeIncludingItsSignature()
    {
        var jwt = Sentinel.For(Jwt);

        var scrubbed = ClientErrorScrubber.Scrub($"Authorization: Bearer {jwt.Secret}");

        Assert.Equal("Authorization: Bearer [redacted]", scrubbed);
        foreach (var segment in jwt.Secret.Split('.'))
        {
            Assert.DoesNotContain(segment, scrubbed, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void ScrubbingTwiceGivesTheSameText(string kind)
    {
        var once = ClientErrorScrubber.Scrub($"{Sentinel.For(kind).Text} and {Sentinel.For(Query).Text}");

        Assert.Equal(once, ClientErrorScrubber.Scrub(once));
    }

    [Theory]
    [InlineData("Cannot read properties of undefined (reading 'number')")]
    [InlineData("TypeError: Cannot read properties of undefined (reading 'number')\n    at JobHeader.vue:42:18")]
    [InlineData("app.errorHandler")]
    [InlineData("window.unhandledrejection")]
    [InlineData("/jobs/01a15488-9680-7989-92af-ed07fc34a890")]
    [InlineData("/technicians/01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e/jobs")]
    [InlineData("927ba8e3-24aa-4d0e-8ef1-d8c4203c37fe")]
    [InlineData("4bf92f3577b34da6a3ce929d0e0e4736")]
    [InlineData("1.0.0+build.7")]
    [InlineData("Failed to fetch dynamically imported module: /assets/JobDetailsPage-4f1d2c8e.js")]
    public void OrdinaryReportTextPassesUnchanged(string text)
    {
        Assert.Equal(text, ClientErrorScrubber.Scrub(text));
    }

    [Fact]
    public void ScrubOptionalKeepsNull()
    {
        Assert.Null(ClientErrorScrubber.ScrubOptional(null));
        Assert.Equal(ClientErrorScrubber.Redacted, ClientErrorScrubber.ScrubOptional(Sentinel.For(Email).Secret));
    }

    public sealed record Sentinel(string Secret, string Text, string Replacement)
    {
        private const string Base64UrlAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_";

        public static Sentinel For(string kind)
        {
            var marker = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4));
            return kind switch
            {
                Jwt => Whole($"eyJ{Segment(18)}.{Segment(48)}.{Segment(32)}"),
                BearerValue => BearerOf(Segment(30)),
                WebhookSecret => Whole($"whsec_{Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))}"),
                RefreshToken => Whole(RefreshShaped()),
                Email => Whole($"dispatcher.{marker}@fieldservice.example"),
                Query => new Sentinel($"secret-{marker}", $"/jobs?view=secret-{marker}&page=2", $"/jobs{ClientErrorScrubber.RedactedQuery}"),
                _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown sentinel kind."),
            };
        }

        public static string RefreshShaped()
        {
            var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32)).ToCharArray();
            token[0] = 'R';
            token[^1] = 'T';
            return new string(token);
        }

        public static string RefreshTokenStartingWith(char first)
        {
            var bytes = RandomNumberGenerator.GetBytes(RefreshTokenFactory.TokenBytes);
            bytes[0] = (byte)((Base64UrlAlphabet.IndexOf(first, StringComparison.Ordinal) << 2) | (bytes[0] & 0b11));
            return Base64Url.EncodeToString(bytes);
        }

        private static Sentinel Whole(string secret) => new(secret, secret, ClientErrorScrubber.Redacted);

        private static Sentinel BearerOf(string value) => new(value, $"Bearer {value}", ClientErrorScrubber.Redacted);

        private static string Segment(int bytes) => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(bytes));
    }
}
