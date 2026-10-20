using System.Security.Cryptography;
using FieldService.Api.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Serilog.Core;

namespace FieldService.Api.Tests.Infrastructure;

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string TestingEnvironment = "Testing";

    public const string WebOrigin = "http://localhost:5173";

    public ApiFactory(string connectionString)
    {
        LogsDirectory = Path.Combine(Path.GetTempPath(), "fieldservice-api-tests", Guid.NewGuid().ToString("N"));
        Settings = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{PersistenceRegistration.ConnectionStringName}"] = connectionString,
            ["Auth:AccessTokenKey"] = RandomKey(),
            ["Auth:SocketTicketKey"] = RandomKey(),
            ["Notifications:BaseUrl"] = string.Empty,
            ["Notifications:WebhookSecret"] = $"whsec_{RandomKey()}",
            ["Seed:DemoData"] = "false",
            ["Cors:AllowedOrigins:0"] = WebOrigin,
            ["Logs:Directory"] = LogsDirectory,
        };
    }

    public string EnvironmentName { get; set; } = TestingEnvironment;

    public Dictionary<string, string?> Settings { get; }

    public string LogsDirectory { get; }

    public FakeTimeProvider Clock { get; } = new(TruncateToSeconds(DateTimeOffset.UtcNow));

    public CapturingLogSink LogSink { get; } = new();

    public Action<IServiceCollection>? TestServices { get; set; }

    public ApiClient CreateApiClient()
    {
        return new ApiClient(CreateClient());
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(EnvironmentName);
        builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(Settings));
        builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ILogEventSink>(LogSink);
            services.AddSingleton<IStartupFilter, TestClientIpStartupFilter>();
            TestServices?.Invoke(services);
        });
    }

    private static string RandomKey()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
    }

    private static DateTimeOffset TruncateToSeconds(DateTimeOffset value)
    {
        return new DateTimeOffset(value.UtcTicks - (value.UtcTicks % TimeSpan.TicksPerSecond), TimeSpan.Zero);
    }
}
