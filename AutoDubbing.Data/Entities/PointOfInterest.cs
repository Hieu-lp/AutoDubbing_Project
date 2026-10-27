using System.ComponentModel.DataAnnotations;

namespace AutoDubbing.Data.Entities;

public class PointOfInterest
{
    [Key]
    public int Id { get; set; }

    // Admin tạo/quản lý POI, theo nguoi_dung_id trong ERD.
    public int UserId { get; set; }

    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    public double Latitude { get; set; }
    public double Longitude { get; set; }

    // Đơn vị mét; đã thống nhất bổ sung vào ERD.
    public double ProximityRadius { get; set; }

    public string? ThumbnailUrl { get; set; }
    public string OriginalDescription { get; set; } = string.Empty;
    public string OriginalLanguage { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    [System.ComponentModel.DataAnnotations.Schema.NotMapped]
    public string SourceVersion => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes(System.Text.Json.JsonSerializer.Serialize(
            new[] { OriginalLanguage, Name, OriginalDescription }))));

    public AdminUser User { get; set; } = null!;
    public ICollection<PoiTranslation> Translations { get; set; }
        = new List<PoiTranslation>();
    public ICollection<PoiAudio> Audios { get; set; }
        = new List<PoiAudio>();
    public ICollection<UsageEvent> UsageEvents { get; set; }
        = new List<UsageEvent>();
}
