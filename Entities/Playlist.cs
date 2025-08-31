using System.ComponentModel.DataAnnotations;

namespace Music.Entities;

public class Playlist : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    public Guid OwnerId { get; set; }

    public string? CoverUrl { get; set; }

    // Navigation properties
    public virtual ICollection<PlaylistSong> PlaylistSongs { get; set; } = new List<PlaylistSong>();
} 