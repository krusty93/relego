using Serilog;
using Serilog.Events;
using Serilog.Formatting.Display;

namespace Relego.Server.Infrastructure.Logging;

internal static class SerilogConfiguration
{
    private const string LogDirectory = ".data/logs";
    private const string LogFilePath = ".data/logs/relego-.log";

    /// <summary>
    /// The shape of a line on disk. It matches the file sink's own default, so enabling redaction
    /// leaves the log format unchanged except that secrets come out as <see cref="SensitiveDataRedaction.Redacted"/>.
    /// </summary>
    internal const string OutputTemplate =
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}";

    internal static void ConfigureLogging(WebApplicationBuilder builder)
    {
        Directory.CreateDirectory(LogDirectory);

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.With<SensitiveDataRedactionEnricher>()
            .WriteTo.File(
                new RedactingTextFormatter(new MessageTemplateTextFormatter(OutputTemplate)),
                LogFilePath,
                rollingInterval: RollingInterval.Day,
                restrictedToMinimumLevel: LogEventLevel.Information)
            .CreateLogger();

        builder.Host.UseSerilog();
    }
}
