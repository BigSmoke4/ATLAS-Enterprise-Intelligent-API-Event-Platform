using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.ServiceRegistry.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "serviceregistry");

            migrationBuilder.CreateTable(
                name: "ServiceDependencies",
                schema: "serviceregistry",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependsOnServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceDependencies", x => new { x.OrganizationId, x.ServiceId, x.DependsOnServiceId });
                });

            migrationBuilder.CreateTable(
                name: "Services",
                schema: "serviceregistry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    EnvironmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceInstances",
                schema: "serviceregistry",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    HostAndPort = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Health = table.Column<int>(type: "integer", nullable: false),
                    LastHealthCheckUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ConsecutiveFailedChecks = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceInstances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceInstances_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalSchema: "serviceregistry",
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceDependencies_OrganizationId_DependsOnServiceId",
                schema: "serviceregistry",
                table: "ServiceDependencies",
                columns: new[] { "OrganizationId", "DependsOnServiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceInstances_ServiceId",
                schema: "serviceregistry",
                table: "ServiceInstances",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_Services_OrganizationId_EnvironmentId_Name",
                schema: "serviceregistry",
                table: "Services",
                columns: new[] { "OrganizationId", "EnvironmentId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceDependencies",
                schema: "serviceregistry");

            migrationBuilder.DropTable(
                name: "ServiceInstances",
                schema: "serviceregistry");

            migrationBuilder.DropTable(
                name: "Services",
                schema: "serviceregistry");
        }
    }
}
