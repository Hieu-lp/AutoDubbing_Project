namespace AutoDubbing.Data.Entities;

// NGUOI_DUNG: mọi tài khoản trong bảng này đều là Admin.
public class AdminUser
{
    public const int UsernameMaxLength = 100;

    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = AdminAccountStatus.Active;

    public ICollection<PointOfInterest> PointsOfInterest { get; set; }
        = new List<PointOfInterest>();
    public ICollection<TourSession> Sessions { get; set; }
        = new List<TourSession>();
    public ICollection<SystemLog> SystemLogs { get; set; }
        = new List<SystemLog>();
}
