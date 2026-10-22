using System.Text.Json;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Tests.Shared.Testing;

namespace FieldService.Mobile.Tests.Shared.Http;

public sealed class MobileJsonContextTests
{
    [Fact]
    public void ASessionResponseRoundTripsWithCamelCaseNamesAndStringEnums()
    {
        var response = new SessionResponse(
            TestSession.NewAccessToken(),
            new DateTimeOffset(2026, 10, 19, 14, 57, 0, TimeSpan.Zero),
            new SessionUser(Guid.Parse("01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e"), "Aisha Karim", UserRole.Technician),
            TestSession.NewRefreshToken());

        var json = JsonSerializer.Serialize(response, MobileJsonContext.Default.SessionResponse);
        var copy = JsonSerializer.Deserialize(json, MobileJsonContext.Default.SessionResponse);

        Assert.Equal(response, copy);
        using var document = JsonDocument.Parse(json);
        Assert.Equal("Technician", document.RootElement.GetProperty("user").GetProperty("role").GetString());
        Assert.True(document.RootElement.TryGetProperty("accessTokenExpiresAt", out _));
    }

    [Fact]
    public void ASignInRequestIsWrittenWithCamelCaseNamesAndTheMobileClient()
    {
        var password = TestSession.NewPassword();

        var json = JsonSerializer.Serialize(
            new SignInRequest("aisha.karim@fieldservice.example", password, SessionClient.Mobile),
            MobileJsonContext.Default.SignInRequest);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("aisha.karim@fieldservice.example", document.RootElement.GetProperty("email").GetString());
        Assert.Equal(password, document.RootElement.GetProperty("password").GetString());
        Assert.Equal("Mobile", document.RootElement.GetProperty("client").GetString());
    }

    [Theory]
    [InlineData("\"Supervisor\"")]
    [InlineData("\"technician\"")]
    [InlineData("\"1\"")]
    [InlineData("2")]
    [InlineData("null")]
    public void AnUnknownRoleReadsAsUnknown(string role)
    {
        var json = $$"""{"id":"01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e","displayName":"Aisha Karim","role":{{role}}}""";

        var user = JsonSerializer.Deserialize(json, MobileJsonContext.Default.SessionUser);

        Assert.Equal(UserRole.Unknown, user!.Role);
    }

    [Theory]
    [InlineData("Administrator", UserRole.Administrator)]
    [InlineData("Technician", UserRole.Technician)]
    public void AKnownRoleReadsAsItsMember(string role, UserRole expected)
    {
        var json = $$"""{"id":"01a0f6b1-2a68-77d9-8aa0-8a58bef6a99e","displayName":"Aisha Karim","role":"{{role}}"}""";

        var user = JsonSerializer.Deserialize(json, MobileJsonContext.Default.SessionUser);

        Assert.Equal(expected, user!.Role);
    }

    [Fact]
    public void EveryDtoEnumHasUnknownAsItsDefaultMember()
    {
        Assert.Equal(UserRole.Unknown, default(UserRole));
        Assert.Equal(SessionClient.Unknown, default(SessionClient));
    }

    [Fact]
    public void CredentialRecordsNeverPrintTheirSecrets()
    {
        var password = TestSession.NewPassword();
        var refreshToken = TestSession.NewRefreshToken();
        var accessToken = TestSession.NewAccessToken();
        var user = new SessionUser(Guid.NewGuid(), "Aisha Karim", UserRole.Technician);

        var printed = string.Join(
            ' ',
            new SignInRequest("aisha.karim@fieldservice.example", password, SessionClient.Mobile).ToString(),
            new RefreshSessionRequest(refreshToken).ToString(),
            new SignOutRequest(refreshToken).ToString(),
            new SessionResponse(accessToken, DateTimeOffset.UnixEpoch, user, refreshToken).ToString());

        Assert.DoesNotContain(password, printed, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, printed, StringComparison.Ordinal);
        Assert.DoesNotContain(accessToken, printed, StringComparison.Ordinal);
        Assert.DoesNotContain("aisha.karim", printed, StringComparison.Ordinal);
    }

    [Fact]
    public void ASessionUserPrintsOnlyItsTypeAndId()
    {
        var id = Guid.NewGuid();
        var user = new SessionUser(id, "Aisha Karim", UserRole.Technician);

        Assert.Equal("SessionUser " + id.ToString("D"), user.ToString());
        Assert.DoesNotContain("Aisha", user.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(nameof(UserRole.Technician), user.ToString(), StringComparison.Ordinal);
    }
}
