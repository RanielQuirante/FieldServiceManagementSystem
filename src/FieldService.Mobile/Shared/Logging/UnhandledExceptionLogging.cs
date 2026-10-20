namespace FieldService.Mobile.Shared.Logging;

public static class UnhandledExceptionLogging
{
    public static void Register(Serilog.ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            logger.Fatal(args.ExceptionObject as Exception, "app.unhandled-exception {IsTerminating}", args.IsTerminating);

        TaskScheduler.UnobservedTaskException += (_, args) =>
            logger.Error(args.Exception, "app.unobserved-task-exception");
    }
}
