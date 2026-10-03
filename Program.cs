using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using MongoDB.Driver;
using Music.Data;
using Music.Services;
using System.Net;
using System.Text;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders().AddSimpleConsole(options => options.SingleLine = true);
string Required(string name) => !string.IsNullOrWhiteSpace(builder.Configuration[name]) ? builder.Configuration[name]! : throw new InvalidOperationException($"{name} is required.");
var secret = Required("Jwt:Secret");
if (Encoding.UTF8.GetByteCount(secret) < 32) throw new InvalidOperationException("Jwt:Secret requires at least 32 random bytes.");
var mongoSettings = MongoClientSettings.FromConnectionString(Required("ConnectionStrings:MongoDb"));
mongoSettings.ServerSelectionTimeout = TimeSpan.FromSeconds(2);
mongoSettings.ConnectTimeout = TimeSpan.FromSeconds(2);
mongoSettings.SocketTimeout = TimeSpan.FromSeconds(5);
mongoSettings.MaxConnectionPoolSize = 50;
mongoSettings.RetryWrites = false;
builder.Services.AddSingleton<IMongoClient>(new MongoClient(mongoSettings));
builder.Services.AddSingleton<MongoDbContext>();
builder.Services.AddSingleton<MutationGate>();
builder.Services.AddSingleton<AzureBlobService>();
builder.Services.AddScoped<MediaCommands>();
builder.Services.AddScoped<CatalogueService>();
builder.Services.AddScoped<PlaylistService>();
builder.Services.AddSingleton<MaintenanceState>();
builder.Services.AddHostedService<MaintenanceWorker>();
builder.Services.AddHttpClient("identity", client =>
{
    client.BaseAddress = new Uri(Required("IdentityService:BaseUrl").TrimEnd('/') + "/");
    client.DefaultRequestHeaders.Add("X-Spotibuds-Service", Required("ServiceAuth:Secret"));
    client.Timeout = TimeSpan.FromSeconds(2);
});
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = Required("Jwt:Issuer"), ValidateAudience = true, ValidAudience = Required("Jwt:Audience"),
        ValidateLifetime = true, ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret)), ClockSkew = TimeSpan.FromSeconds(5)
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var sid = context.Principal?.FindFirst("sid")?.Value;
            if (!Guid.TryParse(sid, out _)) { context.Fail("Session identity is required."); return; }
            try
            {
                var client = context.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("identity");
                using var response = await client.GetAsync($"api/auth/internal/sessions/{sid}", context.HttpContext.RequestAborted);
                if (response.StatusCode == HttpStatusCode.Unauthorized) context.Fail("Session is revoked.");
                else if (response.StatusCode != HttpStatusCode.NoContent) { context.HttpContext.Items["identityUnavailable"] = true; context.Fail("Session validation unavailable."); }
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException) { context.HttpContext.Items["identityUnavailable"] = true; context.Fail("Session validation unavailable."); }
        },
        OnChallenge = context =>
        {
            if (context.HttpContext.Items.ContainsKey("identityUnavailable"))
            {
                context.HandleResponse(); context.Response.StatusCode = 503;
                return context.Response.WriteAsJsonAsync(new { message = "Session validation is temporarily unavailable." });
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddAuthorization(options => options.AddPolicy("AdminOnly", policy => policy.RequireRole("Admin")));
builder.Services.AddControllers();
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options => options.MultipartBodyLengthLimit = 150 * 1024 * 1024);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 150 * 1024 * 1024);
var origins = Required("Cors:AllowedOrigins").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
if (origins.Any(x => x == "*" || !Uri.TryCreate(x, UriKind.Absolute, out _))) throw new InvalidOperationException("CORS requires explicit origins.");
builder.Services.AddCors(options => options.AddPolicy("local", policy => policy.WithOrigins(origins).AllowCredentials().AllowAnyMethod().AllowAnyHeader().WithExposedHeaders("Content-Range", "Accept-Ranges", "ETag", "Content-Length")));
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("search", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
// Resolve and validate configuration eagerly without gating on dependency network connectivity.
_ = app.Services.GetRequiredService<AzureBlobService>();
app.Use(async (context, next) =>
{
    try { await next(); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (ApiException ex) { if (!context.Response.HasStarted) { context.Response.Clear(); context.Response.StatusCode = ex.Status; await context.Response.WriteAsJsonAsync(new { message = ex.Message }); } }
    catch (MongoWriteException ex) when (ex.WriteError.Category == ServerErrorCategory.DuplicateKey) { context.Response.Clear(); context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { message = "An item with this identity already exists." }); }
    catch (Exception ex) when (DemoRules.DependencyFailure(ex))
    {
        app.Logger.LogWarning("Music request dependency failure ({ErrorType}).", ex.GetType().Name);
        if (!context.Response.HasStarted) { context.Response.Clear(); context.Response.StatusCode = 503; await context.Response.WriteAsJsonAsync(new { message = "A required dependency is temporarily unavailable. Retry shortly." }); }
        else context.Abort();
    }
    catch (Exception ex)
    {
        app.Logger.LogError("Music request failed ({ErrorType}).", ex.GetType().Name);
        if (!context.Response.HasStarted) { context.Response.Clear(); context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { message = "The request could not be completed." }); }
        else context.Abort();
    }
});
app.UseCors("local");
app.UseRateLimiter();
app.UseAuthentication();
app.Use(async (context, next) =>
{
    // A supplied session must not silently become anonymous when validation fails on a public read.
    // Otherwise private owner lists look successfully empty and mask dependency outages.
    if (context.Request.Headers.Authorization.Count > 0 && context.User.Identity?.IsAuthenticated != true)
    {
        var unavailable = context.Items.ContainsKey("identityUnavailable");
        context.Response.StatusCode = unavailable ? 503 : 401;
        if (!unavailable) context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsJsonAsync(new { message = unavailable ? "Session validation is temporarily unavailable." : "The supplied session is invalid or revoked." });
        return;
    }
    await next();
});
app.UseAuthorization();
app.MapControllers();
app.MapGet("/", () => new { service = "Music" });
app.MapGet("/health", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/live", () => Results.Ok(new { status = "live" }));
app.MapGet("/health/ready", async (MongoDbContext db, AzureBlobService storage, MaintenanceState state, CancellationToken ct) =>
{
    using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct); timeout.CancelAfter(TimeSpan.FromSeconds(3));
    try { if (state.Initialized && await db.TestConnectionAsync(timeout.Token) && await storage.Ready(timeout.Token)) return Results.Ok(new { status = "ready" }); }
    catch (OperationCanceledException) { }
    return Results.Json(new { status = "unavailable" }, statusCode: 503);
});
app.Run();
public partial class Program { }
