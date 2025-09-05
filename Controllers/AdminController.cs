using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using MongoDB.Bson;
using Music.Data;
using Music.Services;
using Music.Models;

namespace Music.Controllers;

[ApiController]
[Route("api/admin")]
public class AdminController : ControllerBase
{
    private readonly MongoDbContext _context;
    private readonly IAzureBlobService _blobService;
    private readonly ILogger<AdminController> _logger;

    public AdminController(MongoDbContext context, IAzureBlobService blobService, ILogger<AdminController> logger)
    {
        _context = context;
        _blobService = blobService;
        _logger = logger;
    }

    [HttpPost("artists")]
    public async Task<ActionResult<Artist>> CreateArtist([FromForm] CreateArtistRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Artist name is required");
            }

            // Check if artist already exists
            var existingArtist = await _context.Artists!.Find(a => a.Name == request.Name).FirstOrDefaultAsync();
            if (existingArtist != null)
            {
                return Conflict($"Artist with name '{request.Name}' already exists");
            }

            var artist = new Artist
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = request.Name,
                Bio = request.Bio,
                CreatedAt = DateTime.UtcNow
            };

            // Upload artist image if provided
            if (request.ImageFile != null && request.ImageFile.Length > 0)
            {
                try
                {
                    var imageUrl = await _blobService.UploadArtistImageAsync(
                        artist.Id,
                        request.ImageFile.OpenReadStream(),
                        request.ImageFile.FileName
                    );
                    artist.ImageUrl = imageUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload artist image for {ArtistName}", request.Name);
                    return StatusCode(500, "Failed to upload artist image");
                }
            }

            await _context.Artists.InsertOneAsync(artist);
            _logger.LogInformation("Created artist: {ArtistName} with ID: {ArtistId}", artist.Name, artist.Id);

            return CreatedAtAction(nameof(GetArtist), new { id = artist.Id }, artist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating artist");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("albums")]
    public async Task<ActionResult<Album>> CreateAlbum([FromForm] CreateAlbumRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest("Album title is required");
            }

            if (string.IsNullOrWhiteSpace(request.ArtistId))
            {
                return BadRequest("Artist ID is required");
            }

            // Verify artist exists
            var artist = await _context.Artists!.Find(a => a.Id == request.ArtistId).FirstOrDefaultAsync();
            if (artist == null)
            {
                return BadRequest("Artist not found");
            }

            // Check if album already exists for this artist
            var existingAlbum = await _context.Albums!.Find(a => a.Title == request.Title && a.Artist != null && a.Artist.Id == request.ArtistId).FirstOrDefaultAsync();
            if (existingAlbum != null)
            {
                return Conflict($"Album '{request.Title}' already exists for this artist");
            }

            var album = new Album
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Title = request.Title,
                Artist = new ArtistReference { Id = artist.Id, Name = artist.Name },
                ReleaseDate = request.ReleaseDate,
                CreatedAt = DateTime.UtcNow
            };

            // Upload album cover if provided
            if (request.CoverFile != null && request.CoverFile.Length > 0)
            {
                try
                {
                    var coverUrl = await _blobService.UploadAlbumCoverAsync(
                        album.Id,
                        request.CoverFile.OpenReadStream(),
                        request.CoverFile.FileName
                    );
                    album.CoverUrl = coverUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload album cover for {AlbumTitle}", request.Title);
                    return StatusCode(500, "Failed to upload album cover");
                }
            }

            await _context.Albums.InsertOneAsync(album);

            // Update artist's albums list
            var artistFilter = Builders<Artist>.Filter.Eq(a => a.Id, artist.Id);
            var artistUpdate = Builders<Artist>.Update.Push(a => a.Albums, new AlbumReference { Id = album.Id, Title = album.Title });
            await _context.Artists.UpdateOneAsync(artistFilter, artistUpdate);

            _logger.LogInformation("Created album: {AlbumTitle} with ID: {AlbumId}", album.Title, album.Id);

            return CreatedAtAction(nameof(GetAlbum), new { id = album.Id }, album);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating album");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("songs")]
    public async Task<ActionResult<Song>> CreateSong([FromForm] CreateSongRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Title))
            {
                return BadRequest("Song title is required");
            }

            if (string.IsNullOrWhiteSpace(request.ArtistId))
            {
                return BadRequest("Artist ID is required");
            }

            if (request.AudioFile == null || request.AudioFile.Length == 0)
            {
                return BadRequest("Audio file is required");
            }

            if (request.Duration <= 0)
            {
                return BadRequest("Duration must be greater than 0");
            }

            // Verify artist exists
            var artist = await _context.Artists!.Find(a => a.Id == request.ArtistId).FirstOrDefaultAsync();
            if (artist == null)
            {
                return BadRequest("Artist not found");
            }

            // Verify album exists if provided
            Album? album = null;
            if (!string.IsNullOrWhiteSpace(request.AlbumId))
            {
                album = await _context.Albums!.Find(a => a.Id == request.AlbumId).FirstOrDefaultAsync();
                if (album == null)
                {
                    return BadRequest("Album not found");
                }
            }

            var song = new Song
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Title = request.Title,
                Artists = new List<ArtistReference> { new ArtistReference { Id = artist.Id, Name = artist.Name } },
                Genre = request.Genre ?? string.Empty,
                DurationSec = request.Duration,
                CreatedAt = DateTime.UtcNow
            };

            if (album != null)
            {
                song.Album = new AlbumReference { Id = album.Id, Title = album.Title };
            }

            // Upload audio file
            try
            {
                var audioUrl = await _blobService.UploadSongAsync(
                    song.Id,
                    request.AudioFile.OpenReadStream(),
                    request.AudioFile.FileName
                );
                song.FileUrl = audioUrl;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload audio file for song {SongTitle}", request.Title);
                return StatusCode(500, "Failed to upload audio file");
            }

            // Upload cover image if provided
            if (request.CoverFile != null && request.CoverFile.Length > 0)
            {
                try
                {
                    var coverUrl = await _blobService.UploadSongCoverAsync(
                        song.Id,
                        request.CoverFile.OpenReadStream(),
                        request.CoverFile.FileName
                    );
                    song.CoverUrl = coverUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload cover image for song {SongTitle}", request.Title);
                    // Don't fail the entire operation for cover upload failure
                }
            }

            // Upload snippet if provided
            if (request.SnippetFile != null && request.SnippetFile.Length > 0)
            {
                try
                {
                    var snippetUrl = await _blobService.UploadSongSnippetAsync(
                        song.Id,
                        request.SnippetFile.OpenReadStream(),
                        request.SnippetFile.FileName
                    );
                    song.SnippetUrl = snippetUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload snippet for song {SongTitle}", request.Title);
                    // Don't fail the entire operation for snippet upload failure
                }
            }

            await _context.Songs.InsertOneAsync(song);

            // Add song to album if specified
            if (album != null)
            {
                var songReference = new SongReference
                {
                    Id = song.Id,
                    Position = album.Songs.Count,
                    AddedAt = DateTime.UtcNow
                };

                var albumFilter = Builders<Album>.Filter.Eq(a => a.Id, album.Id);
                var albumUpdate = Builders<Album>.Update.Push(a => a.Songs, songReference);
                await _context.Albums.UpdateOneAsync(albumFilter, albumUpdate);
            }

            _logger.LogInformation("Created song: {SongTitle} with ID: {SongId}", song.Title, song.Id);

            return CreatedAtAction(nameof(GetSong), new { id = song.Id }, song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating song");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("artists/{id}")]
    public async Task<ActionResult<Artist>> GetArtist(string id)
    {
        try
        {
            var artist = await _context.Artists!.Find(a => a.Id == id).FirstOrDefaultAsync();
            if (artist == null)
            {
                return NotFound();
            }
            return Ok(artist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting artist {ArtistId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("albums/{id}")]
    public async Task<ActionResult<Album>> GetAlbum(string id)
    {
        try
        {
            var album = await _context.Albums!.Find(a => a.Id == id).FirstOrDefaultAsync();
            if (album == null)
            {
                return NotFound();
            }
            return Ok(album);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting album {AlbumId}", id);
            return StatusCode(500, "Internal server error");
        }
    }


    [HttpGet("songs/{id}")]
    public async Task<ActionResult<Song>> GetSong(string id)
    {
        try
        {
            var song = await _context.Songs!.Find(s => s.Id == id).FirstOrDefaultAsync();
            if (song == null)
            {
                return NotFound();
            }
            return Ok(song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting song {SongId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("artists")]
    public async Task<ActionResult<IEnumerable<Artist>>> GetAllArtists()
    {
        try
        {
            var artists = await _context.Artists!.Find(_ => true).ToListAsync();
            return Ok(artists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all artists");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("albums")]
    public async Task<ActionResult<IEnumerable<Album>>> GetAllAlbums()
    {
        try
        {
            var albums = await _context.Albums!.Find(_ => true).ToListAsync();
            return Ok(albums);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all albums");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("songs")]
    public async Task<ActionResult<IEnumerable<Song>>> GetAllSongs()
    {
        try
        {
            var songs = await _context.Songs!.Find(_ => true).ToListAsync();
            return Ok(songs);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all songs");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("playlists")]
    public async Task<ActionResult<Playlist>> CreatePlaylist([FromBody] CreatePlaylistRequest request)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(request.Name))
            {
                return BadRequest("Playlist name is required");
            }

            var playlist = new Playlist
            {
                Id = ObjectId.GenerateNewId().ToString(),
                Name = request.Name,
                Description = request.Description,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _context.Playlists!.InsertOneAsync(playlist);
            _logger.LogInformation("Created playlist: {PlaylistName} with ID: {PlaylistId}", playlist.Name, playlist.Id);

            return CreatedAtAction(nameof(GetPlaylist), new { id = playlist.Id }, playlist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating playlist");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("playlists/{id}")]
    public async Task<ActionResult<Playlist>> GetPlaylist(string id)
    {
        try
        {
            var playlist = await _context.Playlists!.Find(p => p.Id == id).FirstOrDefaultAsync();
            if (playlist == null)
            {
                return NotFound();
            }
            return Ok(playlist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting playlist {PlaylistId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("playlists")]
    public async Task<ActionResult<IEnumerable<Playlist>>> GetAllPlaylists()
    {
        try
        {
            var playlists = await _context.Playlists!.Find(_ => true).ToListAsync();
            return Ok(playlists);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting all playlists");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("playlists/{playlistId}/songs")]
    public async Task<ActionResult> AddSongToPlaylist(string playlistId, [FromBody] AddSongToPlaylistRequest request)
    {
        try
        {
            var playlist = await _context.Playlists!.Find(p => p.Id == playlistId).FirstOrDefaultAsync();
            if (playlist == null)
            {
                return NotFound("Playlist not found");
            }

            var song = await _context.Songs!.Find(s => s.Id == request.SongId).FirstOrDefaultAsync();
            if (song == null)
            {
                return BadRequest("Song not found");
            }

            // Check if song is already in playlist
            if (playlist.Songs.Any(s => s.Id == request.SongId))
            {
                return BadRequest("Song is already in the playlist");
            }

            var songReference = new SongReference
            {
                Id = request.SongId,
                Position = request.Position ?? playlist.Songs.Count,
                AddedAt = DateTime.UtcNow
            };

            var filter = Builders<Playlist>.Filter.Eq(p => p.Id, playlistId);
            var update = Builders<Playlist>.Update
                .Push(p => p.Songs, songReference)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            await _context.Playlists.UpdateOneAsync(filter, update);
            _logger.LogInformation("Added song {SongId} to playlist {PlaylistId}", request.SongId, playlistId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding song to playlist");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("playlists/{playlistId}/songs/{songId}")]
    public async Task<ActionResult> RemoveSongFromPlaylist(string playlistId, string songId)
    {
        try
        {
            var playlist = await _context.Playlists!.Find(p => p.Id == playlistId).FirstOrDefaultAsync();
            if (playlist == null)
            {
                return NotFound("Playlist not found");
            }

            var filter = Builders<Playlist>.Filter.Eq(p => p.Id, playlistId);
            var update = Builders<Playlist>.Update
                .PullFilter(p => p.Songs, s => s.Id == songId)
                .Set(p => p.UpdatedAt, DateTime.UtcNow);

            await _context.Playlists.UpdateOneAsync(filter, update);
            _logger.LogInformation("Removed song {SongId} from playlist {PlaylistId}", songId, playlistId);

            return Ok();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing song from playlist");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("playlists/{id}")]
    public async Task<ActionResult> DeletePlaylist(string id)
    {
        try
        {
            var playlist = await _context.Playlists!.Find(p => p.Id == id).FirstOrDefaultAsync();
            if (playlist == null)
            {
                return NotFound();
            }

            await _context.Playlists.DeleteOneAsync(p => p.Id == id);
            _logger.LogInformation("Deleted playlist: {PlaylistName} with ID: {PlaylistId}", playlist.Name, playlist.Id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting playlist {PlaylistId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPost("bulk/songs")]
    public async Task<ActionResult<BulkUploadResult>> BulkCreateSongs([FromForm] BulkCreateSongsRequest request)
    {
        try
        {
            if (request.SongsData == null || !request.SongsData.Any())
            {
                return BadRequest("Songs data is required");
            }

            if (request.AudioFiles == null || !request.AudioFiles.Any())
            {
                return BadRequest("Audio files are required");
            }

            var result = new BulkUploadResult();
            var createdSongs = new List<Song>();
            var errors = new List<string>();

            // Verify artist exists
            var artist = await _context.Artists!.Find(a => a.Id == request.ArtistId).FirstOrDefaultAsync();
            if (artist == null)
            {
                return BadRequest("Artist not found");
            }

            // Verify album exists if provided
            Album? album = null;
            if (!string.IsNullOrWhiteSpace(request.AlbumId))
            {
                album = await _context.Albums!.Find(a => a.Id == request.AlbumId).FirstOrDefaultAsync();
                if (album == null)
                {
                    return BadRequest("Album not found");
                }
            }

            for (int i = 0; i < request.SongsData.Count && i < request.AudioFiles.Count; i++)
            {
                try
                {
                    var songData = request.SongsData[i];
                    var audioFile = request.AudioFiles[i];

                    var song = new Song
                    {
                        Id = ObjectId.GenerateNewId().ToString(),
                        Title = songData.Title,
                        Artists = new List<ArtistReference> { new ArtistReference { Id = artist.Id, Name = artist.Name } },
                        Genre = songData.Genre ?? string.Empty,
                        DurationSec = songData.Duration,
                        CreatedAt = DateTime.UtcNow
                    };

                    if (album != null)
                    {
                        song.Album = new AlbumReference { Id = album.Id, Title = album.Title };
                    }

                    // Upload audio file
                    try
                    {
                        var audioUrl = await _blobService.UploadSongAsync(
                            song.Id,
                            audioFile.OpenReadStream(),
                            audioFile.FileName
                        );
                        song.FileUrl = audioUrl;
                    }
                    catch (Exception ex)
                    {
                        errors.Add($"Failed to upload audio for song '{songData.Title}': {ex.Message}");
                        continue;
                    }

                    // Upload cover if provided
                    if (i < request.CoverFiles?.Count)
                    {
                        var coverFile = request.CoverFiles[i];
                        if (coverFile != null && coverFile.Length > 0)
                        {
                            try
                            {
                                var coverUrl = await _blobService.UploadSongCoverAsync(
                                    song.Id,
                                    coverFile.OpenReadStream(),
                                    coverFile.FileName
                                );
                                song.CoverUrl = coverUrl;
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to upload cover for song {SongTitle}", songData.Title);
                            }
                        }
                    }

                    await _context.Songs.InsertOneAsync(song);
                    createdSongs.Add(song);

                    // Add song to album if specified
                    if (album != null)
                    {
                        var songReference = new SongReference
                        {
                            Id = song.Id,
                            Position = album.Songs.Count + createdSongs.Count - 1,
                            AddedAt = DateTime.UtcNow
                        };

                        var albumFilter = Builders<Album>.Filter.Eq(a => a.Id, album.Id);
                        var albumUpdate = Builders<Album>.Update.Push(a => a.Songs, songReference);
                        await _context.Albums.UpdateOneAsync(albumFilter, albumUpdate);
                    }

                    result.SuccessCount++;
                }
                catch (Exception ex)
                {
                    result.ErrorCount++;
                    errors.Add($"Failed to create song '{request.SongsData[i].Title}': {ex.Message}");
                }
            }

            result.CreatedSongs = createdSongs;
            result.Errors = errors;

            _logger.LogInformation("Bulk created {SuccessCount} songs with {ErrorCount} errors", result.SuccessCount, result.ErrorCount);

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in bulk song creation");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("search")]
    public async Task<ActionResult<AdminSearchResult>> Search([FromQuery] string query, [FromQuery] string? type = null)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Search query is required");
            }

            var result = new AdminSearchResult();
            var searchPattern = new MongoDB.Bson.BsonRegularExpression(query, "i"); // case-insensitive

            // Search artists if type is null or "artist"
            if (string.IsNullOrEmpty(type) || type.ToLower() == "artist")
            {
                var artistFilter = Builders<Artist>.Filter.Regex(a => a.Name, searchPattern);
                result.Artists = await _context.Artists!.Find(artistFilter).Limit(10).ToListAsync();
            }

            // Search albums if type is null or "album"
            if (string.IsNullOrEmpty(type) || type.ToLower() == "album")
            {
                var albumFilter = Builders<Album>.Filter.Regex(a => a.Title, searchPattern);
                result.Albums = await _context.Albums!.Find(albumFilter).Limit(10).ToListAsync();
            }

            // Search songs if type is null or "song"
            if (string.IsNullOrEmpty(type) || type.ToLower() == "song")
            {
                var songFilter = Builders<Song>.Filter.Regex(s => s.Title, searchPattern);
                result.Songs = await _context.Songs!.Find(songFilter).Limit(10).ToListAsync();
            }

            // Search playlists if type is null or "playlist"
            if (string.IsNullOrEmpty(type) || type.ToLower() == "playlist")
            {
                var playlistFilter = Builders<Playlist>.Filter.Regex(p => p.Name, searchPattern);
                result.Playlists = await _context.Playlists!.Find(playlistFilter).Limit(10).ToListAsync();
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching for {Query}", query);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("stats")]
    public async Task<ActionResult<AdminStats>> GetStats()
    {
        try
        {
            var stats = new AdminStats();

            // Get counts
            stats.TotalArtists = await _context.Artists!.CountDocumentsAsync(_ => true);
            stats.TotalAlbums = await _context.Albums!.CountDocumentsAsync(_ => true);
            stats.TotalSongs = await _context.Songs!.CountDocumentsAsync(_ => true);
            stats.TotalPlaylists = await _context.Playlists!.CountDocumentsAsync(_ => true);

            // Get recent activity (last 7 days)
            var sevenDaysAgo = DateTime.UtcNow.AddDays(-7);
            stats.RecentArtists = await _context.Artists!.CountDocumentsAsync(a => a.CreatedAt >= sevenDaysAgo);
            stats.RecentAlbums = await _context.Albums!.CountDocumentsAsync(a => a.CreatedAt >= sevenDaysAgo);
            stats.RecentSongs = await _context.Songs!.CountDocumentsAsync(s => s.CreatedAt >= sevenDaysAgo);
            stats.RecentPlaylists = await _context.Playlists!.CountDocumentsAsync(p => p.CreatedAt >= sevenDaysAgo);

            return Ok(stats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting admin stats");
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("artists/{id}")]
    public async Task<ActionResult<Artist>> UpdateArtist(string id, [FromForm] UpdateArtistRequest request)
    {
        try
        {
            var artist = await _context.Artists!.Find(a => a.Id == id).FirstOrDefaultAsync();
            if (artist == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(request.Name))
            {
                artist.Name = request.Name;
            }

            if (!string.IsNullOrWhiteSpace(request.Bio))
            {
                artist.Bio = request.Bio;
            }

            // Upload new image if provided
            if (request.ImageFile != null && request.ImageFile.Length > 0)
            {
                try
                {
                    // Delete old image files before uploading new one
                    await _blobService.DeleteArtistImageFilesAsync(artist.Id);
                    
                    var imageUrl = await _blobService.UploadArtistImageAsync(
                        artist.Id,
                        request.ImageFile.OpenReadStream(),
                        request.ImageFile.FileName
                    );
                    artist.ImageUrl = imageUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload new artist image for {ArtistName}", artist.Name);
                    return StatusCode(500, "Failed to upload artist image");
                }
            }

            var filter = Builders<Artist>.Filter.Eq(a => a.Id, id);
            await _context.Artists.ReplaceOneAsync(filter, artist);

            _logger.LogInformation("Updated artist: {ArtistName} with ID: {ArtistId}", artist.Name, artist.Id);
            return Ok(artist);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating artist {ArtistId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("artists/{id}")]
    public async Task<ActionResult> DeleteArtist(string id)
    {
        try
        {
            var artist = await _context.Artists!.Find(a => a.Id == id).FirstOrDefaultAsync();
            if (artist == null)
            {
                return NotFound();
            }

            // Check if artist has songs or albums
            var hasSongs = await _context.Songs!.Find(s => s.Artists.Any(a => a.Id == id)).AnyAsync();
            var hasAlbums = await _context.Albums!.Find(a => a.Artist != null && a.Artist.Id == id).AnyAsync();

            if (hasSongs || hasAlbums)
            {
                return BadRequest("Cannot delete artist with existing songs or albums");
            }

            // Delete artist files from blob storage
            await _blobService.DeleteArtistFilesAsync(id);

            await _context.Artists.DeleteOneAsync(a => a.Id == id);
            _logger.LogInformation("Deleted artist: {ArtistName} with ID: {ArtistId}", artist.Name, artist.Id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting artist {ArtistId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("songs/{id}")]
    public async Task<ActionResult> DeleteSong(string id)
    {
        try
        {
            var song = await _context.Songs!.Find(s => s.Id == id).FirstOrDefaultAsync();
            if (song == null)
            {
                return NotFound();
            }

            // Remove song from any albums
            if (song.Album != null)
            {
                var albumFilter = Builders<Album>.Filter.Eq(a => a.Id, song.Album.Id);
                var albumUpdate = Builders<Album>.Update.PullFilter(a => a.Songs, s => s.Id == id);
                await _context.Albums.UpdateOneAsync(albumFilter, albumUpdate);
            }

            // Delete song files from blob storage
            await _blobService.DeleteSongFilesAsync(id);

            await _context.Songs.DeleteOneAsync(s => s.Id == id);
            _logger.LogInformation("Deleted song: {SongTitle} with ID: {SongId}", song.Title, song.Id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting song {SongId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("songs/{id}")]
    public async Task<ActionResult<Song>> UpdateSong(string id, [FromForm] UpdateSongRequest request)
    {
        try
        {
            var song = await _context.Songs!.Find(s => s.Id == id).FirstOrDefaultAsync();
            if (song == null)
            {
                return NotFound("Song not found");
            }

            // Track old album for updates
            string? oldAlbumId = song.Album?.Id;

            // Update title
            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                song.Title = request.Title;
            }

            // Update genre
            if (!string.IsNullOrWhiteSpace(request.Genre))
            {
                song.Genre = request.Genre;
            }

            if (request.Duration > 0)
            {
                song.DurationSec = request.Duration;
            }

            // Update artist if changed
            if (!string.IsNullOrWhiteSpace(request.ArtistId))
            {
                var artist = await _context.Artists!.Find(a => a.Id == request.ArtistId).FirstOrDefaultAsync();
                if (artist == null)
                {
                    return BadRequest("Artist not found");
                }

                song.Artists = new List<ArtistReference>
            {
                new ArtistReference { Id = artist.Id, Name = artist.Name }
            };
            }

            // Update album if changed
            if (!string.IsNullOrWhiteSpace(request.AlbumId) && request.AlbumId != oldAlbumId)
            {
                var album = await _context.Albums!.Find(a => a.Id == request.AlbumId).FirstOrDefaultAsync();
                if (album == null)
                {
                    return BadRequest("Album not found");
                }

                song.Album = new AlbumReference { Id = album.Id, Title = album.Title };

                // Remove from old album
                if (!string.IsNullOrEmpty(oldAlbumId))
                {
                    var oldAlbumFilter = Builders<Album>.Filter.Eq(a => a.Id, oldAlbumId);
                    var oldAlbumUpdate = Builders<Album>.Update.PullFilter(a => a.Songs, s => s.Id == song.Id);
                    await _context.Albums!.UpdateOneAsync(oldAlbumFilter, oldAlbumUpdate);
                }

                // Add to new album
                var songReference = new SongReference
                {
                    Id = song.Id,
                    Position = album.Songs.Count,
                    AddedAt = DateTime.UtcNow
                };
                var newAlbumFilter = Builders<Album>.Filter.Eq(a => a.Id, album.Id);
                var newAlbumUpdate = Builders<Album>.Update.Push(a => a.Songs, songReference);
                await _context.Albums!.UpdateOneAsync(newAlbumFilter, newAlbumUpdate);
            }

            if (request.AudioFile != null && request.AudioFile.Length > 0)
            {
                try
                {
                    // Delete old audio files before uploading new one
                    await _blobService.DeleteSongAudioFilesAsync(song.Id);
                    
                    var audioUrl = await _blobService.UploadSongAsync(
                        song.Id,
                        request.AudioFile.OpenReadStream(),
                        request.AudioFile.FileName
                    );
                    song.FileUrl = audioUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload new audio file for song {SongTitle}", song.Title);
                    return StatusCode(500, "Failed to upload audio file");
                }
            }

            if (request.CoverFile != null && request.CoverFile.Length > 0)
            {
                try
                {
                    // Delete old cover files before uploading new one
                    await _blobService.DeleteSongCoverFilesAsync(song.Id);
                    
                    var coverUrl = await _blobService.UploadSongCoverAsync(
                        song.Id,
                        request.CoverFile.OpenReadStream(),
                        request.CoverFile.FileName
                    );
                    song.CoverUrl = coverUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload new cover for song {SongTitle}", song.Title);
                   
                }
            }

            if (request.SnippetFile != null && request.SnippetFile.Length > 0)
            {
                try
                {
                    // Delete old snippet files before uploading new one
                    await _blobService.DeleteSongSnippetFilesAsync(song.Id);
                    
                    var snippetUrl = await _blobService.UploadSongSnippetAsync(
                        song.Id,
                        request.SnippetFile.OpenReadStream(),
                        request.SnippetFile.FileName
                    );
                    song.SnippetUrl = snippetUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload new snippet for song {SongTitle}", song.Title);
                  
                }
            }

            var filter = Builders<Song>.Filter.Eq(s => s.Id, song.Id);
            await _context.Songs!.ReplaceOneAsync(filter, song);

            _logger.LogInformation("Updated song: {SongTitle} with ID: {SongId}", song.Title, song.Id);
            return Ok(song);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating song {SongId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpDelete("albums/{id}")]
    public async Task<ActionResult> DeleteAlbum(string id)
    {
        try
        {
            var album = await _context.Albums!.Find(a => a.Id == id).FirstOrDefaultAsync();
            if (album == null)
            {
                return NotFound();
            }

            // Check if album has songs
            var hasSongs = await _context.Songs!.Find(s => s.Album != null && s.Album.Id == id).AnyAsync();
            if (hasSongs)
            {
                return BadRequest("Cannot delete album with existing songs");
            }

            // Remove album from artist's albums list
            if (album.Artist != null)
            {
                var artistFilter = Builders<Artist>.Filter.Eq(a => a.Id, album.Artist.Id);
                var artistUpdate = Builders<Artist>.Update.PullFilter(a => a.Albums, al => al.Id == id);
                await _context.Artists.UpdateOneAsync(artistFilter, artistUpdate);
            }

            // Delete album files from blob storage
            await _blobService.DeleteAlbumFilesAsync(id);

            await _context.Albums.DeleteOneAsync(a => a.Id == id);
            _logger.LogInformation("Deleted album: {AlbumTitle} with ID: {AlbumId}", album.Title, album.Id);

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting album {AlbumId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpPut("albums/{id}")]
    public async Task<ActionResult<Album>> UpdateAlbum(string id, [FromForm] CreateAlbumRequest request)
    {
        try
        {
            var album = await _context.Albums!
                .Find(a => a.Id == id)
                .FirstOrDefaultAsync();

            if (album == null)
            {
                return NotFound("Album not found");
            }

            string? oldArtistId = album.Artist?.Id;

            if (!string.IsNullOrWhiteSpace(request.ArtistId) && request.ArtistId != oldArtistId)
            {
                var newArtist = await _context.Artists!
                    .Find(a => a.Id == request.ArtistId)
                    .FirstOrDefaultAsync();

                if (newArtist == null)
                {
                    return BadRequest("New artist not found");
                }

                album.Artist = new ArtistReference { Id = newArtist.Id, Name = newArtist.Name };

                if (!string.IsNullOrEmpty(oldArtistId))
                {
                    var oldArtistFilter = Builders<Artist>.Filter.Eq(a => a.Id, oldArtistId);
                    var oldArtistUpdate = Builders<Artist>.Update.PullFilter(
                        a => a.Albums,
                        ar => ar.Id == album.Id
                    );
                    await _context.Artists!.UpdateOneAsync(oldArtistFilter, oldArtistUpdate);
                }

                var newArtistFilter = Builders<Artist>.Filter.Eq(a => a.Id, newArtist.Id);
                var newArtistUpdate = Builders<Artist>.Update.Push(
                    a => a.Albums,
                    new AlbumReference { Id = album.Id, Title = album.Title }
                );
                await _context.Artists!.UpdateOneAsync(newArtistFilter, newArtistUpdate);
            }

            if (!string.IsNullOrWhiteSpace(request.Title))
            {
                album.Title = request.Title;
            }

            if (request.ReleaseDate.HasValue)
            {
                album.ReleaseDate = request.ReleaseDate.Value;
            }

            if (request.CoverFile != null && request.CoverFile.Length > 0)
            {
                try
                {
                    // Delete old cover files before uploading new one
                    await _blobService.DeleteAlbumCoverFilesAsync(album.Id);
                    
                    var coverUrl = await _blobService.UploadAlbumCoverAsync(
                        album.Id,
                        request.CoverFile.OpenReadStream(),
                        request.CoverFile.FileName
                    );
                    album.CoverUrl = coverUrl;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to upload album cover for {AlbumTitle}", album.Title);
                    return StatusCode(500, "Failed to upload album cover");
                }
            }

            var filter = Builders<Album>.Filter.Eq(a => a.Id, album.Id);
            await _context.Albums!.ReplaceOneAsync(filter, album);

            _logger.LogInformation("Updated album: {AlbumTitle} with ID: {AlbumId}", album.Title, album.Id);

            return Ok(album);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error updating album {AlbumId}", id);
            return StatusCode(500, "Internal server error");
        }
    }

}

// Request DTOs
public class CreateArtistRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Bio { get; set; }
    public IFormFile? ImageFile { get; set; }
}

public class UpdateArtistRequest
{
    public string? Name { get; set; }
    public string? Bio { get; set; }
    public IFormFile? ImageFile { get; set; }
}

public class UpdateSongRequest
{
    public string? Title { get; set; }
    public string? ArtistId { get; set; }
    public string? AlbumId { get; set; }
    public string? Genre { get; set; }
    public int Duration { get; set; }
    public IFormFile? AudioFile { get; set; }  // Optional for updates
    public IFormFile? CoverFile { get; set; }
    public IFormFile? SnippetFile { get; set; }
}

public class CreateAlbumRequest
{
    public string Title { get; set; } = string.Empty;
    public string ArtistId { get; set; } = string.Empty;
    public DateTime? ReleaseDate { get; set; }
    public IFormFile? CoverFile { get; set; }
}


public class CreateSongRequest
{
    public string Title { get; set; } = string.Empty;
    public string ArtistId { get; set; } = string.Empty;
    public string? AlbumId { get; set; }
    public string? Genre { get; set; }
    public int Duration { get; set; }
    public IFormFile AudioFile { get; set; } = null!;
    public IFormFile? CoverFile { get; set; }
    public IFormFile? SnippetFile { get; set; }
}

public class CreatePlaylistRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
}

public class AddSongToPlaylistRequest
{
    public string SongId { get; set; } = string.Empty;
    public int? Position { get; set; }
}

public class BulkCreateSongsRequest
{
    public string ArtistId { get; set; } = string.Empty;
    public string? AlbumId { get; set; }
    public List<BulkSongData> SongsData { get; set; } = new();
    public List<IFormFile> AudioFiles { get; set; } = new();
    public List<IFormFile>? CoverFiles { get; set; }
}

public class BulkSongData
{
    public string Title { get; set; } = string.Empty;
    public string? Genre { get; set; }
    public int Duration { get; set; }
}

public class BulkUploadResult
{
    public int SuccessCount { get; set; }
    public int ErrorCount { get; set; }
    public List<Song> CreatedSongs { get; set; } = new();
    public List<string> Errors { get; set; } = new();
}

public class AdminSearchResult
{
    public List<Artist> Artists { get; set; } = new();
    public List<Album> Albums { get; set; } = new();
    public List<Song> Songs { get; set; } = new();
    public List<Playlist> Playlists { get; set; } = new();
}

public class AdminStats
{
    public long TotalArtists { get; set; }
    public long TotalAlbums { get; set; }
    public long TotalSongs { get; set; }
    public long TotalPlaylists { get; set; }
    public long RecentArtists { get; set; }
    public long RecentAlbums { get; set; }
    public long RecentSongs { get; set; }
    public long RecentPlaylists { get; set; }
}
