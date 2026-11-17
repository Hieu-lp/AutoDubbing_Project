using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Text.Json;
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
    public async Task<AdminProfile[]> ListAsync(CancellationToken token) =>
        (await db.Users.AsNoTracking().OrderBy(user => user.Id).ToArrayAsync(token))
            .Select(AdminProfile.FromEntity).ToArray();

    private bool MatchesPassword(AdminUser admin, string hash, string password)
    {
        try { return hasher.VerifyHashedPassword(admin, hash, password) != PasswordVerificationResult.Failed; }
        catch (FormatException) { return false; }
    }

    private void SetNewPassword(AdminUser admin, string password)
    {
        var history = JsonSerializer.Deserialize<List<string>>(admin.PasswordHistoryJson) ?? [];
        if ((!string.IsNullOrEmpty(admin.PasswordHash) && MatchesPassword(admin, admin.PasswordHash, password))
            || history.Any(hash => MatchesPassword(admin, hash, password)))
            throw new ArgumentException("Không được sử dụng lại mật khẩu hiện tại hoặc mật khẩu đã dùng trước đây.");

        if (!string.IsNullOrEmpty(admin.PasswordHash)) history.Add(admin.PasswordHash);
        admin.PasswordHash = hasher.HashPassword(admin, password);
        admin.PasswordHistoryJson = JsonSerializer.Serialize(history);
        admin.PasswordChangedAtUtc = DateTimeOffset.UtcNow;
    }

    public async Task<AdminProfile?> ChangePasswordAsync(int id, string currentPassword,
        string newPassword, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8
            || newPassword.Length > AdminAuthenticationService.PasswordMaxLength)
            throw new ArgumentException("Mật khẩu mới phải có từ 8 đến 1024 ký tự và không chỉ gồm khoảng trắng.");

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, token);
        var admin = await db.Users.SingleOrDefaultAsync(user => user.Id == id, token);
        if (admin is null || admin.Status != AdminAccountStatus.Active) return null;
        if (!MatchesPassword(admin, admin.PasswordHash, currentPassword))
            throw new ArgumentException("Mật khẩu hiện tại không chính xác.");
        SetNewPassword(admin, newPassword);
        await db.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
        return AdminProfile.FromEntity(admin);
    }
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
        if (input.Password is not null) SetNewPassword(row, input.Password);
        if (id is null) db.Users.Add(row);
        await db.SaveChangesAsync(token); await transaction.CommitAsync(token);
        return AdminProfile.FromEntity(row);
    }
}
