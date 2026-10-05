using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Soenneker.Managers.Runners.Abstract;
using Soenneker.OpenZl.Runner.Utils.Abstract;

namespace Soenneker.OpenZl.Runner.Tests;

public sealed class StartupTests
{
    [Test]
    public async Task BuildOnlyResolvesWithoutPublishingCredentials()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenZl:UpdateRepository"] = "false" })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(configuration);
        services.SetupIoC(configuration);
        await using ServiceProvider provider = services.BuildServiceProvider();
        _ = provider.GetServices<IFileOperationsUtil>().Single();
        if (provider.GetServices<IRunnersManager>().Any())
            throw new InvalidOperationException("Build-only mode registered publishing dependencies.");
    }

    [Test]
    public void PublishingRegistersRunnersManager()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["OpenZl:UpdateRepository"] = "true" })
            .Build();
        var services = new ServiceCollection();
        services.SetupIoC(configuration);
        if (!services.Any(service => service.ServiceType == typeof(IRunnersManager)))
            throw new InvalidOperationException("Publishing mode did not register RunnersManager.");
    }
}
