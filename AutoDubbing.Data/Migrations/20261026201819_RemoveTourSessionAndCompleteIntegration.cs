using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoDubbing.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTourSessionAndCompleteIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LO_TRINH_SESSION_session_id",
                table: "LO_TRINH");

            migrationBuilder.DropTable(
                name: "SESSION");

            migrationBuilder.DropIndex(
                name: "IX_LO_TRINH_session_id",
                table: "LO_TRINH");

            migrationBuilder.DropColumn(
                name: "session_id",
                table: "LO_TRINH");

            migrationBuilder.AlterColumn<int>(
                name: "poi_id",
                table: "SU_KIEN_SU_DUNG",
                type: "int",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "int");

            migrationBuilder.AddColumn<Guid>(
                name: "event_id",
                table: "SU_KIEN_SU_DUNG",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "guest_id",
                table: "SU_KIEN_SU_DUNG",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "lo_trinh_id",
                table: "SU_KIEN_SU_DUNG",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "buoc",
                table: "NHAT_KY_HE_THONG",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "job_id",
                table: "NHAT_KY_HE_THONG",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ma_loi",
                table: "NHAT_KY_HE_THONG",
                type: "nvarchar(80)",
                maxLength: 80,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ngon_ngu",
                table: "NHAT_KY_HE_THONG",
                type: "nvarchar(35)",
                maxLength: 35,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "poi_id",
                table: "NHAT_KY_HE_THONG",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "thoi_diem_cap_nhat",
                table: "LO_TRINH",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "context_version",
                table: "BAN_DICH",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "context_version",
                table: "AUDIO",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "text_hash",
                table: "AUDIO",
                type: "nvarchar(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SU_KIEN_SU_DUNG_event_id",
                table: "SU_KIEN_SU_DUNG",
                column: "event_id",
                unique: true,
                filter: "[event_id] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_SU_KIEN_SU_DUNG_lo_trinh_id",
                table: "SU_KIEN_SU_DUNG",
                column: "lo_trinh_id");

            migrationBuilder.AddForeignKey(
                name: "FK_SU_KIEN_SU_DUNG_LO_TRINH_lo_trinh_id",
                table: "SU_KIEN_SU_DUNG",
                column: "lo_trinh_id",
                principalTable: "LO_TRINH",
                principalColumn: "id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException("Session data cannot be restored by Down. Restore the verified pre-migration database backup instead.");
        }
    }
}
