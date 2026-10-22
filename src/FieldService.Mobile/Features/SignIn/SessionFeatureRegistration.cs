using FieldService.Mobile.Shared.Http;
using FieldService.Mobile.Shared.Session;

namespace FieldService.Mobile.Features.SignIn;

public static class SessionFeatureRegistration
{
    public static IServiceCollection AddSessionFeature(this IServiceCollection services)
    {
        services.AddApiClient<AuthApiClient>(authorize: false);
        services.AddSingleton<TokenStore>();
        services.AddSingleton<ISessionService, SessionService>();
        services.AddTransient<SignInViewModel>();
        services.AddTransient<SignInPage>();
        return services;
    }
}
