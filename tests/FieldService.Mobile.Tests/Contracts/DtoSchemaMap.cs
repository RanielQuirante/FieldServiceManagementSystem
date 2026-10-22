using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Features.SignIn;
using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Tests.Contracts;

public enum DtoDirection
{
    Request,
    Response,
}

public sealed record DtoSchema(Type Dto, string Schema, DtoDirection Direction);

public static class DtoSchemaMap
{
    public static IReadOnlyList<DtoSchema> Entries { get; } =
    [
        new(typeof(SignInRequest), "SignInRequest", DtoDirection.Request),
        new(typeof(RefreshSessionRequest), "RefreshSessionRequest", DtoDirection.Request),
        new(typeof(SignOutRequest), "SignOutRequest", DtoDirection.Request),
        new(typeof(SessionResponse), "SessionResponse", DtoDirection.Response),
        new(typeof(SessionUser), "SessionUser", DtoDirection.Response),
        new(typeof(ApiProblem), "HttpValidationProblemDetails", DtoDirection.Response),
        new(typeof(GetAssignedJobsResponse), "GetAssignedJobsResponse", DtoDirection.Response),
        new(typeof(AssignedJobResponse), "AssignedJobResponse", DtoDirection.Response),
        new(typeof(JobDetailsResponse), "JobDetailsResponse", DtoDirection.Response),
        new(typeof(ChecklistTemplateReference), "ChecklistTemplateReference", DtoDirection.Response),
        new(typeof(JobTechnicianReference), "JobTechnicianReference", DtoDirection.Response),
        new(typeof(UserReference), "UserReference", DtoDirection.Response),
        new(typeof(JobHistoryEntryResponse), "JobHistoryEntryResponse", DtoDirection.Response),
        new(typeof(JobAffordances), "JobAffordances", DtoDirection.Response),
        new(typeof(AllowedStatusChange), "AllowedStatusChange", DtoDirection.Response),
        new(typeof(UpdateJobStatusRequest), "UpdateJobStatusRequest", DtoDirection.Request),
    ];

    public static IReadOnlyDictionary<Type, string> Enums { get; } = new Dictionary<Type, string>
    {
        [typeof(SessionClient)] = "SessionClient",
        [typeof(UserRole)] = "UserRole",
        [typeof(JobStatus)] = "JobStatus",
        [typeof(JobPriority)] = "JobPriority",
        [typeof(JobHistoryAction)] = "JobHistoryAction",
        [typeof(Trade)] = "Trade",
    };

    public static IReadOnlyList<string> RequiredSchemas { get; } =
    [
        "SignInRequest",
        "RefreshSessionRequest",
        "SignOutRequest",
        "SessionResponse",
        "GetAssignedJobsResponse",
        "AssignedJobResponse",
        "JobDetailsResponse",
        "JobAffordances",
        "AllowedStatusChange",
        "UpdateJobStatusRequest",
        "HttpValidationProblemDetails",
    ];

    public static string? SchemaFor(Type type) =>
        Entries.FirstOrDefault(entry => entry.Dto == type)?.Schema
        ?? (Enums.TryGetValue(type, out var schema) ? schema : null);
}
