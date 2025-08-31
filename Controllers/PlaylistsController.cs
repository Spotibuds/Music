using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Data;
using Music.Models;
using Music.Services;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController : ControllerBase
{
    private readonly MongoDbContext _context;
    private readonly IAzureBlobService _blobService;

    public PlaylistsController(MongoDbContext context, IAzureBlobService blobService)
    {
        _context = context;
        _blobService = blobService;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlaylistDto>>> GetPlaylists()
    {
        if (!_context.IsConnected || _context.Playlists == null || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var playlists = await _context.Playlists
            .Find(_ => true)
            .ToListAsync();

        var playlistDtos = new List<PlaylistDto>();
        foreach (var playlist in playlists)
        {
            // Populate song details
            var playlistSongs = new List<PlaylistSongDto>();
            foreach (var songRef in playlist.Songs.OrderBy(s => s.Position))
            {
                var song = await _context.Songs
                    .Find(s => s.Id == songRef.Id)
                    .FirstOrDefaultAsync();

                if (song != null)
                {
                    playlistSongs.Add(new PlaylistSongDto
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
                        ReleaseDate = song.ReleaseDate,
                        Position = songRef.Position,
                        AddedAt = songRef.AddedAt
                    });
                }
            }

            playlistDtos.Add(new PlaylistDto
            {
                Id = playlist.Id,
                Name = playlist.Name,
                Description = playlist.Description,
                CreatedBy = playlist.CreatedBy,
                CoverUrl = playlist.CoverUrl,
                Songs = playlistSongs,
                CreatedAt = playlist.CreatedAt,
                UpdatedAt = playlist.UpdatedAt
            });
        }

        return Ok(playlistDtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PlaylistDto>> GetPlaylist(string id)
    {
        if (!_context.IsConnected || _context.Playlists == null || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var playlist = await _context.Playlists
            .Find(p => p.Id == id)
            .FirstOrDefaultAsync();

        if (playlist == null)
        {
            return NotFound();
        }

        // Populate song details
        var playlistSongs = new List<PlaylistSongDto>();
        foreach (var songRef in playlist.Songs.OrderBy(s => s.Position))
        {
            var song = await _context.Songs
                .Find(s => s.Id == songRef.Id)
                .FirstOrDefaultAsync();

            if (song != null)
            {
                playlistSongs.Add(new PlaylistSongDto
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
                    ReleaseDate = song.ReleaseDate,
                    Position = songRef.Position,
                    AddedAt = songRef.AddedAt
                });
            }
        }

        var playlistDto = new PlaylistDto
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            CreatedBy = playlist.CreatedBy,
            CoverUrl = playlist.CoverUrl,
            Songs = playlistSongs,
            CreatedAt = playlist.CreatedAt,
            UpdatedAt = playlist.UpdatedAt
        };

        return Ok(playlistDto);
    }

    [HttpPost]
    public async Task<ActionResult<PlaylistDto>> CreatePlaylist(CreatePlaylistDto dto)
    {
        if (!_context.IsConnected || _context.Playlists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var playlist = new Playlist
        {
            Name = dto.Name,
            Description = dto.Description
        };

        await _context.Playlists.InsertOneAsync(playlist);

        var playlistDto = new PlaylistDto
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            CreatedBy = playlist.CreatedBy,
            Songs = new List<PlaylistSongDto>(), // Empty list for new playlist
            CreatedAt = playlist.CreatedAt,
            UpdatedAt = playlist.UpdatedAt
        };

        return CreatedAtAction(nameof(GetPlaylist), new { id = playlist.Id }, playlistDto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlaylist(string id, UpdatePlaylistDto dto)
    {
        if (!_context.IsConnected || _context.Playlists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var updateDefinition = Builders<Playlist>.Update.Set(p => p.UpdatedAt, DateTime.UtcNow);

        if (!string.IsNullOrEmpty(dto.Name))
            updateDefinition = updateDefinition.Set(p => p.Name, dto.Name);

        if (dto.Description != null)
            updateDefinition = updateDefinition.Set(p => p.Description, dto.Description);

        var result = await _context.Playlists.UpdateOneAsync(
            p => p.Id == id,
            updateDefinition);

        if (result.MatchedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("{playlistId}/songs/{songId}")]
    public async Task<IActionResult> AddSongToPlaylist(string playlistId, string songId, [FromQuery] int position = -1)
    {
        if (!_context.IsConnected || _context.Playlists == null || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var playlist = await _context.Playlists
            .Find(p => p.Id == playlistId)
            .FirstOrDefaultAsync();

        if (playlist == null)
        {
            return NotFound("Playlist not found");
        }

        var song = await _context.Songs
            .Find(s => s.Id == songId)
            .FirstOrDefaultAsync();

        if (song == null)
        {
            return NotFound("Song not found");
        }

        // Check if song is already in the playlist
        if (playlist.Songs.Any(s => s.Id == songId))
        {
            return BadRequest("Song is already in the playlist");
        }

        // If position is -1, add to the end
        if (position == -1)
        {
            position = playlist.Songs.Count;
        }
        else
        {
            // Shift existing songs to make room for the new position
            foreach (var songRef in playlist.Songs.Where(s => s.Position >= position))
            {
                songRef.Position++;
            }
        }

        var songReference = new SongReference
        {
            Id = songId,
            Position = position,
            AddedAt = DateTime.UtcNow
        };

        var updateDefinition = Builders<Playlist>.Update
            .Push(p => p.Songs, songReference)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        await _context.Playlists.UpdateOneAsync(p => p.Id == playlistId, updateDefinition);

        return Ok();
    }

    [HttpDelete("{playlistId}/songs/{songId}")]
    public async Task<IActionResult> RemoveSongFromPlaylist(string playlistId, string songId)
    {
        if (!_context.IsConnected || _context.Playlists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var updateDefinition = Builders<Playlist>.Update
            .PullFilter(p => p.Songs, s => s.Id == songId)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        var result = await _context.Playlists.UpdateOneAsync(p => p.Id == playlistId, updateDefinition);

        if (result.MatchedCount == 0)
        {
            return NotFound("Playlist not found");
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlaylist(string id)
    {
        if (!_context.IsConnected || _context.Playlists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var result = await _context.Playlists.DeleteOneAsync(p => p.Id == id);

        if (result.DeletedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    // User-specific playlist endpoints
    [HttpGet("user/{userId}")]
    public async Task<ActionResult<IEnumerable<PlaylistDto>>> GetUserPlaylists(string userId)
    {
        if (!_context.IsConnected || _context.Playlists == null || _context.Songs == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var playlists = await _context.Playlists
            .Find(p => p.CreatedBy == userId)
            .ToListAsync();

        var playlistDtos = new List<PlaylistDto>();
        foreach (var playlist in playlists)
        {
            // Populate song details
            var playlistSongs = new List<PlaylistSongDto>();
            foreach (var songRef in playlist.Songs.OrderBy(s => s.Position))
            {
                var song = await _context.Songs
                    .Find(s => s.Id == songRef.Id)
                    .FirstOrDefaultAsync();

                if (song != null)
                {
                    playlistSongs.Add(new PlaylistSongDto
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
                        ReleaseDate = song.ReleaseDate,
                        Position = songRef.Position,
                        AddedAt = songRef.AddedAt
                    });
                }
            }

            playlistDtos.Add(new PlaylistDto
            {
                Id = playlist.Id,
                Name = playlist.Name,
                Description = playlist.Description,
                Songs = playlistSongs,
                CreatedBy = playlist.CreatedBy,
                CreatedAt = playlist.CreatedAt,
                UpdatedAt = playlist.UpdatedAt,
                CoverUrl = playlist.CoverUrl
            });
        }

        return Ok(playlistDtos);
    }

    [HttpPost("user/{userId}")]
    public async Task<ActionResult<PlaylistDto>> CreateUserPlaylist(string userId, CreateUserPlaylistDto dto)
    {
        if (!_context.IsConnected || _context.Playlists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        var playlist = new Playlist
        {
            Name = dto.Name,
            Description = dto.Description,
            CreatedBy = userId
        };

        await _context.Playlists.InsertOneAsync(playlist);

        var playlistDto = new PlaylistDto
        {
            Id = playlist.Id,
            Name = playlist.Name,
            Description = playlist.Description,
            Songs = new List<PlaylistSongDto>(), // Empty list for new playlist
            CreatedBy = playlist.CreatedBy,
            CreatedAt = playlist.CreatedAt,
            UpdatedAt = playlist.UpdatedAt
        };

        return CreatedAtAction(nameof(GetPlaylist), new { id = playlist.Id }, playlistDto);
    }

    [HttpPost("{id}/cover")]
    public async Task<IActionResult> UploadPlaylistCover(string id, IFormFile file)
    {
        if (!_context.IsConnected || _context.Playlists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        if (file == null || file.Length == 0)
        {
            return BadRequest(new { message = "No file provided" });
        }

        // Validate file type
        var allowedTypes = new[] { "image/jpeg", "image/jpg", "image/png", "image/webp" };
        if (!allowedTypes.Contains(file.ContentType?.ToLower()))
        {
            return BadRequest(new { message = "Invalid file type. Only JPEG, PNG, and WebP images are allowed." });
        }

        // Validate file size (max 10MB)
        if (file.Length > 10 * 1024 * 1024)
        {
            return BadRequest(new { message = "File size too large. Maximum size is 10MB." });
        }

        try
        {
            // Check if playlist exists
            var playlist = await _context.Playlists
                .Find(p => p.Id == id)
                .FirstOrDefaultAsync();

            if (playlist == null)
            {
                return NotFound(new { message = "Playlist not found" });
            }

            // Delete old cover if it exists
            if (!string.IsNullOrEmpty(playlist.CoverUrl))
            {
                try
                {
                    await _blobService.DeletePlaylistCoverFilesAsync(id);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Warning: Could not delete old playlist cover: {ex.Message}");
                }
            }

            // Upload new cover
            var coverUrl = await _blobService.UploadPlaylistCoverAsync(id, file.OpenReadStream(), file.FileName);
            Console.WriteLine($"Generated cover URL: {coverUrl}");
                
            // Update playlist with new cover URL
            var update = Builders<Playlist>.Update.Set(p => p.CoverUrl, coverUrl);
            var updateResult = await _context.Playlists.UpdateOneAsync(p => p.Id == id, update);
            Console.WriteLine($"MongoDB update result - Matched: {updateResult.MatchedCount}, Modified: {updateResult.ModifiedCount}");

            return Ok(new { coverUrl });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error uploading playlist cover: {ex.Message}");
            return StatusCode(500, new { message = "Failed to upload cover image" });
        }
    }

    [HttpDelete("{id}/cover")]
    public async Task<IActionResult> DeletePlaylistCover(string id)
    {
        if (!_context.IsConnected || _context.Playlists == null)
        {
            return StatusCode(503, new { message = "Database temporarily unavailable" });
        }

        try
        {
            // Check if playlist exists
            var playlist = await _context.Playlists
                .Find(p => p.Id == id)
                .FirstOrDefaultAsync();

            if (playlist == null)
            {
                return NotFound(new { message = "Playlist not found" });
            }

            // Delete cover from blob storage
            if (!string.IsNullOrEmpty(playlist.CoverUrl))
            {
                await _blobService.DeletePlaylistCoverFilesAsync(id);
            }

            // Remove cover URL from database
            var update = Builders<Playlist>.Update.Unset(p => p.CoverUrl);
            await _context.Playlists.UpdateOneAsync(p => p.Id == id, update);

            return Ok(new { message = "Cover image deleted successfully" });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error deleting playlist cover: {ex.Message}");
            return StatusCode(500, new { message = "Failed to delete cover image" });
        }
    }
}

public class PlaylistDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public string? CreatedBy { get; set; }
    public string? CoverUrl { get; set; }
    public List<PlaylistSongDto> Songs { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class PlaylistSongDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<ArtistReference> Artists { get; set; } = new();
    public string? Genre { get; set; }
    public int DurationSec { get; set; }
    public AlbumReference? Album { get; set; }
    public string? FileUrl { get; set; }
    public string? SnippetUrl { get; set; }
    public string? CoverUrl { get; set; }
    public DateTime? CreatedAt { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public int Position { get; set; }
    public DateTime AddedAt { get; set; }
}

public class CreatePlaylistDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class CreateUserPlaylistDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdatePlaylistDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
} 