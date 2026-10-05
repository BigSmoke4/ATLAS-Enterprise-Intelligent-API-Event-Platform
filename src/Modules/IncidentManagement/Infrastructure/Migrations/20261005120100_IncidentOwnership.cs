using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Atlas.Modules.IncidentManagement.Infrastructure;

#nullable disable

namespace Atlas.Modules.IncidentManagement.Infrastructure.Migrations
{
    /// <summary>
    /// Per-incident ownership (ADR-012): the declarer is stored so the write
    /// policy can be enforced per record, not only per role. Nullable because
    /// incidents created before this migration — and alert-sourced incidents,
    /// which have no user behind them — stay valid; the policy treats a null
    /// declarer as "privileged roles only", which fails closed.
    ///
    /// Hand-written for the same reason as the outbox migration: generation
    /// needs an SDK-equipped machine, while CI gates the result twice (the model
    /// drift check and the disposable-schema application).
    /// </summary>
    [DbContext(typeof(IncidentManagementDbContext))]
    [Migration("20261005120100_IncidentOwnership")]
    public partial class IncidentOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "DeclaredByUserId",
                schema: "incidentmanagement",
                table: "Incidents",
                type: "uuid",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DeclaredByUserId",
                schema: "incidentmanagement",
                table: "Incidents");
        }
    }
}
