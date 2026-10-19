using System.Globalization;
using System.Security.Claims;
using AutoDubbing.Service.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AutoDubbing.API.Controllers;

[ApiController]
[Route("api/admin/auth")]
[Authorize]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class AdminAuthController(IAdminAuthenticationService authenticationService) : ControllerBase
{
    [AllowAnonymous]
    [HttpPost("login")]
    [ProducesResponseType<AdminLoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AdminLoginResponse>> Login(AdminLoginRequest request,
        CancellationToken cancellationToken)
    {
        var result = await authenticationService.LoginAsync(request.Username, request.Password,
            cancellationToken);
        if (result is null)
        {
            Response.Headers.WWWAuthenticate = "Bearer";
            return Unauthorized(new ProblemDetails
            {
                Status = StatusCodes.Status401Unauthorized,
                Title = "Không thể đăng nhập.",
                Detail = "Thông tin đăng nhập không hợp lệ hoặc tài khoản không hoạt động."
            });
        }

        return Ok(result);
    }

    [HttpGet("me")]
    [ProducesResponseType<AdminProfile>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AdminProfile>> Me(CancellationToken cancellationToken)
    {
        if (!int.TryParse(User.FindFirstValue("sub"), NumberStyles.None, CultureInfo.InvariantCulture,
                out var adminId))
        {
            return Challenge();
        }

        var admin = await authenticationService.GetActiveAdminAsync(adminId, cancellationToken);
        return admin is null ? Challenge() : Ok(admin);
    }
}
