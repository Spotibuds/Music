using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Azure.Storage.Sas;

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
    Task<bool> DeleteSongFilesAsync(string songId);
    Task<bool> DeleteArtistFilesAsync(string artistId);
    Task<bool> DeleteAlbumFilesAsync(string albumId);
    Task<bool> DeleteSongAudioFilesAsync(string songId);
    Task<bool> DeleteSongCoverFilesAsync(string songId);
    Task<bool> DeleteSongSnippetFilesAsync(string songId);
    Task<bool> DeleteArtistImageFilesAsync(string artistId);
    Task<bool> DeleteAlbumCoverFilesAsync(string albumId);
    Task<List<string>> ListFilesAsync(string containerName, string prefix = "");
    BlobContainerClient GetBlobContainerClient(string containerName);
    string GenerateSasUrl(string containerName, string blobName, TimeSpan? expiry = null);
    Task UpdateContainerAccessLevelAsync(string containerName);
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
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var fileGuid = Guid.NewGuid().ToString();
        var fileExtension = Path.GetExtension(fileName).ToLower();
        var blobName = $"{songId}/song/{fileGuid}{fileExtension}";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(fileStream, overwrite: true);

        return GenerateSasUrl(_songsContainer, blobName, TimeSpan.FromDays(365)); // Long-lived URL for songs
    }

    public async Task<string> UploadSongCoverAsync(string songId, Stream imageStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var imageGuid = Guid.NewGuid().ToString();
        var blobName = $"{songId}/cover/{imageGuid}.jpg";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(imageStream, overwrite: true);

        return GenerateSasUrl(_songsContainer, blobName, TimeSpan.FromDays(365));
    }

    public async Task<string> UploadSongSnippetAsync(string songId, Stream audioStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var snippetGuid = Guid.NewGuid().ToString();
        var blobName = $"{songId}/snippet/{snippetGuid}.mp3";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(audioStream, overwrite: true);

        return GenerateSasUrl(_songsContainer, blobName, TimeSpan.FromDays(365));
    }

    public async Task<string> UploadArtistImageAsync(string artistId, Stream imageStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_artistsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var imageGuid = Guid.NewGuid().ToString();
        var blobName = $"{artistId}/profilePicture/{imageGuid}.jpg";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(imageStream, overwrite: true);

        return GenerateSasUrl(_artistsContainer, blobName, TimeSpan.FromDays(365));
    }

    public async Task<string> UploadAlbumCoverAsync(string albumId, Stream imageStream, string fileName)
    {
        var containerClient = _blobServiceClient.GetBlobContainerClient(_albumsContainer);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.None);

        var coverGuid = Guid.NewGuid().ToString();
        var blobName = $"{albumId}/cover/{coverGuid}.jpg";

        var blobClient = containerClient.GetBlobClient(blobName);
        await blobClient.UploadAsync(imageStream, overwrite: true);

        return GenerateSasUrl(_albumsContainer, blobName, TimeSpan.FromDays(365));
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

    public async Task<bool> DeleteSongFilesAsync(string songId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
            var prefix = $"{songId}/";
            
            var deletedCount = 0;
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted.Value) deletedCount++;
            }
            
            return deletedCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteArtistFilesAsync(string artistId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_artistsContainer);
            var prefix = $"{artistId}/";
            
            var deletedCount = 0;
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted.Value) deletedCount++;
            }
            
            return deletedCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAlbumFilesAsync(string albumId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_albumsContainer);
            var prefix = $"{albumId}/";
            
            var deletedCount = 0;
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted.Value) deletedCount++;
            }
            
            return deletedCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteSongAudioFilesAsync(string songId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
            var prefix = $"{songId}/song/";
            
            var deletedCount = 0;
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted.Value) deletedCount++;
            }
            
            return deletedCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteSongCoverFilesAsync(string songId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
            var prefix = $"{songId}/cover/";
            
            var deletedCount = 0;
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted.Value) deletedCount++;
            }
            
            return deletedCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteSongSnippetFilesAsync(string songId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_songsContainer);
            var prefix = $"{songId}/snippet/";
            
            var deletedCount = 0;
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted.Value) deletedCount++;
            }
            
            return deletedCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteArtistImageFilesAsync(string artistId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_artistsContainer);
            var prefix = $"{artistId}/profilePicture/";
            
            var deletedCount = 0;
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted.Value) deletedCount++;
            }
            
            return deletedCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public async Task<bool> DeleteAlbumCoverFilesAsync(string albumId)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(_albumsContainer);
            var prefix = $"{albumId}/cover/";
            
            var deletedCount = 0;
            await foreach (var blobItem in containerClient.GetBlobsAsync(prefix: prefix))
            {
                var blobClient = containerClient.GetBlobClient(blobItem.Name);
                var deleted = await blobClient.DeleteIfExistsAsync();
                if (deleted.Value) deletedCount++;
            }
            
            return deletedCount > 0;
        }
        catch (Exception)
        {
            return false;
        }
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

    public string GenerateSasUrl(string containerName, string blobName, TimeSpan? expiry = null)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            var blobClient = containerClient.GetBlobClient(blobName);

            Console.WriteLine($"Attempting to generate SAS URL for: {containerName}/{blobName}");
            Console.WriteLine($"CanGenerateSasUri: {blobClient.CanGenerateSasUri}");
            Console.WriteLine($"Blob URI: {blobClient.Uri}");

            // Check if we can generate SAS (requires account key)
            if (!blobClient.CanGenerateSasUri)
            {
                Console.WriteLine($"ERROR: Cannot generate SAS URI for {containerName}/{blobName}");
                Console.WriteLine($"This usually indicates:");
                Console.WriteLine($"1. Connection string doesn't include AccountKey");
                Console.WriteLine($"2. Using managed identity without delegation");
                Console.WriteLine($"3. Storage account configuration issue");
                
                // Try to extract account name from URI for debugging
                var uri = blobClient.Uri.ToString();
                Console.WriteLine($"Storage account URI: {uri}");
                
                throw new InvalidOperationException($"Cannot generate SAS URI for blob {blobName}. Check connection string and Azure Storage account configuration.");
            }

            var sasBuilder = new BlobSasBuilder
            {
                BlobContainerName = containerName,
                BlobName = blobName,
                Resource = "b", // blob resource
                ExpiresOn = DateTimeOffset.UtcNow.Add(expiry ?? TimeSpan.FromDays(365)) // Long-lived for media files
            };

            sasBuilder.SetPermissions(BlobSasPermissions.Read);

            var sasUrl = blobClient.GenerateSasUri(sasBuilder).ToString();
            Console.WriteLine($"SUCCESS: Generated SAS URL for {containerName}/{blobName}");
            return sasUrl;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"EXCEPTION generating SAS URL for {containerName}/{blobName}: {ex.Message}");
            Console.WriteLine($"Exception type: {ex.GetType().Name}");
            throw;
        }
    }

    public async Task UpdateContainerAccessLevelAsync(string containerName)
    {
        try
        {
            var containerClient = _blobServiceClient.GetBlobContainerClient(containerName);
            
            // Check if container exists
            var exists = await containerClient.ExistsAsync();
            if (!exists.Value)
            {
                Console.WriteLine($"Container {containerName} does not exist, creating with public blob access");
                await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob);
                return;
            }

            // Update existing container to allow public blob access
            await containerClient.SetAccessPolicyAsync(PublicAccessType.Blob);
            Console.WriteLine($"Updated container {containerName} to allow public blob access");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error updating container access level for {containerName}: {ex.Message}");
            throw;
        }
    }
}
