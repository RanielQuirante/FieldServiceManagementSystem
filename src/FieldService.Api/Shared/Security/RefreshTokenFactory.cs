using System.Buffers.Text;
using System.Security.Cryptography;

namespace FieldService.Api.Shared.Security;

public static class RefreshTokenFactory
{
    public const int TokenBytes = 32;

    public const int EncodedLength = 43;

    public static NewRefreshToken Create()
    {
        var bytes = RandomNumberGenerator.GetBytes(TokenBytes);
        return new NewRefreshToken(Base64Url.EncodeToString(bytes), SHA256.HashData(bytes));
    }

    public static bool TryHash(string? value, out byte[] hash)
    {
        hash = [];
        if (value is not { Length: EncodedLength } || !value.All(IsBase64UrlCharacter))
        {
            return false;
        }

        Span<byte> bytes = stackalloc byte[TokenBytes + 1];
        if (!Base64Url.TryDecodeFromChars(value, bytes, out var written) || written != TokenBytes)
        {
            return false;
        }

        hash = SHA256.HashData(bytes[..written]);
        return true;
    }

    private static bool IsBase64UrlCharacter(char character) =>
        char.IsAsciiLetterOrDigit(character) || character is '-' or '_';
}
