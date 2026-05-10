using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderLink.Server.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class Add_InventoryRefreshSchedule : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InventoryRefreshSchedules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationID = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    IntervalHours = table.Column<int>(type: "integer", nullable: false),
                    DeviceGroupId = table.Column<string>(type: "text", nullable: true),
                    DeviceTagFilter = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Enabled = table.Column<bool>(type: "boolean", nullable: false),
                    LastRunAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InventoryRefreshSchedules", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InventoryRefreshSchedules_OrganizationID_Enabled",
                table: "InventoryRefreshSchedules",
                columns: new[] { "OrganizationID", "Enabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InventoryRefreshSchedules");
        }
    }
}
