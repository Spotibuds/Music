using System.ComponentModel.DataAnnotations;

namespace Music.Entities;

public class PlaylistSong : BaseEntity
{
    [Required]
    public Guid PlaylistId { get; set; }

    [Required]
    public Guid SongId { get; set; }

    [Required]
    [Range(1, int.MaxValue)]
    public int Position { get; set; }

    // Navigation properties
    public virtual Playlist Playlist { get; set; } = null!;
    public virtual Song Song { get; set; } = null!;
} 