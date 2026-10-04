using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.APIManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "apimanagement",
                table: "ApiVersions",
                newName: "xmin");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "apimanagement",
                table: "ApiRoutes",
                newName: "xmin");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "apimanagement",
                table: "ApiDefinitions",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "apimanagement",
                table: "ApiVersions",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "apimanagement",
                table: "ApiRoutes",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "apimanagement",
                table: "ApiDefinitions",
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
                schema: "apimanagement",
                table: "ApiVersions",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "xmin",
                schema: "apimanagement",
                table: "ApiRoutes",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "xmin",
                schema: "apimanagement",
                table: "ApiDefinitions",
                newName: "RowVersion");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "apimanagement",
                table: "ApiVersions",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "apimanagement",
                table: "ApiRoutes",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "apimanagement",
                table: "ApiDefinitions",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);
        }
    }
}
