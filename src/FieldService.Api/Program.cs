using FieldService.Api.Features;
using FieldService.Api.Persistence;
using FieldService.Api.Shared.Errors;
using FieldService.Api.Shared.Health;
using FieldService.Api.Shared.Http;
using FieldService.Api.Shared.Observability;
using FieldService.Api.Shared.OpenApi;
using FieldService.Api.Shared.Realtime;
using FieldService.Api.Shared.Security;
using Serilog;

Log.Logger = SerilogSetup.CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.AddObservability();
    builder.Services
        .AddSingleton(TimeProvider.System)
        .AddHttpConventions(builder.Configuration)
        .AddErrorHandling()
        .AddSecurity(builder.Configuration)
        .AddPersistence(builder.Configuration)
        .AddRealtime(builder.Configuration)
        .AddHealth()
        .AddApiDocument()
        .AddValidation();

    var app = builder.Build();

    app.ValidateConfiguration();
    app.PrepareSecurity();

    app.UseForwardedHeaders();
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseCorrelationId();
    app.UseRequestLogging();
    if (!app.Environment.IsDevelopment())
    {
        app.UseHsts();
    }
    app.UseCors(HttpRegistration.CorsPolicyName);
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();

    app.MapHealthEndpoints();
    app.MapApiDocument();
    app.MapFeatureEndpoints();

    await app.PrepareDatabaseAsync();
    await app.RunAsync();
}
catch (Exception exception) when (exception is not HostAbortedException)
{
    Log.Fatal(exception, "The API stopped unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}
