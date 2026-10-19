using AutoDubbing.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace AutoDubbing.Data.Contexts;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<AdminUser> Users => Set<AdminUser>();
    public DbSet<PointOfInterest> PointsOfInterest => Set<PointOfInterest>();
    public DbSet<PoiTranslation> Translations => Set<PoiTranslation>();
    public DbSet<PoiAudio> Audios => Set<PoiAudio>();
    public DbSet<TourSession> Sessions => Set<TourSession>();
    public DbSet<TourRoute> Routes => Set<TourRoute>();
    public DbSet<UsageEvent> UsageEvents => Set<UsageEvent>();
    public DbSet<SystemLog> SystemLogs => Set<SystemLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AdminUser>(entity =>
        {
            entity.ToTable("NGUOI_DUNG");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.Username).HasColumnName("ten_dang_nhap");
            entity.Property(x => x.PasswordHash).HasColumnName("mat_khau_hash");
            entity.Property(x => x.FullName).HasColumnName("ho_ten");
            entity.Property(x => x.Status).HasColumnName("trang_thai");
        });

        modelBuilder.Entity<PointOfInterest>(entity =>
        {
            entity.ToTable("POI");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("nguoi_dung_id");
            entity.Property(x => x.Name).HasColumnName("ten");
            entity.Property(x => x.Latitude).HasColumnName("lat");
            entity.Property(x => x.Longitude).HasColumnName("lng");
            entity.Property(x => x.ProximityRadius)
                .HasColumnName("ban_kinh_kich_hoat");
            entity.Property(x => x.ThumbnailUrl).HasColumnName("anh_thumbnail");
            entity.Property(x => x.OriginalDescription).HasColumnName("mo_ta_goc");
            entity.Property(x => x.OriginalLanguage).HasColumnName("ngon_ngu_goc");
            entity.Property(x => x.Status).HasColumnName("trang_thai");

            entity.HasOne(x => x.User)
                .WithMany(x => x.PointsOfInterest)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PoiTranslation>(entity =>
        {
            entity.ToTable("BAN_DICH");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PointOfInterestId).HasColumnName("poi_id");
            entity.Property(x => x.Language).HasColumnName("ngon_ngu");
            entity.Property(x => x.Text).HasColumnName("van_ban");
            entity.Property(x => x.Status).HasColumnName("trang_thai");
            entity.Property(x => x.UpdatedAt).HasColumnName("thoi_diem_cap_nhat");

            entity.HasOne(x => x.PointOfInterest)
                .WithMany(x => x.Translations)
                .HasForeignKey(x => x.PointOfInterestId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<PoiAudio>(entity =>
        {
            entity.ToTable("AUDIO");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PointOfInterestId).HasColumnName("poi_id");
            entity.Property(x => x.Language).HasColumnName("ngon_ngu");
            entity.Property(x => x.FilePath).HasColumnName("duong_dan_file");
            entity.Property(x => x.Duration).HasColumnName("thoi_luong");
            entity.Property(x => x.Status).HasColumnName("trang_thai");

            entity.HasOne(x => x.PointOfInterest)
                .WithMany(x => x.Audios)
                .HasForeignKey(x => x.PointOfInterestId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TourSession>(entity =>
        {
            entity.ToTable("SESSION");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("nguoi_dung_id");
            entity.Property(x => x.Name).HasColumnName("ten");
            entity.Property(x => x.Description).HasColumnName("mo_ta");
            entity.Property(x => x.Status).HasColumnName("trang_thai");

            entity.HasOne(x => x.User)
                .WithMany(x => x.Sessions)
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<TourRoute>(entity =>
        {
            entity.ToTable("LO_TRINH");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.SessionId).HasColumnName("session_id");
            entity.Property(x => x.Name).HasColumnName("ten");
            entity.Property(x => x.PoiIdsJson)
                .HasColumnName("danh_sach_poi")
                .HasColumnType("nvarchar(max)");
            entity.Property(x => x.Status).HasColumnName("trang_thai");

            entity.HasOne(x => x.Session)
                .WithMany(x => x.Routes)
                .HasForeignKey(x => x.SessionId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<UsageEvent>(entity =>
        {
            entity.ToTable("SU_KIEN_SU_DUNG");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.PointOfInterestId).HasColumnName("poi_id");
            entity.Property(x => x.EventType).HasColumnName("loai_su_kien");
            entity.Property(x => x.Language).HasColumnName("ngon_ngu");
            entity.Property(x => x.OccurredAt).HasColumnName("thoi_diem");

            entity.HasOne(x => x.PointOfInterest)
                .WithMany(x => x.UsageEvents)
                .HasForeignKey(x => x.PointOfInterestId)
                .OnDelete(DeleteBehavior.NoAction);
        });

        modelBuilder.Entity<SystemLog>(entity =>
        {
            entity.ToTable("NHAT_KY_HE_THONG");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id");
            entity.Property(x => x.UserId).HasColumnName("nguoi_dung_id");
            entity.Property(x => x.Component).HasColumnName("thanh_phan");
            entity.Property(x => x.Level).HasColumnName("muc_do");
            entity.Property(x => x.Message).HasColumnName("thong_diep");
            entity.Property(x => x.OccurredAt).HasColumnName("thoi_diem");

            entity.HasOne(x => x.User)
                .WithMany(x => x.SystemLogs)
                .HasForeignKey(x => x.UserId)
                .IsRequired(false)
                .OnDelete(DeleteBehavior.NoAction);
        });
    }
}
