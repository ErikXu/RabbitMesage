using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Bson.Serialization.Conventions;

namespace WebApi.Mongo
{
    public static class MongoServiceCollectionExtensions
    {
        public static IServiceCollection AddMongoDbContext(this IServiceCollection services, IConfiguration config)
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

            var option = new MongoOption(config);

            services.Add(ServiceDescriptor.Singleton(new MongoDbContext(option)));

            SetConvention();

            return services;
        }

        private static void SetConvention()
        {
            var pack = new ConventionPack { new IgnoreExtraElementsConvention(true), new IgnoreIfNullConvention(true) };
            ConventionRegistry.Register("IgnoreExtraElements&IgnoreIfNull", pack, type => true);
        }
    }
}