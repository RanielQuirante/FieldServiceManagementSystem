namespace FieldService.Api.Domain;

public sealed record SubmitCapability(bool Allowed, string? BlockingCode, int Answered, int Total);
