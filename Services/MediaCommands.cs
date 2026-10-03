using MongoDB.Bson;
using MongoDB.Driver;
using Music.Data;
using Music.Models;
using System.Security.Claims;

namespace Music.Services;

public sealed class MediaCleanup
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Url { get; set; } = "";
    public DateTime DueAt { get; set; } = DateTime.UtcNow.AddSeconds(30);
}

public sealed class MediaCommands(MongoDbContext db, AzureBlobService storage)
{
    public async Task<string?> Stage(string kind, string id, string purpose, IFormFile? file, bool image, string? oldUrl, CancellationToken ct)
    {
        if (file is null || file.Length == 0) return oldUrl;
        var media = await storage.Validate(file, image, ct);
        var target = storage.Prepare(kind, id, purpose, media.Extension);
        // Durable cleanup intents precede external storage writes. The worker shares the mutation gate
        // and deletes only objects no current document references, including interrupted uploads.
        await Queue(target.Url, ct);
        if (!string.IsNullOrWhiteSpace(oldUrl)) await Queue(oldUrl, ct);
        await storage.Upload(target, media, ct);
        return target.Url;
    }
    public async Task Queue(string? url, CancellationToken ct)
    {
        if (!storage.TryResolve(url, out var target)) return;
        await db.Database.GetCollection<MediaCleanup>("mediaCleanup").InsertOneAsync(new MediaCleanup { Url = target.Url }, cancellationToken: ct);
    }
    public async Task<bool> Referenced(string url, ClaimsPrincipal? viewer, CancellationToken ct)
    {
        if (await db.Songs.Find(x => x.FileUrl == url || x.CoverUrl == url || x.SnippetUrl == url).AnyAsync(ct)
            || await db.Artists.Find(x => x.ImageUrl == url).AnyAsync(ct)
            || await db.Albums.Find(x => x.CoverUrl == url).AnyAsync(ct)) return true;
        var filter = Builders<Playlist>.Filter.Eq(x => x.CoverUrl, url);
        if (viewer is not null && !viewer.IsInRole("Admin"))
        {
            var actor = viewer.FindFirstValue(ClaimTypes.NameIdentifier) ?? viewer.FindFirstValue("sub");
            filter &= Builders<Playlist>.Filter.Or(Builders<Playlist>.Filter.Eq(x => x.IsPublic, true), Builders<Playlist>.Filter.Eq(x => x.CreatedBy, actor));
        }
        return await db.Playlists.Find(filter).AnyAsync(ct);
    }
    public async Task Cleanup(CancellationToken ct)
    {
        var collection = db.Database.GetCollection<MediaCleanup>("mediaCleanup");
        var entries = await collection.Find(x => x.DueAt <= DateTime.UtcNow).SortBy(x => x.DueAt).Limit(25).ToListAsync(ct);
        foreach (var entry in entries)
        {
            if (storage.TryResolve(entry.Url, out var target) && !await Referenced(target.Url, null, ct)) await storage.Delete(target, ct);
            await collection.DeleteOneAsync(x => x.Id == entry.Id, ct);
        }
    }
}

public sealed class MaintenanceState { public volatile bool Initialized; }

public sealed class MaintenanceWorker(IServiceScopeFactory scopes, MutationGate gate, MaintenanceState state, ILogger<MaintenanceWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var initialized = false;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<MongoDbContext>();
                await gate.Run(async () =>
                {
                    if (!initialized)
                    {
                        // Idempotent repair for the historically introduced unknown field, using raw BSON.
                        foreach (var name in new[] { "artists", "albums", "songs" })
                            await db.Database.GetCollection<BsonDocument>(name).UpdateManyAsync(Builders<BsonDocument>.Filter.Exists("placeholder"), Builders<BsonDocument>.Update.Unset("placeholder"), cancellationToken: stoppingToken);
                        var storage = scope.ServiceProvider.GetRequiredService<AzureBlobService>();
                        // One-time resumable normalization: only the configured account and allowed catalogue
                        // containers can become stable URLs. Historical unrelated URLs stay inaccessible.
                        foreach (var schema in new[] { ("songs", new[] { "fileUrl", "coverUrl", "snippetUrl" }), ("artists", new[] { "imageUrl" }), ("albums", new[] { "coverUrl" }), ("playlists", new[] { "coverUrl" }) })
                        {
                            var documents = db.Database.GetCollection<BsonDocument>(schema.Item1);
                            using var cursor = await documents.FindAsync(Builders<BsonDocument>.Filter.Empty, new FindOptions<BsonDocument> { BatchSize = 100 }, stoppingToken);
                            while (await cursor.MoveNextAsync(stoppingToken))
                                foreach (var document in cursor.Current)
                                    foreach (var field in schema.Item2)
                                        if (document.TryGetValue(field, out var value) && value.IsString && storage.TryResolve(value.AsString, out var target) && value.AsString != target.Url)
                                            await documents.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", document["_id"]), Builders<BsonDocument>.Update.Set(field, target.Url), cancellationToken: stoppingToken);
                        }
                        await db.Artists.Indexes.CreateOneAsync(new CreateIndexModel<Artist>(Builders<Artist>.IndexKeys.Ascending(x => x.Name), new CreateIndexOptions { Unique = true, Collation = new Collation("en", strength: CollationStrength.Secondary), Name = "artist_name_unique" }), cancellationToken: stoppingToken);
                        await db.Albums.Indexes.CreateOneAsync(new CreateIndexModel<Album>(Builders<Album>.IndexKeys.Ascending("artist.id").Ascending(x => x.Title), new CreateIndexOptions { Unique = true, Collation = new Collation("en", strength: CollationStrength.Secondary), Name = "artist_album_unique" }), cancellationToken: stoppingToken);
                        await db.Songs.Indexes.CreateOneAsync(new CreateIndexModel<Song>(Builders<Song>.IndexKeys.Ascending("album.id").Ascending(x => x.AlbumPosition)), cancellationToken: stoppingToken);
                        await db.Songs.Indexes.CreateOneAsync(new CreateIndexModel<Song>(Builders<Song>.IndexKeys.Ascending("artists.id")), cancellationToken: stoppingToken);
                        await db.Songs.Indexes.CreateOneAsync(new CreateIndexModel<Song>(Builders<Song>.IndexKeys.Ascending(x => x.Title)), cancellationToken: stoppingToken);
                        await db.Playlists.Indexes.CreateOneAsync(new CreateIndexModel<Playlist>(Builders<Playlist>.IndexKeys.Ascending(x => x.CreatedBy).Descending(x => x.CreatedAt)), cancellationToken: stoppingToken);
                        initialized = true; state.Initialized = true;
                    }
                    await scope.ServiceProvider.GetRequiredService<MediaCommands>().Cleanup(stoppingToken);
                    return true;
                }, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogWarning("Music maintenance will retry after dependency failure ({ErrorType}).", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
            catch (OperationCanceledException) { break; }
        }
    }
}
