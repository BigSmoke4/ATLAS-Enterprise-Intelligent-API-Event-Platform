using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.EventPlatform.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "eventplatform",
                table: "IdempotencyRecords");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "eventplatform",
                table: "DeadLetterEvents",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "eventplatform",
                table: "DeadLetterEvents",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "xmin",
                schema: "eventplatform",
                table: "DeadLetterEvents",
                newName: "RowVersion");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "eventplatform",
                table: "IdempotencyRecords",
                type: "bytea",
                nullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "eventplatform",
                table: "DeadLetterEvents",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);
        }
    }
}
