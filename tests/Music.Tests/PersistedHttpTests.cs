using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using MongoDB.Bson;
using MongoDB.Driver;
using Music.Data;
using Music.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Music.Tests;

public sealed class PersistedHttpTests
{
    [DependencyFact]
    public async Task MultipartEditsClearExplicitOptionalTextPreserveOmittedFieldsAndRejectRequiredBlanks()
    {
        using var factory = new DemoFactory(); using var admin = factory.Client(true); using var anonymous = factory.CreateClient();
        try
        {
            using var create = DemoFactory.Form(new() { ["Name"] = "Form Artist", ["Bio"] = "Retained biography" });
            var artist = await DemoFactory.Json(await admin.PostAsync("/api/admin/artists", create), HttpStatusCode.Created); var aid = artist.GetProperty("id").GetString()!;
            using var omitted = DemoFactory.Form(new() { ["Name"] = "Renamed Artist" });
            await DemoFactory.Json(await admin.PutAsync($"/api/admin/artists/{aid}", omitted), HttpStatusCode.OK);
            Assert.Equal("Retained biography", (await anonymous.GetFromJsonAsync<JsonElement>($"/api/artists/{aid}")).GetProperty("bio").GetString());
            foreach (var clear in new[] { "", "   " })
            {
                Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/artists/{aid}", new { bio = "Another biography" })).StatusCode);
                using var edit = DemoFactory.Form(new() { ["Name"] = "Renamed Artist", ["Bio"] = clear });
                await DemoFactory.Json(await admin.PutAsync($"/api/admin/artists/{aid}", edit), HttpStatusCode.OK);
                var persisted = await anonymous.GetFromJsonAsync<JsonElement>($"/api/artists/{aid}"); Assert.Equal("", persisted.GetProperty("bio").GetString()); Assert.Equal("Renamed Artist", persisted.GetProperty("name").GetString());
            }
            using var invalidArtist = DemoFactory.Form(new() { ["Name"] = "   ", ["Bio"] = "Should not save" });
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/artists/{aid}", invalidArtist)).StatusCode);
            var album = await DemoFactory.Json(await admin.PostAsJsonAsync("/api/albums", new { title = "Form Album", artist = new { id = aid } }), HttpStatusCode.Created); var albumId = album.GetProperty("id").GetString()!;
            using var songCreate = DemoFactory.Form(new() { ["Title"] = "Form Track", ["ArtistId"] = aid, ["AlbumId"] = albumId, ["Genre"] = "Original genre", ["Duration"] = "1" }, "AudioFile");
            var song = await DemoFactory.Json(await admin.PostAsync("/api/admin/songs", songCreate), HttpStatusCode.Created); var sid = song.GetProperty("id").GetString()!; var fileUrl = song.GetProperty("fileUrl").GetString()!;
            using var titleOnly = DemoFactory.Form(new() { ["Title"] = "Renamed Track" });
            await DemoFactory.Json(await admin.PutAsync($"/api/admin/songs/{sid}", titleOnly), HttpStatusCode.OK);
            Assert.Equal("Original genre", (await anonymous.GetFromJsonAsync<JsonElement>($"/api/songs/{sid}")).GetProperty("genre").GetString());
            using var songClear = DemoFactory.Form(new() { ["Genre"] = "", ["AlbumId"] = "" });
            await DemoFactory.Json(await admin.PutAsync($"/api/admin/songs/{sid}", songClear), HttpStatusCode.OK);
            var updated = await anonymous.GetFromJsonAsync<JsonElement>($"/api/songs/{sid}"); Assert.Equal("", updated.GetProperty("genre").GetString()); Assert.Equal(JsonValueKind.Null, updated.GetProperty("album").ValueKind); Assert.Equal(fileUrl, updated.GetProperty("fileUrl").GetString());
            Assert.Empty((await anonymous.GetFromJsonAsync<JsonElement>($"/api/albums/{albumId}")).GetProperty("songs").EnumerateArray());
            Assert.Equal(DemoFactory.Wave(), await anonymous.GetByteArrayAsync(new Uri(fileUrl).PathAndQuery));
            foreach (var field in new[] { "Title", "ArtistId" })
            {
                using var invalid = DemoFactory.Form(new() { [field] = "   ", ["Genre"] = "Should not save" });
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/songs/{sid}", invalid)).StatusCode);
            }
            var db = factory.Services.GetRequiredService<MongoDbContext>();
            var storedArtist = await db.Artists.Find(x => x.Id == aid).SingleAsync(); Assert.Equal("", storedArtist.Bio); Assert.Equal("Renamed Artist", storedArtist.Name);
            var storedSong = await db.Songs.Find(x => x.Id == sid).SingleAsync(); Assert.Equal("", storedSong.Genre); Assert.Equal("Renamed Track", storedSong.Title); Assert.Null(storedSong.Album); Assert.Equal(fileUrl, storedSong.FileUrl);
        }
        finally { await factory.Cleanup(); }
    }

