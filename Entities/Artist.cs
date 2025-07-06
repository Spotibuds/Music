using System.ComponentModel.DataAnnotations;

namespace Music.Entities;

public class Artist : BaseEntity
{
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;
    
    [MaxLength(1000)]
    public string? Bio { get; set; }
    
    [MaxLength(500)]
    public string? ImageUrl { get; set; }

    // Navigation properties
    public virtual ICollection<Album> Albums { get; set; } = new List<Album>();
    public virtual ICollection<Song> Songs { get; set; } = new List<Song>();
} 