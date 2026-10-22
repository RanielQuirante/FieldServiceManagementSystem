using FieldService.Mobile.Shared.Http;

namespace FieldService.Mobile.Features.Jobs;

public static class JobsFeatureRegistration
{
    public static IServiceCollection AddJobsFeature(this IServiceCollection services)
    {
        services.AddApiClient<JobsApiClient>();
        services.AddTransient<JobDetailsViewModel>();
        services.AddTransient<Func<JobDetailsViewModel>>(provider => provider.GetRequiredService<JobDetailsViewModel>);
        services.AddTransient<JobDetailsPage>();
        services.AddTransient<MyJobsViewModel>();
        services.AddTransient<MyJobsPage>();
        return services;
    }
}
