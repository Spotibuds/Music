using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Music.Services;
namespace Music.Controllers;
[ApiController, Route("api/playlists")]
public sealed class PlaylistsController(PlaylistService playlists, IConfiguration configuration) : ControllerBase
{
    [HttpDelete("internal/owner/{owner}")]
    public async Task<IActionResult> DeleteOwner(string owner)
    {
        var supplied = Request.Headers["X-Spotibuds-Service"].ToString();
        var expected = configuration["ServiceAuth:Secret"] ?? "";
        if (expected.Length < 32 || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(supplied), System.Text.Encoding.UTF8.GetBytes(expected))) return Unauthorized();
        await playlists.DeleteOwner(owner, HttpContext.RequestAborted);
        return NoContent();
    }
    [HttpGet] public async Task<IActionResult> List(int? limit = null, int skip = 0) => Ok(await playlists.List(null, User, limit, skip, HttpContext.RequestAborted));
    [HttpGet("{id}")] public async Task<IActionResult> Get(string id) => Ok(await playlists.Detail(id, User, HttpContext.RequestAborted));
    [HttpGet("user/{userId}")] public async Task<IActionResult> UserList(string userId, int? limit = null, int skip = 0) => Ok(await playlists.List(userId, User, limit, skip, HttpContext.RequestAborted));
    [Authorize, HttpPost] public async Task<IActionResult> Create(CreatePlaylistDto dto) { var p = await playlists.Create(dto, User, null, HttpContext.RequestAborted); return Created($"/api/playlists/{p.Id}", p); }
    [Authorize, HttpPost("user/{userId}")] public async Task<IActionResult> UserCreate(string userId, CreatePlaylistDto dto) { var p = await playlists.Create(dto, User, userId, HttpContext.RequestAborted); return Created($"/api/playlists/{p.Id}", p); }
    [Authorize, HttpPut("{id}")] public async Task<IActionResult> Update(string id, UpdatePlaylistDto dto) { await playlists.Update(id, dto, User, HttpContext.RequestAborted); return NoContent(); }
    [Authorize, HttpDelete("{id}")] public async Task<IActionResult> Delete(string id) { await playlists.Delete(id, User, HttpContext.RequestAborted); return NoContent(); }
    [Authorize, HttpPost("{playlistId}/songs/{songId}")] public async Task<IActionResult> Add(string playlistId, string songId, int position = -1) { await playlists.Add(playlistId, songId, position, User, HttpContext.RequestAborted); return Ok(); }
    [Authorize, HttpDelete("{playlistId}/songs/{songId}")] public async Task<IActionResult> Remove(string playlistId, string songId) { await playlists.Remove(playlistId, songId, User, HttpContext.RequestAborted); return Ok(); }
    [Authorize, HttpPut("{id}/songs/reorder")] public async Task<IActionResult> Reorder(string id, ReorderPlaylistDto dto) { await playlists.Reorder(id, dto.SongIds, User, HttpContext.RequestAborted); return NoContent(); }
    [Authorize, HttpPost("{id}/cover")] public async Task<IActionResult> Cover(string id, IFormFile file) => Ok(new { coverUrl = await playlists.Cover(id, file, false, User, HttpContext.RequestAborted) });
    [Authorize, HttpDelete("{id}/cover")] public async Task<IActionResult> DeleteCover(string id) { await playlists.Cover(id, null, true, User, HttpContext.RequestAborted); return Ok(new { message = "Cover removed." }); }
}
