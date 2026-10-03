using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Music.Services;
namespace Music.Controllers;
[ApiController, Route("api/songs")]
public sealed class SongsController(CatalogueService catalogue) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(int? limit = null, int skip = 0) => Ok(await catalogue.Songs(null, limit, skip, HttpContext.RequestAborted));
    [HttpGet("{id}")] public async Task<IActionResult> Get(string id) => Ok(await catalogue.Song(id, HttpContext.RequestAborted));
    [Authorize(Policy = "AdminOnly"), HttpPost] public async Task<IActionResult> Create(CreateSongDto dto)
    {
        var s = await catalogue.SaveSong(null, new UpdateSongDto { Title = dto.Title, Artists = dto.Artists, Genre = dto.Genre, DurationSec = dto.DurationSec, Album = dto.Album, FileUrl = dto.FileUrl, CoverUrl = dto.CoverUrl, SnippetUrl = dto.SnippetUrl, ReleaseDate = dto.ReleaseDate }, null, null, null, HttpContext.RequestAborted);
        return Created($"/api/songs/{s.Id}", s);
    }
    [Authorize(Policy = "AdminOnly"), HttpPut("{id}")] public async Task<IActionResult> Update(string id, UpdateSongDto dto) { await catalogue.SaveSong(id, dto, null, null, null, HttpContext.RequestAborted); return NoContent(); }
    [Authorize(Policy = "AdminOnly"), HttpDelete("{id}")] public async Task<IActionResult> Delete(string id) { await catalogue.DeleteSong(id, HttpContext.RequestAborted); return NoContent(); }
    [Authorize(Policy = "AdminOnly"), HttpPost("{id}/upload-file")] public async Task<IActionResult> Audio(string id, IFormFile audioFile)
    { if (audioFile.Length == 0) throw new ApiException(400, "Audio file is required."); var s = await catalogue.SaveSong(id, new(), audioFile, null, null, HttpContext.RequestAborted); return Ok(new { s.FileUrl }); }
    [Authorize(Policy = "AdminOnly"), HttpPost("{id}/upload-cover")] public async Task<IActionResult> Cover(string id, IFormFile imageFile)
    { if (imageFile.Length == 0) throw new ApiException(400, "Cover file is required."); var s = await catalogue.SaveSong(id, new(), null, imageFile, null, HttpContext.RequestAborted); return Ok(new { s.CoverUrl }); }
    [Authorize(Policy = "AdminOnly"), HttpPost("{id}/upload-snippet")] public async Task<IActionResult> Snippet(string id, IFormFile audioFile)
    { if (audioFile.Length == 0) throw new ApiException(400, "Snippet file is required."); var s = await catalogue.SaveSong(id, new(), null, null, audioFile, HttpContext.RequestAborted); return Ok(new { s.SnippetUrl }); }
}
