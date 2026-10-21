using System.Security.Cryptography;
using FieldService.Api.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FieldService.Api.Shared.Security;

public sealed class PasswordVerifier
{
    public const int IterationCount = 210_000;

    private static readonly User _unknownAccount = User.Create(
        "unknown-account@fieldservice.invalid",
        "Unknown account",
        string.Empty,
        UserRole.Technician,
        DateTimeOffset.UnixEpoch);

    private readonly IPasswordHasher<User> _hasher;

    private readonly string _dummyHash;

    public PasswordVerifier(IPasswordHasher<User> hasher)
    {
        _hasher = hasher;
        _dummyHash = hasher.HashPassword(_unknownAccount, Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
    }

    public static IPasswordHasher<User> CreateHasher() =>
        new PasswordHasher<User>(Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));

    public PasswordVerificationResult Verify(User? user, string password)
    {
        if (user is null)
        {
            Check(_unknownAccount, _dummyHash, password);
            return PasswordVerificationResult.Failed;
        }

        return Check(user, user.PasswordHash, password);
    }

    public string Hash(User user, string password) => _hasher.HashPassword(user, password);

    private PasswordVerificationResult Check(User user, string hash, string password)
    {
        try
        {
            return _hasher.VerifyHashedPassword(user, hash, password);
        }
        catch (FormatException)
        {
            return PasswordVerificationResult.Failed;
        }
    }
}
