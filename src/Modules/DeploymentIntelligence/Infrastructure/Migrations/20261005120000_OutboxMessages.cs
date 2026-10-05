using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Atlas.Modules.DeploymentIntelligence.Infrastructure;

#nullable disable

namespace Atlas.Modules.DeploymentIntelligence.Infrastructure.Migrations
{
    /// <summary>
    /// Transactional outbox for the deployment integration event.
    ///
    /// This migration is hand-written because the generation step
    /// (`.github/workflows/generate-migrations.yml`) needs a machine with the
    /// .NET SDK, and the change is a single new table whose shape is fully
    /// described by the entity configuration. The CI gates it two ways: the
    /// model-drift check fails if the snapshot and the model disagree, and the
    /// disposable-schema step applies every migration to a real PostgreSQL
    /// instance, so a wrong column type or index fails the build rather than a
    /// deployment. Attributes live on this class (there is no designer partial)
    /// because EF discovers migrations by the [Migration] + [DbContext] pair.
    /// </summary>
    [DbContext(typeof(DeploymentIntelligenceDbContext))]
    [Migration("20261005120000_OutboxMessages")]
    public partial class OutboxMessages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutboxMessages",
                schema: "deploymentintelligence",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Topic = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EventType = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    EventId = table.Column<Guid>(type: "uuid", nullable: false),
                    CorrelationId = table.Column<Guid>(type: "uuid", nullable: false),
                    CausationId = table.Column<Guid>(type: "uuid", nullable: true),
                    Producer = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    OccurredAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PayloadJson = table.Column<string>(type: "text", nullable: false),
                    Attempts = table.Column<int>(type: "integer", nullable: false),
                    NextAttemptAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SentAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    AbandonedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutboxMessages", x => x.Id);
                });

            // The relay's claim query: pending rows that are due, oldest first.
            migrationBuilder.CreateIndex(
                name: "IX_OutboxMessages_SentAtUtc_AbandonedAtUtc_NextAttemptAtUtc",
                schema: "deploymentintelligence",
                table: "OutboxMessages",
                columns: new[] { "SentAtUtc", "AbandonedAtUtc", "NextAttemptAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Dropping the table discards every unpublished message. That is the
            // honest consequence of rolling back the outbox itself: the events it
            // had not yet delivered are gone, which is why this migration should
            // only be reverted after the pending rows have been inspected.
            migrationBuilder.DropTable(
                name: "OutboxMessages",
                schema: "deploymentintelligence");
        }
    }
}
