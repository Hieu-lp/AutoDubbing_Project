using AutoDubbing.Service.Routes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoDubbing.API.Controllers;

[ApiController, Authorize, Route("api/admin/routes")]
public sealed class AdminSavedRoutesController(SavedRouteService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken token) => Ok(await service.ListAsync(false, token));
    [HttpPost] public async Task<IActionResult> Create(SavedRouteInput request, CancellationToken token) => await Save(null, request, token);
    [HttpPut("{id:int}")] public async Task<IActionResult> Update(int id, SavedRouteInput request, CancellationToken token) => await Save(id, request, token);
    private async Task<IActionResult> Save(int? id, SavedRouteInput request, CancellationToken token)
    {
        try { var result = await service.SaveAsync(id, request, token); return result is null ? NotFound() : Ok(result); }
        catch (ArgumentException error) { return Problem(statusCode: 400, title: error.Message); }
    }
    [HttpDelete("{id:int}")] public async Task<IActionResult> Delete(int id, CancellationToken token) =>
        await service.DeleteAsync(id, token) ? NoContent() : NotFound();
}

[ApiController, AllowAnonymous, Route("api/routes")]
public sealed class SavedRoutesController(SavedRouteService service, IRoutePreviewService preview) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken token)
    { Response.Headers.CacheControl = "no-store"; return Ok(await service.ListAsync(true, token)); }
    [HttpPost("{id:int}/preview")]
    public async Task<IActionResult> Preview(int id, SavedRouteStart request, CancellationToken token)
    {
        var route = (await service.ListAsync(true, token)).SingleOrDefault(route => route.Id == id);
        if (route is null) return NotFound(new ProblemDetails { Title = "Lộ trình không còn công khai hoặc có POI không khả dụng." });
        var result = await preview.PreviewAsync(request.StartLatitude!.Value, request.StartLongitude!.Value, route.PoiIds, token, true);
        if (result.Preview is not null)
        {
            var current = (await service.ListAsync(true, token)).SingleOrDefault(item => item.Id == id);
            if (current?.Version != route.Version) return Conflict(new ProblemDetails { Title = "Lộ trình đã thay đổi trong lúc tính đường; hãy làm mới gợi ý." });
            return Ok(result.Preview);
        }
        return Problem(statusCode: result.UnavailablePoiIds.Count > 0 ? 409 : 503,
            title: "Không tạo được đường đi cho lộ trình; hãy làm mới danh sách hoặc thử lại sau.");
    }
}
public sealed class SavedRouteStart
{
    [System.ComponentModel.DataAnnotations.Required, AutoDubbing.Service.Validation.FiniteRange(-90, 90)] public double? StartLatitude { get; init; }
    [System.ComponentModel.DataAnnotations.Required, AutoDubbing.Service.Validation.FiniteRange(-180, 180)] public double? StartLongitude { get; init; }
}
