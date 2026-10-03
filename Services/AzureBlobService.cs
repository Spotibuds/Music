using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using SkiaSharp;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;

namespace Music.Services;

public record MediaTarget(string Container, string Blob, string Url);
public record ValidatedMedia(byte[] Bytes, string Extension, string ContentType, double? Duration = null);

public sealed class AzureBlobService
{
    private readonly BlobServiceClient client;
    private readonly Uri publicBase;
    private readonly HashSet<string> containers;
    private readonly IConfiguration configuration;
    public AzureBlobService(IConfiguration configuration)
    {
        this.configuration = configuration;
        var connection = configuration["AzureStorage:ConnectionString"];
        if (string.IsNullOrWhiteSpace(connection)) throw new InvalidOperationException("AzureStorage:ConnectionString is required.");
        client = new BlobServiceClient(connection, new BlobClientOptions { Retry = { MaxRetries = 1, NetworkTimeout = TimeSpan.FromSeconds(10) } });
        publicBase = new Uri(configuration["AzureStorage:PublicBaseUrl"] ?? throw new InvalidOperationException("AzureStorage:PublicBaseUrl is required."));
        if (publicBase.Scheme is not ("http" or "https")) throw new InvalidOperationException("Invalid storage public base URL.");
        containers = [Container("songs"), Container("artists"), Container("albums"), Container("playlists")];
    }
    public string Container(string kind)
    {
        var value = configuration[$"AzureStorage:{char.ToUpperInvariant(kind[0])}{kind[1..]}Container"];
        value = string.IsNullOrWhiteSpace(value) ? kind : value;
        if (!System.Text.RegularExpressions.Regex.IsMatch(value, "^[a-z0-9][a-z0-9-]{1,61}[a-z0-9]$")) throw new InvalidOperationException("Invalid storage container configuration.");
        return value;
    }
    public string Url(string container, string blob) => $"{publicBase.ToString().TrimEnd('/')}/api/media/blob/{container}/{string.Join('/', blob.Split('/').Select(Uri.EscapeDataString))}";
    public MediaTarget Prepare(string kind, string id, string purpose, string extension)
    {
        var container = Container(kind);
        var blob = $"{DemoRules.Id(id)}/{purpose}/{Guid.NewGuid():N}{extension}";
        return new(container, blob, Url(container, blob));
    }
    public bool TryResolve(string? value, out MediaTarget target)
    {
        target = new("", "", "");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return false;
        string path;
        if (uri.GetLeftPart(UriPartial.Authority) == publicBase.GetLeftPart(UriPartial.Authority) && uri.AbsolutePath.StartsWith("/api/media/blob/", StringComparison.Ordinal)) path = uri.AbsolutePath[16..];
        else if (uri.GetLeftPart(UriPartial.Authority) == client.Uri.GetLeftPart(UriPartial.Authority) && uri.AbsolutePath.StartsWith(client.Uri.AbsolutePath.TrimEnd('/') + "/", StringComparison.Ordinal)) path = uri.AbsolutePath[(client.Uri.AbsolutePath.TrimEnd('/').Length + 1)..];
        else return false;
        var pieces = path.Split('/', 2);
        if (pieces.Length != 2 || !containers.Contains(pieces[0])) return false;
        var blob = Uri.UnescapeDataString(pieces[1]);
        if (blob.Split('/').Any(x => x is ".." or "." or "") || blob.Contains('\\')) return false;
        target = new(pieces[0], blob, Url(pieces[0], blob));
        return true;
    }
    public BlobClient Blob(MediaTarget target)
    {
        if (!containers.Contains(target.Container)) throw new ApiException(404, "Media not found.");
        return client.GetBlobContainerClient(target.Container).GetBlobClient(target.Blob);
    }
    public async Task Upload(MediaTarget target, ValidatedMedia media, CancellationToken ct)
    {
        var container = client.GetBlobContainerClient(target.Container);
        await container.CreateIfNotExistsAsync(PublicAccessType.None, cancellationToken: ct);
        using var stream = new MemoryStream(media.Bytes, false);
        await container.GetBlobClient(target.Blob).UploadAsync(stream, new BlobUploadOptions { HttpHeaders = new BlobHttpHeaders { ContentType = media.ContentType, CacheControl = "no-cache" } }, ct);
    }
    public async Task Delete(MediaTarget target, CancellationToken ct) => await Blob(target).DeleteIfExistsAsync(cancellationToken: ct);
    public async Task<bool> Ready(CancellationToken ct)
    {
        try { await client.GetPropertiesAsync(ct); return true; }
        catch (Azure.RequestFailedException) { return false; }
        catch (HttpRequestException) { return false; }
    }
    public async Task<ValidatedMedia> Validate(IFormFile file, bool image, CancellationToken ct)
    {
        var max = image ? 10 * 1024 * 1024 : 50 * 1024 * 1024;
        if (file.Length is <= 0 || file.Length > max) throw new ApiException(400, image ? "Image must be 1 byte to 10 MiB." : "Audio must be 1 byte to 50 MiB.");
        await using var input = file.OpenReadStream();
        using var memory = new MemoryStream((int)file.Length);
        await input.CopyToAsync(memory, ct);
        if (memory.Length > max) throw new ApiException(400, "File is too large.");
        var bytes = memory.ToArray();
        if (image)
        {
            using var data = SKData.CreateCopy(bytes);
            using var codec = SKCodec.Create(data);
            if (codec is null) throw new ApiException(400, "Invalid image content.");
            var format = codec.EncodedFormat;
            if (format is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Webp)) throw new ApiException(400, "Supported images: PNG, JPEG, WebP.");
            var info = codec.Info;
            if (info.Width <= 0 || info.Height <= 0 || info.Width > 4096 || info.Height > 4096 || (long)info.Width * info.Height > 16000000) throw new ApiException(400, "Image dimensions are too large.");
            if (codec.FrameCount > 1) throw new ApiException(400, "A static image is required.");
            using var bitmap = new SKBitmap(info);
            if (codec.GetPixels(info, bitmap.GetPixels()) != SKCodecResult.Success) throw new ApiException(400, "Invalid image content.");
            return format switch
            {
                SKEncodedImageFormat.Png => new(bytes, ".png", "image/png"),
                SKEncodedImageFormat.Webp => new(bytes, ".webp", "image/webp"),
                _ => new(bytes, ".jpg", "image/jpeg")
            };
        }
        if (bytes.Length >= 12 && Encoding.ASCII.GetString(bytes, 0, 4) == "RIFF" && Encoding.ASCII.GetString(bytes, 8, 4) == "WAVE")
        {
            var duration = ValidateWave(bytes);
            return new(bytes, ".wav", "audio/wav", duration);
        }
        string ext, contentType;
        if (bytes.Length >= 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "fLaC") { ext = ".flac"; contentType = "audio/flac"; }
        else if (bytes.Length >= 4 && Encoding.ASCII.GetString(bytes, 0, 4) == "OggS") { ext = ".ogg"; contentType = "audio/ogg"; }
        else if (bytes.Length >= 3 && (Encoding.ASCII.GetString(bytes, 0, 3) == "ID3" || (bytes[0] == 0xff && (bytes[1] & 0xe0) == 0xe0))) { ext = ".mp3"; contentType = "audio/mpeg"; }
        else throw new ApiException(400, "Supported audio: PCM WAV, MP3, FLAC, Ogg.");
        var path = Path.Combine(Path.GetTempPath(), $"spotibuds-{Guid.NewGuid():N}{ext}");
        try
        {
            await File.WriteAllBytesAsync(path, bytes, ct);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var start = new ProcessStartInfo(configuration["Media:DecoderPath"] ?? "ffmpeg") { RedirectStandardError = true, RedirectStandardOutput = true, UseShellExecute = false, CreateNoWindow = true };
            foreach (var arg in new[] { "-nostdin", "-v", "error", "-xerror", "-threads", "1", "-i", path, "-map", "0:a:0", "-f", "null", "-" }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new ApiException(503, "Audio decoder is unavailable.");
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            try { await process.WaitForExitAsync(timeout.Token); }
            catch (OperationCanceledException) { process.Kill(true); throw new ApiException(400, "Audio decoding exceeded its time limit."); }
            await Task.WhenAll(error, output);
            if (process.ExitCode != 0) throw new ApiException(400, "Invalid audio content.");
            return new(bytes, ext, contentType);
        }
        catch (System.ComponentModel.Win32Exception) { throw new ApiException(503, "Audio decoder is unavailable; install ffmpeg or use PCM WAV."); }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
    public static double ValidateWave(byte[] bytes)
    {
        if (bytes.Length < 44 || BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4)) + 8L != bytes.Length) throw new ApiException(400, "Invalid WAV length.");
        var offset = 12; int byteRate = 0; ushort blockAlign = 0; long dataLength = 0;
        while (offset + 8 <= bytes.Length)
        {
            var type = Encoding.ASCII.GetString(bytes, offset, 4);
            var size = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 4, 4));
            if (offset + 8L + size > bytes.Length) throw new ApiException(400, "Truncated WAV chunk.");
            if (type == "fmt ")
            {
                if (size < 16) throw new ApiException(400, "Invalid WAV format.");
                var format = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 8, 2));
                var channels = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 10, 2));
                var sampleRate = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 12, 4));
                byteRate = (int)BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(offset + 16, 4));
                blockAlign = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 20, 2));
                var bits = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(offset + 22, 2));
                if (format != 1 || channels is < 1 or > 2 || sampleRate is < 8000 or > 192000 || bits is not (8 or 16 or 24 or 32) || blockAlign != channels * bits / 8 || byteRate != sampleRate * blockAlign) throw new ApiException(400, "Supported WAV: mono/stereo PCM, 8..192 kHz, 8/16/24/32 bits.");
            }
            if (type == "data") dataLength += size;
            offset = checked((int)(offset + 8L + size + (size % 2)));
        }
        if (offset != bytes.Length || byteRate <= 0 || dataLength <= 0 || dataLength % blockAlign != 0) throw new ApiException(400, "Invalid WAV data.");
        var duration = (double)dataLength / byteRate;
        if (duration > 7200) throw new ApiException(400, "Audio duration must not exceed 2 hours.");
        return duration;
    }
}
