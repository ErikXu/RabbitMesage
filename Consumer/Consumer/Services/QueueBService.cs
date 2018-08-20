using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Consumer.Services
{
    public class QueueBService : IHostedService, IDisposable
    {
        private readonly ILogger _logger;
        private readonly IConnection _rabbitConnection;
        private readonly IModel _channel;

        private readonly List<int> _retryTime;

        public QueueBService(ILogger<AuditService> logger, IConnection rabbitConnection)
        {
            _logger = logger;
            _rabbitConnection = rabbitConnection;
            _channel = _rabbitConnection.CreateModel();

            _retryTime = new List<int>
            {
                1 * 1000,
                10 * 1000,
                30 * 1000
            };
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _channel.BasicQos(0, 1, false);

            _channel.ExchangeDeclare("Exchange", "direct");
            _channel.QueueDeclare("QueueB", true, false, false);
            _channel.QueueBind("QueueB", "Exchange", "RouteB");

            var retryDic = new Dictionary<string, object>
            {
                {"x-dead-letter-exchange", "Exchange"},
                {"x-dead-letter-routing-key", "RouteB"}
            };

            _channel.ExchangeDeclare("Exchange_Retry", "direct");
            _channel.QueueDeclare("QueueB_Retry", true, false, false, retryDic);
            _channel.QueueBind("QueueB_Retry", "Exchange_Retry", "RouteB_Retry");

            var consumer = new EventingBasicConsumer(_channel);
            consumer.Received += (model, ea) =>
            {
                //The QueueB is always failed.
                bool canAck;
                var retryCount = 0;
                if (ea.BasicProperties.Headers != null && ea.BasicProperties.Headers.ContainsKey("retryCount"))
                {
                    retryCount = (int)ea.BasicProperties.Headers["retryCount"];
                    _logger.LogWarning($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]Message:{ea.BasicProperties.MessageId}, {++retryCount} retry started...");
                }

                try
                {
                    Handle();
                    canAck = true;
                }
                catch (Exception ex)
                {
                    _logger.LogCritical(ex, "Error!");
                    if (CanRetry(retryCount))
                    {
                        SetupRetry(retryCount, "Exchange_Retry", "RouteB_Retry", ea);
                        canAck = true;
                    }
                    else
                    {
                        canAck = false;
                    }
                }

                try
                {
                    if (canAck)
                    {
                        _channel.BasicAck(ea.DeliveryTag, false);
                    }
                    else
                    {
                        _channel.BasicNack(ea.DeliveryTag, false, false);
                    }
                }
                catch (AlreadyClosedException ex)
                {
                    _logger.LogCritical(ex, "RabbitMQ is closed！");
                }
            };

            _channel.BasicConsume("QueueB", false, consumer);

            _logger.LogInformation("QueueB started...");
            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("QueueB ended...");
            return Task.CompletedTask;
        }

        public void Dispose()
        {
            _channel?.Close();
            _channel?.Dispose();

            _rabbitConnection?.Close();
            _rabbitConnection?.Dispose();
        }

        private void Handle()
        {
            throw new Exception("Always fails!");
        }

        private bool CanRetry(int retryCount)
        {
            return retryCount <= _retryTime.Count - 1;
        }

        private void SetupRetry(int retryCount, string retryExchange, string retryRoute, BasicDeliverEventArgs ea)
        {
            var body = ea.Body;
            var properties = ea.BasicProperties;
            properties.Headers = properties.Headers ?? new Dictionary<string, object>();
            properties.Headers["retryCount"] = retryCount;
            properties.Expiration = _retryTime[retryCount].ToString();

            try
            {
                _channel.BasicPublish(retryExchange, retryRoute, properties, body);
            }
            catch (AlreadyClosedException ex)
            {
                _logger.LogCritical(ex, "RabbitMQ is closed!");
            }
        }
    }
}