# RabbitMQ一个简单可靠的方案(.Net Core实现)

## 前言

最近需要使用到消息队列相关技术，于是重新接触RabbitMQ。遇到一些可靠性方面的问题，归纳了一下，大概有以下几种：

1. 临时异常，如数据库网络闪断、http请求临时失效等；
2. 时序异常，如A任务依赖于B任务，但可能由于调度或消费者分配的原因，导致A任务先于B任务执行；
3. 业务异常，由于系统测试不充分，上线后发现某几个或某几种消息无法正常处理；
4. 系统异常，业务中间件无法正常操作，如网络中断、数据库宕机等；
5. 非法异常，一些伪造、攻击类型的消息。

针对这些异常，我采用了一种基于消息审计、消息重试、消息检索、消息重发的方案。

<br />

## 方案
![sulotion](Docs/solution.svg)

1. 消息均使用`Exchange`进行通讯，方式可以是direct或topic，不建议fanout。
2. 根据业务在Exchange下分配一个或多个Queue，同时设置一个`审计线程(Audit)`监听所有Queue，用于记录消息到`MongoDB`，同时又不阻塞正常业务处理。
3. `生产者(Publisher)`在发布消息时，基于AMQP协议，生成消息标识MessageId和时间戳Timestamp，根据消息业务添加头信息Headers便于跟踪。
4. `消费者(Comsumer)`消息处理失败时，则把消息发送到`重试交换机(Retry Exchange)`，并设置过期（重试）时间及更新重试次数；如果超过重试次数则删除消息。
5. 重试交换机Exchange设置`死信交换机(Dead Letter Exchange)`，消息过期后自动转发到业务交换机(Exchange)。
6. `WebApi`可以根据消息标识MessageId、时间戳Timestamp以及头信息Headers在MongoDB中对消息进行检索或重试。

注：选择MongoDB作为存储介质的主要原因是其对头信息（headers）的动态查询支持较好，同等的替代产品还可以是Elastic Search这些。

<br />

## 生产者(Publisher)

1. 设置断线自动恢复

```csharp
var factory = new ConnectionFactory
{
    Uri = new Uri("amqp://guest:guest@192.168.132.137:5672"),
    AutomaticRecoveryEnabled = true
};
```

2. 定义Exchange，模式为direct

```csharp
channel.ExchangeDeclare("Exchange", "direct");
```

3. 根据业务定义QueueA和QueueB

```csharp
channel.QueueDeclare("QueueA", true, false, false);
channel.QueueBind("QueueA", "Exchange", "RouteA");

channel.QueueDeclare("QueueB", true, false, false);
channel.QueueBind("QueueB", "Exchange", "RouteB");
```

4. 启动消息发送确认机制，即需要收到RabbitMQ服务端的确认消息

```csharp
channel.ConfirmSelect();
```

5. 设置消息持久化

```csharp
var properties = channel.CreateBasicProperties();
properties.Persistent = true;
```

6. 生成消息标识MessageId、时间戳Timestamp以及头信息Headers

```csharp
properties.MessageId = Guid.NewGuid().ToString("N");
properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
properties.Headers = new Dictionary<string, object>
{
    { "key", "value" + i}
};
```

7. 发送消息，偶数序列发送到QueueA（RouteA），奇数序列发送到QueueB（RouteB）

```csharp
channel.BasicPublish("Exchange", i % 2 == 0 ? "RouteA" : "RouteB", properties, body);
```

8. 确定收到RabbitMQ服务端的确认消息

```csharp
var isOk = channel.WaitForConfirms();
if (!isOk)
{
    throw new Exception("The message is not reached to the server!");
}
```

<br />

## 正常消费者(ComsumerA)

1. 设置预取消息，避免公平轮训问题，可以根据需要设置预取消息数，这里是1

```csharp
_channel.BasicQos(0, 1, false);
```

2. 声明Exchange和Queue

```csharp
_channel.ExchangeDeclare("Exchange", "direct");
_channel.QueueDeclare("QueueA", true, false, false);
_channel.QueueBind("QueueA", "Exchange", "RouteA");
```

3. 编写回调函数

```csharp
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
```

注：设置了RabbitMQ的断线恢复机制，当RabbitMQ连接不可用时，与MQ通讯的操作会抛出AlreadyClosedException的异常，导致主线程退出，哪怕连接恢复了，程序也无法恢复，因此，需要捕获处理该异常。

<br />

## 异常消费者(ComsumerB)

1. 设置预取消息

```csharp
_channel.BasicQos(0, 1, false);
```

2. 声明Exchange和Queue

```csharp
_channel.ExchangeDeclare("Exchange", "direct");
_channel.QueueDeclare("QueueB", true, false, false);
_channel.QueueBind("QueueB", "Exchange", "RouteB");
```

3.  设置死信交换机(Dead Letter Exchange)

```csharp
var retryDic = new Dictionary<string, object>
{
　　{"x-dead-letter-exchange", "Exchange"},
　　{"x-dead-letter-routing-key", "RouteB"}
};

_channel.ExchangeDeclare("Exchange_Retry", "direct");
_channel.QueueDeclare("QueueB_Retry", true, false, false, retryDic);
_channel.QueueBind("QueueB_Retry", "Exchange_Retry", "RouteB_Retry");
```

4. 重试设置，3次重试；第一次1秒，第二次10秒，第三次30秒

```csharp
_retryTime = new List<int>
{
　　1 * 1000,
　　10 * 1000,
　　30 * 1000
};
```

5. 获取当前重试次数

```csharp
var retryCount = 0;
if (ea.BasicProperties.Headers != null && ea.BasicProperties.Headers.ContainsKey("retryCount"))
{
　　retryCount = (int)ea.BasicProperties.Headers["retryCount"];
　　_logger.LogWarning($"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}]Message:{ea.BasicProperties.MessageId}, {++retryCount} retry started...");
}
```

6. 发生异常，判断是否可以重试

```csharp
private bool CanRetry(int retryCount)
{
　　return retryCount <= _retryTime.Count - 1;
}
```

7. 可以重试，则启动重试机制

```csharp
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
```

<br />

## 审计消费者(Audit Comsumer)

1. 声明Exchange和Queue

```csharp
_channel.ExchangeDeclare("Exchange", "direct");

_channel.QueueDeclare("QueueAudit", true, false, false);
_channel.QueueBind("QueueAudit", "Exchange", "RouteA");
_channel.QueueBind("QueueAudit", "Exchange", "RouteB");
```

2. 排除死信Exchange转发过来的重复消息

```csharp
if (ea.BasicProperties.Headers == null || !ea.BasicProperties.Headers.ContainsKey("x-death"))
{
　　...
}
```

3. 生成消息实体

```csharp
var message = new Message
{
　　MessageId = ea.BasicProperties.MessageId,
　　Body = ea.Body,
　　Exchange = ea.Exchange,
　　Route = ea.RoutingKey
};
```

4. RabbitMQ会用bytes来存储字符串，因此，要把头中bytes转回字符串

```csharp
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
```

5. 把Unix格式的Timestamp转成UTC时间

```csharp
if (ea.BasicProperties.Timestamp.UnixTime > 0)
{
　　message.TimestampUnix = ea.BasicProperties.Timestamp.UnixTime;
　　var offset = DateTimeOffset.FromUnixTimeMilliseconds(ea.BasicProperties.Timestamp.UnixTime);
　　message.Timestamp = offset.UtcDateTime;
}
```

6. 消息存入MongoDB

```csharp
_mongoDbContext.Collection<Message>().InsertOne(message, cancellationToken: cancellationToken);
```
