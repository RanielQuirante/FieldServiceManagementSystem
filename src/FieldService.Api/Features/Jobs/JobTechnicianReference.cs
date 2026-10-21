using FieldService.Api.Domain;

namespace FieldService.Api.Features.Jobs;

public sealed record JobTechnicianReference(Guid Id, string DisplayName, Trade Trade);
