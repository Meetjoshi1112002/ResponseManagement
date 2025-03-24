using MongoDB.Driver;

namespace ReponseManagement.Data
{
    public class AppDbContext
    {
        private readonly IMongoClient _client;
        private readonly IMongoDatabase _database;

        public IMongoDatabase Database => _database;

        public AppDbContext(IConfiguration configuration)
        {
            var connectionString = configuration["MongoDB:ConnectionString"];
            var databaseName = configuration["MongoDB:DatabaseName"];

            _client = new MongoClient(connectionString);
            _database = _client.GetDatabase(databaseName);
        }

        public IMongoCollection<T> GetCollection<T>(string collectionName)
        {
            return _database.GetCollection<T>(collectionName);
        }

        // Transaction-related methods
        public IClientSessionHandle StartSession()
        {
            return _client.StartSession();
        }
    }
}
