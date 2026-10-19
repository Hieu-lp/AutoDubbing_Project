namespace AutoDubbing.Service.Authentication;

public interface IAdminProvisioningService
{
    Task<bool> CreateFirstAdminAsync(string username, string fullName, string password,
        CancellationToken cancellationToken = default);
}
