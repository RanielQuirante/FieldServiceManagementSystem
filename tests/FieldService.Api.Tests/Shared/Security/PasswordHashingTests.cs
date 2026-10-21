using System.Buffers.Binary;
using System.Security.Cryptography;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Security;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;

namespace FieldService.Api.Tests.Shared.Security;

public sealed class PasswordHashingTests
{
    private const byte FormatMarkerV3 = 0x01;

    private const uint HmacSha512 = 2;

    private static readonly string _password = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(12));

    private static readonly User _user = User.Create(
        "hashing@fieldservice.example",
        "Hashing Test",
        string.Empty,
        UserRole.Technician,
        DateTimeOffset.UnixEpoch);

    [Fact]
    public void TheRegisteredHasherWritesPbkdf2Sha512HashesWith210000Iterations()
    {
        using var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton(TimeProvider.System)
            .AddSecurity(new ConfigurationBuilder().Build())
            .BuildServiceProvider();

        var hash = Convert.FromBase64String(services.GetRequiredService<IPasswordHasher<User>>().HashPassword(_user, _password));

        Assert.Equal(FormatMarkerV3, hash[0]);
        Assert.Equal(HmacSha512, BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(1, 4)));
        Assert.Equal(210_000u, BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(5, 4)));
        Assert.Equal(16u, BinaryPrimitives.ReadUInt32BigEndian(hash.AsSpan(9, 4)));
        Assert.Equal(13 + 16 + 32, hash.Length);
    }

    [Fact]
    public void AnUnknownAccountRunsExactlyOneVerificationAgainstTheDummyHashCreatedAtConstruction()
    {
        var hasher = Substitute.For<IPasswordHasher<User>>();
        hasher.HashPassword(Arg.Any<User>(), Arg.Any<string>()).Returns("dummy-hash");
        var verifier = new PasswordVerifier(hasher);
        hasher.Received(1).HashPassword(Arg.Any<User>(), Arg.Any<string>());
        hasher.ClearReceivedCalls();

        var result = verifier.Verify(null, _password);

        Assert.Equal(PasswordVerificationResult.Failed, result);
        hasher.Received(1).VerifyHashedPassword(Arg.Any<User>(), "dummy-hash", _password);
        hasher.DidNotReceiveWithAnyArgs().HashPassword(default!, default!);
    }

    [Fact]
    public void AKnownAccountIsVerifiedAgainstItsOwnHashAndARehashRequestIsPassedOn()
    {
        var hasher = Substitute.For<IPasswordHasher<User>>();
        var user = User.Create("owner@fieldservice.example", "Owner", "stored-hash", UserRole.Administrator, DateTimeOffset.UnixEpoch);
        hasher.VerifyHashedPassword(user, "stored-hash", _password).Returns(PasswordVerificationResult.SuccessRehashNeeded);
        var verifier = new PasswordVerifier(hasher);

        var result = verifier.Verify(user, _password);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, result);
        hasher.Received(1).VerifyHashedPassword(user, "stored-hash", _password);
    }

    [Fact]
    public void AStoredHashThatIsNotBase64FailsWithoutThrowing()
    {
        var verifier = new PasswordVerifier(PasswordVerifier.CreateHasher());
        var user = User.Create("broken@fieldservice.example", "Broken", "not base64 !", UserRole.Administrator, DateTimeOffset.UnixEpoch);

        Assert.Equal(PasswordVerificationResult.Failed, verifier.Verify(user, _password));
    }

    [Fact]
    public void ARealHashVerifiesOnlyTheRightPassword()
    {
        var verifier = new PasswordVerifier(PasswordVerifier.CreateHasher());
        var user = User.Create("real@fieldservice.example", "Real", string.Empty, UserRole.Administrator, DateTimeOffset.UnixEpoch);
        user.ReplacePasswordHash(verifier.Hash(user, _password), DateTimeOffset.UnixEpoch);

        Assert.Equal(PasswordVerificationResult.Success, verifier.Verify(user, _password));
        Assert.Equal(PasswordVerificationResult.Failed, verifier.Verify(user, $"{_password}!"));
    }
}
