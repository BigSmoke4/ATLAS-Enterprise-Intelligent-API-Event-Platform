using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Atlas.Modules.APIManagement.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "apimanagement");

            migrationBuilder.CreateTable(
                name: "ApiDefinitions",
                schema: "apimanagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    BasePath = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiDefinitions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ApiVersions",
                schema: "apimanagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiDefinitionId = table.Column<Guid>(type: "uuid", nullable: false),
                    VersionNumber = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiVersions_ApiDefinitions_ApiDefinitionId",
                        column: x => x.ApiDefinitionId,
                        principalSchema: "apimanagement",
                        principalTable: "ApiDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ApiRoutes",
                schema: "apimanagement",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ApiVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Path = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    HttpMethod = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    RateLimit = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Timeout = table.Column<TimeSpan>(type: "interval", nullable: false),
                    MaxRetries = table.Column<int>(type: "integer", nullable: false),
                    TargetServiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "bytea", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ApiRoutes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ApiRoutes_ApiVersions_ApiVersionId",
                        column: x => x.ApiVersionId,
                        principalSchema: "apimanagement",
                        principalTable: "ApiVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ApiDefinitions_OrganizationId_BasePath",
                schema: "apimanagement",
                table: "ApiDefinitions",
                columns: new[] { "OrganizationId", "BasePath" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiRoutes_ApiVersionId_Path_HttpMethod",
                schema: "apimanagement",
                table: "ApiRoutes",
                columns: new[] { "ApiVersionId", "Path", "HttpMethod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ApiVersions_ApiDefinitionId_VersionNumber",
                schema: "apimanagement",
                table: "ApiVersions",
                columns: new[] { "ApiDefinitionId", "VersionNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ApiRoutes",
                schema: "apimanagement");

            migrationBuilder.DropTable(
                name: "ApiVersions",
                schema: "apimanagement");

            migrationBuilder.DropTable(
                name: "ApiDefinitions",
                schema: "apimanagement");
        }
    }
}
