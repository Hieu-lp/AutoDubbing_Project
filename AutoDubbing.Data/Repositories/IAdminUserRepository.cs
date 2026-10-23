using AutoDubbing.Data.Entities;

namespace AutoDubbing.Data.Repositories;

public interface IAdminUserRepository
{
    Task<AdminUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken);
    Task<AdminUser?> FindByIdAsync(int id, CancellationToken cancellationToken);
    Task UpdatePasswordHashAsync(int id, string previousHash, string newHash,
        CancellationToken cancellationToken);
    Task<bool> TryCreateFirstAdminAsync(AdminUser admin, CancellationToken cancellationToken);
}
