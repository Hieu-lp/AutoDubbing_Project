using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoDubbing.Data.Migrations;

public partial class AddAdminPasswordHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "mat_khau_doi_luc_utc", table: "NGUOI_DUNG", type: "datetimeoffset",
            nullable: false, defaultValue: DateTimeOffset.MinValue);
        migrationBuilder.AddColumn<string>(
            name: "lich_su_mat_khau", table: "NGUOI_DUNG", type: "nvarchar(max)",
            nullable: false, defaultValue: "[]");
        migrationBuilder.Sql("UPDATE [NGUOI_DUNG] SET [mat_khau_doi_luc_utc] = DATEADD(hour, -24, TODATETIMEOFFSET(SYSUTCDATETIME(), '+00:00'));");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "mat_khau_doi_luc_utc", table: "NGUOI_DUNG");
        migrationBuilder.DropColumn(name: "lich_su_mat_khau", table: "NGUOI_DUNG");
    }
}
