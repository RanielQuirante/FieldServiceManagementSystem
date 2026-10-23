using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Features.Inspection;

public static class InspectionFeatureRegistration
{
    public static IServiceCollection AddInspectionFeature(this IServiceCollection services)
    {
        services.AddApiClient<InspectionApiClient>();
        services.AddSingleton<InspectionStore>();
        services.AddTransient<InspectionViewModel>();
        services.AddTransient<InspectionPage>();
        services.AddTransient<ReviewAndSubmitViewModel>();
        services.AddTransient<ReviewAndSubmitPage>();
        return services;
    }
}
