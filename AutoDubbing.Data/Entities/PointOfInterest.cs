using System.ComponentModel.DataAnnotations;

namespace AutoDubbing.Data.Entities
{
    public class PointOfInterest
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        // Tọa độ GPS
        public double Latitude { get; set; }
        public double Longitude { get; set; }

        // Bán kính kích hoạt (tính bằng mét)
        public double ProximityRadius { get; set; }

        // Đường dẫn ảnh lưu trên Supabase Storage
        public string? ThumbnailUrl { get; set; }
    }
}