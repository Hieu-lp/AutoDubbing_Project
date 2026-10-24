namespace AutoDubbing.Service.Pois;

public interface IPoiManagementService
{
    Task<IReadOnlyList<PoiManagementResponse>> ListAsync(CancellationToken cancellationToken);
    Task<PoiManagementResponse?> FindAsync(int id, CancellationToken cancellationToken);
    Task<PoiManagementResponse> CreateAsync(int adminId, PoiManagementRequest request,
        CancellationToken cancellationToken);
    Task<PoiManagementResponse?> UpdateAsync(int id, PoiManagementRequest request,
        CancellationToken cancellationToken);
    Task<PoiDeleteResult> DeleteAsync(int id, CancellationToken cancellationToken);
}
