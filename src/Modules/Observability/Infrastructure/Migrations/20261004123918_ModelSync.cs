using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.Observability.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "observability",
                table: "MetricSamples");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "observability",
                table: "ServiceLevelObjectives",
                newName: "xmin");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "observability",
                table: "RequestTelemetryAggregates",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "observability",
                table: "ServiceLevelObjectives",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldNullable: true);

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "observability",
                table: "RequestTelemetryAggregates",
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
                schema: "observability",
                table: "ServiceLevelObjectives",
                newName: "RowVersion");

            migrationBuilder.RenameColumn(
                name: "xmin",
                schema: "observability",
                table: "RequestTelemetryAggregates",
                newName: "RowVersion");

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "observability",
                table: "ServiceLevelObjectives",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "observability",
                table: "RequestTelemetryAggregates",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "observability",
                table: "MetricSamples",
                type: "bytea",
                nullable: true);
        }
    }
}
