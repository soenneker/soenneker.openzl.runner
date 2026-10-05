using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Soenneker.OpenZl.Runner.Utils.Abstract;

namespace Soenneker.OpenZl.Runner;

public sealed class ConsoleHostedService(
    IFileOperationsUtil files,
    IHostApplicationLifetime lifetime,
    ILogger<ConsoleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await files.Process(stoppingToken);
            Environment.ExitCode = 0;
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            Environment.ExitCode = 130;
        }
        catch (Exception e)
        {
            logger.LogError(e, "OpenZL native build failed");
            Environment.ExitCode = 1;
        }
        finally
        {
            lifetime.StopApplication();
        }
    }
}