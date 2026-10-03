using MongoDB.Bson;
using MongoDB.Driver;
using Music.Controllers;
using Music.Data;
using Music.Models;
using System.Text.RegularExpressions;

namespace Music.Services;

public sealed class CatalogueService(MongoDbContext db, MutationGate gate, MediaCommands media, AzureBlobService storage)
{
    private async Task<T> Transaction<T>(Func<IClientSessionHandle, CancellationToken, Task<T>> command, CancellationToken ct)
    {
        using var session = await db.Database.Client.StartSessionAsync(cancellationToken: ct);
        return await session.WithTransactionAsync(command, new TransactionOptions(readConcern: ReadConcern.Snapshot, writeConcern: WriteConcern.WMajority), ct);
    }
    private async Task ReindexAlbum(IClientSessionHandle session, string? id, CancellationToken ct)
    {
        if (id is null) return;
        var songs = await db.Songs.Find(session, x => x.Album != null && x.Album.Id == id).SortBy(x => x.AlbumPosition).ThenBy(x => x.Id).Limit(500).ToListAsync(ct);
        for (var i = 0; i < songs.Count; i++)
            if (songs[i].AlbumPosition != i) await db.Songs.UpdateOneAsync(session, x => x.Id == songs[i].Id, Builders<Song>.Update.Set(x => x.AlbumPosition, i), cancellationToken: ct);
    }
    public async Task<Artist> Artist(string id, CancellationToken ct)
    {
        DemoRules.Id(id);
        var artist = await db.Artists.Find(x => x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Artist not found.");
        artist.Albums = (await db.Albums.Find(x => x.Artist != null && x.Artist.Id == id).SortBy(x => x.CreatedAt).Limit(500).ToListAsync(ct)).Select(x => new AlbumReference { Id = x.Id, Title = x.Title }).ToList();
        return artist;
    }
    public async Task<Album> Album(string id, CancellationToken ct)
    {
        DemoRules.Id(id);
        var album = await db.Albums.Find(x => x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Album not found.");
        if (album.Artist is not null) album.Artist = await ArtistRef(album.Artist.Id, ct);
        album.Songs = (await db.Songs.Find(x => x.Album != null && x.Album.Id == id).SortBy(x => x.AlbumPosition).ThenBy(x => x.Id).Limit(500).ToListAsync(ct)).Select((x, index) => new SongReference { Id = x.Id, Position = index, AddedAt = x.CreatedAt }).ToList();
        return album;
    }
    public async Task<Song> Song(string id, CancellationToken ct)
    {
        DemoRules.Id(id);
        var song = await db.Songs.Find(x => x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Song not found.");
        await Hydrate([song], ct);
        return song;
    }
    public async Task Hydrate(List<Song> songs, CancellationToken ct)
    {
        var artistIds = songs.SelectMany(x => x.Artists).Select(x => x.Id).Distinct().ToList();
        var albumIds = songs.Where(x => x.Album is not null).Select(x => x.Album!.Id).Distinct().ToList();
        var artists = (await db.Artists.Find(Builders<Artist>.Filter.In(x => x.Id, artistIds)).ToListAsync(ct)).ToDictionary(x => x.Id);
        var albums = (await db.Albums.Find(Builders<Album>.Filter.In(x => x.Id, albumIds)).ToListAsync(ct)).ToDictionary(x => x.Id);
        foreach (var song in songs)
        {
            song.Artists = song.Artists.Where(x => artists.ContainsKey(x.Id)).Select(x => new ArtistReference { Id = x.Id, Name = artists[x.Id].Name }).ToList();
            if (song.Album is not null) song.Album = albums.TryGetValue(song.Album.Id, out var album) ? new AlbumReference { Id = album.Id, Title = album.Title } : null;
        }
    }
    public async Task<List<Song>> Songs(FilterDefinition<Song>? filter, int? limit, int skip, CancellationToken ct, bool albumOrder = false)
    {
        var effective = DemoRules.Page(limit, skip);
        var order = albumOrder ? Builders<Song>.Sort.Ascending(x => x.AlbumPosition).Ascending(x => x.Id) : Builders<Song>.Sort.Ascending(x => x.Title).Ascending(x => x.Id);
        var songs = await db.Songs.Find(filter ?? Builders<Song>.Filter.Empty, new FindOptions { MaxTime = TimeSpan.FromSeconds(3) }).Sort(order).Skip(skip).Limit(effective).ToListAsync(ct);
        await Hydrate(songs, ct);
        return songs;
    }
    public async Task<List<Artist>> Artists(int? limit, int skip, CancellationToken ct)
    {
        var artists = await db.Artists.Find(Builders<Artist>.Filter.Empty).SortBy(x => x.Name).ThenBy(x => x.Id).Skip(skip).Limit(DemoRules.Page(limit, skip)).ToListAsync(ct);
        var albums = await db.Albums.Find(Builders<Album>.Filter.In("artist.id", artists.Select(x => x.Id))).Limit(10000).ToListAsync(ct);
        foreach (var artist in artists) artist.Albums = albums.Where(x => x.Artist?.Id == artist.Id).Select(x => new AlbumReference { Id = x.Id, Title = x.Title }).ToList();
        return artists;
    }
    public async Task<List<Album>> Albums(FilterDefinition<Album>? filter, int? limit, int skip, CancellationToken ct)
    {
        var albums = await db.Albums.Find(filter ?? Builders<Album>.Filter.Empty).SortBy(x => x.Title).ThenBy(x => x.Id).Skip(skip).Limit(DemoRules.Page(limit, skip)).ToListAsync(ct);
        var ids = albums.Where(x => x.Artist != null).Select(x => x.Artist!.Id).Distinct().ToList();
        var artists = (await db.Artists.Find(Builders<Artist>.Filter.In(x => x.Id, ids)).ToListAsync(ct)).ToDictionary(x => x.Id);
        var songs = await db.Songs.Find(Builders<Song>.Filter.In("album.id", albums.Select(x => x.Id))).Project(x => new { x.Id, x.Album, x.AlbumPosition, x.CreatedAt }).Limit(50000).ToListAsync(ct);
        foreach (var album in albums) if (album.Artist is not null && artists.TryGetValue(album.Artist.Id, out var artist)) album.Artist.Name = artist.Name;
        foreach (var album in albums) album.Songs = songs.Where(x => x.Album?.Id == album.Id).OrderBy(x => x.AlbumPosition).ThenBy(x => x.Id).Select((x, i) => new SongReference { Id = x.Id, Position = i, AddedAt = x.CreatedAt }).ToList();
        return albums;
    }
    private async Task<ArtistReference> ArtistRef(string id, CancellationToken ct)
    {
        DemoRules.Id(id);
        var artist = await db.Artists.Find(x => x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(400, "Referenced artist does not exist.");
        return new() { Id = artist.Id, Name = artist.Name };
    }
    private async Task<List<ArtistReference>> ArtistRefs(IEnumerable<string> ids, CancellationToken ct)
    {
        var values = ids.Distinct().ToList();
        if (values.Count is < 1 or > 10) throw new ApiException(400, "A song requires 1..10 distinct artists.");
        var refs = new List<ArtistReference>();
        foreach (var id in values) refs.Add(await ArtistRef(id, ct));
        return refs;
    }
    private async Task<AlbumReference?> AlbumRef(string? id, List<ArtistReference> artists, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(id)) return null;
        DemoRules.Id(id);
        var album = await db.Albums.Find(x => x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(400, "Referenced album does not exist.");
        if (album.Artist is null || !artists.Any(x => x.Id == album.Artist.Id)) throw new ApiException(400, "Album artist must be among the song artists.");
        return new() { Id = album.Id, Title = album.Title };
    }
    private async Task<string?> MediaUrl(string? url, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        if (!storage.TryResolve(url, out var target) || !await media.Referenced(target.Url, null, ct)) throw new ApiException(400, "Media URLs must identify existing permitted catalogue uploads.");
        return target.Url;
    }
    public Task<Artist> SaveArtist(string? id, string? name, string? bio, IFormFile? file, string? imageUrl, CancellationToken ct) => gate.Run(async () =>
    {
        var artist = id is null ? new Artist() : await db.Artists.Find(x => x.Id == DemoRules.Id(id)).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Artist not found.");
        if (name is not null || id is null) artist.Name = DemoRules.Text(name, "Name", 200);
        if (bio is not null) artist.Bio = DemoRules.Text(bio, "Bio", 5000, false);
        if (imageUrl is not null) artist.ImageUrl = await MediaUrl(imageUrl, ct);
        artist.ImageUrl = await media.Stage("artists", artist.Id, "profilePicture", file, true, artist.ImageUrl, ct);
        if (id is null) await db.Artists.InsertOneAsync(artist, cancellationToken: ct);
        else await db.Artists.ReplaceOneAsync(x => x.Id == id, artist, cancellationToken: ct);
        return artist;
    }, ct);
    public Task<Album> SaveAlbum(string? id, string? title, string? artistId, DateTime? releaseDate, IFormFile? file, string? coverUrl, CancellationToken ct) => gate.Run(async () =>
    {
        var album = id is null ? new Album() : await db.Albums.Find(x => x.Id == DemoRules.Id(id)).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Album not found.");
        if (title is not null || id is null) album.Title = DemoRules.Text(title, "Title", 200);
        if (artistId is not null || id is null) album.Artist = await ArtistRef(artistId ?? "", ct);
        if (id is null && await db.Albums.CountDocumentsAsync(x => x.Artist != null && x.Artist.Id == album.Artist!.Id, cancellationToken: ct) >= 100) throw new ApiException(400, "Artists support at most 100 albums.");
        if (id is not null && artistId is not null && await db.Songs.Find(x => x.Album != null && x.Album.Id == id && !x.Artists.Any(a => a.Id == artistId)).AnyAsync(ct)) throw new ApiException(409, "Existing album songs must include the new artist.");
        if (releaseDate.HasValue) album.ReleaseDate = releaseDate;
        if (coverUrl is not null) album.CoverUrl = await MediaUrl(coverUrl, ct);
        album.CoverUrl = await media.Stage("albums", album.Id, "cover", file, true, album.CoverUrl, ct);
        if (id is null) await db.Albums.InsertOneAsync(album, cancellationToken: ct);
        else await db.Albums.ReplaceOneAsync(x => x.Id == id, album, cancellationToken: ct);
        return album;
    }, ct);
    public Task<Song> SaveSong(string? id, UpdateSongDto request, IFormFile? audio, IFormFile? cover, IFormFile? snippet, CancellationToken ct, string? creationId = null) => gate.Run(async () =>
    {
        if (id is null && creationId is not null)
        {
            var replay = await db.Songs.Find(x => x.Id == DemoRules.Id(creationId)).FirstOrDefaultAsync(ct);
            if (replay is not null) return replay;
        }
        var song = id is null ? new Song() : await db.Songs.Find(x => x.Id == DemoRules.Id(id)).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Song not found.");
        if (id is null && creationId is not null) song.Id = creationId;
        var previousAlbum = song.Album?.Id;
        if (request.Title is not null || id is null) song.Title = DemoRules.Text(request.Title, "Title", 200);
        if (request.Genre is not null) song.Genre = DemoRules.Text(request.Genre, "Genre", 100, false);
        if (request.DurationSec is not null || id is null) { DemoRules.Duration(request.DurationSec ?? 0); song.DurationSec = request.DurationSec ?? 0; }
        if (request.Artists is not null || id is null) song.Artists = await ArtistRefs(request.Artists?.Select(x => x.Id) ?? [], ct);
        if (request.Album is not null || id is null) song.Album = await AlbumRef(request.Album?.Id, song.Artists, ct);
        else if (song.Album is not null) song.Album = await AlbumRef(song.Album.Id, song.Artists, ct);
        if (request.ReleaseDate.HasValue) song.ReleaseDate = request.ReleaseDate;
        if (request.FileUrl is not null) song.FileUrl = await MediaUrl(request.FileUrl, ct) ?? "";
        if (request.CoverUrl is not null) song.CoverUrl = await MediaUrl(request.CoverUrl, ct) ?? "";
        if (request.SnippetUrl is not null) song.SnippetUrl = await MediaUrl(request.SnippetUrl, ct);
        if (song.Album is not null && (id is null || song.Album.Id != previousAlbum))
        {
            song.AlbumPosition = checked((int)await db.Songs.CountDocumentsAsync(x => x.Album != null && x.Album.Id == song.Album.Id, cancellationToken: ct));
            if (song.AlbumPosition >= 500) throw new ApiException(400, "Albums support at most 500 songs.");
        }
        song.FileUrl = await media.Stage("songs", song.Id, "song", audio, false, song.FileUrl, ct) ?? "";
        song.CoverUrl = await media.Stage("songs", song.Id, "cover", cover, true, song.CoverUrl, ct) ?? "";
        song.SnippetUrl = await media.Stage("songs", song.Id, "snippet", snippet, false, song.SnippetUrl, ct);
        await Transaction(async (session, transactionCt) =>
        {
            if (id is null) await db.Songs.InsertOneAsync(session, song, cancellationToken: transactionCt);
            else await db.Songs.ReplaceOneAsync(session, x => x.Id == id, song, cancellationToken: transactionCt);
            if (previousAlbum != song.Album?.Id) await ReindexAlbum(session, previousAlbum, transactionCt);
            await ReindexAlbum(session, song.Album?.Id, transactionCt);
            return true;
        }, ct);
        return song;
    }, ct);
    public Task<bool> DeleteArtist(string id, CancellationToken ct) => gate.Run(async () =>
    {
        var artist = await Artist(id, ct);
        if (await db.Albums.Find(x => x.Artist != null && x.Artist.Id == id).AnyAsync(ct) || await db.Songs.Find(x => x.Artists.Any(a => a.Id == id)).AnyAsync(ct)) throw new ApiException(409, "Remove this artist's songs and albums first.");
        await media.Queue(artist.ImageUrl, ct);
        await db.Artists.DeleteOneAsync(x => x.Id == id, ct);
        return true;
    }, ct);
    public Task<bool> DeleteAlbum(string id, CancellationToken ct) => gate.Run(async () =>
    {
        var album = await Album(id, ct);
        if (await db.Songs.Find(x => x.Album != null && x.Album.Id == id).AnyAsync(ct)) throw new ApiException(409, "Remove the album's songs first.");
        await media.Queue(album.CoverUrl, ct);
        await db.Albums.DeleteOneAsync(x => x.Id == id, ct);
        return true;
    }, ct);
    public Task<bool> DeleteSong(string id, CancellationToken ct) => gate.Run(async () =>
    {
        var song = await Song(id, ct);
        foreach (var url in new[] { song.FileUrl, song.CoverUrl, song.SnippetUrl }) await media.Queue(url, ct);
        // Atomic pipeline removes references and restores contiguous playlist positions.
        var pipeline = new[] { new BsonDocument("$set", new BsonDocument("songs", new BsonDocument("$filter", new BsonDocument { { "input", "$songs" }, { "as", "s" }, { "cond", new BsonDocument("$ne", new BsonArray { "$$s.id", id }) } }))),
            new BsonDocument("$set", new BsonDocument { { "songs", new BsonDocument("$map", new BsonDocument { { "input", new BsonDocument("$range", new BsonArray { 0, new BsonDocument("$size", "$songs") }) }, { "as", "i" }, { "in", new BsonDocument("$mergeObjects", new BsonArray { new BsonDocument("$arrayElemAt", new BsonArray { "$songs", "$$i" }), new BsonDocument("position", "$$i") }) } }) }, { "updatedAt", DateTime.UtcNow }, { "version", new BsonDocument("$add", new BsonArray { new BsonDocument("$ifNull", new BsonArray { "$version", 0 }), 1 }) } }) };
        await Transaction(async (session, transactionCt) =>
        {
            await db.Playlists.UpdateManyAsync(session, Builders<Playlist>.Filter.ElemMatch(x => x.Songs, x => x.Id == id), new PipelineUpdateDefinition<Playlist>(pipeline), cancellationToken: transactionCt);
            await db.Songs.DeleteOneAsync(session, x => x.Id == id, cancellationToken: transactionCt);
            await ReindexAlbum(session, song.Album?.Id, transactionCt);
            return true;
        }, ct);
        return true;
    }, ct);
    public Task<bool> AddAlbumSong(string albumId, string songId, int position, CancellationToken ct) => gate.Run(async () =>
    {
        var album = await Album(albumId, ct); var song = await Song(songId, ct);
        if (song.Album?.Id == albumId) throw new ApiException(409, "Song is already in the album.");
        if (song.Album is not null) throw new ApiException(409, "Song already belongs to another album; edit its album first.");
        if (album.Artist is null || !song.Artists.Any(x => x.Id == album.Artist.Id)) throw new ApiException(400, "Album artist must be among the song artists.");
        var songs = await db.Songs.Find(x => x.Album != null && x.Album.Id == albumId).SortBy(x => x.AlbumPosition).ThenBy(x => x.Id).Limit(501).ToListAsync(ct);
        if (songs.Count >= 500) throw new ApiException(400, "Albums support at most 500 songs.");
        if (position == -1) position = songs.Count;
        if (position < 0 || position > songs.Count) throw new ApiException(400, "Invalid song position.");
        songs.Insert(position, song);
        await Transaction(async (session, transactionCt) =>
        {
            for (var i = songs.Count - 1; i >= 0; i--) await db.Songs.UpdateOneAsync(session, x => x.Id == songs[i].Id, Builders<Song>.Update.Set(x => x.AlbumPosition, i).Set(x => x.Album, new AlbumReference { Id = album.Id, Title = album.Title }), cancellationToken: transactionCt);
            return true;
        }, ct);
        return true;
    }, ct);
    public async Task<object> Search(string? query, CancellationToken ct)
    {
        var q = DemoRules.Text(query, "Search", 100, false);
        if (q.Length == 0) return new { songs = Array.Empty<Song>(), albums = Array.Empty<Album>(), artists = Array.Empty<Artist>() };
        var regex = new BsonRegularExpression(Regex.Escape(q), "i");
        var artists = await db.Artists.Find(Builders<Artist>.Filter.Or(Builders<Artist>.Filter.Regex(x => x.Name, regex), Builders<Artist>.Filter.Regex(x => x.Bio, regex)), new FindOptions { MaxTime = TimeSpan.FromSeconds(2) }).SortBy(x => x.Name).Limit(20).ToListAsync(ct);
        var artistIds = artists.Select(x => x.Id).ToList();
        var albums = await db.Albums.Find(Builders<Album>.Filter.Or(Builders<Album>.Filter.Regex(x => x.Title, regex), Builders<Album>.Filter.In("artist.id", artistIds)), new FindOptions { MaxTime = TimeSpan.FromSeconds(2) }).SortBy(x => x.Title).Limit(20).ToListAsync(ct);
        var songs = await Songs(Builders<Song>.Filter.Or(Builders<Song>.Filter.Regex(x => x.Title, regex), Builders<Song>.Filter.Regex(x => x.Genre, regex), Builders<Song>.Filter.In("artists.id", artistIds), Builders<Song>.Filter.In("album.id", albums.Select(x => x.Id))), 20, 0, ct);
        return new { songs, albums, artists };
    }
}
