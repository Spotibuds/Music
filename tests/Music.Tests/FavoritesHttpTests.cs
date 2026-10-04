using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using MongoDB.Driver;
using Music.Data;
using Music.Models;

namespace Music.Tests;

public sealed class FavoritesHttpTests
{
    [DependencyFact]
    public async Task CanonicalFavoritesCreationRemainsIdempotentAtAccountQuota()
    {
        using var factory = new DemoFactory();
        using var alice = factory.Client();
        try
        {
            var liked = await DemoFactory.Json(await alice.PostAsJsonAsync("/api/playlists", new { name = "  liked songs  ", isPublic = false }), HttpStatusCode.Created);
            var id = liked.GetProperty("id").GetString()!;
            Assert.Equal("Liked Songs", liked.GetProperty("name").GetString());
            var db = factory.Services.GetRequiredService<MongoDbContext>();
            await db.Playlists.InsertManyAsync(Enumerable.Range(0, 99).Select(index => new Playlist { Name = "Collection " + index, CreatedBy = factory.Alice }));
            var repeated = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => alice.PostAsJsonAsync($"/api/playlists/user/{factory.Alice}", new { name = "LIKED SONGS" })));
            foreach (var response in repeated)
            {
                var existing = await DemoFactory.Json(response, HttpStatusCode.Created);
                Assert.Equal(id, existing.GetProperty("id").GetString());
                Assert.False(existing.GetProperty("isPublic").GetBoolean());
            }
            Assert.Equal(100, await db.Playlists.CountDocumentsAsync(x => x.CreatedBy == factory.Alice));
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PostAsJsonAsync("/api/playlists", new { name = "One more" })).StatusCode);
            Assert.Equal(100, (await alice.GetFromJsonAsync<JsonElement>($"/api/playlists/user/{factory.Alice}?limit=100")).GetArrayLength());
        }
        finally { await factory.Cleanup(); }
    }

    [DependencyFact]
    public async Task FavoritesNameCannotBeRemovedOrAssignedToAnOrdinaryPlaylist()
    {
        using var factory = new DemoFactory();
        using var alice = factory.Client();
        try
        {
            var liked = await DemoFactory.Json(await alice.PostAsJsonAsync("/api/playlists", new { name = "Liked Songs", isPublic = false }), HttpStatusCode.Created);
            var ordinary = await DemoFactory.Json(await alice.PostAsJsonAsync("/api/playlists", new { name = "Evening" }), HttpStatusCode.Created);
            var likedId = liked.GetProperty("id").GetString()!;
            var ordinaryId = ordinary.GetProperty("id").GetString()!;
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/playlists/{likedId}", new { name = "Renamed favorites" })).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await alice.PutAsJsonAsync($"/api/playlists/{ordinaryId}", new { name = " liked songs " })).StatusCode);
            Assert.Equal(HttpStatusCode.NoContent, (await alice.PutAsJsonAsync($"/api/playlists/{likedId}", new { name = "Liked Songs", description = "My saved songs" })).StatusCode);
            var persisted = await alice.GetFromJsonAsync<JsonElement>($"/api/playlists/{likedId}");
            Assert.Equal("Liked Songs", persisted.GetProperty("name").GetString());
            Assert.Equal("My saved songs", persisted.GetProperty("description").GetString());
            Assert.Equal("Evening", (await alice.GetFromJsonAsync<JsonElement>($"/api/playlists/{ordinaryId}")).GetProperty("name").GetString());
        }
        finally { await factory.Cleanup(); }
    }
}
