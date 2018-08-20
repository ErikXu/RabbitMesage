using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Consumer.Services
{
    public class QueueAService : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IConnection _rabbitConnection;
        private readonly IModel _channel;

        public QueueAService(ILogger<AuditService> logger, IConnection rabbitConnection)
        {
            _logger = logger;
            _rabbitConnection = rabbitConnection;
            _channel = _rabbitConnection.CreateModel();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _channel.BasicQos(0, 1, false);

            _channel.ExchangeDeclare("Exchange", "direct");
            _channel.QueueDeclare("QueueA", true, false, false);
            _channel.QueueBind("QueueA", "Exchange", "RouteA");

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                //The QueueA is always successful.
                try
                {
                    _channel.BasicAck(ea.DeliveryTag, false);
                }
                catch (AlreadyClosedException ex)
                {
                    _logger.LogCritical(ex, "RabbitMQ is closed!");
                }
            };

            _channel.BasicConsume("QueueA", false, consumer);

            _logger.LogInformation("QueueA started...");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("QueueA ended...");
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