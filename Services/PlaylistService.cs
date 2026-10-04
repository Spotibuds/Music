using MongoDB.Driver;
using Music.Authorization;
using Music.Controllers;
using Music.Data;
using Music.Models;
using System.Security.Claims;

namespace Music.Services;

public sealed class PlaylistService(MongoDbContext db, MutationGate gate, MediaCommands media, CatalogueService catalogue)
{
    public Task<bool> DeleteOwner(string owner, CancellationToken ct) => gate.Run(async () =>
    {
        if (!Guid.TryParse(owner, out _)) throw new ApiException(400, "Playlist owners use Identity GUIDs.");
        var entries = await db.Playlists.Find(x => x.CreatedBy == owner).Limit(101).ToListAsync(ct);
        foreach (var entry in entries) await media.Queue(entry.CoverUrl, ct);
        await db.Playlists.DeleteManyAsync(x => x.CreatedBy == owner, ct);
        return true;
    }, ct);
    private static bool CanRead(Playlist p, ClaimsPrincipal viewer) => p.IsPublic || PlaylistAccessPolicy.CanManage(p.CreatedBy, viewer);
    private async Task<Playlist> Find(string id, ClaimsPrincipal viewer, bool mutation, CancellationToken ct)
    {
        DemoRules.Id(id);
        var playlist = await db.Playlists.Find(x => x.Id == id).FirstOrDefaultAsync(ct) ?? throw new ApiException(404, "Playlist not found.");
        if (!CanRead(playlist, viewer)) throw new ApiException(404, "Playlist not found.");
        if (mutation && !PlaylistAccessPolicy.CanManage(playlist.CreatedBy, viewer)) throw new ApiException(403, "Only the playlist owner or an administrator may change it.");
        return playlist;
    }
    public async Task<object> Detail(string id, ClaimsPrincipal viewer, CancellationToken ct) => (await Expand([await Find(id, viewer, false, ct)], false, ct)).Single();
    public async Task<List<object>> List(string? owner, ClaimsPrincipal viewer, int? limit, int skip, CancellationToken ct)
    {
        if (owner is not null && !Guid.TryParse(owner, out _)) throw new ApiException(400, "Playlist owners use Identity GUIDs.");
        var filter = Builders<Playlist>.Filter.Empty;
        if (owner is not null) filter &= Builders<Playlist>.Filter.Eq(x => x.CreatedBy, owner);
        if (!viewer.IsInRole("Admin"))
        {
            var actor = viewer.FindFirstValue(ClaimTypes.NameIdentifier) ?? viewer.FindFirstValue("sub");
            filter &= Builders<Playlist>.Filter.Or(Builders<Playlist>.Filter.Eq(x => x.IsPublic, true), Builders<Playlist>.Filter.Eq(x => x.CreatedBy, actor));
        }
        var entries = await db.Playlists.Find(filter).SortByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip(skip).Limit(DemoRules.Page(limit, skip)).ToListAsync(ct);
        return await Expand(entries, true, ct);
    }
    private async Task<List<object>> Expand(List<Playlist> playlists, bool summary, CancellationToken ct)
    {
        var references = playlists.SelectMany(x => summary ? x.Songs.OrderBy(s => s.Position).Take(20) : x.Songs).Select(x => x.Id).Distinct().ToList();
        var songs = await db.Songs.Find(Builders<Song>.Filter.In(x => x.Id, references)).ToListAsync(ct);
        await catalogue.Hydrate(songs, ct);
        var byId = songs.ToDictionary(x => x.Id);
        return playlists.Select(p => (object)new
        {
            p.Id, p.Name, p.Description, p.CreatedBy, p.CoverUrl, p.CreatedAt, p.UpdatedAt, p.IsPublic, songCount = p.Songs.Count,
            songs = (summary ? p.Songs.OrderBy(x => x.Position).Take(20) : p.Songs.OrderBy(x => x.Position)).Where(x => byId.ContainsKey(x.Id)).Select((r, i) =>
            {
                var s = byId[r.Id];
                return new { s.Id, s.Title, s.Artists, s.Genre, s.DurationSec, s.Album, s.FileUrl, s.CoverUrl, s.SnippetUrl, s.CreatedAt, s.ReleaseDate, position = i, r.AddedAt };
            }).ToList()
        }).ToList();
    }
    public Task<Playlist> Create(CreatePlaylistDto dto, ClaimsPrincipal viewer, string? expectedOwner, CancellationToken ct) => gate.Run(async () =>
    {
        var owner = DemoRules.Actor(viewer);
        if (expectedOwner is not null && owner != expectedOwner) throw new ApiException(403, "You may create playlists only for your own account.");
        // Case-insensitive liked-list creation is idempotent so concurrent first likes cannot create two lists.
        var name = DemoRules.Text(dto.Name, "Name", 200);
        if (name.Equals("Liked Songs", StringComparison.OrdinalIgnoreCase))
        {
            var liked = await db.Playlists.Find(x => x.CreatedBy == owner && x.Name == "Liked Songs").FirstOrDefaultAsync(ct);
            if (liked is not null) return liked;
            name = "Liked Songs";
        }
        if (await db.Playlists.CountDocumentsAsync(x => x.CreatedBy == owner, cancellationToken: ct) >= 100) throw new ApiException(400, "An account supports at most 100 playlists.");
        var playlist = new Playlist { Name = name, Description = DemoRules.Text(dto.Description, "Description", 2000, false), CreatedBy = owner, IsPublic = dto.IsPublic };
        await db.Playlists.InsertOneAsync(playlist, cancellationToken: ct);
        return playlist;
    }, ct);
    private async Task Persist(Playlist playlist, long version, CancellationToken ct)
    {
        for (var i = 0; i < playlist.Songs.Count; i++) playlist.Songs[i].Position = i;
        playlist.Version = version + 1; playlist.UpdatedAt = DateTime.UtcNow;
        var filter = Builders<Playlist>.Filter.Eq(x => x.Id, playlist.Id) & (version == 0 ? Builders<Playlist>.Filter.Or(Builders<Playlist>.Filter.Eq(x => x.Version, 0), Builders<Playlist>.Filter.Exists("version", false)) : Builders<Playlist>.Filter.Eq(x => x.Version, version));
        var result = await db.Playlists.ReplaceOneAsync(filter, playlist, cancellationToken: ct);
        if (result.MatchedCount == 0) throw new ApiException(409, "Playlist changed concurrently. Refresh and retry.");
    }
    public Task<bool> Update(string id, UpdatePlaylistDto dto, ClaimsPrincipal viewer, CancellationToken ct) => gate.Run(async () =>
    {
        var p = await Find(id, viewer, true, ct); var version = p.Version;
        if (dto.Name is not null)
        {
            var name = DemoRules.Text(dto.Name, "Name", 200);
            if (p.Name == "Liked Songs" && name != "Liked Songs") throw new ApiException(400, "Liked Songs keeps its name so your favorites stay together.");
            if (p.Name != "Liked Songs" && name.Equals("Liked Songs", StringComparison.OrdinalIgnoreCase)) throw new ApiException(400, "Liked Songs is reserved for your favorites.");
            p.Name = name;
        }
        if (dto.Description is not null) p.Description = DemoRules.Text(dto.Description, "Description", 2000, false);
        if (dto.IsPublic.HasValue) p.IsPublic = dto.IsPublic.Value;
        await Persist(p, version, ct); return true;
    }, ct);
    public Task<bool> Add(string id, string songId, int position, ClaimsPrincipal viewer, CancellationToken ct) => gate.Run(async () =>
    {
        DemoRules.Id(songId);
        var p = await Find(id, viewer, true, ct); var version = p.Version;
        if (!await db.Songs.Find(x => x.Id == songId).AnyAsync(ct)) throw new ApiException(404, "Song not found.");
        if (p.Songs.Any(x => x.Id == songId)) throw new ApiException(409, "Song is already in the playlist.");
        if (p.Songs.Count >= 500) throw new ApiException(400, "Playlists support at most 500 songs.");
        p.Songs = p.Songs.OrderBy(x => x.Position).ToList();
        if (position == -1) position = p.Songs.Count;
        if (position < 0 || position > p.Songs.Count) throw new ApiException(400, "Invalid song position.");
        p.Songs.Insert(position, new SongReference { Id = songId, AddedAt = DateTime.UtcNow });
        await Persist(p, version, ct); return true;
    }, ct);
    public Task<bool> Remove(string id, string songId, ClaimsPrincipal viewer, CancellationToken ct) => gate.Run(async () =>
    {
        DemoRules.Id(songId);
        var p = await Find(id, viewer, true, ct); var version = p.Version;
        if (!p.Songs.Any(x => x.Id == songId)) throw new ApiException(404, "Song is not in the playlist.");
        p.Songs = p.Songs.OrderBy(x => x.Position).Where(x => x.Id != songId).ToList();
        await Persist(p, version, ct); return true;
    }, ct);
    public Task<bool> Reorder(string id, List<string> ids, ClaimsPrincipal viewer, CancellationToken ct) => gate.Run(async () =>
    {
        var p = await Find(id, viewer, true, ct); var version = p.Version;
        if (ids.Count != p.Songs.Count || ids.Distinct().Count() != ids.Count || !ids.ToHashSet().SetEquals(p.Songs.Select(x => x.Id))) throw new ApiException(400, "SongIds must be an exact permutation of the playlist songs.");
        var refs = p.Songs.ToDictionary(x => x.Id);
        p.Songs = ids.Select(x => refs[x]).ToList();
        await Persist(p, version, ct); return true;
    }, ct);
    public Task<string?> Cover(string id, IFormFile? file, bool remove, ClaimsPrincipal viewer, CancellationToken ct) => gate.Run(async () =>
    {
        var p = await Find(id, viewer, true, ct); var version = p.Version;
        if (remove) { await media.Queue(p.CoverUrl, ct); p.CoverUrl = null; }
        else
        {
            if (file is null || file.Length == 0) throw new ApiException(400, "Cover image is required.");
            p.CoverUrl = await media.Stage("playlists", p.Id, "cover", file, true, p.CoverUrl, ct);
        }
        await Persist(p, version, ct); return p.CoverUrl;
    }, ct);
    public Task<bool> Delete(string id, ClaimsPrincipal viewer, CancellationToken ct) => gate.Run(async () =>
    {
        var p = await Find(id, viewer, true, ct);
        await media.Queue(p.CoverUrl, ct);
        await db.Playlists.DeleteOneAsync(x => x.Id == id, ct); return true;
    }, ct);
}
