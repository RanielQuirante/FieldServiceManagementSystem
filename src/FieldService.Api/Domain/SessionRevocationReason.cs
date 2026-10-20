namespace FieldService.Api.Domain;

public enum SessionRevocationReason
{
    SignOut,
    RefreshTokenReuse,
    UserInactive,
    Operator,
}
