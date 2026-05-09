using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BorderLink.Server.Migrations.PostgreSql;

/// <inheritdoc />
public partial class Add_KeyValueRecords : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "Discriminator",
            table: "BorderLinkUsers",
            type: "character varying(13)",
            maxLength: 13,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "text");

        migrationBuilder.CreateTable(
            name: "KeyValueRecords",
            columns: table => new
            {
                Key = table.Column<Guid>(type: "uuid", nullable: false),
                Value = table.Column<string>(type: "text", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_KeyValueRecords", x => x.Key);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "KeyValueRecords");

        migrationBuilder.AlterColumn<string>(
            name: "Discriminator",
            table: "BorderLinkUsers",
            type: "text",
            nullable: false,
            oldClrType: typeof(string),
            oldType: "character varying(13)",
            oldMaxLength: 13);
    }
}
