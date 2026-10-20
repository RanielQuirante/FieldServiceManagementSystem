namespace FieldService.Api.Shared.Errors;

public static class ErrorCodes
{
    public const string ValidationFailed = "request.validation-failed";

    public const string Unauthenticated = "auth.unauthenticated";

    public const string InvalidCredentials = "auth.invalid-credentials";

    public const string SessionExpired = "auth.session-expired";

    public const string Forbidden = "auth.forbidden";

    public const string WrongApplication = "auth.wrong-application";

    public const string CsrfRequired = "auth.csrf-required";

    public const string JobNotFound = "job.not-found";

    public const string ResourceNotFound = "resource.not-found";

    public const string MethodNotAllowed = "request.method-not-allowed";

    public const string TooLarge = "request.too-large";

    public const string UnsupportedMediaType = "request.unsupported-media-type";

    public const string ConcurrencyConflict = "concurrency.conflict";

    public const string InspectionLocked = "inspection.locked";

    public const string RateLimitExceeded = "rate-limit.exceeded";

    public const string ServerError = "server.error";

    private const string Rfc9110 = "https://tools.ietf.org/html/rfc9110#section-";

    public static string? ForStatus(int status) => status switch
    {
        StatusCodes.Status400BadRequest => ValidationFailed,
        StatusCodes.Status401Unauthorized => Unauthenticated,
        StatusCodes.Status403Forbidden => Forbidden,
        StatusCodes.Status404NotFound => ResourceNotFound,
        StatusCodes.Status405MethodNotAllowed => MethodNotAllowed,
        StatusCodes.Status413PayloadTooLarge => TooLarge,
        StatusCodes.Status415UnsupportedMediaType => UnsupportedMediaType,
        StatusCodes.Status429TooManyRequests => RateLimitExceeded,
        >= StatusCodes.Status500InternalServerError and <= 599 => ServerError,
        _ => null,
    };

    public static string? TitleOf(string code) => code switch
    {
        ValidationFailed => "One or more fields are invalid.",
        Unauthenticated => "Authentication is required.",
        InvalidCredentials => "The email or password is incorrect.",
        SessionExpired => "The session has expired.",
        Forbidden => "This action is not allowed for your role.",
        WrongApplication => "This account uses the other FieldService app.",
        CsrfRequired => "The request is missing the anti-forgery header.",
        JobNotFound => "The job was not found.",
        ResourceNotFound => "The resource was not found.",
        MethodNotAllowed => "The method is not allowed for this resource.",
        TooLarge => "The request body is too large.",
        UnsupportedMediaType => "The request body must be JSON.",
        ConcurrencyConflict => "The job was changed by someone else.",
        InspectionLocked => "The inspection can no longer be changed.",
        RateLimitExceeded => "Too many requests.",
        ServerError => "An unexpected error occurred.",
        _ => null,
    };

    public static string? TypeFor(int status) => status switch
    {
        StatusCodes.Status400BadRequest => Rfc9110 + "15.5.1",
        StatusCodes.Status401Unauthorized => Rfc9110 + "15.5.2",
        StatusCodes.Status403Forbidden => Rfc9110 + "15.5.4",
        StatusCodes.Status404NotFound => Rfc9110 + "15.5.5",
        StatusCodes.Status405MethodNotAllowed => Rfc9110 + "15.5.6",
        StatusCodes.Status409Conflict => Rfc9110 + "15.5.10",
        StatusCodes.Status413PayloadTooLarge => Rfc9110 + "15.5.14",
        StatusCodes.Status415UnsupportedMediaType => Rfc9110 + "15.5.16",
        StatusCodes.Status429TooManyRequests => "https://tools.ietf.org/html/rfc6585#section-4",
        StatusCodes.Status500InternalServerError => Rfc9110 + "15.6.1",
        _ => null,
    };
}