    [DependencyFact]
    public async Task CatalogueCommandsAuthorizePersistRenameRelationshipsRepairAndDeleteThroughEveryRoute()
    {
        using var factory = new DemoFactory();
        using var admin = factory.Client(true); using var owner = factory.Client(); using var anonymous = factory.CreateClient();
        try
        {
            var artist = await DemoFactory.Json(await admin.PostAsJsonAsync("/api/artists", new { name = "First Artist", bio = "fixture" }), HttpStatusCode.Created);
            var aid = artist.GetProperty("id").GetString()!;
            var duplicateArtists = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => admin.PostAsJsonAsync("/api/artists", new { name = "FIRST ARTIST" })));
            Assert.All(duplicateArtists, response => Assert.Equal(HttpStatusCode.Conflict, response.StatusCode));
            var album = await DemoFactory.Json(await admin.PostAsJsonAsync("/api/albums", new { title = "Album", artist = new { id = aid, name = "untrusted-name" } }), HttpStatusCode.Created);
            var albumId = album.GetProperty("id").GetString()!;
            var song = await DemoFactory.Json(await admin.PostAsJsonAsync("/api/songs", new { title = "First Track", artists = new[] { new { id = aid } }, durationSec = 1, genre = "Demo", album = new { id = albumId } }), HttpStatusCode.Created);
            var sid = song.GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PutAsJsonAsync($"/api/artists/{aid}", new { name = "Unauthorized" })).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/songs/{sid}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/songs/not-an-object-id")).StatusCode);
            foreach (var suffix in new[] { "limit=0", "limit=-1", "skip=-1", "limit=101" }) Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/songs?" + suffix)).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/songs", new { title = "", durationSec = -5, fileUrl = "javascript:invalid", artists = new[] { new { id = aid } } })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsJsonAsync($"/api/songs/{sid}", new { fileUrl = "https://attacker.invalid/private/blob.wav" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/api/albums", new { title = "Invalid", artist = new { id = ObjectId.GenerateNewId().ToString() } })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/artists/{aid}", new { name = "Renamed Artist", bio = "" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/albums/{albumId}", new { title = "Renamed Album" })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.PutAsJsonAsync($"/api/songs/{sid}", new { title = "Renamed Track" })).StatusCode);
            var albums = await anonymous.GetFromJsonAsync<JsonElement>($"/api/artists/{aid}/albums"); Assert.Single(albums.EnumerateArray());
            var refreshed = await anonymous.GetFromJsonAsync<JsonElement>($"/api/songs/{sid}");
            Assert.Equal("Renamed Artist", refreshed.GetProperty("artists")[0].GetProperty("name").GetString());
            Assert.Equal("Renamed Album", refreshed.GetProperty("album").GetProperty("title").GetString());
            var search = await anonymous.GetFromJsonAsync<JsonElement>("/api/search?q=Renamed%20Artist"); Assert.Single(search.GetProperty("songs").EnumerateArray());
            var literal = await anonymous.GetFromJsonAsync<JsonElement>("/api/search?q=%5B"); Assert.Empty(literal.GetProperty("songs").EnumerateArray());
            var db = factory.Services.GetRequiredService<MongoDbContext>();
            foreach (var collection in new[] { "artists", "albums", "songs" })
            {
                Assert.Equal(0, await db.Database.GetCollection<BsonDocument>(collection).CountDocumentsAsync(Builders<BsonDocument>.Filter.Exists("placeholder")));
                await db.Database.GetCollection<BsonDocument>(collection).UpdateManyAsync(Builders<BsonDocument>.Filter.Empty, Builders<BsonDocument>.Update.Set("placeholder", "placeholder"));
            }
            var repair = await DemoFactory.Json(await admin.PostAsync("/api/admin/repair", null), HttpStatusCode.OK);
            Assert.Equal(3, repair.GetProperty("repaired").GetInt64());
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/songs/{sid}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/artists/{aid}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/admin/artists/{aid}")).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await admin.DeleteAsync($"/api/albums/{albumId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/songs/{sid}")).StatusCode);
            Assert.Empty((await anonymous.GetFromJsonAsync<JsonElement>($"/api/albums/{albumId}")).GetProperty("songs").EnumerateArray());
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/albums/{albumId}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/admin/artists/{aid}")).StatusCode);
            Assert.Equal(0, await db.Songs.CountDocumentsAsync(Builders<Music.Models.Song>.Filter.Empty));
        }
        finally { await factory.Cleanup(); }
    }

    [DependencyFact]
    public async Task PlaylistsPersistContiguousOrderAtomicUniquenessPrivacyAndOwnerPermissions()
    {
        using var factory = new DemoFactory();
        using var admin = factory.Client(true); using var alice = factory.Client(); using var mallory = factory.Client(actor: factory.Mallory); using var anonymous = factory.CreateClient();
        try
        {
            var artist = await DemoFactory.Json(await admin.PostAsJsonAsync("/api/artists", new { name = "Playlist Artist" }), HttpStatusCode.Created);
            var aid = artist.GetProperty("id").GetString()!;
            var ids = new List<string>();
            for (var i = 0; i < 3; i++) ids.Add((await DemoFactory.Json(await admin.PostAsJsonAsync("/api/songs", new { title = "Track " + i, artists = new[] { new { id = aid } }, durationSec = 1 }), HttpStatusCode.Created)).GetProperty("id").GetString()!);
            var playlist = await DemoFactory.Json(await alice.PostAsJsonAsync($"/api/playlists/user/{factory.Alice}", new { name = "Private", isPublic = false }), HttpStatusCode.Created);
            var pid = playlist.GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.Forbidden, (await mallory.PostAsJsonAsync($"/api/playlists/user/{factory.Alice}", new { name = "Forged" })).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/playlists/{pid}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await mallory.GetAsync($"/api/playlists/{pid}")).StatusCode);
            Assert.Empty((await anonymous.GetFromJsonAsync<JsonElement>($"/api/playlists/user/{factory.Alice}")).EnumerateArray());
            factory.Sessions.Unavailable = true;
            Assert.Equal(HttpStatusCode.ServiceUnavailable, (await alice.GetAsync($"/api/playlists/user/{factory.Alice}")).StatusCode);
            Assert.Equal(1, await factory.Services.GetRequiredService<MongoDbContext>().Playlists.CountDocumentsAsync(x => x.CreatedBy == factory.Alice));
            factory.Sessions.Unavailable = false;
            Assert.Single((await alice.GetFromJsonAsync<JsonElement>($"/api/playlists/user/{factory.Alice}")).EnumerateArray());
            Assert.Equal(HttpStatusCode.OK, (await alice.PostAsync($"/api/playlists/{pid}/songs/{ids[0]}", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.PostAsync($"/api/playlists/{pid}/songs/{ids[1]}", null)).StatusCode);
            var submissions = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => alice.PostAsync($"/api/playlists/{pid}/songs/{ids[2]}?position=0", null)));
            Assert.Equal(1, submissions.Count(x => x.StatusCode == HttpStatusCode.OK));
            Assert.Equal(5, submissions.Count(x => x.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/playlists/{pid}/songs/reorder", new { songIds = new[] { ids[0], ids[0] } })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync($"/api/playlists/{pid}/songs/reorder", new { songIds = ids })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.DeleteAsync($"/api/playlists/{pid}/songs/{ids[1]}")).StatusCode);
            var db = factory.Services.GetRequiredService<MongoDbContext>();
            var persisted = await db.Playlists.Find(x => x.Id == pid).SingleAsync();
            Assert.Equal(new[] { ids[0], ids[2] }, persisted.Songs.Select(x => x.Id));
            Assert.Equal(new[] { 0, 1 }, persisted.Songs.Select(x => x.Position));
            Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync($"/api/playlists/{pid}", new { description = "", isPublic = true })).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await mallory.DeleteAsync($"/api/playlists/{pid}")).StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/playlists/{pid}")).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/playlists/{pid}")).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/songs/{ids[0]}")).StatusCode);
            persisted = await db.Playlists.Find(x => x.Id == pid).SingleAsync();
            Assert.Equal(ids[2], Assert.Single(persisted.Songs).Id); Assert.Equal(0, persisted.Songs[0].Position);
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/playlists/{pid}")).StatusCode);
            Assert.False(await db.Playlists.Find(x => x.Id == pid).AnyAsync());
        }
        finally { await factory.Cleanup(); }
    }

    [DependencyFact]
    public async Task MediaIsByteAccuratePrivateMimeCorrectAndReplacementFailurePreservesWorkingObject()
    {
        using var factory = new DemoFactory();
        using var admin = factory.Client(true); using var alice = factory.Client(); using var anonymous = factory.CreateClient();
        try
        {
            using var artistForm = DemoFactory.Form(new() { ["Name"] = "Media Artist" }, "ImageFile", DemoFactory.Png(), "fixture.png");
            var artist = await DemoFactory.Json(await admin.PostAsync("/api/admin/artists", artistForm), HttpStatusCode.Created);
            var aid = artist.GetProperty("id").GetString()!;
            var audio = DemoFactory.Wave(2);
            using var songForm = DemoFactory.Form(new() { ["Title"] = "Media", ["ArtistId"] = aid, ["Duration"] = "2" }, "AudioFile", audio);
            songForm.Add(new ByteArrayContent(DemoFactory.Png()), "CoverFile", "misleading.jpg");
            var song = await DemoFactory.Json(await admin.PostAsync("/api/admin/songs", songForm), HttpStatusCode.Created);
            var sid = song.GetProperty("id").GetString()!; var fileUrl = song.GetProperty("fileUrl").GetString()!; var coverUrl = song.GetProperty("coverUrl").GetString()!;
            Assert.DoesNotContain("?", fileUrl); Assert.DoesNotContain("azurite", fileUrl);
            var full = await anonymous.GetAsync(fileUrl); Assert.Equal(HttpStatusCode.OK, full.StatusCode); Assert.Equal(audio, await full.Content.ReadAsByteArrayAsync()); Assert.Equal(audio.Length, full.Content.Headers.ContentLength);
            Assert.Equal("image/png", (await anonymous.GetAsync(coverUrl)).Content.Headers.ContentType!.MediaType);
            foreach (var range in new[] { "bytes=0-0", "bytes=-3", "bytes=3-", "bytes=500-999" })
            {
                Assert.True(Music.Controllers.MediaController.TryRange(range, audio.Length, out var start, out var length));
                using var request = new HttpRequestMessage(HttpMethod.Get, fileUrl); request.Headers.Add("Range", range);
                var response = await anonymous.SendAsync(request); Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
                Assert.Equal(audio.Skip((int)start).Take((int)length), await response.Content.ReadAsByteArrayAsync());
                Assert.Equal(length, response.Content.Headers.ContentLength);
                Assert.Equal($"bytes {start}-{start + length - 1}/{audio.Length}", response.Content.Headers.ContentRange!.ToString());
            }
            using (var request = new HttpRequestMessage(HttpMethod.Get, fileUrl))
            {
                request.Headers.Add("Range", "bytes=999999-"); var response = await anonymous.SendAsync(request);
                Assert.Equal(HttpStatusCode.RequestedRangeNotSatisfiable, response.StatusCode); Assert.Equal($"bytes */{audio.Length}", response.Content.Headers.ContentRange!.ToString());
            }
            Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.GetAsync("/api/media/audio?url=" + Uri.EscapeDataString("https://attacker.invalid/users/private.wav"))).StatusCode);
            using var invalid = DemoFactory.Form(null, "audioFile", [1, 2, 3]);
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/songs/{sid}/upload-file", invalid)).StatusCode);
            Assert.Equal(audio, await anonymous.GetByteArrayAsync(fileUrl));
            using (var invalidCover = DemoFactory.Form(null, "imageFile", [1, 2, 3], "spoofed.png"))
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/api/songs/{sid}/upload-cover", invalidCover)).StatusCode);
            using (var interruptedReplacement = DemoFactory.Form(new() { ["Title"] = "Must not publish" }, "AudioFile", DemoFactory.Wave()))
            {
                interruptedReplacement.Add(new ByteArrayContent([1, 2, 3]), "CoverFile", "spoofed.png");
                Assert.Equal(HttpStatusCode.BadRequest, (await admin.PutAsync($"/api/admin/songs/{sid}", interruptedReplacement)).StatusCode);
            }
            var db = factory.Services.GetRequiredService<MongoDbContext>();
            Assert.Equal(fileUrl, (await db.Songs.Find(x => x.Id == sid).SingleAsync()).FileUrl);
            Assert.Equal("Media", (await db.Songs.Find(x => x.Id == sid).SingleAsync()).Title);
            // A second real HTTP host uses the same persisted documents but an unavailable storage endpoint.
            // Its upload fails after reserving a cleanup intent, before metadata publication.
            var unavailableStorage = System.Text.RegularExpressions.Regex.Replace(Environment.GetEnvironmentVariable("MUSIC_TEST_STORAGE")!, "BlobEndpoint=[^;]+", "BlobEndpoint=http://127.0.0.1:1/unavailable");
            using (var unavailable = new DemoFactory(storage: unavailableStorage, database: factory.Database, prefix: factory.Prefix))
            using (var unavailableAdmin = unavailable.Client(true))
            using (var validReplacement = DemoFactory.Form(null, "audioFile", DemoFactory.Wave()))
                Assert.Equal(HttpStatusCode.ServiceUnavailable, (await unavailableAdmin.PostAsync($"/api/songs/{sid}/upload-file", validReplacement)).StatusCode);
            Assert.Equal(fileUrl, (await db.Songs.Find(x => x.Id == sid).SingleAsync()).FileUrl);
            Assert.Equal(audio, await anonymous.GetByteArrayAsync(fileUrl));
            var playlist = await DemoFactory.Json(await alice.PostAsJsonAsync("/api/playlists", new { name = "Cover", isPublic = true }), HttpStatusCode.Created); var pid = playlist.GetProperty("id").GetString()!;
            using var cover = DemoFactory.Form(null, "file", DemoFactory.Png(), "cover.png");
            var coverResult = await DemoFactory.Json(await alice.PostAsync($"/api/playlists/{pid}/cover", cover), HttpStatusCode.OK); var playlistCover = coverResult.GetProperty("coverUrl").GetString()!;
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(playlistCover)).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await alice.DeleteAsync($"/api/playlists/{pid}/cover")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(playlistCover)).StatusCode);
            using var scope = factory.Services.CreateScope();
            var commands = scope.ServiceProvider.GetRequiredService<MediaCommands>();
            await db.Database.GetCollection<MediaCleanup>("mediaCleanup").UpdateManyAsync(Builders<MediaCleanup>.Filter.Empty, Builders<MediaCleanup>.Update.Set(x => x.DueAt, DateTime.UtcNow.AddMinutes(-1)));
            await factory.Services.GetRequiredService<MutationGate>().Run(async () => { await commands.Cleanup(default); return true; }, default);
            Assert.Equal(audio, await anonymous.GetByteArrayAsync(fileUrl));
            Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/songs/{sid}")).StatusCode);
            Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync(fileUrl)).StatusCode);
        }
        finally { await factory.Cleanup(); }
    }

    [DependencyFact]
    public async Task BulkRejectsMismatchesAndReportsEveryFailedRowWithoutPartialMetadata()
    {
        using var factory = new DemoFactory(); using var admin = factory.Client(true);
        try
        {
            var artist = await DemoFactory.Json(await admin.PostAsJsonAsync("/api/artists", new { name = "Bulk Artist" }), HttpStatusCode.Created); var aid = artist.GetProperty("id").GetString()!;
            using var mismatch = DemoFactory.Form(new() { ["ArtistId"] = aid, ["SongsData[0].Title"] = "Ignored", ["SongsData[0].Duration"] = "1" });
            Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync("/api/admin/bulk/songs", mismatch)).StatusCode);
            using var bulk = DemoFactory.Form(new() { ["ArtistId"] = aid, ["IdempotencyKey"] = "regression-1", ["SongsData[0].Title"] = "Valid", ["SongsData[0].Duration"] = "1", ["SongsData[1].Title"] = "Invalid", ["SongsData[1].Duration"] = "-1" });
            bulk.Add(new ByteArrayContent(DemoFactory.Wave()), "AudioFiles", "one.wav"); bulk.Add(new ByteArrayContent(DemoFactory.Wave()), "AudioFiles", "two.wav");
            var result = await DemoFactory.Json(await admin.PostAsync("/api/admin/bulk/songs", bulk), HttpStatusCode.OK);
            Assert.Equal(1, result.GetProperty("successCount").GetInt32()); Assert.Equal(1, result.GetProperty("errorCount").GetInt32()); Assert.Equal(2, result.GetProperty("items").GetArrayLength());
            var db = factory.Services.GetRequiredService<MongoDbContext>(); Assert.Equal(1, await db.Songs.CountDocumentsAsync(Builders<Music.Models.Song>.Filter.Empty));
            using var retry = DemoFactory.Form(new() { ["ArtistId"] = aid, ["IdempotencyKey"] = "regression-1", ["SongsData[0].Title"] = "Valid", ["SongsData[0].Duration"] = "1", ["SongsData[1].Title"] = "Invalid", ["SongsData[1].Duration"] = "-1" });
            retry.Add(new ByteArrayContent(DemoFactory.Wave()), "AudioFiles", "one.wav"); retry.Add(new ByteArrayContent(DemoFactory.Wave()), "AudioFiles", "two.wav");
            var replay = await DemoFactory.Json(await admin.PostAsync("/api/admin/bulk/songs", retry), HttpStatusCode.OK);
            Assert.Equal(result.GetProperty("createdSongs")[0].GetProperty("id").GetString(), replay.GetProperty("createdSongs")[0].GetProperty("id").GetString());
            Assert.Equal(1, await db.Songs.CountDocumentsAsync(Builders<Music.Models.Song>.Filter.Empty));
            using var changed = DemoFactory.Form(new() { ["ArtistId"] = aid, ["IdempotencyKey"] = "regression-1", ["SongsData[0].Title"] = "Changed", ["SongsData[0].Duration"] = "1" }, "AudioFiles");
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync("/api/admin/bulk/songs", changed)).StatusCode);
        }
        finally { await factory.Cleanup(); }
    }
}
