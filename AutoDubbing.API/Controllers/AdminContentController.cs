using AutoDubbing.Data.Contexts;
using AutoDubbing.Service.ContentGeneration;
using AutoDubbing.Service.ContentPreparation;
using AutoDubbing.Service.Pois;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.API.Controllers;

[ApiController, Authorize, Route("api/admin/pois")]
public sealed class AdminContentController(AppDbContext db, IContentPreparationService preparation, IPoiReadService content) : ControllerBase
{
    [HttpGet("{id:int}/content")]
    public async Task<IActionResult> Content(int id, [FromQuery] ContentQuery query, CancellationToken token)
    {
        var result = await content.ContentForAdminAsync(id, query.Language!, token);
        return result is null ? NotFound() : Ok(result);
    }
    [HttpGet("{id:int}/contents")]
    public async Task<IActionResult> Contents(int id, CancellationToken token)
    {
        var poi = await db.PointsOfInterest.AsNoTracking().SingleOrDefaultAsync(poi => poi.Id == id, token);
        if (poi is null) return NotFound();
        var texts = await db.Translations.AsNoTracking().Where(item => item.PointOfInterestId == id)
            .OrderBy(item => item.Language).ThenByDescending(item => item.UpdatedAt).Select(item => new {
                item.Id, item.Language, item.Text, item.Status, UpdatedAtUtc = item.UpdatedAt, item.SourceVersion }).ToArrayAsync(token);
        var audioRows = await db.Audios.AsNoTracking().Where(item => item.PointOfInterestId == id).OrderByDescending(item => item.Id).ToArrayAsync(token);
        return Ok(new { poi.Id, SourceVersion = poi.SourceVersion, Translations = texts,
            Audios = audioRows.Select(item => new { item.Id, item.Language, item.Status, DurationSeconds = item.Duration,
                Url = GeneratedAudioPaths.IsHttps(item.FilePath) ? item.FilePath : null, item.SourceVersion, item.TextHash }) });
    }
    [HttpPost("{id:int}/prepare-content")]
    public async Task<IActionResult> Prepare(int id, PrepareContentRequest request, CancellationToken token)
    {
        var result = await preparation.PrepareAsync(id, request.Language!, token);
        if (result is null) return NotFound();
        return StatusCode(result.Status is "Queued" or "Processing" ? 202 : result.Status is "QuotaExceeded" or "QueueFull" ? 429
            : result.Status == "Unavailable" ? 503 : 200, result);
    }
}
