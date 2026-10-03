using Music.Models;
using System.ComponentModel.DataAnnotations;

namespace Music.Controllers;

public class CreateArtistDto { public string Name { get; set; } = ""; public string? Bio { get; set; } public string? ImageUrl { get; set; } }
public class UpdateArtistDto { public string? Name { get; set; } public string? Bio { get; set; } public string? ImageUrl { get; set; } }
public class CreateAlbumDto { public string Title { get; set; } = ""; public ArtistReference? Artist { get; set; } public DateTime? ReleaseDate { get; set; } }
public class UpdateAlbumDto { public string? Title { get; set; } public ArtistReference? Artist { get; set; } public string? CoverUrl { get; set; } public DateTime? ReleaseDate { get; set; } }
public class CreateSongDto { public string Title { get; set; } = ""; public List<ArtistReference> Artists { get; set; } = []; public string Genre { get; set; } = ""; public int DurationSec { get; set; } public AlbumReference? Album { get; set; } public string FileUrl { get; set; } = ""; public string? SnippetUrl { get; set; } public string CoverUrl { get; set; } = ""; public DateTime? ReleaseDate { get; set; } }
public class UpdateSongDto { public string? Title { get; set; } public List<ArtistReference>? Artists { get; set; } public string? Genre { get; set; } public int? DurationSec { get; set; } public AlbumReference? Album { get; set; } public string? FileUrl { get; set; } public string? SnippetUrl { get; set; } public string? CoverUrl { get; set; } public DateTime? ReleaseDate { get; set; } }
public class CreateArtistRequest { public string Name { get; set; } = ""; public string? Bio { get; set; } public IFormFile? ImageFile { get; set; } }
public class UpdateArtistRequest
{
    // Form binding must retain a supplied empty value so clear and omission differ.
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Name { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Bio { get; set; }
    public IFormFile? ImageFile { get; set; }
}
public class CreateAlbumRequest { public string Title { get; set; } = ""; public string ArtistId { get; set; } = ""; public DateTime? ReleaseDate { get; set; } public IFormFile? CoverFile { get; set; } }
public class CreateSongRequest { public string Title { get; set; } = ""; public string ArtistId { get; set; } = ""; public string? AlbumId { get; set; } public string? Genre { get; set; } public int Duration { get; set; } public IFormFile? AudioFile { get; set; } public IFormFile? CoverFile { get; set; } public IFormFile? SnippetFile { get; set; } }
public class UpdateSongRequest
{
    // Required text still validates; empty optional text clears persisted fields.
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Title { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? ArtistId { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? AlbumId { get; set; }
    [DisplayFormat(ConvertEmptyStringToNull = false)] public string? Genre { get; set; }
    public int? Duration { get; set; }
    public IFormFile? AudioFile { get; set; }
    public IFormFile? CoverFile { get; set; }
    public IFormFile? SnippetFile { get; set; }
}
public class CreatePlaylistDto { public string Name { get; set; } = ""; public string? Description { get; set; } public bool IsPublic { get; set; } = true; }
public class UpdatePlaylistDto { public string? Name { get; set; } public string? Description { get; set; } public bool? IsPublic { get; set; } }
public class ReorderPlaylistDto { public List<string> SongIds { get; set; } = []; }
public class AddSongToPlaylistRequest { public string SongId { get; set; } = ""; public int? Position { get; set; } }
public class BulkCreateSongsRequest { public string ArtistId { get; set; } = ""; public string? AlbumId { get; set; } public string? IdempotencyKey { get; set; } public List<BulkSongData> SongsData { get; set; } = []; public List<IFormFile> AudioFiles { get; set; } = []; public List<IFormFile>? CoverFiles { get; set; } }
public class BulkSongData { public string Title { get; set; } = ""; public string? Genre { get; set; } public int Duration { get; set; } }
public record BulkItemResult(int Index, bool Success, string? SongId, string? Error, string ItemKey);
public class BulkUploadResult { public string OperationId { get; set; } = ""; public int SuccessCount { get; set; } public int ErrorCount { get; set; } public List<Song> CreatedSongs { get; set; } = []; public List<string> Errors { get; set; } = []; public List<BulkItemResult> Items { get; set; } = []; }
