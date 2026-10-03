using Music.Services;
using Music.Controllers;
using System.Net;
using System.Net.Http.Json;
namespace Music.Tests;
public class DependencyFailureTests
{
    [Fact]
    public async Task RealMiddlewareBootsDuringColdMongoAndReturnsBounded503()
    {
        using var factory = new DemoFactory("mongodb://127.0.0.1:1");
        using var client = factory.CreateClient();
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var response = await client.GetAsync("/api/songs");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10));
        Assert.DoesNotContain("127.0.0.1:1", await response.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health/live")).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await client.GetAsync("/health/ready")).StatusCode);
    }
    [Fact]
    public async Task RealJwtMiddlewareRejectsAnonymousForeignRoleRevokedAndUnavailableSessions()
    {
        using var factory = new DemoFactory("mongodb://127.0.0.1:1");
        using var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/artists", new { name = "denied" })).StatusCode);
        using var ordinary = factory.Client();
        Assert.Equal(HttpStatusCode.Forbidden, (await ordinary.PostAsJsonAsync("/api/artists", new { name = "denied" })).StatusCode);
        factory.Sessions.Revoked.Add(factory.SessionId);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ordinary.PostAsJsonAsync("/api/playlists", new { name = "denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await ordinary.GetAsync($"/api/playlists/user/{factory.Alice}")).StatusCode);
        factory.Sessions.Revoked.Clear(); factory.Sessions.Unavailable = true;
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ordinary.PostAsJsonAsync("/api/playlists", new { name = "denied" })).StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await ordinary.GetAsync($"/api/playlists/user/{factory.Alice}")).StatusCode);
    }
    [Fact]
    public async Task CorsHasExactAllowlistOnActualAndPreflightRequests()
    {
        using var factory = new DemoFactory("mongodb://127.0.0.1:1");
        using var client = factory.CreateClient();
        foreach (var allowed in new[] { true, false }) foreach (var method in new[] { HttpMethod.Get, HttpMethod.Options })
        {
            using var request = new HttpRequestMessage(method, "/health/live");
            request.Headers.Add("Origin", allowed ? "http://127.0.0.1:3100" : "https://attacker.invalid");
            if (method == HttpMethod.Options) request.Headers.Add("Access-Control-Request-Method", "GET");
            var response = await client.SendAsync(request);
            Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Origin"));
            Assert.Equal(allowed, response.Headers.Contains("Access-Control-Allow-Credentials"));
            if (allowed)
            {
                Assert.Equal("http://127.0.0.1:3100", response.Headers.GetValues("Access-Control-Allow-Origin").Single());
                Assert.Equal("true", response.Headers.GetValues("Access-Control-Allow-Credentials").Single());
            }
        }
    }
}
