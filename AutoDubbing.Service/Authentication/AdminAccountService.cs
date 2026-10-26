using System.ComponentModel.DataAnnotations;
using System.Data;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Service.Authentication;

public sealed class AdminAccountInput : IValidatableObject
{
    [Required, StringLength(100, MinimumLength = 1)] public string Username { get; init; } = "";
    [Required, StringLength(200, MinimumLength = 1)] public string FullName { get; init; } = "";
    [StringLength(1024, MinimumLength = 8)] public string? Password { get; init; }
    [Required, RegularExpression("^(Active|Locked)$")] public string Status { get; init; } = "Active";
    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (!AdminAuthenticationService.IsValidUsername(Username)) yield return new("Tên đăng nhập không hợp lệ.", [nameof(Username)]);
        if (string.IsNullOrWhiteSpace(FullName)) yield return new("Họ tên không được rỗng.", [nameof(FullName)]);
        if (Password is not null && string.IsNullOrWhiteSpace(Password)) yield return new("Mật khẩu không được chỉ gồm khoảng trắng.", [nameof(Password)]);
    }
}
public sealed class AdminAccountService(AppDbContext db, IPasswordHasher<AdminUser> hasher)
{
    public Task<AdminProfile[]> ListAsync(CancellationToken token) => db.Users.AsNoTracking().OrderBy(user => user.Id)
        .Select(user => new AdminProfile(user.Id, user.Username, user.FullName, user.Status)).ToArrayAsync(token);
    public async Task<AdminProfile?> SaveAsync(int? id, AdminAccountInput input, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var users = await db.Users.OrderBy(user => user.Id).ToListAsync(token);
        var row = id is null ? new AdminUser() : users.SingleOrDefault(user => user.Id == id);
        if (row is null) return null;
        if (users.Any(user => user.Id != id && user.Username == input.Username)) throw new ArgumentException("Tên đăng nhập đã tồn tại.");
        if (id is null && input.Password is null) throw new ArgumentException("Tài khoản mới cần mật khẩu ít nhất 8 ký tự.");
        if (input.Status == "Locked" && !users.Any(user => user.Id != id && user.Status == "Active"))
            throw new ArgumentException("Không thể khóa tài khoản quản trị hoạt động cuối cùng.");
        row.Username = input.Username; row.FullName = input.FullName.Trim(); row.Status = input.Status;
        if (input.Password is not null) row.PasswordHash = hasher.HashPassword(row, input.Password);
        if (id is null) db.Users.Add(row);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return AdminProfile.FromEntity(row);
    }
}
