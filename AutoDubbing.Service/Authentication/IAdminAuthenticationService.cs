namespace AutoDubbing.Service.Authentication;

public sealed record AdminLoginResponse(
    string AccessToken, string TokenType, DateTimeOffset ExpiresAtUtc, AdminProfile Admin);

public interface IAdminAuthenticationService
{
    Task<AdminLoginResponse?> LoginAsync(string username, string password,
        CancellationToken cancellationToken = default);
    Task<AdminProfile?> GetActiveAdminAsync(int id, CancellationToken cancellationToken = default);
}
