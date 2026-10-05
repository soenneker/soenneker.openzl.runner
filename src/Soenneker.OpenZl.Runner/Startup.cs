using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Soenneker.Utils.Process.Registrars;
using Soenneker.Utils.Directory.Registrars;
using Soenneker.Utils.File.Registrars;
using Soenneker.OpenZl.Runner.Utils;
using Soenneker.OpenZl.Runner.Utils.Abstract;
using Soenneker.Managers.Runners.Registrars;


namespace Soenneker.OpenZl.Runner;

/// <summary>
/// Console type startup
/// </summary>
public static class Startup
{
    // This method gets called by the runtime. Use this method to add services to the container.
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.SetupIoC(configuration);
    }

    public static IServiceCollection SetupIoC(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHostedService<ConsoleHostedService>()
                .AddSingleton<IFileOperationsUtil, FileOperationsUtil>()
                .AddDirectoryUtilAsSingleton()
                .AddFileUtilAsSingleton()
                .AddProcessUtilAsSingleton();

        if (configuration.GetValue<bool>("OpenZl:UpdateRepository"))
            services.AddRunnersManagerAsSingleton();

        return services;
    }
}
