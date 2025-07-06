using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Data;
using Music.Models;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlbumsController : ControllerBase
{
    private readonly MongoDbContext _context;

    public AlbumsController(MongoDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<AlbumDto>>> GetAlbums()
    {
        var albums = await _context.Albums
            .Find(_ => true)
            .ToListAsync();

        var albumDtos = albums.Select(a => new AlbumDto
        {
            Id = a.Id,
            Title = a.Title,
            Songs = a.Songs.Select(s => new SongReferenceDto { Id = s.Id }).ToList(),
            Artist = new ArtistReferenceDto { Id = a.Artist.Id },
            CoverUrl = a.CoverUrl,
            ReleaseDate = a.ReleaseDate,
            CreatedAt = a.CreatedAt
        }).ToList();

        return Ok(albumDtos);
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<AlbumDto>> GetAlbum(string id)
    {
        var album = await _context.Albums
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync();

        if (album == null)
        {
            return NotFound();
        }

        var albumDto = new AlbumDto
        {
            Id = album.Id,
            Title = album.Title,
            Songs = album.Songs.Select(s => new SongReferenceDto { Id = s.Id }).ToList(),
            Artist = new ArtistReferenceDto { Id = album.Artist.Id },
            CoverUrl = album.CoverUrl,
            ReleaseDate = album.ReleaseDate,
            CreatedAt = album.CreatedAt
        };

        return Ok(albumDto);
    }

    [HttpPost]
    public async Task<ActionResult<AlbumDto>> CreateAlbum(CreateAlbumDto dto)
    {
        // Verify artist exists
        var artist = await _context.Artists
            .Find(a => a.Id == dto.ArtistId)
            .FirstOrDefaultAsync();

        if (artist == null)
        {
            return BadRequest("Artist not found");
        }

        var album = new Album
        {
            Title = dto.Title,
            Artist = new ArtistReference { Id = dto.ArtistId },
            CoverUrl = dto.CoverUrl,
            ReleaseDate = dto.ReleaseDate
        };

        await _context.Albums.InsertOneAsync(album);

        // Update artist's albums list
        await _context.Artists.UpdateOneAsync(
            a => a.Id == dto.ArtistId,
            Builders<Artist>.Update
                .Push(a => a.Albums, new AlbumReference { Id = album.Id })
                .Set(a => a.UpdatedAt, DateTime.UtcNow));

        var albumDto = new AlbumDto
        {
            Id = album.Id,
            Title = album.Title,
            Songs = album.Songs.Select(s => new SongReferenceDto { Id = s.Id }).ToList(),
            Artist = new ArtistReferenceDto { Id = album.Artist.Id },
            CoverUrl = album.CoverUrl,
            ReleaseDate = album.ReleaseDate,
            CreatedAt = album.CreatedAt
        };

        return CreatedAtAction(nameof(GetAlbum), new { id = album.Id }, albumDto);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAlbum(string id, UpdateAlbumDto dto)
    {
        var updateDefinition = Builders<Album>.Update
            .Set(a => a.UpdatedAt, DateTime.UtcNow);

        if (!string.IsNullOrEmpty(dto.Title))
            updateDefinition = updateDefinition.Set(a => a.Title, dto.Title);

        if (!string.IsNullOrEmpty(dto.CoverUrl))
            updateDefinition = updateDefinition.Set(a => a.CoverUrl, dto.CoverUrl);

        if (dto.ReleaseDate.HasValue)
            updateDefinition = updateDefinition.Set(a => a.ReleaseDate, dto.ReleaseDate.Value);

        var result = await _context.Albums.UpdateOneAsync(
            a => a.Id == id,
            updateDefinition);

        if (result.MatchedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteAlbum(string id)
    {
        var result = await _context.Albums.DeleteOneAsync(a => a.Id == id);

        if (result.DeletedCount == 0)
        {
            return NotFound();
        }

        return NoContent();
    }
}

public class AlbumDto
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public List<SongReferenceDto> Songs { get; set; } = new();
    public ArtistReferenceDto Artist { get; set; } = new();
    public string CoverUrl { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateAlbumDto
{
    public string Title { get; set; } = string.Empty;
    public string ArtistId { get; set; } = string.Empty;
    public string CoverUrl { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
}

public class UpdateAlbumDto
{
    public string? Title { get; set; }
    public string? CoverUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
}

public class SongReferenceDto
{
    public string Id { get; set; } = string.Empty;
}

public class ArtistReferenceDto
{
    public string Id { get; set; } = string.Empty;
} 