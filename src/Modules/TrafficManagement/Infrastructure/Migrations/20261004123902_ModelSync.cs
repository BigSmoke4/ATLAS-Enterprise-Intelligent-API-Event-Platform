using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.TrafficManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "trafficmanagement",
                table: "TrafficPolicyTargets",
                newName: "xmin");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "trafficmanagement",
                table: "TrafficPolicies",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "trafficmanagement",
                table: "TrafficPolicyTargets",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "trafficmanagement",
                table: "TrafficPolicies",
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
                schema: "trafficmanagement",
                table: "TrafficPolicyTargets",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "xmin",
                schema: "trafficmanagement",
                table: "TrafficPolicies",
                newName: "RowVersion");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "trafficmanagement",
                table: "TrafficPolicyTargets",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "trafficmanagement",
                table: "TrafficPolicies",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);
        }
    }
}
