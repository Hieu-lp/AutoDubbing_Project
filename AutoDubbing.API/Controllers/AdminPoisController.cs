using System.Globalization;
using System.Security.Claims;
using AutoDubbing.Data.Entities;
using AutoDubbing.Service.Pois;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoDubbing.API.Controllers;

[ApiController]
[Route("api/admin/pois")]
[Authorize]
public sealed class AdminPoisController(IPoiManagementService service) : ControllerBase
{
    [HttpGet]
    [EndpointSummary("List all POIs for the admin dashboard")]
    [EndpointDescription("Returns published and hidden POIs. Requires an active administrator account.")]
    [ProducesResponseType<IReadOnlyList<PoiManagementResponse>>(StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PoiManagementResponse>>> List(
        CancellationToken cancellationToken) =>
        Ok(await service.ListAsync(cancellationToken));

    [HttpGet("{id:int}")]
    [EndpointSummary("Get a POI for administration")]
    [EndpointDescription("Returns the editable details of a POI, including hidden POIs.")]
    [ProducesResponseType<PoiManagementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PoiManagementResponse>> Detail(int id,
        CancellationToken cancellationToken)
    {
        var poi = await service.FindAsync(id, cancellationToken);
        return poi is null ? NotFound() : Ok(poi);
    }

    [HttpPost]
    [EndpointSummary("Create a POI")]
    [EndpointDescription("Creates a POI owned by the authenticated administrator.")]
    [ProducesResponseType<PoiManagementResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PoiManagementResponse>> Create(PoiManagementRequest request,
        CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("sub"), NumberStyles.None,
                CultureInfo.InvariantCulture, out var adminId))
        {
            return Challenge();
        }

        var poi = await service.CreateAsync(adminId, request, cancellationToken);
        return CreatedAtAction(nameof(Detail), new { id = poi.Id }, poi);
    }

    [HttpPut("{id:int}")]
    [EndpointSummary("Update a POI")]
    [EndpointDescription("Updates the details and publication status of a POI.")]
    [ProducesResponseType<PoiManagementResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PoiManagementResponse>> Update(int id, PoiManagementRequest request,
        CancellationToken cancellationToken)
    {
        var poi = await service.UpdateAsync(id, request, cancellationToken);
        return poi is null ? NotFound() : Ok(poi);
    }

    [HttpDelete("{id:int}")]
    [EndpointSummary("Delete a POI")]
    [EndpointDescription("Deletes a POI only when it has no translations, audio, or usage history.")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken);
        return result switch
        {
            PoiDeleteResult.Deleted => NoContent(),
            PoiDeleteResult.NotFound => NotFound(),
            PoiDeleteResult.HasDependents => Conflict(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict,
                Title = "Không thể xóa địa điểm.",
                Detail = "Địa điểm đang được tham chiếu bởi bản dịch, audio hoặc lịch sử sử dụng."
            }),
            _ => throw new InvalidOperationException("Kết quả xóa POI không hợp lệ.")
        };
    }
}
