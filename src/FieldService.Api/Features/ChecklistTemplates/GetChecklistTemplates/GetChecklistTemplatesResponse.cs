using FieldService.Api.Domain;

namespace FieldService.Api.Features.ChecklistTemplates.GetChecklistTemplates;

public sealed record GetChecklistTemplatesResponse(IReadOnlyList<ChecklistTemplateResponse> Items);

public sealed record ChecklistTemplateResponse(Guid Id, string Name, Trade Trade, int ItemCount);
