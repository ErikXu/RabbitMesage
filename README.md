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

**注：选择MongoDB作为存储介质的主要原因是其对头信息（headers）的动态查询支持较好，同等的替代产品还可以是Elastic Search这些。**

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
