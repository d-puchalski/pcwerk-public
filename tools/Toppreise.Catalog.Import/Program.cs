using EFDB;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;

namespace ToppreiseCatalog.Import;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var presetsOnly = HasPresetsOnlyArgument(args);
        var hostArgs = args.Where(argument => !IsPresetsOnlyArgument(argument)).ToArray();
        var warningLogPath = Path.Combine(
            AppContext.BaseDirectory,
            "logs",
            "toppreise-warnings-errors-.log");
        Log.Logger = CreateLogger(warningLogPath);
        Log.Information("Warningi i błędy są zapisywane w {WarningLogPath}", warningLogPath);

        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, eventArgs) =>
        {
            eventArgs.Cancel = true;
            cancellation.Cancel();
        };

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                Args = hostArgs,
                ContentRootPath = AppContext.BaseDirectory
            });
            var connectionString = builder.Configuration.GetConnectionString("PcWerk")
                ?? throw new InvalidOperationException("Brak ConnectionStrings:PcWerk w appsettings.json.");

            builder.Logging.ClearProviders();
            builder.Services.AddSerilog(Log.Logger);
            builder.Services.Configure<ImporterOptions>(builder.Configuration.GetSection(ImporterOptions.SectionName));
            builder.Services.Configure<PcPresetOptions>(builder.Configuration.GetSection(PcPresetOptions.SectionName));
            builder.Services.AddSingleton<ToppreiseScraper>();
            builder.Services.AddDbContext<AppDbContext>(options => options.UseNpgsql(connectionString));
            builder.Services.AddScoped<CatalogImportService>();
            builder.Services.AddScoped<PcPresetGenerator>();

            using var host = builder.Build();
            await using var scope = host.Services.CreateAsyncScope();
            if (presetsOnly)
            {
                Log.Information("Uruchamiam wyłącznie przebudowę presetów PC z aktualnego katalogu.");
                await scope.ServiceProvider.GetRequiredService<PcPresetGenerator>().RefreshAsync(cancellation.Token);
            }
            else
            {
                await scope.ServiceProvider.GetRequiredService<CatalogImportService>().RunAsync(cancellation.Token);
            }
            return 0;
        }
        catch (OperationCanceledException)
        {
            Log.Information(presetsOnly
                ? "Przebudowa presetów PC zatrzymana."
                : "Import katalogu Toppreise zatrzymany.");
            return 2;
        }
        catch (Exception exception)
        {
            Log.Error(exception, presetsOnly
                ? "Błąd przebudowy presetów PC"
                : "Błąd importu katalogu Toppreise");
            return 1;
        }
        finally
        {
            await Log.CloseAndFlushAsync();
        }
    }

    internal static bool HasPresetsOnlyArgument(IEnumerable<string> args) =>
        args.Any(IsPresetsOnlyArgument);

    private static bool IsPresetsOnlyArgument(string argument) =>
        argument.Equals("--presets-only", StringComparison.OrdinalIgnoreCase);

    internal static Serilog.ILogger CreateLogger(string warningLogPath, bool includeConsole = true)
    {
        var configuration = new LoggerConfiguration()
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Database.Command", LogEventLevel.Fatal)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore.Update", LogEventLevel.Fatal)
            .MinimumLevel.Override("Npgsql", LogEventLevel.Warning)
            .WriteTo.File(
                warningLogPath,
                restrictedToMinimumLevel: LogEventLevel.Warning,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 31,
                fileSizeLimitBytes: 50 * 1024 * 1024,
                rollOnFileSizeLimit: true,
                shared: true,
                outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} {Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}");
        if (includeConsole)
        {
            configuration.WriteTo.Console(
                outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
        }

        return configuration.CreateLogger();
    }
}
