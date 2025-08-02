using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Data;
using Music.Models;
using Music.Services;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SongsController : ControllerBase
{
    private readonly MongoDbContext _context;
    private readonly IAzureBlobService _blobService;

    public SongsController(MongoDbContext context, IAzureBlobService blobService)
    {
        _context = context;
        _blobService = blobService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SongDto>>> GetSongs()
    {
        Console.WriteLine($"GetSongs called - IsConnected: {_context.IsConnected}, Songs collection: {_context.Songs != null}");
        
        if (!_context.IsConnected || _context.Songs == null)
        {
            Console.WriteLine("GetSongs: MongoDB not connected or Songs collection is null");
            return StatusCode(503, new { 
                error = "Service unavailable - database connection failed",
                details = "MongoDB is not accessible. Please check the connection string and server status.",
                timestamp = DateTime.UtcNow
            });
        }

        try
        {
            // Test connection before proceeding
            var isConnected = await _context.TestConnectionAsync();
            if (!isConnected)
            {
                Console.WriteLine("GetSongs: Connection test failed");
                return StatusCode(503, new { 
                    error = "Database connection failed. Please try again later.",
                    details = "MongoDB server is not responding to ping requests.",
                    timestamp = DateTime.UtcNow
                });
            }

            Console.WriteLine("GetSongs: Starting database query...");
            
            var songs = await _context.ExecuteWithRetryAsync(async () =>
            {
                return await _context.Songs!.Find(_ => true).ToListAsync();
            });

            if (songs == null)
            {
                Console.WriteLine("GetSongs: Database operation failed after retries");
                return StatusCode(503, new { 
                    error = "Database operation failed. Please try again later.",
                    details = "All retry attempts failed to retrieve data from MongoDB.",
                    timestamp = DateTime.UtcNow
                });
            }

            Console.WriteLine($"GetSongs: Found {songs.Count} songs in database");

            var songDtos = songs.Select(s => new SongDto
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

            Console.WriteLine("GetSongs: Successfully returning song data");
            return Ok(songDtos);
        }
        catch (MongoDB.Driver.MongoConnectionException ex)
        {
            Console.WriteLine($"GetSongs: MongoDB connection error: {ex.Message}");
            return StatusCode(503, new { 
                error = "Database connection failed. Please try again later.",
                details = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
        catch (MongoDB.Driver.MongoException ex) when (ex.Message.Contains("timeout") || ex.Message.Contains("Timeout"))
        {
            Console.WriteLine($"GetSongs: MongoDB timeout error: {ex.Message}");
            return StatusCode(503, new { 
                error = "Database operation timed out. Please try again later.",
                details = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetSongs: General error: {ex.Message}");
            return StatusCode(500, new { 
                error = "Internal server error",
                details = ex.Message,
                timestamp = DateTime.UtcNow
            });
        }
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SongDto>> GetSong(string id)
    {
        if (!_context.IsConnected || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var song = await _context.Songs
            .Find(s => s.Id == id)
            .FirstOrDefaultAsync();

        if (song == null)
        {
            return NotFound();
        }

        var songDto = new SongDto
        {
            Id = song.Id,
            Title = song.Title,
            Artists = song.Artists,
            Genre = song.Genre,
            DurationSec = song.DurationSec,
            Album = song.Album,
            FileUrl = song.FileUrl,
            SnippetUrl = song.SnippetUrl,
            CoverUrl = song.CoverUrl,
            CreatedAt = song.CreatedAt,
            ReleaseDate = song.ReleaseDate
        };

        return Ok(songDto);
    }

    [HttpPost]
    public async Task<ActionResult<SongDto>> CreateSong(CreateSongDto dto)
    {
        if (!_context.IsConnected || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var song = new Song
        {
            Title = dto.Title,
            Artists = dto.Artists,
            Genre = dto.Genre,
            DurationSec = dto.DurationSec,
            Album = dto.Album,
            FileUrl = dto.FileUrl,
            SnippetUrl = dto.SnippetUrl,
            CoverUrl = dto.CoverUrl,
            ReleaseDate = dto.ReleaseDate
        };

        await _context.Songs.InsertOneAsync(song);

        var songDto = new SongDto
        {
            Id = song.Id,
            Title = song.Title,
            Artists = song.Artists,
            Genre = song.Genre,
            DurationSec = song.DurationSec,
            Album = song.Album,
            FileUrl = song.FileUrl,
            SnippetUrl = song.SnippetUrl,
            CoverUrl = song.CoverUrl,
            CreatedAt = song.CreatedAt,
            ReleaseDate = song.ReleaseDate
        };

        return CreatedAtAction(nameof(GetSong), new { id = song.Id }, songDto);
    }

    [HttpPost("{id}/upload-file")]
    public async Task<IActionResult> UploadSongFile(string id, IFormFile audioFile)
    {
        if (!_context.IsConnected || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var song = await _context.Songs
            .Find(s => s.Id == id)
            .FirstOrDefaultAsync();

        if (song == null)
        {
            return NotFound("Song not found");
        }

        if (audioFile == null || audioFile.Length == 0)
        {
            return BadRequest("No file provided");
        }

        try
        {
            using var stream = audioFile.OpenReadStream();
            var fileUrl = await _blobService.UploadSongAsync(id, stream, audioFile.FileName);

            var updateDefinition = Builders<Song>.Update.Set(s => s.FileUrl, fileUrl);
            await _context.Songs.UpdateOneAsync(s => s.Id == id, updateDefinition);

            return Ok(new { fileUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error uploading file: {ex.Message}");
        }
    }

    [HttpPost("{id}/upload-cover")]
    public async Task<IActionResult> UploadSongCover(string id, IFormFile imageFile)
    {
        if (!_context.IsConnected || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var song = await _context.Songs
            .Find(s => s.Id == id)
            .FirstOrDefaultAsync();

        if (song == null)
        {
            return NotFound("Song not found");
        }

        if (imageFile == null || imageFile.Length == 0)
        {
            return BadRequest("No file provided");
        }

        try
        {
            using var stream = imageFile.OpenReadStream();
            var coverUrl = await _blobService.UploadSongCoverAsync(id, stream, imageFile.FileName);

            var updateDefinition = Builders<Song>.Update.Set(s => s.CoverUrl, coverUrl);
            await _context.Songs.UpdateOneAsync(s => s.Id == id, updateDefinition);

            return Ok(new { coverUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error uploading cover: {ex.Message}");
        }
    }

    [HttpPost("{id}/upload-snippet")]
    public async Task<IActionResult> UploadSongSnippet(string id, IFormFile audioFile)
    {
        if (!_context.IsConnected || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var song = await _context.Songs
            .Find(s => s.Id == id)
            .FirstOrDefaultAsync();

        if (song == null)
        {
            return NotFound("Song not found");
        }

        if (audioFile == null || audioFile.Length == 0)
        {
            return BadRequest("No file provided");
        }

        try
        {
            using var stream = audioFile.OpenReadStream();
            var snippetUrl = await _blobService.UploadSongSnippetAsync(id, stream, audioFile.FileName);

            var updateDefinition = Builders<Song>.Update.Set(s => s.SnippetUrl, snippetUrl);
            await _context.Songs.UpdateOneAsync(s => s.Id == id, updateDefinition);

            return Ok(new { snippetUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error uploading snippet: {ex.Message}");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSong(string id, UpdateSongDto dto)
    {
        if (!_context.IsConnected || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var updateDefinition = Builders<Song>.Update.Set("placeholder", "placeholder");

        if (!string.IsNullOrEmpty(dto.Title))
            updateDefinition = updateDefinition.Set(s => s.Title, dto.Title);

        if (dto.Artists != null)
            updateDefinition = updateDefinition.Set(s => s.Artists, dto.Artists);

        if (!string.IsNullOrEmpty(dto.Genre))
            updateDefinition = updateDefinition.Set(s => s.Genre, dto.Genre);

        if (dto.DurationSec.HasValue)
            updateDefinition = updateDefinition.Set(s => s.DurationSec, dto.DurationSec.Value);

        if (dto.Album != null)
            updateDefinition = updateDefinition.Set(s => s.Album, dto.Album);

        if (!string.IsNullOrEmpty(dto.FileUrl))
            updateDefinition = updateDefinition.Set(s => s.FileUrl, dto.FileUrl);

        if (!string.IsNullOrEmpty(dto.SnippetUrl))
            updateDefinition = updateDefinition.Set(s => s.SnippetUrl, dto.SnippetUrl);

        if (!string.IsNullOrEmpty(dto.CoverUrl))
            updateDefinition = updateDefinition.Set(s => s.CoverUrl, dto.CoverUrl);

        if (dto.ReleaseDate.HasValue)
            updateDefinition = updateDefinition.Set(s => s.ReleaseDate, dto.ReleaseDate.Value);

        var result = await _context.Songs.UpdateOneAsync(
            s => s.Id == id,
            updateDefinition);

        if (result.MatchedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteSong(string id)
    {
        if (!_context.IsConnected || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var result = await _context.Songs.DeleteOneAsync(s => s.Id == id);

        if (result.DeletedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}

public class SongDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<ArtistReference> Artists { get; set; } = new();
    public string Genre { get; set; } = string.Empty;
    public int DurationSec { get; set; }
    public AlbumReference? Album { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string? SnippetUrl { get; set; }
    public string CoverUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? ReleaseDate { get; set; }
}

public class CreateSongDto
{
    public string Title { get; set; } = string.Empty;
    public List<ArtistReference> Artists { get; set; } = new();
    public string Genre { get; set; } = string.Empty;
    public int DurationSec { get; set; }
    public AlbumReference? Album { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string? SnippetUrl { get; set; }
    public string CoverUrl { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
}

public class UpdateSongDto
{
    public string? Title { get; set; }
    public List<ArtistReference>? Artists { get; set; }
    public string? Genre { get; set; }
    public int? DurationSec { get; set; }
    public AlbumReference? Album { get; set; }
    public string? FileUrl { get; set; }
    public string? SnippetUrl { get; set; }
    public string? CoverUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
} 