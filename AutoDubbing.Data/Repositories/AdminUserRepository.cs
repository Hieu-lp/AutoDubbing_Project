using System.Data;
using AutoDubbing.Data.Contexts;
using AutoDubbing.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Data.Repositories;

public sealed class AdminUserRepository(AppDbContext dbContext) : IAdminUserRepository
{
    public Task<AdminUser?> FindByUsernameAsync(string username, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().SingleOrDefaultAsync(
            user => user.Username == username, cancellationToken);

    public Task<AdminUser?> FindByIdAsync(int id, CancellationToken cancellationToken) =>
        dbContext.Users.AsNoTracking().SingleOrDefaultAsync(user => user.Id == id, cancellationToken);

    public async Task UpdatePasswordHashAsync(int id, string previousHash, string newHash,
        CancellationToken cancellationToken)
    {
        // Tránh ghi đè nếu mật khẩu đã được đổi bởi một thao tác khác.
        await dbContext.Users.Where(user => user.Id == id && user.PasswordHash == previousHash)
            .ExecuteUpdateAsync(update => update.SetProperty(user => user.PasswordHash, newHash),
                cancellationToken);
    }

    public async Task<bool> TryCreateFirstAdminAsync(AdminUser admin, CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable, cancellationToken);

        if (await dbContext.Users.AnyAsync(cancellationToken))
        {
            return false;
        }

        dbContext.Users.Add(admin);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
