using MongoDB.Driver;
using Music.Data;
using Music.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Add Redis for distributed caching
var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Redis");

Console.WriteLine($"Redis Connection String: {redisConnectionString}");

if (string.IsNullOrEmpty(redisConnectionString))
{
    throw new InvalidOperationException("Redis connection string not found");
}

builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    return ConnectionMultiplexer.Connect(redisConnectionString);
});

builder.Services.AddSingleton<IDatabase>(provider =>
{
    var connection = provider.GetRequiredService<IConnectionMultiplexer>();
    return connection.GetDatabase();
});

// Keep memory cache as fallback
builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 50 * 1024 * 1024; // 50MB cache limit (reduced since Redis is primary)
});

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDb")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__MongoDb");

// Fix auth mechanism if it's set to DEFAULT
if (!string.IsNullOrEmpty(connectionString) && connectionString.Contains("authMechanism=DEFAULT"))
{
    connectionString = connectionString.Replace("authMechanism=DEFAULT", "authMechanism=SCRAM-SHA-1");
}

if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("MongoDB connection string not found");
}
    
    return new MongoClient(connectionString);
});

builder.Services.AddScoped<MongoDbContext>(serviceProvider =>
{
    var mongoClient = serviceProvider.GetRequiredService<IMongoClient>();
    return new MongoDbContext(mongoClient, "spotibuds");
});

// Add Azure Blob Storage service
builder.Services.AddScoped<IAzureBlobService, AzureBlobService>();

var corsSection = builder.Configuration.GetSection("Cors");
var allowedOrigins = corsSection["AllowedOrigins"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("SpotibudsPolicy", policy =>
    {
        if (allowedOrigins == "*")
        {
            policy.AllowAnyOrigin()
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
        else
        {
            var origins = allowedOrigins?.Split(',') ?? Array.Empty<string>();
            policy.WithOrigins(origins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials();
        }
    });
});

// Make the app listen on port 80 for Azure compatibility
builder.WebHost.UseUrls("http://0.0.0.0:80");

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("SpotibudsPolicy");
app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => "Music API is running!");
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

app.Run(); 