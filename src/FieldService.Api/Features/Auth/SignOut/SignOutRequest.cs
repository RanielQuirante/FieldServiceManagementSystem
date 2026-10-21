using System.ComponentModel.DataAnnotations;

namespace FieldService.Api.Features.Auth.SignOut;

public sealed record SignOutRequest
{
    [StringLength(64)]
    public string? RefreshToken { get; init; }
}
