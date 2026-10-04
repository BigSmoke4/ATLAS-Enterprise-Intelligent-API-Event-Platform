using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.Organizations.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "organizations",
                table: "Teams",
                newName: "xmin");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "organizations",
                table: "Organizations",
                newName: "xmin");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "organizations",
                table: "Environments",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "organizations",
                table: "Teams",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "organizations",
                table: "Organizations",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldRowVersion: true,
                oldNullable: true);

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "organizations",
                table: "Environments",
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
                schema: "organizations",
                table: "Teams",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "xmin",
                schema: "organizations",
                table: "Organizations",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "xmin",
                schema: "organizations",
                table: "Environments",
                newName: "RowVersion");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "organizations",
                table: "Teams",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "organizations",
                table: "Organizations",
                type: "bytea",
                rowVersion: true,
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "organizations",
                table: "Environments",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);
        }
    }
}
