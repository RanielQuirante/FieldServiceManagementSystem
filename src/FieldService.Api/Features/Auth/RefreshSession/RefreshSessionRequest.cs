using System.ComponentModel.DataAnnotations;

namespace FieldService.Api.Features.Auth.RefreshSession;

public sealed record RefreshSessionRequest
{
    [StringLength(64)]
    public string? RefreshToken { get; init; }
}
