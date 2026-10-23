namespace FieldService.Api.Features;

public sealed record SubmitAffordance(bool Allowed, string? BlockingCode, int Answered, int Total);
