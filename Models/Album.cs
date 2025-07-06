using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Music.Models;

public class Album
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("songs")]
    public List<SongReference> Songs { get; set; } = new();

    [BsonElement("artist")]
    public ArtistReference Artist { get; set; } = new();

    [BsonElement("coverUrl")]
    public string CoverUrl { get; set; } = string.Empty;

    [BsonElement("releaseDate")]
    public DateTime? ReleaseDate { get; set; }

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

public class SongReference
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;
} 