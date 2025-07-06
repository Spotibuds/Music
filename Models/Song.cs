using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace Music.Models;

public class Song
{
    [BsonId]
    [BsonRepresentation(BsonType.ObjectId)]
    public string Id { get; set; } = ObjectId.GenerateNewId().ToString();

    [BsonElement("title")]
    public string Title { get; set; } = string.Empty;

    [BsonElement("artists")]
    public List<ArtistReference> Artists { get; set; } = new();

    [BsonElement("genre")]
    public string Genre { get; set; } = string.Empty;

    [BsonElement("durationSec")]
    public int DurationSec { get; set; }

    [BsonElement("album")]
    public AlbumReference? Album { get; set; }

    [BsonElement("fileUrl")]
    public string FileUrl { get; set; } = string.Empty;

    [BsonElement("snippetUrl")]
    public string SnippetUrl { get; set; } = string.Empty;

    [BsonElement("coverUrl")]
    public string CoverUrl { get; set; } = string.Empty;

    [BsonElement("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [BsonElement("updatedAt")]
    public DateTime? UpdatedAt { get; set; }
}

public class ArtistReference
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;
}

public class AlbumReference
{
    [BsonElement("id")]
    public string Id { get; set; } = string.Empty;
} 