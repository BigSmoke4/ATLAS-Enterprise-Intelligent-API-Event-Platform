using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.PolicyEngine.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "policyengine",
                table: "PolicyRules",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "policyengine",
                table: "PolicyRules",
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
                schema: "policyengine",
                table: "PolicyRules",
                newName: "RowVersion");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "policyengine",
                table: "PolicyRules",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);
        }
    }
}
