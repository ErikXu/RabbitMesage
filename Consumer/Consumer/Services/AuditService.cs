using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Consumer.Mongo;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Consumer.Services
{
    public class AuditService : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IConnection _rabbitConnection;
        private readonly IModel _channel;
        private readonly MongoDbContext _mongoDbContext;

        public AuditService(ILogger<AuditService> logger, IConnection rabbitConnection, MongoDbContext mongoDbContext)
        {
            _logger = logger;
            _rabbitConnection = rabbitConnection;
            _channel = _rabbitConnection.CreateModel();
            _mongoDbContext = mongoDbContext;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _channel.BasicQos(0, 1, false);

            _channel.ExchangeDeclare("Exchange", "direct");

            _channel.QueueDeclare("QueueAudit", true, false, false);
            _channel.QueueBind("QueueAudit", "Exchange", "RouteA");
            _channel.QueueBind("QueueAudit", "Exchange", "RouteB");

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                var isOk = true;
                try
                {
                    if (ea.BasicProperties.Headers == null || !ea.BasicProperties.Headers.ContainsKey("x-death"))
                    {
                        var message = new Message
                        {
                            MessageId = ea.BasicProperties.MessageId,
                            Body = ea.Body,
                            Exchange = ea.Exchange,
                            Route = ea.RoutingKey
                        };

                        if (ea.BasicProperties.Headers != null)
                        {
                            var headers = new Dictionary<string, object>();

                            foreach (var header in ea.BasicProperties.Headers)
                            {
                                if (header.Value is byte[] bytes)
                                {
                                    headers[header.Key] = Encoding.UTF8.GetString(bytes);
                                }
                                else
                                {
                                    headers[header.Key] = header.Value;
                                }
                            }

                            message.Headers = headers;
                        }


                        if (ea.BasicProperties.Timestamp.UnixTime > 0)
                        {
                            message.TimestampUnix = ea.BasicProperties.Timestamp.UnixTime;
                            var offset = DateTimeOffset.FromUnixTimeMilliseconds(ea.BasicProperties.Timestamp.UnixTime);
                            message.Timestamp = offset.UtcDateTime;
                        }

                        _mongoDbContext.Collection<Message>().InsertOne(message, cancellationToken: cancellationToken);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Error!");
                    isOk = false;
                }

                try
                {
                    if (isOk)
                    {
                        _channel.BasicAck(ea.DeliveryTag, false);
                    }
                    else
                    {
                        _channel.BasicNack(ea.DeliveryTag, false, true);
                    }
                }
                catch (AlreadyClosedException ex)
                {
                    _logger.LogCritical(ex, "RabbitMQ is closed!");
                }
            };

            _channel.BasicConsume("QueueAudit", false, consumer);

            _logger.LogInformation("Audit service started...");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Audit service ended...");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _channel?.Close();
            _channel?.Dispose();

            _rabbitConnection?.Close();
            _rabbitConnection?.Dispose();
        }
    }
}