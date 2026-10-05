using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.TrafficManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "trafficmanagement");

            migrationBuilder.CreateTable(
                name: "TrafficPolicies",
                schema: "trafficmanagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Strategy = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Mode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrafficPolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TrafficPolicyTargets",
                schema: "trafficmanagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PolicyId = table.Column<Guid>(type: "uuid", nullable: false),
                    InstanceId = table.Column<Guid>(type: "uuid", nullable: false),
                    WeightPercent = table.Column<int>(type: "integer", nullable: false),
                    Priority = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TrafficPolicyTargets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TrafficPolicyTargets_TrafficPolicies_PolicyId",
                        column: x => x.PolicyId,
                        principalSchema: "trafficmanagement",
                        principalTable: "TrafficPolicies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TrafficPolicies_OrganizationId_ServiceId",
                schema: "trafficmanagement",
                table: "TrafficPolicies",
                columns: new[] { "OrganizationId", "ServiceId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TrafficPolicyTargets_PolicyId_InstanceId",
                schema: "trafficmanagement",
                table: "TrafficPolicyTargets",
                columns: new[] { "PolicyId", "InstanceId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TrafficPolicyTargets",
                schema: "trafficmanagement");

            migrationBuilder.DropTable(
                name: "TrafficPolicies",
                schema: "trafficmanagement");
        }
    }
}
