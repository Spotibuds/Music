using Azure;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Music.Services;

namespace Music.Controllers;

[ApiController, Route("api/media")]
public sealed class MediaController(AzureBlobService storage, MediaCommands media) : ControllerBase
{
    [HttpGet("blob/{container}/{**blob}"), HttpHead("blob/{container}/{**blob}")]
    public Task<IActionResult> Blob(string container, string blob) => Read(storage.Url(container, blob), null);
    [HttpGet("image"), HttpHead("image")] public Task<IActionResult> Image(string url) => Read(url, true);
    [HttpGet("audio"), HttpHead("audio")] public Task<IActionResult> Audio(string url) => Read(url, false);
    private async Task<IActionResult> Read(string url, bool? image)
    {
        if (!storage.TryResolve(url, out var target)) throw new ApiException(400, "Media URL is not a configured catalogue storage identity.");
        if (!await media.Referenced(target.Url, User, HttpContext.RequestAborted)) throw new ApiException(404, "Media not found.");
        var blob = storage.Blob(target);
        BlobProperties properties;
        try { properties = (await blob.GetPropertiesAsync(cancellationToken: HttpContext.RequestAborted)).Value; }
        catch (RequestFailedException ex) when (ex.Status == 404) { return NotFound(new { message = "Media not found." }); }
        var type = properties.ContentType;
        var isImage = type is "image/png" or "image/jpeg" or "image/webp";
        var isAudio = type is "audio/wav" or "audio/mpeg" or "audio/ogg" or "audio/flac";
        if ((!isImage && !isAudio) || (image == true && !isImage) || (image == false && !isAudio)) throw new ApiException(404, "Media type does not match this endpoint.");
        var max = isImage ? 10 * 1024 * 1024 : 50 * 1024 * 1024;
        if (properties.ContentLength <= 0 || properties.ContentLength > max) throw new ApiException(413, "Media size exceeds the demo limit.");
        var etag = properties.ETag.ToString();
        Response.Headers.ETag = etag;
        Response.Headers.CacheControl = "no-cache";
        Response.Headers.AcceptRanges = "bytes";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        Response.Headers.LastModified = properties.LastModified.ToString("R");
        if (Request.Headers.IfNoneMatch.ToString().Split(',').Any(x => x.Trim() == etag)) return StatusCode(304);
        long start = 0, length = properties.ContentLength;
        var rangeValue = Request.Headers.Range.ToString();
        var ifRange = Request.Headers.IfRange.ToString();
        var partial = rangeValue.Length > 0 && (ifRange.Length == 0 || ifRange == etag);
        if (partial)
        {
            if (!TryRange(rangeValue, properties.ContentLength, out start, out length))
            {
                Response.Headers.ContentRange = $"bytes */{properties.ContentLength}";
                return StatusCode(416);
            }
        }
        if (Request.Method == "HEAD")
        {
            Response.ContentType = type; Response.ContentLength = length;
            if (partial) Response.Headers.ContentRange = $"bytes {start}-{start + length - 1}/{properties.ContentLength}";
            return StatusCode(partial ? 206 : 200);
        }
        var download = await blob.DownloadStreamingAsync(new BlobDownloadOptions { Range = new HttpRange(start, length), Conditions = new BlobRequestConditions { IfMatch = properties.ETag } }, HttpContext.RequestAborted);
        Response.ContentLength = length;
        if (partial) { Response.StatusCode = 206; Response.Headers.ContentRange = $"bytes {start}-{start + length - 1}/{properties.ContentLength}"; }
        return File(download.Value.Content, type);
    }
    public static bool TryRange(string value, long total, out long start, out long length)
    {
        start = 0; length = 0;
        if (total <= 0 || !RangeHeaderValue.TryParse(value, out var header) || header.Unit != "bytes" || header.Ranges.Count != 1) return false;
        var range = header.Ranges.Single();
        if (!range.From.HasValue)
        {
            var suffix = range.To ?? 0;
            if (suffix <= 0) return false;
            length = Math.Min(suffix, total); start = total - length; return true;
        }
        start = range.From.Value;
        if (start >= total || start < 0 || (range.To.HasValue && range.To.Value < start)) return false;
        var end = Math.Min(range.To ?? total - 1, total - 1);
        length = end - start + 1; return true;
    }
    // Cache tiers were removed: Blob ETags plus mandatory revalidation avoid stale or colliding content.
    [Authorize(Policy = "AdminOnly"), HttpGet("cache/status")] public IActionResult CacheStatus() => Ok(new { enabled = false, totalImageKeys = 0 });
    [Authorize(Policy = "AdminOnly"), HttpPost("cache/clear")] public IActionResult ClearCache() => Ok(new { enabled = false, cleared = 0 });
}
