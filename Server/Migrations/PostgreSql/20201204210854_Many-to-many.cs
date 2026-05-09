using Microsoft.EntityFrameworkCore.Migrations;

namespace BorderLink.Server.Migrations.PostgreSql;

public partial class Manytomany : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "PermissionLinks");

        migrationBuilder.CreateTable(
            name: "DeviceGroupBorderLinkUser",
            columns: table => new
            {
                DeviceGroupsID = table.Column<string>(type: "text", nullable: false),
                UsersId = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DeviceGroupBorderLinkUser", x => new { x.DeviceGroupsID, x.UsersId });
                table.ForeignKey(
                    name: "FK_DeviceGroupBorderLinkUser_DeviceGroups_DeviceGroupsID",
                    column: x => x.DeviceGroupsID,
                    principalTable: "DeviceGroups",
                    principalColumn: "ID",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_DeviceGroupBorderLinkUser_BorderLinkUsers_UsersId",
                    column: x => x.UsersId,
                    principalTable: "BorderLinkUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_DeviceGroupBorderLinkUser_UsersId",
            table: "DeviceGroupBorderLinkUser",
            column: "UsersId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DeviceGroupBorderLinkUser");

        migrationBuilder.CreateTable(
            name: "PermissionLinks",
            columns: table => new
            {
                ID = table.Column<string>(type: "text", nullable: false),
                DeviceGroupID = table.Column<string>(type: "text", nullable: true),
                UserID = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_PermissionLinks", x => x.ID);
                table.ForeignKey(
                    name: "FK_PermissionLinks_DeviceGroups_DeviceGroupID",
                    column: x => x.DeviceGroupID,
                    principalTable: "DeviceGroups",
                    principalColumn: "ID",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_PermissionLinks_BorderLinkUsers_UserID",
                    column: x => x.UserID,
                    principalTable: "BorderLinkUsers",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_PermissionLinks_DeviceGroupID",
            table: "PermissionLinks",
            column: "DeviceGroupID");

        migrationBuilder.CreateIndex(
            name: "IX_PermissionLinks_UserID",
            table: "PermissionLinks",
            column: "UserID");
    }
}
