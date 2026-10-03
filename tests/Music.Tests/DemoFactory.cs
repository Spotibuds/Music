using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Azure.Storage.Blobs;

namespace Music.Tests;

public sealed class DependencyFactAttribute : FactAttribute
{
    public DependencyFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MUSIC_TEST_MONGO")) || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("MUSIC_TEST_STORAGE")))
            Skip = "Set MUSIC_TEST_MONGO and MUSIC_TEST_STORAGE to isolated disposable dependencies; persisted HTTP tests are not executed without them.";
    }
}
public sealed class SessionHandler : HttpMessageHandler
{
    public bool Unavailable { get; set; }
    public HashSet<string> Revoked { get; } = [];
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var id = request.RequestUri!.Segments.Last();
        return Task.FromResult(new HttpResponseMessage(Unavailable ? HttpStatusCode.ServiceUnavailable : Revoked.Contains(id) ? HttpStatusCode.Unauthorized : HttpStatusCode.NoContent));
    }
}

public sealed class DemoFactory : WebApplicationFactory<Program>
{
    public string Secret { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    public string Database { get; } = "music_regression_" + Guid.NewGuid().ToString("N");
    public string Prefix { get; } = "reg" + Guid.NewGuid().ToString("N")[..16];
    public SessionHandler Sessions { get; } = new();
    public string SessionId { get; } = Guid.NewGuid().ToString();
    public string Alice { get; } = Guid.NewGuid().ToString();
    public string Mallory { get; } = Guid.NewGuid().ToString();
    private readonly string? mongo;
    private readonly string? storage;
    public DemoFactory(string? mongo = null, string? storage = null, string? database = null, string? prefix = null)
    {
        this.mongo = mongo; this.storage = storage;
        if (database is not null) Database = database;
        if (prefix is not null) Prefix = prefix;
    }
    protected override IHost CreateHost(IHostBuilder builder)
    {
        builder.ConfigureHostConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Secret"] = Secret, ["Jwt:Issuer"] = "spotibuds-local", ["Jwt:Audience"] = "spotibuds-demo",
            ["ConnectionStrings:MongoDb"] = mongo ?? Environment.GetEnvironmentVariable("MUSIC_TEST_MONGO") ?? "mongodb://127.0.0.1:1",
            ["MongoDb:DatabaseName"] = Database,
            ["AzureStorage:ConnectionString"] = storage ?? Environment.GetEnvironmentVariable("MUSIC_TEST_STORAGE") ?? $"DefaultEndpointsProtocol=http;AccountName=disposable;AccountKey={Convert.ToBase64String(RandomNumberGenerator.GetBytes(64))};BlobEndpoint=http://127.0.0.1:1/disposable;",
            ["AzureStorage:PublicBaseUrl"] = "http://127.0.0.1:5102",
            ["AzureStorage:SongsContainer"] = Prefix + "songs", ["AzureStorage:ArtistsContainer"] = Prefix + "artists", ["AzureStorage:AlbumsContainer"] = Prefix + "albums", ["AzureStorage:PlaylistsContainer"] = Prefix + "playlists",
            ["IdentityService:BaseUrl"] = "http://identity.invalid", ["ServiceAuth:Secret"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)),
            ["Cors:AllowedOrigins"] = "http://127.0.0.1:3100", ["AllowedHosts"] = "*"
        }));
        return base.CreateHost(builder);
    }
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureServices(services => services.AddHttpClient("identity").ConfigurePrimaryHttpMessageHandler(() => Sessions));
    }
    public HttpClient Client(bool admin = false, string? actor = null)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("http://127.0.0.1:5102"), AllowAutoRedirect = false });
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, actor ?? Alice), new("sid", SessionId) };
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var token = new JwtSecurityToken("spotibuds-local", "spotibuds-demo", claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)), SecurityAlgorithms.HmacSha256));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        return client;
    }
    public async Task Cleanup()
    {
        if (!Database.StartsWith("music_regression_", StringComparison.Ordinal)) throw new InvalidOperationException("Not a generated test database.");
        if (Environment.GetEnvironmentVariable("MUSIC_TEST_MONGO") is { Length: > 0 } connection) await new MongoClient(connection).DropDatabaseAsync(Database);
        if (Environment.GetEnvironmentVariable("MUSIC_TEST_STORAGE") is { Length: > 0 } storageConnection)
        {
            var blob = new BlobServiceClient(storageConnection);
            foreach (var kind in new[] { "songs", "artists", "albums", "playlists" }) await blob.GetBlobContainerClient(Prefix + kind).DeleteIfExistsAsync();
        }
    }
    public static async Task<JsonElement> Json(HttpResponseMessage response, HttpStatusCode expected)
    {
        Assert.Equal(expected, response.StatusCode);
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
    public static byte[] Wave(int seconds = 1)
    {
        var data = Enumerable.Range(0, 8000 * seconds).SelectMany(x => BitConverter.GetBytes((short)(Math.Sin(x * 2 * Math.PI * 220 / 8000) * 1000))).ToArray();
        using var output = new MemoryStream(); using var writer = new BinaryWriter(output);
        writer.Write(Encoding.ASCII.GetBytes("RIFF")); writer.Write(36 + data.Length); writer.Write(Encoding.ASCII.GetBytes("WAVEfmt ")); writer.Write(16); writer.Write((short)1); writer.Write((short)1); writer.Write(8000); writer.Write(16000); writer.Write((short)2); writer.Write((short)16); writer.Write(Encoding.ASCII.GetBytes("data")); writer.Write(data.Length); writer.Write(data); return output.ToArray();
    }
    public static byte[] Png()
    {
        using var bitmap = new SkiaSharp.SKBitmap(8, 8); bitmap.Erase(SkiaSharp.SKColors.Purple);
        using var image = SkiaSharp.SKImage.FromBitmap(bitmap); using var data = image.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100); return data.ToArray();
    }
    public static MultipartFormDataContent Form(Dictionary<string, string>? fields = null, string? name = null, byte[]? bytes = null, string filename = "fixture.wav")
    {
        var form = new MultipartFormDataContent();
        foreach (var field in fields ?? []) form.Add(new StringContent(field.Value), field.Key);
        if (name is not null) form.Add(new ByteArrayContent(bytes ?? Wave()), name, filename);
        return form;
    }
}
