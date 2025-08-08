using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Data;
using Music.Models;
using Music.Services;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ArtistsController : ControllerBase
{
    private readonly MongoDbContext _context;
    private readonly IAzureBlobService _blobService;

    public ArtistsController(MongoDbContext context, IAzureBlobService blobService)
    {
        _context = context;
        _blobService = blobService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<ArtistDto>>> GetArtists(
        [FromQuery] int? limit = null, 
        [FromQuery] int skip = 0)
    {
        Console.WriteLine($"GetArtists called - IsConnected: {_context.IsConnected}, Artists collection: {_context.Artists != null}");
        
        if (!_context.IsConnected || _context.Artists == null)
        {
            Console.WriteLine("GetArtists: MongoDB not connected or Artists collection is null");
            return StatusCode(503, "Service unavailable - database connection failed");
        }

        try
        {
            // Test connection before proceeding
            var isConnected = await _context.TestConnectionAsync();
            if (!isConnected)
            {
                Console.WriteLine("GetArtists: Connection test failed");
                return StatusCode(503, "Database connection failed. Please try again later.");
            }

            Console.WriteLine("GetArtists: Starting database query...");
            
            var artists = await _context.ExecuteWithRetryAsync(async () =>
            {
                var query = _context.Artists!.Find(_ => true).Skip(skip);
                
                // If no limit specified or limit > 100, default to 20 for performance
                int effectiveLimit = limit ?? 20;
                if (effectiveLimit > 100) effectiveLimit = 100;
                
                return await query.Limit(effectiveLimit).ToListAsync();
            });

            if (artists == null)
            {
                Console.WriteLine("GetArtists: Database operation failed after retries");
                return StatusCode(503, "Database operation failed. Please try again later.");
            }

            Console.WriteLine($"GetArtists: Found {artists.Count} artists in database");

            var artistDtos = artists.Select(a => new ArtistDto
            {
                Id = a.Id,
                Name = a.Name,
                Bio = a.Bio,
                ImageUrl = a.ImageUrl,
                CreatedAt = a.CreatedAt
            }).ToList();

            Console.WriteLine("GetArtists: Successfully returning artist data");
            return Ok(artistDtos);
        }
        catch (MongoDB.Driver.MongoConnectionException ex)
        {
            Console.WriteLine($"GetArtists: MongoDB connection error: {ex.Message}");
            return StatusCode(503, "Database connection failed. Please try again later.");
        }
        catch (MongoDB.Driver.MongoException ex) when (ex.Message.Contains("timeout") || ex.Message.Contains("Timeout"))
        {
            Console.WriteLine($"GetArtists: MongoDB timeout error: {ex.Message}");
            return StatusCode(503, "Database operation timed out. Please try again later.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetArtists: General error: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ArtistDto>> GetArtist(string id)
    {
        if (!_context.IsConnected || _context.Artists == null)
        {
            Console.WriteLine("GetArtist: MongoDB not connected or Artists collection is null");
            return StatusCode(503, "Service unavailable - database connection failed");
        }

        var artist = await _context.Artists
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync();

        if (artist == null)
        {
            return NotFound();
        }

        var artistDto = new ArtistDto
        {
            Id = artist.Id,
            Name = artist.Name,
            Bio = artist.Bio,
            ImageUrl = artist.ImageUrl,
            Albums = artist.Albums,
            CreatedAt = artist.CreatedAt
        };

        return Ok(artistDto);
    }

    [HttpPost]
    public async Task<ActionResult<ArtistDto>> CreateArtist(CreateArtistDto dto)
    {
        if (!_context.IsConnected || _context.Artists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var artist = new Artist
        {
            Name = dto.Name,
            Bio = dto.Bio
        };

        await _context.Artists.InsertOneAsync(artist);

        var artistDto = new ArtistDto
        {
            Id = artist.Id,
            Name = artist.Name,
            Bio = artist.Bio,
            ImageUrl = artist.ImageUrl,
            Albums = artist.Albums,
            CreatedAt = artist.CreatedAt
        };

        return CreatedAtAction(nameof(GetArtist), new { id = artist.Id }, artistDto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateArtist(string id, UpdateArtistDto dto)
    {
        if (!_context.IsConnected || _context.Artists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var updateDefinition = Builders<Artist>.Update.Set("placeholder", "placeholder");

        if (!string.IsNullOrEmpty(dto.Name))
            updateDefinition = updateDefinition.Set(a => a.Name, dto.Name);

        if (dto.Bio != null)
            updateDefinition = updateDefinition.Set(a => a.Bio, dto.Bio);

        var result = await _context.Artists.UpdateOneAsync(
            a => a.Id == id,
            updateDefinition);

        if (result.MatchedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("{id}/image")]
    public async Task<IActionResult> UploadArtistImage(string id, IFormFile image)
    {
        if (!_context.IsConnected || _context.Artists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var artist = await _context.Artists
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync();

        if (artist == null)
        {
            return NotFound("Artist not found");
        }

        if (image == null || image.Length == 0)
        {
            return BadRequest("No file provided");
        }

        try
        {
            using var stream = image.OpenReadStream();
            var imageUrl = await _blobService.UploadArtistImageAsync(id, stream, image.FileName);

            var updateDefinition = Builders<Artist>.Update.Set(a => a.ImageUrl, imageUrl);
            await _context.Artists.UpdateOneAsync(a => a.Id == id, updateDefinition);

            return Ok(new { imageUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error uploading image: {ex.Message}");
        }
    }

    [HttpGet("{id}/albums")]
    public async Task<ActionResult<object>> GetArtistAlbums(string id,
        [FromQuery] int? limit = null,
        [FromQuery] int skip = 0)
    {
        if (!_context.IsConnected || _context.Albums == null)
        {
            return StatusCode(503, "Service unavailable - database connection failed");
        }

        try
        {
            // First, get the artist to verify it exists and get the artist name
            var artist = await _context.Artists!.Find(a => a.Id == id).FirstOrDefaultAsync();
            if (artist == null)
            {
                return NotFound("Artist not found");
            }

            var albums = await _context.ExecuteWithRetryAsync(async () =>
            {
                var query = _context.Albums!.Find(album => album.Artist.Name == artist.Name).Skip(skip);
                
                int effectiveLimit = limit ?? 20;
                if (effectiveLimit > 100) effectiveLimit = 100;
                
                return await query.Limit(effectiveLimit).ToListAsync();
            });

            if (albums == null)
            {
                return StatusCode(503, "Database operation failed. Please try again later.");
            }

            var result = albums.Select(a => new
            {
                Id = a.Id,
                Title = a.Title,
                Artist = a.Artist,
                CoverUrl = a.CoverUrl,
                ReleaseDate = a.ReleaseDate,
                CreatedAt = a.CreatedAt
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetArtistAlbums error: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("{id}/songs")]
    public async Task<ActionResult<object>> GetArtistSongs(string id,
        [FromQuery] int? limit = null,
        [FromQuery] int skip = 0)
    {
        if (!_context.IsConnected || _context.Songs == null)
        {
            return StatusCode(503, "Service unavailable - database connection failed");
        }

        try
        {
            // First, get the artist to verify it exists and get the artist name
            var artist = await _context.Artists!.Find(a => a.Id == id).FirstOrDefaultAsync();
            if (artist == null)
            {
                return NotFound("Artist not found");
            }

            var songs = await _context.ExecuteWithRetryAsync(async () =>
            {
                var query = _context.Songs!.Find(song => 
                    song.Artists.Any(a => a.Id == artist.Id) || 
                    song.Artists.Any(a => a.Name == artist.Name)
                ).Skip(skip);
                
                int effectiveLimit = limit ?? 50;
                if (effectiveLimit > 100) effectiveLimit = 100;
                
                return await query.Limit(effectiveLimit).ToListAsync();
            });

            if (songs == null)
            {
                return StatusCode(503, "Database operation failed. Please try again later.");
            }

            var result = songs.Select(s => new
            {
                Id = s.Id,
                Title = s.Title,
                Artists = s.Artists,
                Genre = s.Genre,
                DurationSec = s.DurationSec,
                Album = s.Album,
                FileUrl = s.FileUrl,
                SnippetUrl = s.SnippetUrl,
                CoverUrl = s.CoverUrl,
                CreatedAt = s.CreatedAt,
                ReleaseDate = s.ReleaseDate
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetArtistSongs error: {ex.Message}");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteArtist(string id)
    {
        if (!_context.IsConnected || _context.Artists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var result = await _context.Artists.DeleteOneAsync(a => a.Id == id);

        if (result.DeletedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}

public class ArtistDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public string? ImageUrl { get; set; }
    public List<AlbumReference> Albums { get; set; } = new();
    public DateTime CreatedAt { get; set; }
}

public class CreateArtistDto
{
    public string Name { get; set; } = string.Empty;
    public string? Bio { get; set; }
}

public class UpdateArtistDto
{
    public string? Name { get; set; }
    public string? Bio { get; set; }
} 