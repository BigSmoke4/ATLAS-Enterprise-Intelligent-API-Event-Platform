using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.ServiceRegistry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "serviceregistry",
                table: "ServiceDependencies");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "serviceregistry",
                table: "Services",
                newName: "xmin");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "serviceregistry",
                table: "ServiceInstances",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "serviceregistry",
                table: "Services",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "serviceregistry",
                table: "ServiceInstances",
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
                schema: "serviceregistry",
                table: "Services",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "xmin",
                schema: "serviceregistry",
                table: "ServiceInstances",
                newName: "RowVersion");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "serviceregistry",
                table: "Services",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "serviceregistry",
                table: "ServiceInstances",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "serviceregistry",
                table: "ServiceDependencies",
                type: "bytea",
                nullable: true);
        }
    }
}
