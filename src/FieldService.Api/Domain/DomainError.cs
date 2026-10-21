namespace FieldService.Api.Domain;

public sealed record DomainError(string Code, string Message, string? Field = null)
{
    public bool IsFieldError => Field is not null;
}
