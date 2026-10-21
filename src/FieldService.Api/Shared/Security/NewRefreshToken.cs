namespace FieldService.Api.Shared.Security;

public sealed record NewRefreshToken(string Value, byte[] Hash);
