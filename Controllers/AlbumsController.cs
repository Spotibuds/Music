using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Models;
using Music.Services;
namespace Music.Controllers;
[ApiController, Route("api/albums")]
public sealed class AlbumsController(CatalogueService catalogue) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(int? limit = null, int skip = 0) => Ok(await catalogue.Albums(null, limit, skip, HttpContext.RequestAborted));
    [HttpGet("{id}")] public async Task<IActionResult> Get(string id) => Ok(await catalogue.Album(id, HttpContext.RequestAborted));
    [HttpGet("{id}/songs")] public async Task<IActionResult> Songs(string id, int? limit = null, int skip = 0)
    { DemoRules.Id(id); await catalogue.Album(id, HttpContext.RequestAborted); return Ok(await catalogue.Songs(Builders<Song>.Filter.Eq("album.id", id), limit, skip, HttpContext.RequestAborted, albumOrder: true)); }
    [Authorize(Policy = "AdminOnly"), HttpPost] public async Task<IActionResult> Create(CreateAlbumDto dto) { var a = await catalogue.SaveAlbum(null, dto.Title, dto.Artist?.Id, dto.ReleaseDate, null, null, HttpContext.RequestAborted); return Created($"/api/albums/{a.Id}", a); }
    [Authorize(Policy = "AdminOnly"), HttpPut("{id}")] public async Task<IActionResult> Update(string id, UpdateAlbumDto dto) { await catalogue.SaveAlbum(id, dto.Title, dto.Artist?.Id, dto.ReleaseDate, null, dto.CoverUrl, HttpContext.RequestAborted); return NoContent(); }
    [Authorize(Policy = "AdminOnly"), HttpDelete("{id}")] public async Task<IActionResult> Delete(string id) { await catalogue.DeleteAlbum(id, HttpContext.RequestAborted); return NoContent(); }
    [Authorize(Policy = "AdminOnly"), HttpPost("{albumId}/songs/{songId}")] public async Task<IActionResult> Add(string albumId, string songId, int position = -1) { await catalogue.AddAlbumSong(albumId, songId, position, HttpContext.RequestAborted); return Ok(); }
}
