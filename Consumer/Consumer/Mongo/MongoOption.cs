using System;
using Microsoft.Extensions.Configuration;

namespace Consumer.Mongo
{
    public class MongoOption
    {
        public MongoOption(IConfiguration config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            var section = config.GetSection("mongo");
            section.Bind(this);
        }

        public string ConnectionString { get; set; }

        public string DatabaseName { get; set; }
    }
}