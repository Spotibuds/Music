using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MongoDB.Driver;
using Music.Models;
using Music.Services;
namespace Music.Controllers;
[ApiController, Route("api/artists")]
public sealed class ArtistsController(CatalogueService catalogue) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(int? limit = null, int skip = 0) => Ok(await catalogue.Artists(limit, skip, HttpContext.RequestAborted));
    [HttpGet("{id}")] public async Task<IActionResult> Get(string id) => Ok(await catalogue.Artist(id, HttpContext.RequestAborted));
    [HttpGet("{id}/albums")] public async Task<IActionResult> Albums(string id, int? limit = null, int skip = 0) { DemoRules.Id(id); return Ok(await catalogue.Albums(Builders<Album>.Filter.Eq("artist.id", id), limit, skip, HttpContext.RequestAborted)); }
    [HttpGet("{id}/songs")] public async Task<IActionResult> Songs(string id, int? limit = null, int skip = 0) { DemoRules.Id(id); return Ok(await catalogue.Songs(Builders<Song>.Filter.Eq("artists.id", id), limit, skip, HttpContext.RequestAborted)); }
    [Authorize(Policy = "AdminOnly"), HttpPost] public async Task<IActionResult> Create(CreateArtistDto dto) { var a = await catalogue.SaveArtist(null, dto.Name, dto.Bio, null, dto.ImageUrl, HttpContext.RequestAborted); return Created($"/api/artists/{a.Id}", a); }
    [Authorize(Policy = "AdminOnly"), HttpPut("{id}")] public async Task<IActionResult> Update(string id, UpdateArtistDto dto) { await catalogue.SaveArtist(id, dto.Name, dto.Bio, null, dto.ImageUrl, HttpContext.RequestAborted); return NoContent(); }
    [Authorize(Policy = "AdminOnly"), HttpDelete("{id}")] public async Task<IActionResult> Delete(string id) { await catalogue.DeleteArtist(id, HttpContext.RequestAborted); return NoContent(); }
}
