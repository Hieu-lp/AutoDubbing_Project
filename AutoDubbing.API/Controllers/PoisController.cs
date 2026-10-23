using AutoDubbing.Service.Pois;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoDubbing.API.Controllers;

[ApiController]
[Route("api/pois")]
[AllowAnonymous]
public sealed class PoisController(IPoiReadService service) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PoiSummaryDto>>> List(CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PoiDetailDto>> Detail(int id, CancellationToken cancellationToken)
    {
        var poi = await service.FindAsync(id, cancellationToken);
        return poi is null ? NotFound() : Ok(poi);
    }

    [HttpGet("nearby")]
    public async Task<ActionResult<IReadOnlyList<NearbyPoiDto>>> Nearby([FromQuery] NearbyQuery query,
        CancellationToken cancellationToken) =>
        Ok(await service.NearbyAsync(query.Latitude!.Value, query.Longitude!.Value,
            query.RadiusMeters!.Value, cancellationToken));

    [HttpGet("{id:int}/content")]
    public async Task<ActionResult<PoiContentDto>> GetContent(int id, [FromQuery] ContentQuery query,
        CancellationToken cancellationToken)
    {
        var content = await service.ContentAsync(id, query.Language!, cancellationToken);
        return content is null ? NotFound() : Ok(content);
    }
}
