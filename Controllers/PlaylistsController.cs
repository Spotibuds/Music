using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Data;
using Music.Models;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PlaylistsController : ControllerBase
{
    private readonly MongoDbContext _context;

    public PlaylistsController(MongoDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<PlaylistDto>>> GetPlaylists()
    {
        var playlists = await _context.Playlists
            .Find(_ => true)
            .ToListAsync();

        var playlistDtos = playlists.Select(p => new PlaylistDto
        {
            Id = p.Id,
            Title = p.Title,
            Songs = p.Songs.Select(s => new SongReferenceDto { Id = s.Id }).ToList(),
            OwnerId = p.OwnerId,
            IsPublic = p.IsPublic,
            CreatedAt = p.CreatedAt
        }).ToList();

        return Ok(playlistDtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<PlaylistDto>> GetPlaylist(string id)
    {
        var playlist = await _context.Playlists
            .Find(p => p.Id == id)
            .FirstOrDefaultAsync();

        if (playlist == null)
        {
            return NotFound();
        }

        var playlistDto = new PlaylistDto
        {
            Id = playlist.Id,
            Title = playlist.Title,
            Songs = playlist.Songs.Select(s => new SongReferenceDto { Id = s.Id }).ToList(),
            OwnerId = playlist.OwnerId,
            IsPublic = playlist.IsPublic,
            CreatedAt = playlist.CreatedAt
        };

        return Ok(playlistDto);
    }

    [HttpPost]
    public async Task<ActionResult<PlaylistDto>> CreatePlaylist(CreatePlaylistDto dto)
    {
        var playlist = new Playlist
        {
            Title = dto.Title,
            OwnerId = dto.OwnerId,
            IsPublic = dto.IsPublic
        };

        await _context.Playlists.InsertOneAsync(playlist);

        var playlistDto = new PlaylistDto
        {
            Id = playlist.Id,
            Title = playlist.Title,
            Songs = playlist.Songs.Select(s => new SongReferenceDto { Id = s.Id }).ToList(),
            OwnerId = playlist.OwnerId,
            IsPublic = playlist.IsPublic,
            CreatedAt = playlist.CreatedAt
        };

        return CreatedAtAction(nameof(GetPlaylist), new { id = playlist.Id }, playlistDto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlaylist(string id, UpdatePlaylistDto dto)
    {
        var updateDefinition = Builders<Playlist>.Update
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        if (!string.IsNullOrEmpty(dto.Title))
            updateDefinition = updateDefinition.Set(p => p.Title, dto.Title);

        if (dto.IsPublic.HasValue)
            updateDefinition = updateDefinition.Set(p => p.IsPublic, dto.IsPublic.Value);

        var result = await _context.Playlists.UpdateOneAsync(
            p => p.Id == id,
            updateDefinition);

        if (result.MatchedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeletePlaylist(string id)
    {
        var result = await _context.Playlists.DeleteOneAsync(p => p.Id == id);

        if (result.DeletedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpPost("{playlistId}/songs/{songId}")]
    public async Task<IActionResult> AddSongToPlaylist(string playlistId, string songId)
    {
        // Check if song exists
        var song = await _context.Songs
            .Find(s => s.Id == songId)
            .FirstOrDefaultAsync();

        if (song == null)
        {
            return NotFound("Song not found");
        }

        // Check if playlist exists
        var playlist = await _context.Playlists
            .Find(p => p.Id == playlistId)
            .FirstOrDefaultAsync();

        if (playlist == null)
        {
            return NotFound("Playlist not found");
        }

        // Check if song is already in playlist
        if (playlist.Songs.Any(s => s.Id == songId))
        {
            return BadRequest("Song is already in playlist");
        }

        // Add song to playlist
        await _context.Playlists.UpdateOneAsync(
            p => p.Id == playlistId,
            Builders<Playlist>.Update
                .Push(p => p.Songs, new SongReference { Id = songId })
                .Set(p => p.UpdatedAt, DateTime.UtcNow));

        return Ok(new { message = "Song added to playlist successfully" });
    }

    [HttpDelete("{playlistId}/songs/{songId}")]
    public async Task<IActionResult> RemoveSongFromPlaylist(string playlistId, string songId)
    {
        // Remove song from playlist
        await _context.Playlists.UpdateOneAsync(
            p => p.Id == playlistId,
            Builders<Playlist>.Update
                .PullFilter(p => p.Songs, s => s.Id == songId)
                .Set(p => p.UpdatedAt, DateTime.UtcNow));

        return Ok(new { message = "Song removed from playlist successfully" });
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<IEnumerable<PlaylistDto>>> GetUserPlaylists(string userId)
    {
        var playlists = await _context.Playlists
            .Find(p => p.OwnerId == userId)
            .ToListAsync();

        var playlistDtos = playlists.Select(p => new PlaylistDto
        {
            Id = p.Id,
            Title = p.Title,
            Songs = p.Songs.Select(s => new SongReferenceDto { Id = s.Id }).ToList(),
            OwnerId = p.OwnerId,
            IsPublic = p.IsPublic,
            CreatedAt = p.CreatedAt
        }).ToList();

        return Ok(playlistDtos);
    }
}

public class PlaylistDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<SongReferenceDto> Songs { get; set; } = new();
    public string OwnerId { get; set; } = string.Empty;
    public bool IsPublic { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreatePlaylistDto
{
    public string Title { get; set; } = string.Empty;
    public string OwnerId { get; set; } = string.Empty;
    public bool IsPublic { get; set; } = true;
}

public class UpdatePlaylistDto
{
    public string? Title { get; set; }
    public bool? IsPublic { get; set; }
} 