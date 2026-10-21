namespace FieldService.Api.Shared.Security;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
