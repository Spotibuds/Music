using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Data;
using Music.Models;
using Music.Services;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AlbumsController : ControllerBase
{
    private readonly MongoDbContext _context;
    private readonly IAzureBlobService _blobService;

    public AlbumsController(MongoDbContext context, IAzureBlobService blobService)
    {
        _context = context;
        _blobService = blobService;
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
            Songs = a.Songs,
            Artist = a.Artist,
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
            Songs = album.Songs,
            Artist = album.Artist,
            CoverUrl = album.CoverUrl,
            ReleaseDate = album.ReleaseDate,
            CreatedAt = album.CreatedAt
        };

        return Ok(albumDto);
    }

    [HttpGet("{id}/songs")]
    public async Task<ActionResult<IEnumerable<Song>>> GetAlbumSongs(string id)
    {
        var album = await _context.Albums
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync();

        if (album == null)
        {
            return NotFound();
        }

        var songIds = album.Songs.OrderBy(s => s.Position).Select(s => s.Id).ToList();
        var songs = await _context.Songs
            .Find(s => songIds.Contains(s.Id))
            .ToListAsync();

        // Sort songs by their position in the album
        var orderedSongs = songIds
            .Select(id => songs.FirstOrDefault(s => s.Id == id))
            .Where(s => s != null)
            .ToList();

        return Ok(orderedSongs);
    }

    [HttpPost]
    public async Task<ActionResult<AlbumDto>> CreateAlbum(CreateAlbumDto dto)
    {
        var album = new Album
        {
            Title = dto.Title,
            Artist = dto.Artist,
            ReleaseDate = dto.ReleaseDate
        };

        await _context.Albums.InsertOneAsync(album);

        var albumDto = new AlbumDto
        {
            Id = album.Id,
            Title = album.Title,
            Songs = album.Songs,
            Artist = album.Artist,
            CoverUrl = album.CoverUrl,
            ReleaseDate = album.ReleaseDate,
            CreatedAt = album.CreatedAt
        };

        return CreatedAtAction(nameof(GetAlbum), new { id = album.Id }, albumDto);
    }

    [HttpPost("{id}/upload-cover")]
    public async Task<IActionResult> UploadAlbumCover(string id, IFormFile imageFile)
    {
        if (imageFile == null || imageFile.Length == 0)
        {
            return BadRequest("No image file provided");
        }

        var album = await _context.Albums
            .Find(a => a.Id == id)
            .FirstOrDefaultAsync();

        if (album == null)
        {
            return NotFound();
        }

        try
        {
            using var stream = imageFile.OpenReadStream();
            var coverUrl = await _blobService.UploadAlbumCoverAsync(id, stream, imageFile.FileName);

            var updateDefinition = Builders<Album>.Update.Set(a => a.CoverUrl, coverUrl);
            await _context.Albums.UpdateOneAsync(a => a.Id == id, updateDefinition);

            return Ok(new { coverUrl });
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error uploading cover: {ex.Message}");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateAlbum(string id, UpdateAlbumDto dto)
    {
        var updateDefinition = Builders<Album>.Update.Set("placeholder", "placeholder");

        if (!string.IsNullOrEmpty(dto.Title))
            updateDefinition = updateDefinition.Set(a => a.Title, dto.Title);

        if (dto.Artist != null)
            updateDefinition = updateDefinition.Set(a => a.Artist, dto.Artist);

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

    [HttpPost("{albumId}/songs/{songId}")]
    public async Task<IActionResult> AddSongToAlbum(string albumId, string songId, [FromQuery] int position = -1)
    {
        var album = await _context.Albums
            .Find(a => a.Id == albumId)
            .FirstOrDefaultAsync();

        if (album == null)
        {
            return NotFound("Album not found");
        }

        var song = await _context.Songs
            .Find(s => s.Id == songId)
            .FirstOrDefaultAsync();

        if (song == null)
        {
            return NotFound("Song not found");
        }

        // Check if song is already in the album
        if (album.Songs.Any(s => s.Id == songId))
        {
            return BadRequest("Song is already in the album");
        }

        // If position is -1, add to the end
        if (position == -1)
        {
            position = album.Songs.Count;
        }
        else
        {
            // Shift existing songs to make room for the new position
            foreach (var songRef in album.Songs.Where(s => s.Position >= position))
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

        var updateDefinition = Builders<Album>.Update.Push(a => a.Songs, songReference);
        await _context.Albums.UpdateOneAsync(a => a.Id == albumId, updateDefinition);

        return Ok();
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
    public List<SongReference> Songs { get; set; } = new();
    public ArtistReference? Artist { get; set; }
    public string? CoverUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CreateAlbumDto
{
    public string Title { get; set; } = string.Empty;
    public ArtistReference? Artist { get; set; }
    public DateTime? ReleaseDate { get; set; }
}

public class UpdateAlbumDto
{
    public string? Title { get; set; }
    public ArtistReference? Artist { get; set; }
    public string? CoverUrl { get; set; }
    public DateTime? ReleaseDate { get; set; }
} 