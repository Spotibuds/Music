using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Data;
using Music.Models;
using System.Text.RegularExpressions;

namespace Music.Controllers;

[ApiController]
[Route("api/[controller]")]
public class SearchController : ControllerBase
{
    private readonly MongoDbContext _context;

    public SearchController(MongoDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<SearchResult>> Search([FromQuery] string q)
    {
        if (!_context.IsConnected || _context.Songs == null || _context.Albums == null || _context.Artists == null)
        {
            Console.WriteLine("Search: MongoDB not connected or collections are null");
            return StatusCode(503, "Service unavailable - database connection failed");
        }

        if (string.IsNullOrWhiteSpace(q))
        {
            return Ok(new SearchResult
            {
                Songs = new List<SongDto>(),
                Albums = new List<AlbumDto>(),
                Artists = new List<ArtistDto>()
            });
        }

        try
        {
            var query = q.Trim();
            var queryLower = query.ToLower();
            
            // Create regex patterns for better matching
            var words = queryLower.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var regexPatterns = words.Select(word => new Regex(word, RegexOptions.IgnoreCase)).ToList();

            // Search songs
            var songs = await _context.Songs.Find(_ => true).ToListAsync();
            var filteredSongs = songs.Where(song => 
                IsMatch(song.Title, regexPatterns) ||
                IsMatch(song.Album?.Title, regexPatterns) ||
                IsMatch(song.Genre, regexPatterns) ||
                (song.Artists != null && song.Artists.Any(artist => IsMatch(artist.Name, regexPatterns)))
            ).Take(20).ToList();

            // Search albums
            var albums = await _context.Albums.Find(_ => true).ToListAsync();
            var filteredAlbums = albums.Where(album => 
                IsMatch(album.Title, regexPatterns) ||
                IsMatch(album.Artist?.Name, regexPatterns)
            ).Take(20).ToList();

            // Search artists
            var artists = await _context.Artists.Find(_ => true).ToListAsync();
            var filteredArtists = artists.Where(artist => 
                IsMatch(artist.Name, regexPatterns) ||
                IsMatch(artist.Bio, regexPatterns)
            ).Take(20).ToList();

            var result = new SearchResult
            {
                Songs = filteredSongs.Select(s => new SongDto
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
                }).ToList(),
                Albums = filteredAlbums.Select(a => new AlbumDto
                {
                    Id = a.Id,
                    Title = a.Title,
                    Songs = a.Songs,
                    Artist = a.Artist,
                    CoverUrl = a.CoverUrl,
                    ReleaseDate = a.ReleaseDate,
                    CreatedAt = a.CreatedAt
                }).ToList(),
                Artists = filteredArtists.Select(a => new ArtistDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Bio = a.Bio,
                    ImageUrl = a.ImageUrl,
                    Albums = a.Albums,
                    CreatedAt = a.CreatedAt
                }).ToList()
            };

            return Ok(result);
        }
        catch (MongoDB.Driver.MongoConnectionException ex)
        {
            Console.WriteLine($"Search: MongoDB connection error: {ex.Message}");
            return StatusCode(503, "Database connection failed. Please try again later.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Search: General error: {ex.Message}");
            return StatusCode(500, new { message = "Search failed", error = ex.Message });
        }
    }

    private bool IsMatch(string? text, List<Regex> patterns)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var textLower = text.ToLower();
        
        // Check if all patterns match (AND logic)
        return patterns.All(pattern => pattern.IsMatch(textLower));
    }
}

public class SearchResult
{
    public List<SongDto> Songs { get; set; } = new();
    public List<AlbumDto> Albums { get; set; } = new();
    public List<ArtistDto> Artists { get; set; } = new();
} 