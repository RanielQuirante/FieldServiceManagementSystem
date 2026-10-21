using FieldService.Api.Domain;

namespace FieldService.Api.Features;

public sealed record ChecklistTemplateReference(Guid Id, string Name, Trade Trade);
