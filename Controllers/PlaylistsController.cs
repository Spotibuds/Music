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
            Name = p.Name,
            Description = p.Description,
            Songs = p.Songs,
            CreatedAt = p.CreatedAt,
            UpdatedAt = p.UpdatedAt
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
            Name = playlist.Name,
            Description = playlist.Description,
            Songs = playlist.Songs,
            CreatedAt = playlist.CreatedAt,
            UpdatedAt = playlist.UpdatedAt
        };

        return Ok(playlistDto);
    }

    [HttpGet("{id}/songs")]
    public async Task<ActionResult<IEnumerable<Song>>> GetPlaylistSongs(string id)
    {
        var playlist = await _context.Playlists
            .Find(p => p.Id == id)
            .FirstOrDefaultAsync();

        if (playlist == null)
        {
            return NotFound();
        }

        var songIds = playlist.Songs.OrderBy(s => s.Position).Select(s => s.Id).ToList();
        var songs = await _context.Songs
            .Find(s => songIds.Contains(s.Id))
            .ToListAsync();

        // Sort songs by their position in the playlist
        var orderedSongs = songIds
            .Select(id => songs.FirstOrDefault(s => s.Id == id))
            .Where(s => s != null)
            .ToList();

        return Ok(orderedSongs);
    }

    [HttpPost]
    public async Task<ActionResult<PlaylistDto>> CreatePlaylist(CreatePlaylistDto dto)
    {
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
            Songs = playlist.Songs,
            CreatedAt = playlist.CreatedAt,
            UpdatedAt = playlist.UpdatedAt
        };

        return CreatedAtAction(nameof(GetPlaylist), new { id = playlist.Id }, playlistDto);
    }

    [HttpPost("{playlistId}/songs/{songId}")]
    public async Task<IActionResult> AddSongToPlaylist(string playlistId, string songId, [FromQuery] int position = -1)
    {
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

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlaylist(string id, UpdatePlaylistDto dto)
    {
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

    [HttpPut("{id}/reorder")]
    public async Task<IActionResult> ReorderPlaylistSongs(string id, ReorderSongsDto dto)
    {
        var playlist = await _context.Playlists
            .Find(p => p.Id == id)
            .FirstOrDefaultAsync();

        if (playlist == null)
        {
            return NotFound();
        }

        var updatedSongs = new List<SongReference>();
        for (int i = 0; i < dto.SongIds.Count; i++)
        {
            var existingSong = playlist.Songs.FirstOrDefault(s => s.Id == dto.SongIds[i]);
            if (existingSong != null)
            {
                existingSong.Position = i;
                updatedSongs.Add(existingSong);
            }
        }

        var updateDefinition = Builders<Playlist>.Update
            .Set(p => p.Songs, updatedSongs)
            .Set(p => p.UpdatedAt, DateTime.UtcNow);

        await _context.Playlists.UpdateOneAsync(p => p.Id == id, updateDefinition);

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
}

public class PlaylistDto
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public List<SongReference> Songs { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreatePlaylistDto
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class UpdatePlaylistDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

public class ReorderSongsDto
{
    public List<string> SongIds { get; set; } = new();
} 