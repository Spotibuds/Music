using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Music.Services;

public interface IAzureBlobService
{
    Task<string> UploadSongAsync(string songId, Stream fileStream, string fileName);
    Task<string> UploadSongCoverAsync(string songId, Stream imageStream, string fileName);
    Task<string> UploadSongSnippetAsync(string songId, Stream audioStream, string fileName);
    Task<string> UploadArtistImageAsync(string artistId, Stream imageStream, string fileName);
    Task<string> UploadAlbumCoverAsync(string albumId, Stream imageStream, string fileName);
    Task<Stream> DownloadFileAsync(string containerName, string blobName);
    Task<bool> DeleteFileAsync(string containerName, string blobName);
    Task<List<string>> ListFilesAsync(string containerName, string prefix = "");
    BlobContainerClient GetBlobContainerClient(string containerName);
}

public class AzureBlobService : IAzureBlobService
{
    private readonly BlobServiceClient _blobServiceClient;
    private readonly string _songsContainer;
    private readonly string _artistsContainer;
    private readonly string _albumsContainer;

    public AzureBlobService(IConfiguration configuration)
    {
        var connectionString = configuration["AzureStorage:ConnectionString"];
        if (string.IsNullOrEmpty(connectionString))
        {
            throw new ArgumentException("Azure Storage connection string not found");
        }

        _blobServiceClient = new BlobServiceClient(connectionString);
        _songsContainer = configuration["AzureStorage:SongsContainer"] ?? "songs";
        _artistsContainer = configuration["AzureStorage:ArtistsContainer"] ?? "artists";
        _albumsContainer = configuration["AzureStorage:AlbumsContainer"] ?? "albums";
    }

    public async Task<string> UploadSongAsync(string songId, Stream fileStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var fileGuid = Guid.NewGuid().ToString();
        var fileExtension = Path.GetExtension(fileName).ToLower();
        var blobName = $"{songId}/song/{fileGuid}{fileExtension}";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(fileStream, overwrite: true);

        return blobClient.Uri.ToString();
    }

    public async Task<string> UploadSongCoverAsync(string songId, Stream imageStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var imageGuid = Guid.NewGuid().ToString();
        var blobName = $"{songId}/cover/{imageGuid}.jpg";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(imageStream, overwrite: true);

        return blobClient.Uri.ToString();
    }

    public async Task<string> UploadSongSnippetAsync(string songId, Stream audioStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var snippetGuid = Guid.NewGuid().ToString();
        var blobName = $"{songId}/snippet/{snippetGuid}.mp3";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(audioStream, overwrite: true);

        return blobClient.Uri.ToString();
    }

    public async Task<string> UploadArtistImageAsync(string artistId, Stream imageStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_artistsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var imageGuid = Guid.NewGuid().ToString();
        var blobName = $"{artistId}/profilePicture/{imageGuid}.jpg";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(imageStream, overwrite: true);

        return blobClient.Uri.ToString();
    }

    public async Task<string> UploadAlbumCoverAsync(string albumId, Stream imageStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_albumsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var coverGuid = Guid.NewGuid().ToString();
        var blobName = $"{albumId}/cover/{coverGuid}.jpg";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(imageStream, overwrite: true);

        return blobClient.Uri.ToString();
    }

    public async Task<Stream> DownloadFileAsync(string containerName, string blobName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var response = await blobClient.DownloadStreamingAsync();
        return response.Value.Content;
    }

    public async Task<bool> DeleteFileAsync(string containerName, string blobName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobClient = containerClient.GetBlobClient(blobName);

        var response = await blobClient.DeleteIfExistsAsync();
        return response.Value;
    }

    public async Task<List<string>> ListFilesAsync(string containerName, string prefix = "")
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
        var blobs = new List<string>();

        await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
        {
            blobs.Add(blobItem.Name);
        }

        return blobs;
    }

    public BlobContainerClient GetBlobContainerClient(string containerName)
    {
        return _blobServiceClient.GetBlobContainerClient(containerName);
    }
}
