using MongoDB.Driver;

namespace Consumer.Mongo
{
    public class MongoDbContext
    {
        private readonly IMongoDatabase _db;

        public MongoDbContext(MongoOption option)
        {
            var client = new MongoClient(option.ConnectionString);
            _db = client.GetDatabase(option.DatabaseName);
        }

        public IMongoCollection<T> Collection<T, TId>() where T : EntityWithTypedId<TId>
        {
            var collectionName = InferCollectionNameFrom<T>();
            return _db.GetCollection<T>(collectionName);
        }

        public IMongoCollection<T> Collection<T>() where T : Entity
        {
            var collectionName = InferCollectionNameFrom<T>();
            return _db.GetCollection<T>(collectionName);
        }

        private static string InferCollectionNameFrom<T>()
        {
            var type = typeof(T);
            return type.Name;
        }
    }
}