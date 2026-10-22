using System.Text.Json;
using System.Text.Json.Serialization;
using FieldService.Mobile.Features.Jobs;
using FieldService.Mobile.Features.SignIn;

namespace FieldService.Mobile.Shared.Http;

[JsonSourceGenerationOptions(
    JsonSerializerDefaults.Web,
    Converters =
    [
        typeof(TolerantEnumConverter<SessionClient>),
        typeof(TolerantEnumConverter<UserRole>),
        typeof(TolerantEnumConverter<JobStatus>),
        typeof(TolerantEnumConverter<JobPriority>),
        typeof(TolerantEnumConverter<JobHistoryAction>),
        typeof(TolerantEnumConverter<Trade>),
    ])]
[JsonSerializable(typeof(SignInRequest))]
[JsonSerializable(typeof(RefreshSessionRequest))]
[JsonSerializable(typeof(SignOutRequest))]
[JsonSerializable(typeof(SessionResponse))]
[JsonSerializable(typeof(GetAssignedJobsResponse))]
[JsonSerializable(typeof(JobDetailsResponse))]
[JsonSerializable(typeof(UpdateJobStatusRequest))]
[JsonSerializable(typeof(ApiProblem))]
public sealed partial class MobileJsonContext : JsonSerializerContext;
