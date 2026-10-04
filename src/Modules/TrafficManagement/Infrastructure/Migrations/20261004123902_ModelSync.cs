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
            // Reviewed edit: Entity.RowVersion (uint) maps to PostgreSQL's "xmin"
            // system column, but the Npgsql generator also emits physical DDL for
            // that mapping (RenameColumn "RowVersion" -> "xmin", then AlterColumn
            // to type "xid"). PostgreSQL rejects it -- 42701, column name "xmin"
            // conflicts with a system column name (npgsql/efcore.pg#3854). The
            // system column already exists on every table, so the only real schema
            // change is dropping the obsolete physical "RowVersion" column that
            // the earlier byte[] mapping created. A regeneration of this migration
            // must repeat this edit; docs/database.md links the provider issue.
            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "trafficmanagement",
                table: "TrafficPolicyTargets");

            migrationBuilder.DropColumn(
                name: "RowVersion",
                schema: "trafficmanagement",
                table: "TrafficPolicies");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "trafficmanagement",
                table: "TrafficPolicyTargets",
                type: "bytea",
                rowVersion: true,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RowVersion",
                schema: "trafficmanagement",
                table: "TrafficPolicies",
                type: "bytea",
                rowVersion: true,
                nullable: true);
        }
    }
}
