namespace AutoDubbing.Data.Entities;

// Session chủ đề/sự kiện du lịch, không phải phiên đăng nhập.
public class TourSession
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public AdminUser User { get; set; } = null!;
    public ICollection<TourRoute> Routes { get; set; }
        = new List<TourRoute>();
}
