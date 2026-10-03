using MongoDB.Bson;
using MongoDB.Driver;
using Music.Models;

namespace Music.Data;

public sealed class MongoDbContext(IMongoClient client, IConfiguration configuration)
{
    public IMongoDatabase Database { get; } = client.GetDatabase(configuration["MongoDb:DatabaseName"] ?? "spotibuds_music_demo");
    public IMongoCollection<Song> Songs => Database.GetCollection<Song>("songs");
    public IMongoCollection<Album> Albums => Database.GetCollection<Album>("albums");
    public IMongoCollection<Artist> Artists => Database.GetCollection<Artist>("artists");
    public IMongoCollection<Playlist> Playlists => Database.GetCollection<Playlist>("playlists");
    public async Task<bool> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        try { await Database.RunCommandAsync<BsonDocument>(new BsonDocument("ping", 1), cancellationToken: cancellationToken); return true; }
        catch (MongoException) { return false; }
        catch (TimeoutException) { return false; }
    }
}
