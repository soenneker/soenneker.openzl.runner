using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Serilog;
using Soenneker.Enums.DeployEnvironment;
using Soenneker.Extensions.LoggerConfiguration;
using Soenneker.Extensions.ValueTask;

namespace Soenneker.OpenZl.Runner;

public sealed class Program
{
    private static string? _environment;

    private static CancellationTokenSource? _cts;

    public static async Task Main(string[] args)
    {
        _environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Local";

        if (string.IsNullOrWhiteSpace(_environment))
            throw new Exception("ASPNETCORE_ENVIRONMENT is not set");

        // Declare CancellationTokenSource in a broader scope
        _cts = new CancellationTokenSource(); // Use 'using' to ensure proper disposal
        Console.CancelKeyPress += OnCancelKeyPress;

        try
        {
            await CreateHostBuilder(args).RunConsoleAsync(_cts.Token);
        }
        catch (Exception e)
        {
            Log.Error(e, "Stopped program because of exception");
            throw;
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress; // Detach the handler

            _cts.Dispose();
            await Log.CloseAndFlushAsync().NoSync();
        }
    }

    /// <summary>
    /// Used for WebApplicationFactory, cannot delete, cannot change access, cannot change number of parameters.
    /// </summary>
    public static IHostBuilder CreateHostBuilder(string[] args)
    {
        DeployEnvironment envEnum = DeployEnvironment.FromName(_environment ?? "Local");

        LoggerConfigurationExtension.BuildBootstrapLoggerAndSetGloballySync(envEnum);

        IHostBuilder? host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((hostingContext, builder) =>
            {
                builder.AddEnvironmentVariables();
                builder.SetBasePath(hostingContext.HostingEnvironment.ContentRootPath);
            })
            .UseSerilog()
            .ConfigureServices((context, services) => { Startup.ConfigureServices(services, context.Configuration); });

        return host;
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs eventArgs)
    {
        eventArgs.Cancel = true; // Prevents immediate termination
        _cts?.Cancel();
    }
}
