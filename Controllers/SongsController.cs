using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Data;
using Music.Models;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SongsController : ControllerBase
{
    private readonly MongoDbContext _context;

    public SongsController(MongoDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<SongDto>>> GetSongs()
    {
        var songs = await _context.Songs
            .Find(_ => true)
            .ToListAsync();

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
            CreatedAt = s.CreatedAt
        }).ToList();

        return Ok(songDtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<SongDto>> GetSong(string id)
    {
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
            CreatedAt = song.CreatedAt
        };

        return Ok(songDto);
    }

    [HttpPost]
    public async Task<ActionResult<SongDto>> CreateSong(CreateSongDto dto)
    {
        var song = new Song
        {
            Title = dto.Title,
            Artists = dto.Artists,
            Genre = dto.Genre,
            DurationSec = dto.DurationSec,
            Album = dto.Album,
            FileUrl = dto.FileUrl,
            SnippetUrl = dto.SnippetUrl,
            CoverUrl = dto.CoverUrl
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
            CreatedAt = song.CreatedAt
        };

        return CreatedAtAction(nameof(GetSong), new { id = song.Id }, songDto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateSong(string id, UpdateSongDto dto)
    {
        var updateDefinition = Builders<Song>.Update
            .Set(s => s.UpdatedAt, DateTime.UtcNow);

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
    public string SnippetUrl { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}

public class CreateSongDto
{
    public string Title { get; set; } = string.Empty;
    public List<ArtistReference> Artists { get; set; } = new();
    public string Genre { get; set; } = string.Empty;
    public int DurationSec { get; set; }
    public AlbumReference? Album { get; set; }
    public string FileUrl { get; set; } = string.Empty;
    public string SnippetUrl { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
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
} 