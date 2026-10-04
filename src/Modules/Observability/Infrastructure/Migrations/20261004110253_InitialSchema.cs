using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.Observability.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "observability");

            migrationBuilder.CreateTable(
                name: "MetricSamples",
                schema: "observability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricType = table.Column<int>(type: "integer", nullable: false),
                    Value = table.Column<double>(type: "double precision", nullable: false),
                    Success = table.Column<bool>(type: "boolean", nullable: true),
                    RecordedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeploymentVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MetricSamples", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RequestTelemetryAggregates",
                schema: "observability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Route = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    WindowStartUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DeploymentVersion = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RequestCount = table.Column<long>(type: "bigint", nullable: false),
                    ServerErrorCount = table.Column<long>(type: "bigint", nullable: false),
                    ClientErrorCount = table.Column<long>(type: "bigint", nullable: false),
                    TotalDurationMs = table.Column<double>(type: "double precision", nullable: false),
                    MaxDurationMs = table.Column<double>(type: "double precision", nullable: false),
                    DurationBucketCounts = table.Column<long[]>(type: "bigint[]", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RequestTelemetryAggregates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceLevelObjectives",
                schema: "observability",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    MetricType = table.Column<int>(type: "integer", nullable: false),
                    TargetValue = table.Column<double>(type: "double precision", nullable: false),
                    WindowDuration = table.Column<TimeSpan>(type: "interval", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLevelObjectives", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MetricSamples_OrganizationId_ServiceId_DeploymentVersion_Re~",
                schema: "observability",
                table: "MetricSamples",
                columns: new[] { "OrganizationId", "ServiceId", "DeploymentVersion", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MetricSamples_OrganizationId_ServiceId_MetricType_RecordedA~",
                schema: "observability",
                table: "MetricSamples",
                columns: new[] { "OrganizationId", "ServiceId", "MetricType", "RecordedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestTelemetryAggregates_OrganizationId_ServiceId_Route_H~",
                schema: "observability",
                table: "RequestTelemetryAggregates",
                columns: new[] { "OrganizationId", "ServiceId", "Route", "HttpMethod", "DeploymentVersion", "WindowStartUtc" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RequestTelemetryAggregates_OrganizationId_ServiceId_WindowS~",
                schema: "observability",
                table: "RequestTelemetryAggregates",
                columns: new[] { "OrganizationId", "ServiceId", "WindowStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestTelemetryAggregates_OrganizationId_WindowStartUtc",
                schema: "observability",
                table: "RequestTelemetryAggregates",
                columns: new[] { "OrganizationId", "WindowStartUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLevelObjectives_OrganizationId_ServiceId",
                schema: "observability",
                table: "ServiceLevelObjectives",
                columns: new[] { "OrganizationId", "ServiceId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MetricSamples",
                schema: "observability");

            migrationBuilder.DropTable(
                name: "RequestTelemetryAggregates",
                schema: "observability");

            migrationBuilder.DropTable(
                name: "ServiceLevelObjectives",
                schema: "observability");
        }
    }
}
