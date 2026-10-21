using FieldService.Api.Domain;

namespace FieldService.Api.Features.Auth;

public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    SessionUser User,
    string? RefreshToken);

public sealed record SessionUser(Guid Id, string DisplayName, UserRole Role);
