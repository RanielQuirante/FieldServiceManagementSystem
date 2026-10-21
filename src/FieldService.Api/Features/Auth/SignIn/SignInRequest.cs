using System.ComponentModel.DataAnnotations;
using FieldService.Api.Domain;
using FieldService.Api.Shared.Errors;

namespace FieldService.Api.Features.Auth.SignIn;

public sealed record SignInRequest
{
    [Required]
    [StringLength(256)]
    [EmailAddress]
    [SingleLine]
    public string Email { get; init; } = string.Empty;

    [Required(AllowEmptyStrings = true)]
    [StringLength(256, MinimumLength = 1)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public SessionClient? Client { get; init; }
}
