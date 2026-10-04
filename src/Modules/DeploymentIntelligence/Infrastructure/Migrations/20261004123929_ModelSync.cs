using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.DeploymentIntelligence.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "deploymentintelligence",
                table: "Deployments",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "deploymentintelligence",
                table: "Deployments",
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
                schema: "deploymentintelligence",
                table: "Deployments",
                newName: "RowVersion");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "deploymentintelligence",
                table: "Deployments",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);
        }
    }
}
