using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderLink.Server.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Add_TelemetryAndMonitorRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "MetricHistory",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceID = table.Column<string>(type: "TEXT", nullable: false),
                    CapturedAt = table.Column<string>(type: "TEXT", nullable: false),
                    CpuPercent = table.Column<double>(type: "REAL", nullable: false),
                    UsedMemoryPercent = table.Column<double>(type: "REAL", nullable: false),
                    UsedStoragePercent = table.Column<double>(type: "REAL", nullable: false),
                    OrganizationID = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricHistory", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MetricHistory_Devices_DeviceID",
                        column: x => x.DeviceID,
                        principalTable: "Devices",
                        principalColumn: "ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MonitorRules",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    OrganizationID = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Metric = table.Column<int>(type: "INTEGER", nullable: false),
                    Operator = table.Column<int>(type: "INTEGER", nullable: false),
                    Threshold = table.Column<double>(type: "REAL", nullable: false),
                    DurationSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    DeviceFilterTag = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DeviceGroupId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    Channel = table.Column<int>(type: "INTEGER", nullable: false),
                    ChannelTarget = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Enabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<string>(type: "TEXT", nullable: false),
                    LastFiredAt = table.Column<string>(type: "TEXT", nullable: true),
                    CooldownMinutes = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitorRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitorRules_Organizations_OrganizationID",
                        column: x => x.OrganizationID,
                        principalTable: "Organizations",
                        principalColumn: "ID");
                });

            migrationBuilder.CreateTable(
                name: "MonitorRuleFirings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MonitorRuleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DeviceID = table.Column<string>(type: "TEXT", nullable: false),
                    OrganizationID = table.Column<string>(type: "TEXT", nullable: false),
                    FiredAt = table.Column<string>(type: "TEXT", nullable: false),
                    ValueAtFire = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonitorRuleFirings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonitorRuleFirings_Devices_DeviceID",
                        column: x => x.DeviceID,
                        principalTable: "Devices",
                        principalColumn: "ID");
                    table.ForeignKey(
                        name: "FK_MonitorRuleFirings_MonitorRules_MonitorRuleId",
                        column: x => x.MonitorRuleId,
                        principalTable: "MonitorRules",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricHistory_DeviceID_CapturedAt",
                table: "MetricHistory",
                columns: new[] { "DeviceID", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricHistory_OrganizationID_CapturedAt",
                table: "MetricHistory",
                columns: new[] { "OrganizationID", "CapturedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitorRuleFirings_DeviceID",
                table: "MonitorRuleFirings",
                column: "DeviceID");

            migrationBuilder.CreateIndex(
                name: "IX_MonitorRuleFirings_MonitorRuleId_DeviceID_FiredAt",
                table: "MonitorRuleFirings",
                columns: new[] { "MonitorRuleId", "DeviceID", "FiredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MonitorRules_OrganizationID_Enabled",
                table: "MonitorRules",
                columns: new[] { "OrganizationID", "Enabled" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetricHistory");

            migrationBuilder.DropTable(
                name: "MonitorRuleFirings");

            migrationBuilder.DropTable(
                name: "MonitorRules");
        }
    }
}
