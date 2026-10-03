using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using MongoDB.Bson;
using MongoDB.Driver;
using Music.Data;
using Music.Models;
using Music.Services;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Music.Controllers;

[ApiController, Route("api/admin"), Authorize(Policy = "AdminOnly")]
public sealed class AdminController(CatalogueService catalogue, PlaylistService playlists, MongoDbContext db) : ControllerBase
{
    private CancellationToken Ct => HttpContext.RequestAborted;
    [HttpGet("artists")] public async Task<IActionResult> Artists(int? limit = null, int skip = 0) => Ok(await catalogue.Artists(limit, skip, Ct));
    [HttpGet("albums")] public async Task<IActionResult> Albums(int? limit = null, int skip = 0) => Ok(await catalogue.Albums(null, limit, skip, Ct));
    [HttpGet("songs")] public async Task<IActionResult> Songs(int? limit = null, int skip = 0) => Ok(await catalogue.Songs(null, limit, skip, Ct));
    [HttpGet("artists/{id}")] public async Task<IActionResult> Artist(string id) => Ok(await catalogue.Artist(id, Ct));
    [HttpGet("albums/{id}")] public async Task<IActionResult> Album(string id) => Ok(await catalogue.Album(id, Ct));
    [HttpGet("songs/{id}")] public async Task<IActionResult> Song(string id) => Ok(await catalogue.Song(id, Ct));
    [HttpPost("artists")] public async Task<IActionResult> CreateArtist([FromForm] CreateArtistRequest request)
    { var a = await catalogue.SaveArtist(null, request.Name, request.Bio, request.ImageFile, null, Ct); return Created($"/api/artists/{a.Id}", a); }
    [HttpPut("artists/{id}")] public async Task<IActionResult> UpdateArtist(string id, [FromForm] UpdateArtistRequest request) => Ok(await catalogue.SaveArtist(id, request.Name, request.Bio, request.ImageFile, null, Ct));
    [HttpDelete("artists/{id}")] public async Task<IActionResult> DeleteArtist(string id) { await catalogue.DeleteArtist(id, Ct); return NoContent(); }
    [HttpPost("albums")] public async Task<IActionResult> CreateAlbum([FromForm] CreateAlbumRequest request)
    { var a = await catalogue.SaveAlbum(null, request.Title, request.ArtistId, request.ReleaseDate, request.CoverFile, null, Ct); return Created($"/api/albums/{a.Id}", a); }
    [HttpPut("albums/{id}")] public async Task<IActionResult> UpdateAlbum(string id, [FromForm] CreateAlbumRequest request) => Ok(await catalogue.SaveAlbum(id, request.Title, request.ArtistId, request.ReleaseDate, request.CoverFile, null, Ct));
    [HttpDelete("albums/{id}")] public async Task<IActionResult> DeleteAlbum(string id) { await catalogue.DeleteAlbum(id, Ct); return NoContent(); }
    [HttpPost("songs")] public async Task<IActionResult> CreateSong([FromForm] CreateSongRequest request)
    {
        if (request.AudioFile is null || request.AudioFile.Length == 0) throw new ApiException(400, "AudioFile is required.");
        var s = await catalogue.SaveSong(null, SongRequest(request.Title, request.ArtistId, request.AlbumId, request.Genre, request.Duration), request.AudioFile, request.CoverFile, request.SnippetFile, Ct);
        return Created($"/api/songs/{s.Id}", s);
    }
    [HttpPut("songs/{id}")] public async Task<IActionResult> UpdateSong(string id, [FromForm] UpdateSongRequest request) => Ok(await catalogue.SaveSong(id, SongRequest(request.Title, request.ArtistId, request.AlbumId, request.Genre, request.Duration), request.AudioFile, request.CoverFile, request.SnippetFile, Ct));
    private static UpdateSongDto SongRequest(string? title, string? artistId, string? albumId, string? genre, int? duration) => new()
    { Title = title, Artists = artistId is null ? null : [new ArtistReference { Id = artistId }], Album = albumId is null ? null : new AlbumReference { Id = albumId }, Genre = genre, DurationSec = duration };
    [HttpDelete("songs/{id}")] public async Task<IActionResult> DeleteSong(string id) { await catalogue.DeleteSong(id, Ct); return NoContent(); }
    [HttpGet("playlists")] public async Task<IActionResult> Playlists(int? limit = null, int skip = 0) => Ok(await playlists.List(null, User, limit, skip, Ct));
    [HttpGet("playlists/{id}")] public async Task<IActionResult> Playlist(string id) => Ok(await playlists.Detail(id, User, Ct));
    [HttpPost("playlists")] public async Task<IActionResult> CreatePlaylist(CreatePlaylistDto request) { var p = await playlists.Create(request, User, null, Ct); return Created($"/api/playlists/{p.Id}", p); }
    [HttpPost("playlists/{playlistId}/songs")] public async Task<IActionResult> AddSong(string playlistId, AddSongToPlaylistRequest request) { await playlists.Add(playlistId, request.SongId, request.Position ?? -1, User, Ct); return Ok(); }
    [HttpDelete("playlists/{playlistId}/songs/{songId}")] public async Task<IActionResult> RemoveSong(string playlistId, string songId) { await playlists.Remove(playlistId, songId, User, Ct); return Ok(); }
    [HttpDelete("playlists/{id}")] public async Task<IActionResult> DeletePlaylist(string id) { await playlists.Delete(id, User, Ct); return NoContent(); }
    [HttpGet("search"), EnableRateLimiting("search")] public async Task<IActionResult> Search(string? query, string? type = null)
    {
        if (type is not (null or "artists" or "albums" or "songs" or "playlists")) throw new ApiException(400, "Unknown search type.");
        return Ok(await catalogue.Search(query, Ct));
    }
    [HttpGet("stats")] public async Task<IActionResult> Stats()
    {
        var recent = DateTime.UtcNow.AddDays(-7);
        return Ok(new
        {
            totalArtists = await db.Artists.CountDocumentsAsync(Builders<Artist>.Filter.Empty, cancellationToken: Ct),
            totalAlbums = await db.Albums.CountDocumentsAsync(Builders<Album>.Filter.Empty, cancellationToken: Ct),
            totalSongs = await db.Songs.CountDocumentsAsync(Builders<Song>.Filter.Empty, cancellationToken: Ct),
            totalPlaylists = await db.Playlists.CountDocumentsAsync(Builders<Playlist>.Filter.Empty, cancellationToken: Ct),
            recentArtists = await db.Artists.CountDocumentsAsync(x => x.CreatedAt >= recent, cancellationToken: Ct),
            recentAlbums = await db.Albums.CountDocumentsAsync(x => x.CreatedAt >= recent, cancellationToken: Ct),
            recentSongs = await db.Songs.CountDocumentsAsync(x => x.CreatedAt >= recent, cancellationToken: Ct),
            recentPlaylists = await db.Playlists.CountDocumentsAsync(x => x.CreatedAt >= recent, cancellationToken: Ct)
        });
    }
    [HttpPost("bulk/songs"), RequestSizeLimit(150 * 1024 * 1024)]
    public async Task<IActionResult> Bulk([FromForm] BulkCreateSongsRequest request)
    {
        if (request.SongsData.Count is < 1 or > 20 || request.SongsData.Count != request.AudioFiles.Count || (request.CoverFiles is { Count: > 0 } && request.CoverFiles.Count != request.SongsData.Count)) throw new ApiException(400, "Provide 1..20 metadata rows and matching audio/optional cover file counts.");
        DemoRules.Id(request.ArtistId);
        if (!string.IsNullOrWhiteSpace(request.AlbumId)) DemoRules.Id(request.AlbumId);
        var key = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(key)) key = request.IdempotencyKey ?? Guid.NewGuid().ToString("N");
        if (key.Length > 100 || !System.Text.RegularExpressions.Regex.IsMatch(key, "^[A-Za-z0-9:_-]+$")) throw new ApiException(400, "Idempotency key must contain 1..100 letters, numbers, colon, underscore or hyphen.");
        var operationId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(DemoRules.Actor(User) + ":" + key))).ToLowerInvariant();
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(JsonSerializer.SerializeToUtf8Bytes(new { request.ArtistId, request.AlbumId, request.SongsData }));
        foreach (var file in request.AudioFiles.Concat(request.CoverFiles ?? []))
        {
            hash.AppendData(BitConverter.GetBytes(file.Length));
            await using var stream = file.OpenReadStream();
            hash.AppendData(await SHA256.HashDataAsync(stream, Ct));
        }
        var fingerprint = Convert.ToHexString(hash.GetHashAndReset());
        var operations = db.Database.GetCollection<BsonDocument>("bulkOperations");
        var operation = await operations.FindOneAndUpdateAsync(Builders<BsonDocument>.Filter.Eq("_id", operationId), Builders<BsonDocument>.Update.SetOnInsert("fingerprint", fingerprint).SetOnInsert("createdAt", DateTime.UtcNow), new FindOneAndUpdateOptions<BsonDocument> { IsUpsert = true, ReturnDocument = ReturnDocument.After }, Ct);
        if (operation["fingerprint"].AsString != fingerprint) throw new ApiException(409, "Idempotency key was already used for a different upload.");
        if (operation.TryGetValue("resultJson", out var completed)) return Ok(JsonSerializer.Deserialize<BulkUploadResult>(completed.AsString));
        var result = new BulkUploadResult { OperationId = key };
        for (var i = 0; i < request.SongsData.Count; i++)
        {
            var data = request.SongsData[i];
            var itemKey = operationId + ":" + i;
            var songId = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(itemKey)).AsSpan(0, 12)).ToLowerInvariant();
            try
            {
                var song = await catalogue.SaveSong(null, SongRequest(data.Title, request.ArtistId, request.AlbumId, data.Genre, data.Duration), request.AudioFiles[i], request.CoverFiles is { Count: > 0 } ? request.CoverFiles[i] : null, null, Ct, songId);
                result.CreatedSongs.Add(song); result.SuccessCount++;
                result.Items.Add(new(i, true, song.Id, null, itemKey));
            }
            catch (ApiException ex)
            { result.ErrorCount++; result.Errors.Add($"Item {i}: {ex.Message}"); result.Items.Add(new(i, false, null, ex.Message, itemKey)); }
            catch (Exception ex) when (DemoRules.DependencyFailure(ex))
            { result.ErrorCount++; result.Errors.Add($"Item {i}: dependency unavailable; retry this item."); result.Items.Add(new(i, false, null, "Dependency unavailable; retry this item.", itemKey)); }
        }
        if (result.ErrorCount == 0) await operations.UpdateOneAsync(Builders<BsonDocument>.Filter.Eq("_id", operationId), Builders<BsonDocument>.Update.Set("resultJson", JsonSerializer.Serialize(result)), cancellationToken: Ct);
        return Ok(result);
    }
    [HttpPost("repair")]
    public async Task<IActionResult> Repair()
    {
        long repaired = 0;
        foreach (var name in new[] { "artists", "albums", "songs" })
        {
            var result = await db.Database.GetCollection<BsonDocument>(name).UpdateManyAsync(Builders<BsonDocument>.Filter.Exists("placeholder"), Builders<BsonDocument>.Update.Unset("placeholder"), cancellationToken: Ct);
            repaired += result.ModifiedCount;
        }
        return Ok(new { repaired });
    }
}
