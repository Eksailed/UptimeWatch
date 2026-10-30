using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace UptimeWatch.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OwnerForeignKey : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Monitors of accounts that were already deleted would violate the new foreign key and are never
            // reachable again; their CheckResults and Incidents go with them (ON DELETE CASCADE from Monitors).
            migrationBuilder.Sql("""DELETE FROM "Monitors" WHERE "OwnerId" NOT IN (SELECT "Id" FROM "AspNetUsers");""");

            // The error columns become varchar(1000): cut longer stored texts first (999 chars + the "…" marker).
            migrationBuilder.Sql("""UPDATE "Incidents" SET "LastError" = left("LastError", 999) || '…' WHERE length("LastError") > 1000;""");
            migrationBuilder.Sql("""UPDATE "CheckResults" SET "Error" = left("Error", 999) || '…' WHERE length("Error") > 1000;""");

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "Incidents",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Error",
                table: "CheckResults",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Monitors_AspNetUsers_OwnerId",
                table: "Monitors",
                column: "OwnerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Monitors_AspNetUsers_OwnerId",
                table: "Monitors");

            migrationBuilder.AlterColumn<string>(
                name: "LastError",
                table: "Incidents",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Error",
                table: "CheckResults",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(1000)",
                oldMaxLength: 1000,
                oldNullable: true);
        }
    }
}
