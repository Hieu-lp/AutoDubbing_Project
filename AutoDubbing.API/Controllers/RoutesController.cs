using AutoDubbing.Service.Routes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoDubbing.API.Controllers;

[ApiController]
[Route("api/routes")]
[AllowAnonymous]
public sealed class RoutesController(IRoutePreviewService service) : ControllerBase
{
    [HttpPost("preview")]
    public async Task<ActionResult<RoutePreviewDto>> Preview(RoutePreviewRequest request,
        CancellationToken cancellationToken)
    {
        var result = await service.PreviewAsync(request.StartLatitude!.Value, request.StartLongitude!.Value,
            request.PoiIds!, cancellationToken);
        if (result.Preview is not null) { return Ok(result.Preview); }
        if (result.RoutingFailure is { } failure)
        {
            var (status, code, detail) = failure switch
            {
                WalkingRouteFailure.NotConfigured => (503, "routing_not_configured", "Dịch vụ tuyến đi bộ chưa được cấu hình hợp lệ."),
                WalkingRouteFailure.QuotaExceeded => (503, "routing_quota_exceeded", "Dịch vụ tuyến đi bộ đã hết quota hoặc đang bị giới hạn tần suất."),
                WalkingRouteFailure.Timeout => (504, "routing_timeout", "Dịch vụ tuyến đi bộ không phản hồi trong thời hạn."),
                WalkingRouteFailure.NoRoute => (422, "walking_route_not_found", "Không tìm được tuyến đi bộ qua toàn bộ các điểm đã chọn."),
                WalkingRouteFailure.Rejected => (422, "routing_request_rejected", "Dịch vụ tuyến đi bộ không thể xử lý các điểm đã chọn."),
                WalkingRouteFailure.Unauthorized => (503, "routing_provider_rejected", "Dịch vụ tuyến đi bộ từ chối quyền truy cập của backend."),
                WalkingRouteFailure.InvalidResponse => (502, "routing_invalid_response", "Dịch vụ tuyến đi bộ trả kết quả không hợp lệ."),
                _ => (502, "routing_unavailable", "Không kết nối được dịch vụ tuyến đi bộ hoặc dịch vụ đang gặp lỗi.")
            };
            return Problem(statusCode: status, title: "Không thể tạo tuyến đi bộ.", detail: detail,
                extensions: new Dictionary<string, object?> { ["code"] = code });
        }
        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status400BadRequest,
            Title = "Danh sách POI không hợp lệ.",
            Detail = "Có POI không tồn tại hoặc không được công khai; không tạo preview một phần."
        };
        problem.Extensions["unavailablePoiIds"] = result.UnavailablePoiIds;
        return BadRequest(problem);
    }
}
