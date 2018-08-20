using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Linq;
using RabbitMQ.Client;
using WebApi.Mongo;

namespace WebApi.Controllers
{
    [Route("api/messages")]
    [ApiController]
    public class MessagesController : ControllerBase
    {
        private readonly IConnection _rabbitConnection;
        private readonly MongoDbContext _mongoDbContext;

        public MessagesController(IConnection rabbitConnection, MongoDbContext mongoDbContext)
        {
            _rabbitConnection = rabbitConnection;
            _mongoDbContext = mongoDbContext;
        }

        /// <summary>
        /// Get message list
        /// </summary>
        /// <returns></returns>
        [HttpGet]
        public async Task<IActionResult> List()
        {
            var messages = await _mongoDbContext.Collection<Message>().Find(new BsonDocument())
                .Sort(Builders<Message>.Sort.Descending(n => n.Timestamp))
                .ToListAsync();

            return Ok(messages);
        }

        /// <summary>
        /// Get message list by message id
        /// </summary>
        /// <returns></returns>
        [HttpGet("{messageId}")]
        public async Task<IActionResult> GetListByMessageId(string messageId)
        {
            var messages = await _mongoDbContext.Collection<Message>().Find(n => n.MessageId == messageId)
                .Sort(Builders<Message>.Sort.Descending(n => n.Timestamp))
                .ToListAsync();

            return Ok(messages);
        }

        /// <summary>
        /// Search messages by headers
        /// </summary>
        /// <param name="headers"></param>
        /// <returns></returns>
        [HttpPost("action/search")]
        public IActionResult Search([FromBody]IDictionary<string, object> headers)
        {
            var messages = _mongoDbContext.Collection<Message>().AsQueryable();

            foreach (var header in headers)
            {
                messages = messages.Where(n => n.Headers.ContainsKey(header.Key) && n.Headers[header.Key] == header.Value);
            }

            return Ok(messages);
        }

        /// <summary>
        /// Resend message
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        [HttpPost("{id}/action/resend")]
        public async Task<IActionResult> Resend(string id)
        {
            var message = await _mongoDbContext.Collection<Message>().Find(n => n.Id == new ObjectId(id)).SingleOrDefaultAsync();

            if (message == null)
            {
                return NotFound(new { message = "Message is not found!" });
            }

            using (var channel = _rabbitConnection.CreateModel())
            {
                channel.ConfirmSelect();
                var properties = channel.CreateBasicProperties();
                properties.Persistent = true;
                properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
                properties.MessageId = Guid.NewGuid().ToString("N");
                properties.Headers = message.Headers;
                channel.BasicPublish(message.Exchange, message.Route, properties, message.Body);
                var isOk = channel.WaitForConfirms();
                if (!isOk)
                {
                    throw new Exception("The message is not reached to the server!");
                }
            }

            return Ok();
        }
    }
}
