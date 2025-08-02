using MongoDB.Driver;
using Music.Models;

namespace Music.Data;

public class MongoDbContext
{
    private readonly IMongoDatabase? _database;
    private readonly bool _isConnected;
    private readonly IMongoClient? _mongoClient;

    public MongoDbContext(IMongoClient? mongoClient, string databaseName)
    {
        _mongoClient = mongoClient;
        
        if (mongoClient != null)
        {
            try
            {
                _database = mongoClient.GetDatabase(databaseName);
                
                // Actually test the connection instead of just assuming it works
                try
                {
                    // Test the connection with a timeout
                    var pingTask = _database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
                    pingTask.Wait(TimeSpan.FromSeconds(10));
                    
                    _isConnected = true;
                }
                catch (Exception ex)
                {
                    _database = null;
                    _isConnected = false;
                }
            }
            catch (Exception ex)
            {
                _database = null;
                _isConnected = false;
            }
        }
        else
        {
            _database = null;
            _isConnected = false;
        }
    }

    public bool IsConnected => _isConnected && _database != null;

    public IMongoCollection<Song>? Songs => _database?.GetCollection<Song>("songs");
    public IMongoCollection<Album>? Albums => _database?.GetCollection<Album>("albums");
    public IMongoCollection<Artist>? Artists => _database?.GetCollection<Artist>("artists");
    public IMongoCollection<Playlist>? Playlists => _database?.GetCollection<Playlist>("playlists");

    public async Task<bool> TestConnectionAsync()
    {
        if (_database == null || _mongoClient == null)
        {
            return false;
        }

        try
        {
            await _database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
            return true;
        }
        catch (Exception ex)
        {
            return false;
        }
    }

    public async Task<T?> ExecuteWithRetryAsync<T>(Func<Task<T>> operation, int maxRetries = 3)
    {
        var retryCount = 0;
        
        while (retryCount < maxRetries)
        {
            try
            {
                return await operation();
            }
            catch (MongoDB.Driver.MongoConnectionException ex)
            {
                retryCount++;
                
                if (retryCount >= maxRetries)
                {
                    throw;
                }
                
                // Wait before retrying
                await Task.Delay(1000 * retryCount);
            }
            catch (MongoDB.Driver.MongoException ex) when (ex.Message.Contains("timeout") || ex.Message.Contains("Timeout"))
            {
                retryCount++;
                
                if (retryCount >= maxRetries)
                {
                    throw;
                }
                
                // Wait before retrying
                await Task.Delay(1000 * retryCount);
            }
        }
        
        return default(T);
    }
} 