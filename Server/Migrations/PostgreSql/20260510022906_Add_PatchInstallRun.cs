using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderLink.Server.Migrations.PostgreSql
{
    /// <inheritdoc />
    public partial class Add_PatchInstallRun : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PatchInstallRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DeviceID = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    OrganizationID = table.Column<string>(type: "text", nullable: false),
                    UpdateId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    UpdateTitle = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    InitiatorId = table.Column<string>(type: "character varying(450)", maxLength: 450, nullable: true),
                    StartedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RebootRequired = table.Column<bool>(type: "boolean", nullable: false),
                    Notes = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PatchInstallRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PatchInstallRuns_Organizations_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "Organizations",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateIndex(
                name: "IX_PatchInstallRuns_DeviceID_UpdateId_Status",
                table: "PatchInstallRuns",
                columns: new[] { "DeviceID", "UpdateId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PatchInstallRuns_OrganizationID_StartedAt",
                table: "PatchInstallRuns",
                columns: new[] { "OrganizationID", "StartedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PatchInstallRuns");
        }
    }
}
