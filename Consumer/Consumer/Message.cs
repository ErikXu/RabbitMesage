using System;
using System.Collections.Generic;
using Consumer.Mongo;

namespace Consumer
{
    public class Message : Entity
    {
        public string Exchange { get; set; }

        public string Route { get; set; }

        public string MessageId { get; set; }

        public DateTime Timestamp { get; set; }

        public long TimestampUnix { get; set; }

        public IDictionary<string, object> Headers { get; set; }

        public byte[] Body { get; set; }
    }
}