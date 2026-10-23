using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Repositories;
using Microsoft.AspNetCore.Identity;

namespace AutoDubbing.Service.Authentication;

public sealed class AdminProvisioningService(
    IAdminUserRepository repository,
    IPasswordHasher<AdminUser> passwordHasher) : IAdminProvisioningService
{
    public Task<bool> CreateFirstAdminAsync(string username, string fullName, string password,
        CancellationToken cancellationToken = default)
    {
        if (!AdminAuthenticationService.IsValidUsername(username))
        {
            throw new ArgumentException(
                "Username phải có 1–100 ký tự, không có khoảng trắng đầu/cuối.", nameof(username));
        }

        if (string.IsNullOrWhiteSpace(fullName))
        {
            throw new ArgumentException("Họ tên không được để trống.", nameof(fullName));
        }

        if (string.IsNullOrWhiteSpace(password)
            || password.Length > AdminAuthenticationService.PasswordMaxLength)
        {
            throw new ArgumentException("Mật khẩu phải có 1–1024 ký tự và không chỉ chứa khoảng trắng.",
                nameof(password));
        }

        var admin = new AdminUser
        {
            Username = username,
            FullName = fullName,
            Status = AdminAccountStatus.Active
        };
        admin.PasswordHash = passwordHasher.HashPassword(admin, password);
        return repository.TryCreateFirstAdminAsync(admin, cancellationToken);
    }
}
