using System;
using System.Collections.Generic;
using System.Text;
using RabbitMQ.Client;

namespace Publisher
{
    class Program
    {
        static void Main()
        {
            var factory = new ConnectionFactory
            {
                Uri = new Uri("amqp://guest:guest@localhost:5672"),
                AutomaticRecoveryEnabled = true
            };

            using (var connection = factory.CreateConnection())
            {
                using (var channel = connection.CreateModel())
                {
                    channel.ExchangeDeclare("Exchange", "direct");

                    channel.QueueDeclare("QueueA", true, false, false);
                    channel.QueueBind("QueueA", "Exchange", "RouteA");

                    channel.QueueDeclare("QueueB", true, false, false);
                    channel.QueueBind("QueueB", "Exchange", "RouteB");

                    channel.ConfirmSelect();

                    for (var i = 0; i < 2; i++)
                    {
                        var properties = channel.CreateBasicProperties();
                        properties.Persistent = true;
                        properties.MessageId = Guid.NewGuid().ToString("N");
                        properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

                        properties.Headers = new Dictionary<string, object>
                        {
                            { "key", "value" + i}
                        };

                        var message = "Hello " + i;
                        var body = Encoding.UTF8.GetBytes(message);

                        channel.BasicPublish("Exchange", i % 2 == 0 ? "RouteA" : "RouteB", properties, body);
                        var isOk = channel.WaitForConfirms();
                        if (!isOk)
                        {
                            throw new Exception("The message is not reached to the server!");
                        }
                    }
                }
            }
        }
    }
}
