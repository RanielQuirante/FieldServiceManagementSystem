using FieldService.Api.Domain;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace FieldService.Api.Shared.Security;

public static class SecurityRegistration
{
    private const string InvalidTokenError = "invalid_token";

    public static IServiceCollection AddSecurity(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<AuthOptions>, AuthOptionsValidator>();
        services.AddOptions<AuthOptions>()
            .Bind(configuration.GetSection(AuthOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton(PasswordVerifier.CreateHasher());
        services.AddSingleton<PasswordVerifier>();
        services.AddSingleton<AccessTokenIssuer>();
        services.AddSingleton<SocketTicketIssuer>();

        services.AddAuthenticationCore(options => options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme);
        services.AddWebEncoders();
        services.TryAddSingleton<IAuthenticationConfigurationProvider, AuthenticationConfigurationProvider>();
        new AuthenticationBuilder(services).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthOptions>, TimeProvider>((options, auth, time) =>
                ConfigureJwtBearer(options, auth.Value, time));

        services.AddSingleton<IAuthorizationMiddlewareResultHandler, AuthorizationResultHandler>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())
            .AddPolicy(Policies.Administrator, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.Administrator)))
            .AddPolicy(Policies.Technician, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.Technician)))
            .AddPolicy(Policies.AdministratorOrTechnician, policy => policy
                .RequireAuthenticatedUser()
                .RequireRole(nameof(UserRole.Administrator), nameof(UserRole.Technician)));

        return services;
    }

    public static WebApplication PrepareSecurity(this WebApplication app)
    {
        _ = app.Services.GetRequiredService<PasswordVerifier>();
        return app;
    }

    private static void ConfigureJwtBearer(JwtBearerOptions options, AuthOptions auth, TimeProvider time)
    {
        options.MapInboundClaims = false;
        options.IncludeErrorDetails = false;
        options.SaveToken = false;
        options.TimeProvider = time;
        options.Events = new JwtBearerEvents
        {
            OnChallenge = context =>
            {
                if (context.AuthenticateFailure is not null)
                {
                    context.Error = InvalidTokenError;
                    context.ErrorDescription = null;
                }

                return Task.CompletedTask;
            },
        };
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = AuthOptions.Issuer,
            ValidAudience = AuthOptions.AccessTokenAudience,
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            IssuerSigningKey = AccessTokenIssuer.SigningKey(auth),
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            ValidateLifetime = true,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = AuthOptions.ClockSkew,
            NameClaimType = AccessTokenClaims.Subject,
            RoleClaimType = AccessTokenClaims.Role,
            LifetimeValidator = (notBefore, expires, _, parameters) =>
                IsWithinLifetime(notBefore, expires, time.GetUtcNow().UtcDateTime, parameters.ClockSkew),
        };
    }

    private static bool IsWithinLifetime(DateTime? notBefore, DateTime? expires, DateTime now, TimeSpan clockSkew)
    {
        if (expires is not { } expiresAt)
        {
            return false;
        }

        if (notBefore is { } validFrom && (validFrom > expiresAt || validFrom > now + clockSkew))
        {
            return false;
        }

        return expiresAt >= now - clockSkew;
    }
}
