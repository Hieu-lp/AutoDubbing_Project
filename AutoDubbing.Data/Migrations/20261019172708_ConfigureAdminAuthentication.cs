using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoDubbing.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConfigureAdminAuthentication : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Kiểm tra trước khi thu hẹp cột; không cắt ngắn hay tự sửa dữ liệu cũ.
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [NGUOI_DUNG] WHERE DATALENGTH([ten_dang_nhap]) > 200)
                BEGIN
                    THROW 51000, N'Username vuot qua 100 ky tu. Can xu ly du lieu truoc khi migrate.', 1;
                END;

                IF EXISTS (
                    SELECT [ten_dang_nhap] COLLATE Latin1_General_100_BIN2
                    FROM [NGUOI_DUNG]
                    GROUP BY [ten_dang_nhap] COLLATE Latin1_General_100_BIN2
                    HAVING COUNT(*) > 1)
                BEGIN
                    THROW 51001, N'Username bi trung theo collation phan biet hoa thuong. Can xu ly du lieu truoc khi migrate.', 1;
                END;

                IF EXISTS (
                    SELECT 1 FROM [NGUOI_DUNG]
                    WHERE DATALENGTH([trang_thai]) <> 12
                        OR [trang_thai] COLLATE Latin1_General_100_BIN2 NOT IN (N'Active', N'Locked'))
                BEGIN
                    THROW 51002, N'Trang thai tai khoan phai la Active hoac Locked. Can xu ly du lieu truoc khi migrate.', 1;
                END;
                """);

            migrationBuilder.AlterColumn<string>(
                name: "trang_thai",
                table: "NGUOI_DUNG",
                type: "nvarchar(6)",
                maxLength: 6,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.AlterColumn<string>(
                name: "ten_dang_nhap",
                table: "NGUOI_DUNG",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                collation: "Latin1_General_100_BIN2",
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_NGUOI_DUNG_ten_dang_nhap",
                table: "NGUOI_DUNG",
                column: "ten_dang_nhap",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_NGUOI_DUNG_trang_thai",
                table: "NGUOI_DUNG",
                sql: "DATALENGTH([trang_thai]) = 12 AND [trang_thai] COLLATE Latin1_General_100_BIN2 IN ('Active', 'Locked')");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_NGUOI_DUNG_ten_dang_nhap",
                table: "NGUOI_DUNG");

            migrationBuilder.DropCheckConstraint(
                name: "CK_NGUOI_DUNG_trang_thai",
                table: "NGUOI_DUNG");

            migrationBuilder.AlterColumn<string>(
                name: "trang_thai",
                table: "NGUOI_DUNG",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(6)",
                oldMaxLength: 6);

            migrationBuilder.AlterColumn<string>(
                name: "ten_dang_nhap",
                table: "NGUOI_DUNG",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldCollation: "Latin1_General_100_BIN2");
        }
    }
}
