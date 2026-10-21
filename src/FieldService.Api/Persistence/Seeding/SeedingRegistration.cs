using System.Globalization;
using FieldService.Api.Shared.Observability;

namespace FieldService.Api.Persistence.Seeding;

public static class SeedingRegistration
{
    public const string DemoDataKey = "Seed:DemoData";

    public const string DemoPasswordKey = "Seed:DemoPassword";

    private const string ProductionRule = "must be false in Production";

    private static readonly string _passwordRule = string.Create(
        CultureInfo.InvariantCulture,
        $"must have {SeedOptions.MinimumDemoPasswordLength} or more characters when {DemoDataKey} is true");

    public static IServiceCollection AddDemoDataSeeding(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SeedOptions>()
            .Bind(configuration.GetSection(SeedOptions.SectionName))
            .Validate<IHostEnvironment, ILoggerFactory>(
                (options, environment, loggers) =>
                    Holds(!(options.DemoData && environment.IsProduction()), loggers, DemoDataKey, ProductionRule),
                $"{DemoDataKey}: {ProductionRule}.")
            .Validate<ILoggerFactory>(
                (options, loggers) => Holds(
                    !options.DemoData || options.DemoPassword is { Length: >= SeedOptions.MinimumDemoPasswordLength },
                    loggers,
                    DemoPasswordKey,
                    _passwordRule),
                $"{DemoPasswordKey}: {_passwordRule}.")
            .ValidateOnStart();
        services.AddScoped<DemoDataSeeder>();
        return services;
    }

    private static bool Holds(bool rule, ILoggerFactory loggers, string key, string description)
    {
        if (!rule)
        {
            ConfigurationLog.ConfigInvalid(loggers.CreateLogger(typeof(SeedingRegistration)), key, description);
        }

        return rule;
    }
}
