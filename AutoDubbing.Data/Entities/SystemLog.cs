namespace AutoDubbing.Data.Entities;

public class SystemLog
{
    public int Id { get; set; }

    // NULL khi log do hệ thống/pipeline ghi mà không có Admin thao tác.
    public int? UserId { get; set; }

    public string Component { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; }

    public AdminUser? User { get; set; }
}
