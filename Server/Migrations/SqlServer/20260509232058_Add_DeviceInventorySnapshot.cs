using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderLink.Server.Migrations.SqlServer
{
    /// <inheritdoc />
    public partial class Add_DeviceInventorySnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceInventorySnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    DeviceID = table.Column<string>(type: "nvarchar(450)", nullable: false),
                    CapturedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Apps = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceInventorySnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceInventorySnapshots_Devices_DeviceID",
                        column: x => x.DeviceID,
                        principalTable: "Devices",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceInventorySnapshots_DeviceID_CapturedAt",
                table: "DeviceInventorySnapshots",
                columns: new[] { "DeviceID", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceInventorySnapshots");
        }
    }
}
