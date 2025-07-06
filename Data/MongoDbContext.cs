using MongoDB.Driver;
using Music.Models;

namespace Music.Data;

public class MongoDbContext
{
    private readonly IMongoDatabase _database;

    public MongoDbContext(IMongoClient mongoClient, string databaseName)
    {
        _database = mongoClient.GetDatabase(databaseName);
    }

    public IMongoCollection<Song> Songs => _database.GetCollection<Song>("songs");
    public IMongoCollection<Artist> Artists => _database.GetCollection<Artist>("artists");
    public IMongoCollection<Album> Albums => _database.GetCollection<Album>("albums");
    public IMongoCollection<Playlist> Playlists => _database.GetCollection<Playlist>("playlists");
} 