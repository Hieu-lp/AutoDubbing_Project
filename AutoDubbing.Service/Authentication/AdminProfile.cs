using AutoDubbing.Data.Entities;

namespace AutoDubbing.Service.Authentication;

public sealed record AdminProfile(int Id, string Username, string FullName, string Status,
    DateTimeOffset? PasswordChangeDueAtUtc = null, bool PasswordChangeRequired = false)
{
    internal static AdminProfile FromEntity(AdminUser admin) =>
        new(admin.Id, admin.Username, admin.FullName, admin.Status,
            admin.PasswordChangedAtUtc.AddHours(24),
            DateTimeOffset.UtcNow >= admin.PasswordChangedAtUtc.AddHours(24));
}
