using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AutoDubbing.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "NGUOI_DUNG",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ten_dang_nhap = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    mat_khau_hash = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ho_ten = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    trang_thai = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NGUOI_DUNG", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "NHAT_KY_HE_THONG",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nguoi_dung_id = table.Column<int>(type: "int", nullable: true),
                    thanh_phan = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    muc_do = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    thong_diep = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    thoi_diem = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NHAT_KY_HE_THONG", x => x.id);
                    table.ForeignKey(
                        name: "FK_NHAT_KY_HE_THONG_NGUOI_DUNG_nguoi_dung_id",
                        column: x => x.nguoi_dung_id,
                        principalTable: "NGUOI_DUNG",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "POI",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nguoi_dung_id = table.Column<int>(type: "int", nullable: false),
                    ten = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    lat = table.Column<double>(type: "float", nullable: false),
                    lng = table.Column<double>(type: "float", nullable: false),
                    ban_kinh_kich_hoat = table.Column<double>(type: "float", nullable: false),
                    anh_thumbnail = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    mo_ta_goc = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ngon_ngu_goc = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    trang_thai = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_POI", x => x.id);
                    table.ForeignKey(
                        name: "FK_POI_NGUOI_DUNG_nguoi_dung_id",
                        column: x => x.nguoi_dung_id,
                        principalTable: "NGUOI_DUNG",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SESSION",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    nguoi_dung_id = table.Column<int>(type: "int", nullable: false),
                    ten = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    mo_ta = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    trang_thai = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SESSION", x => x.id);
                    table.ForeignKey(
                        name: "FK_SESSION_NGUOI_DUNG_nguoi_dung_id",
                        column: x => x.nguoi_dung_id,
                        principalTable: "NGUOI_DUNG",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "AUDIO",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    poi_id = table.Column<int>(type: "int", nullable: false),
                    ngon_ngu = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    duong_dan_file = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    thoi_luong = table.Column<int>(type: "int", nullable: false),
                    trang_thai = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AUDIO", x => x.id);
                    table.ForeignKey(
                        name: "FK_AUDIO_POI_poi_id",
                        column: x => x.poi_id,
                        principalTable: "POI",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "BAN_DICH",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    poi_id = table.Column<int>(type: "int", nullable: false),
                    ngon_ngu = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    van_ban = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    trang_thai = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    thoi_diem_cap_nhat = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BAN_DICH", x => x.id);
                    table.ForeignKey(
                        name: "FK_BAN_DICH_POI_poi_id",
                        column: x => x.poi_id,
                        principalTable: "POI",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "SU_KIEN_SU_DUNG",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    poi_id = table.Column<int>(type: "int", nullable: false),
                    loai_su_kien = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ngon_ngu = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    thoi_diem = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SU_KIEN_SU_DUNG", x => x.id);
                    table.ForeignKey(
                        name: "FK_SU_KIEN_SU_DUNG_POI_poi_id",
                        column: x => x.poi_id,
                        principalTable: "POI",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "LO_TRINH",
                columns: table => new
                {
                    id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    session_id = table.Column<int>(type: "int", nullable: false),
                    ten = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    danh_sach_poi = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    trang_thai = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LO_TRINH", x => x.id);
                    table.ForeignKey(
                        name: "FK_LO_TRINH_SESSION_session_id",
                        column: x => x.session_id,
                        principalTable: "SESSION",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_AUDIO_poi_id",
                table: "AUDIO",
                column: "poi_id");

            migrationBuilder.CreateIndex(
                name: "IX_BAN_DICH_poi_id",
                table: "BAN_DICH",
                column: "poi_id");

            migrationBuilder.CreateIndex(
                name: "IX_LO_TRINH_session_id",
                table: "LO_TRINH",
                column: "session_id");

            migrationBuilder.CreateIndex(
                name: "IX_NHAT_KY_HE_THONG_nguoi_dung_id",
                table: "NHAT_KY_HE_THONG",
                column: "nguoi_dung_id");

            migrationBuilder.CreateIndex(
                name: "IX_POI_nguoi_dung_id",
                table: "POI",
                column: "nguoi_dung_id");

            migrationBuilder.CreateIndex(
                name: "IX_SESSION_nguoi_dung_id",
                table: "SESSION",
                column: "nguoi_dung_id");

            migrationBuilder.CreateIndex(
                name: "IX_SU_KIEN_SU_DUNG_poi_id",
                table: "SU_KIEN_SU_DUNG",
                column: "poi_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AUDIO");

            migrationBuilder.DropTable(
                name: "BAN_DICH");

            migrationBuilder.DropTable(
                name: "LO_TRINH");

            migrationBuilder.DropTable(
                name: "NHAT_KY_HE_THONG");

            migrationBuilder.DropTable(
                name: "SU_KIEN_SU_DUNG");

            migrationBuilder.DropTable(
                name: "SESSION");

            migrationBuilder.DropTable(
                name: "POI");

            migrationBuilder.DropTable(
                name: "NGUOI_DUNG");
        }
    }
}
