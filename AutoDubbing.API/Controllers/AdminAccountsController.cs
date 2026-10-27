using AutoDubbing.Service.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.API.Controllers;

[ApiController, Authorize, Route("api/admin/users")]
public sealed class AdminAccountsController(AdminAccountService service) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> List(CancellationToken token) => Ok(await service.ListAsync(token));
    [HttpPost] public Task<IActionResult> Create(AdminAccountInput request, CancellationToken token) => Save(null, request, token);
    [HttpPut("{id:int}")] public Task<IActionResult> Update(int id, AdminAccountInput request, CancellationToken token) => Save(id, request, token);
    private async Task<IActionResult> Save(int? id, AdminAccountInput request, CancellationToken token)
    {
        try { var result = await service.SaveAsync(id, request, token); return result is null ? NotFound() : Ok(result); }
        catch (ArgumentException error) { return Problem(statusCode: 409, title: error.Message); }
        catch (DbUpdateException) { return Problem(statusCode: 409, title: "Không lưu được tài khoản; kiểm tra tên đăng nhập trùng hoặc dữ liệu đã thay đổi."); }
    }
}
