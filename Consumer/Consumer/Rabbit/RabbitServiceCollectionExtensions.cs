using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Consumer.Rabbit
{
    public static class RabbitServiceCollectionExtensions
    {
        public static IServiceCollection AddRabbitConnection(this IServiceCollection services, IConfiguration config)
        {
            if (services == null)
            {
                throw new ArgumentNullException(nameof(services));
            }

            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            services.AddOptions();

            var rabbitOption = new RabbitOption(config);

            var factory = new ConnectionFactory
            {
                Uri = new Uri(rabbitOption.Uri),
                AutomaticRecoveryEnabled = true
            };

            services.Add(ServiceDescriptor.Singleton(factory));
            services.Add(ServiceDescriptor.Singleton(factory.CreateConnection()));
            return services;
        }
    }
}