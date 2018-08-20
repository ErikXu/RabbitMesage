using System.IO;
using Consumer.Mongo;
using Consumer.Rabbit;
using Consumer.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Consumer
{
    class Program
    {
        static void Main()
        {
            var host = new HostBuilder()
                .ConfigureHostConfiguration(configHost =>
                {
                    configHost.SetBasePath(Directory.GetCurrentDirectory());
                    configHost.AddEnvironmentVariables("ASPNETCORE_");
                })
                .ConfigureAppConfiguration((hostContext, configApp) =>
                {
                    configApp.AddJsonFile("appsettings.json", true);
                    configApp.AddJsonFile($"appsettings.{hostContext.HostingEnvironment.EnvironmentName}.json", true);
                    configApp.AddEnvironmentVariables();
                })
                .ConfigureServices((hostContext, services) =>
                {
                    services.AddLogging();

                    services.AddMongoDbContext(hostContext.Configuration);
                    services.AddRabbitConnection(hostContext.Configuration);

                    services.AddHostedService<QueueAService>();
                    services.AddHostedService<QueueBService>();
                    services.AddHostedService<AuditService>();
                })
                .ConfigureLogging((hostContext, configLogging) =>
                {
                    if (hostContext.HostingEnvironment.EnvironmentName == EnvironmentName.Development)
                    {
                        configLogging.AddDebug();
                    }

                    configLogging.AddConsole();
                })
                .UseConsoleLifetime()
                .Build();

            host.Run();
        }
    }
}
