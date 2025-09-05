using MongoDB.Driver;
using MongoDB.Bson;
using Music.Data;
using Music.Services;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var redisConnectionString = builder.Configuration.GetConnectionString("Redis")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__Redis");

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

builder.Services.AddMemoryCache(options =>
{
    options.SizeLimit = 50 * 1024 * 1024;
});

builder.Services.AddSingleton<IMongoClient>(serviceProvider =>
{
    var connectionString = builder.Configuration.GetConnectionString("MongoDb")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__MongoDb");

    if (!string.IsNullOrEmpty(connectionString) && connectionString.Contains("authMechanism=DEFAULT"))
    {
        connectionString = connectionString.Replace("authMechanism=DEFAULT", "authMechanism=SCRAM-SHA-1");
    }

    if (string.IsNullOrEmpty(connectionString))
    {
        return null!;
    }

    try
    {
        var settings = MongoClientSettings.FromConnectionString(connectionString);

        // Improved connection settings for better reliability
        settings.ServerSelectionTimeout = TimeSpan.FromSeconds(30); // Reduced from 60 to fail faster
        settings.ConnectTimeout = TimeSpan.FromSeconds(30); // Reduced from 60 to fail faster
        settings.SocketTimeout = TimeSpan.FromSeconds(30); // Reduced from 60 to fail faster
        settings.MaxConnectionPoolSize = 50;
        settings.MinConnectionPoolSize = 10;
        settings.MaxConnectionIdleTime = TimeSpan.FromMinutes(5);
        settings.MaxConnectionLifeTime = TimeSpan.FromMinutes(15);
        settings.HeartbeatInterval = TimeSpan.FromSeconds(10);
        settings.HeartbeatTimeout = TimeSpan.FromSeconds(10);
        settings.RetryWrites = true;
        settings.RetryReads = true;





        var client = new MongoClient(settings);

        // Test the connection with retry logic
        var maxRetries = 2; // Reduced from 3 to fail faster
        var retryCount = 0;

        while (retryCount < maxRetries)
        {
            try
            {
                client.GetDatabase("admin").RunCommandAsync((Command<BsonDocument>)"{ping:1}").Wait(TimeSpan.FromSeconds(10)); // Reduced timeout
                break;
            }
            catch (Exception ex)
            {
                retryCount++;

                if (retryCount >= maxRetries)
                {
                    return null!;
                }

                // Wait before retrying
                Thread.Sleep(1000); // Reduced wait time
            }
        }

        return client;
    }
    catch (Exception ex)
    {
        return null!;
    }
});

builder.Services.AddScoped<MongoDbContext>(serviceProvider =>
{
    var mongoClient = serviceProvider.GetRequiredService<IMongoClient>();
    if (mongoClient == null)
    {
        return new MongoDbContext(null, "spotibuds");
    }
    return new MongoDbContext(mongoClient, "spotibuds");
});

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

builder.WebHost.UseUrls($"http://0.0.0.0:80");

var app = builder.Build();

app.UseSwagger();
app.UseSwaggerUI();

// Disable HTTPS redirection for development
if (app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}

app.UseCors("SpotibudsPolicy");

app.Use(async (context, next) =>
{
    context.Response.Headers["Access-Control-Allow-Origin"] = "*";
    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";
    
    if (context.Request.Method == "OPTIONS")
    {
        context.Response.StatusCode = 200;
        return;
    }
    
    await next();
});

app.UseAuthorization();
app.MapControllers();

app.MapGet("/", () => "Music API is running!");
app.MapGet("/health", () => new { status = "healthy", timestamp = DateTime.UtcNow });

// MongoDB health check endpoint
app.MapGet("/health/mongodb", async (MongoDbContext dbContext) =>
{
    try
    {
        if (!dbContext.IsConnected || dbContext.Songs == null)
        {
            return Results.Problem(
                detail: "MongoDB is not connected",
                title: "MongoDB Connection Failed",
                statusCode: 503
            );
        }

        var database = dbContext.Songs.Database;
        await database.RunCommandAsync<MongoDB.Bson.BsonDocument>(new MongoDB.Bson.BsonDocument("ping", 1));
        return Results.Ok(new { status = "healthy", service = "mongodb", timestamp = DateTime.UtcNow });
    }
    catch (Exception ex)
    {
        return Results.Problem(
            detail: ex.Message,
            title: "MongoDB Connection Failed",
            statusCode: 503
        );
    }
});

// Comprehensive MongoDB diagnostic endpoint
app.MapGet("/diagnostics/mongodb", async (IMongoClient mongoClient, MongoDbContext dbContext) =>
{
    var diagnostics = new
    {
        timestamp = DateTime.UtcNow,
        mongoClient = mongoClient != null ? "Available" : "Null",
        dbContextConnected = dbContext.IsConnected,
        connectionTest = false,
        collections = new
        {
            songs = dbContext.Songs != null ? "Available" : "Null",
            albums = dbContext.Albums != null ? "Available" : "Null",
            artists = dbContext.Artists != null ? "Available" : "Null",
            playlists = dbContext.Playlists != null ? "Available" : "Null"
        },
        error = (string?)null
    };

    try
    {
        if (mongoClient == null)
        {
            return Results.Ok(new
            {
                diagnostics.timestamp,
                diagnostics.mongoClient,
                diagnostics.dbContextConnected,
                diagnostics.collections,
                error = "MongoDB client is null"
            });
        }

        if (!dbContext.IsConnected)
        {
            return Results.Ok(new
            {
                diagnostics.timestamp,
                diagnostics.mongoClient,
                diagnostics.dbContextConnected,
                diagnostics.collections,
                error = "MongoDB context is not connected"
            });
        }

        // Test connection
        var connectionTest = await dbContext.TestConnectionAsync();

        return Results.Ok(new
        {
            diagnostics.timestamp,
            diagnostics.mongoClient,
            diagnostics.dbContextConnected,
            connectionTest,
            diagnostics.collections,
            diagnostics.error
        });
    }
    catch (Exception ex)
    {
        return Results.Ok(new
        {
            diagnostics.timestamp,
            diagnostics.mongoClient,
            diagnostics.dbContextConnected,
            diagnostics.collections,
            error = ex.Message
        });
    }
});



app.Run();