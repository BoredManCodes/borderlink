using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderLink.Server.Migrations.Sqlite
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
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationID = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IntervalHours = table.Column<int>(type: "INTEGER", nullable: false),
                    DeviceGroupId = table.Column<string>(type: "TEXT", nullable: true),
                    DeviceTagFilter = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LastRunAt = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false)
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
