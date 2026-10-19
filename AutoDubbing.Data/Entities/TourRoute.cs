namespace AutoDubbing.Data.Entities;

public class TourRoute
{
    public int Id { get; set; }
    public int SessionId { get; set; }
    public string Name { get; set; } = string.Empty;

    // Mảng JSON chứa ID các POI theo thứ tự của lộ trình.
    // Dùng chuỗi để lưu JSON, không khai báo quan hệ N-N tự động của EF.
    public string PoiIdsJson { get; set; } = "[]";

    public string Status { get; set; } = string.Empty;

    public TourSession Session { get; set; } = null!;
}
