using System.ComponentModel.DataAnnotations;

namespace Music.Entities;

public class Song : BaseEntity
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string ArtistName { get; set; } = string.Empty;

    [MaxLength(50)]
    public string? Genre { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int Duration { get; set; } // in seconds

    [Required]
    [MaxLength(500)]
    public string FileUrl { get; set; } = string.Empty;

    [MaxLength(500)]
    public string? CoverUrl { get; set; }

    public Guid ArtistId { get; set; }
    public virtual Artist Artist { get; set; } = null!;

    // Navigation properties
    public virtual Album? Album { get; set; }
    public virtual ICollection<PlaylistSong> PlaylistSongs { get; set; } = new List<PlaylistSong>();
} 