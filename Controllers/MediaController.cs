using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Music.Services;
using StackExchange.Redis;
using System.IO.Compression;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class MediaController : ControllerBase
{
    private readonly IAzureBlobService _blobService;
    private readonly IMemoryCache _memoryCache;
    private readonly IDatabase _redisDatabase;
    private readonly IConfiguration _configuration;

    public MediaController(IAzureBlobService blobService, IMemoryCache memoryCache, IDatabase redisDatabase, IConfiguration configuration)
    {
        _blobService = blobService;
        _memoryCache = memoryCache;
        _redisDatabase = redisDatabase;
        _configuration = configuration;
    }

    [HttpGet("image")]
    public async Task<IActionResult> GetImage([FromQuery] string url)
    {
        
        try
        {
            if (string.IsNullOrEmpty(url))
            {
                return BadRequest("URL parameter is required");
            }

            // Create cache key from URL
            var cacheKey = $"image_{url.GetHashCode()}";
            
            
            // 1. Check Redis cache first
            try
            {
                var redisImage = await _redisDatabase.StringGetAsync(cacheKey);
                if (redisImage.HasValue)
                {
                    
                    var redisImageBytes = (byte[])redisImage!;
                    
                    // Also cache in memory for even faster access
                    var memoryCacheOptions = new MemoryCacheEntryOptions
                    {
                        SlidingExpiration = TimeSpan.FromMinutes(30),
                        Size = redisImageBytes.Length,
                        Priority = CacheItemPriority.High
                    };
                    _memoryCache.Set(cacheKey, redisImageBytes, memoryCacheOptions);
                    
                    return File(redisImageBytes, GetContentType(url), enableRangeProcessing: false);
                }
            }
            catch (Exception redisEx)
{
    // Redis cache error - continue with fallback
}
            
            // 2. Check memory cache as fallback
            if (_memoryCache.TryGetValue(cacheKey, out byte[]? cachedImage) && cachedImage != null)
            {
                
                return File(cachedImage, GetContentType(url), enableRangeProcessing: false);
            }

            // 3. Download from Azure Blob Storage
            var uri = new Uri(url);
            var pathSegments = uri.AbsolutePath.TrimStart('/').Split('/');
            
            if (pathSegments.Length < 2)
            {
                return BadRequest("Invalid Azure Blob URL format");
            }

            var containerName = pathSegments[0];
            var blobName = string.Join("/", pathSegments.Skip(1));

            

            // Download the image from Azure Blob Storage
            var imageStream = await _blobService.DownloadFileAsync(containerName, blobName);
            
            // Read stream into memory for caching
            using var memoryStream = new MemoryStream();
            await imageStream.CopyToAsync(memoryStream);
            var imageBytes = memoryStream.ToArray();

            // 4. Cache in both Redis and Memory (async, don't block response)
            _ = Task.Run(async () =>
            {
                try
                {
                    // Cache in Redis for 6 hours
                    var success = await _redisDatabase.StringSetAsync(cacheKey, imageBytes, TimeSpan.FromHours(6));

// Verify it was stored
var verify = await _redisDatabase.StringGetAsync(cacheKey);
                }
                catch (Exception ex)
{
    // Redis cache error - continue with memory cache
}
                
                // Cache in memory for 1 hour
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    SlidingExpiration = TimeSpan.FromHours(1),
                    Size = imageBytes.Length,
                    Priority = CacheItemPriority.Normal
                };
                _memoryCache.Set(cacheKey, imageBytes, cacheOptions);
                
            });

            // Determine content type based on file extension
            var contentType = GetContentType(blobName);
            
            // Set aggressive cache headers for browser caching
            Response.Headers["Cache-Control"] = "public, max-age=86400, stale-while-revalidate=604800"; // 1 day cache, 1 week stale
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["ETag"] = $"\"{url.GetHashCode()}\"";
            
            return File(imageBytes, contentType, enableRangeProcessing: false);
        }
        catch (Exception ex)
{
    return NotFound("Image not found");
}
    }

    private static string GetContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            _ => "application/octet-stream"
        };
    }

    [HttpGet("audio")]
    public async Task<IActionResult> GetAudio([FromQuery] string url)
    {
        try
        {
            if (string.IsNullOrEmpty(url))
            {
                return BadRequest("URL parameter is required");
            }

            // Parse the Azure Blob URL to extract container and blob name
            var uri = new Uri(url);
            var pathSegments = uri.AbsolutePath.TrimStart('/').Split('/');
            
            if (pathSegments.Length < 2)
            {
                return BadRequest("Invalid Azure Blob URL format");
            }

            var containerName = pathSegments[0];
            var blobName = string.Join("/", pathSegments.Skip(1));

            // Download the audio from Azure Blob Storage
            var audioStream = await _blobService.DownloadFileAsync(containerName, blobName);
            
            // Determine content type based on file extension
            var contentType = GetAudioContentType(blobName);
            
            // Set headers for audio streaming with caching
            Response.Headers["Accept-Ranges"] = "bytes";
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Access-Control-Allow-Headers"] = "Range";
            Response.Headers["Cache-Control"] = "public, max-age=3600"; // 1 hour cache
            
            return File(audioStream, contentType, enableRangeProcessing: true);
        }
        catch (Exception ex)
{
    return NotFound("Audio not found");
}
    }

    private static string GetAudioContentType(string fileName)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return extension switch
        {
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".flac" => "audio/flac",
            _ => "application/octet-stream"
        };
    }

    [HttpGet("cache/status")]
    public async Task<IActionResult> GetCacheStatus()
    {
        try
        {
            var redisConnectionString = _configuration.GetConnectionString("Redis");
            var server = _redisDatabase.Multiplexer.GetServer(redisConnectionString);
            var keys = server.Keys(pattern: "image_*").Take(20).ToList();
            
            var cacheInfo = new
            {
                TotalImageKeys = keys.Count,
                SampleKeys = keys.Select(k => k.ToString()).ToList(),
                RedisInfo = await _redisDatabase.PingAsync(),
                ConnectionString = redisConnectionString // Show what connection string is being used
            };

            return Ok(cacheInfo);
        }
        catch (Exception ex)
        {
            return Ok(new { Error = ex.Message, Status = "Redis connection failed" });
        }
    }

    [HttpGet("cache/clear")]
    public async Task<IActionResult> ClearCache()
    {
        try
        {
            var redisConnectionString = _configuration.GetConnectionString("Redis");
            var server = _redisDatabase.Multiplexer.GetServer(redisConnectionString);
            var keys = server.Keys(pattern: "image_*");
            var deletedCount = 0;

            foreach (var key in keys)
            {
                await _redisDatabase.KeyDeleteAsync(key);
                deletedCount++;
            }

            return Ok(new { Message = $"Cleared {deletedCount} cached images" });
        }
        catch (Exception ex)
        {
            return Ok(new { Error = ex.Message });
        }
    }
} 