using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Music.Models;

public class Artist
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("name")]
    public string Name { get; set; } = string.Empty;

    [BsonElement("bio")]
    public string? Bio { get; set; }

    [BsonElement("imageUrl")]
    public string? ImageUrl { get; set; }

    [BsonElement("albums")]
    public List<AlbumReference> Albums { get; set; } = new List<AlbumReference>();

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
} 