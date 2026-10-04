using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.IncidentManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ModelSync : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "incidentmanagement",
                table: "IncidentTimelineEntries");

            migrationBuilder.RenameColumn(
                name: "RowVersion",
                schema: "incidentmanagement",
                table: "Incidents",
                newName: "xmin");

            migrationBuilder.AlterColumn<uint>(
                name: "xmin",
                schema: "incidentmanagement",
                table: "Incidents",
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
                schema: "incidentmanagement",
                table: "Incidents",
                newName: "RowVersion");

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "incidentmanagement",
                table: "IncidentTimelineEntries",
                type: "bytea",
                nullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "RowVersion",
                schema: "incidentmanagement",
                table: "Incidents",
                type: "bytea",
                nullable: true,
                oldClrType: typeof(uint),
                oldType: "xid",
                oldRowVersion: true);
        }
    }
}
