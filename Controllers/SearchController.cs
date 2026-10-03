using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Music.Services;
namespace Music.Controllers;
[ApiController, Route("api/search"), EnableRateLimiting("search")]
public sealed class SearchController(CatalogueService catalogue) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> Search(string? q) => Ok(await catalogue.Search(q, HttpContext.RequestAborted));
}
