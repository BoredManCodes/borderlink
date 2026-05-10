using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderLink.Server.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Add_SoftwareActions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SoftwareActionRuns",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ScriptRunId = table.Column<int>(type: "INTEGER", nullable: false),
                    DeviceID = table.Column<string>(type: "TEXT", nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    Source = table.Column<int>(type: "INTEGER", nullable: false),
                    PackageId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    PackageName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    OrganizationID = table.Column<string>(type: "TEXT", nullable: false),
                    InitiatorId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoftwareActionRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoftwareActionRuns_BorderLinkUsers_InitiatorId",
                        column: x => x.InitiatorId,
                        principalTable: "BorderLinkUsers",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SoftwareActionRuns_Organizations_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "Organizations",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_SoftwareActionRuns_ScriptRuns_ScriptRunId",
                        column: x => x.ScriptRunId,
                        principalTable: "ScriptRuns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareActionRuns_InitiatorId",
                table: "SoftwareActionRuns",
                column: "InitiatorId");

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareActionRuns_OrganizationID_CreatedAt",
                table: "SoftwareActionRuns",
                columns: new[] { "OrganizationID", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_SoftwareActionRuns_ScriptRunId",
                table: "SoftwareActionRuns",
                column: "ScriptRunId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SoftwareActionRuns");
        }
    }
}
