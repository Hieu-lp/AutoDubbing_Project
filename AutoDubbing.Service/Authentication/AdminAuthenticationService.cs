using AutoDubbing.Data.Entities;
using AutoDubbing.Data.Repositories;
using Microsoft.AspNetCore.Identity;

namespace AutoDubbing.Service.Authentication;

public sealed class AdminAuthenticationService(
    IAdminUserRepository repository,
    IPasswordHasher<AdminUser> passwordHasher,
    IAccessTokenIssuer tokenIssuer) : IAdminAuthenticationService
{
    public const int PasswordMaxLength = 1024;

    public static bool IsValidUsername(string username) =>
        !string.IsNullOrWhiteSpace(username)
        && username.Length <= AdminUser.UsernameMaxLength
        && username == username.Trim();

    public async Task<AdminLoginResponse?> LoginAsync(string username, string password,
        CancellationToken cancellationToken = default)
    {
        if (!IsValidUsername(username) || string.IsNullOrWhiteSpace(password)
            || password.Length > PasswordMaxLength)
        {
            return null;
        }

        var admin = await repository.FindByUsernameAsync(username, cancellationToken);
        if (admin is null || admin.Status != AdminAccountStatus.Active)
        {
            return null;
        }

        PasswordVerificationResult verification;
        try
        {
            verification = passwordHasher.VerifyHashedPassword(admin, admin.PasswordHash, password);
        }
        catch (FormatException)
        {
            // Hash cũ/hỏng phải bị từ chối, không làm API trả lỗi 500.
            return null;
        }

        if (verification == PasswordVerificationResult.Failed)
        {
            return null;
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            var newHash = passwordHasher.HashPassword(admin, password);
            await repository.UpdatePasswordHashAsync(admin.Id, admin.PasswordHash, newHash,
                cancellationToken);
        }

        var profile = AdminProfile.FromEntity(admin);
        var token = tokenIssuer.Issue(profile);
        return new AdminLoginResponse(token.AccessToken, "Bearer", token.ExpiresAtUtc, profile);
    }

    public async Task<AdminProfile?> GetActiveAdminAsync(int id,
        CancellationToken cancellationToken = default)
    {
        var admin = await repository.FindByIdAsync(id, cancellationToken);
        return admin is { Status: AdminAccountStatus.Active } ? AdminProfile.FromEntity(admin) : null;
    }
}
