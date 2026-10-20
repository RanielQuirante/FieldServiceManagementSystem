namespace FieldService.Api.Shared.Errors;

public static class ErrorsRegistration
{
    public static IServiceCollection AddErrorHandling(this IServiceCollection services)
    {
        services.AddProblemDetails(options => options.CustomizeProblemDetails = ProblemDetailsSetup.Customize);
        services.AddExceptionHandler<ConcurrencyExceptionHandler>();
        services.AddExceptionHandler<BadRequestExceptionHandler>();
        services.AddExceptionHandler<FallbackExceptionHandler>();

        return services;
    }
}
