using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace KukaMiddleware.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Robots",
                columns: table => new
                {
                    RobotId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    RobotType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    StatusCode = table.Column<short>(type: "smallint", nullable: false),
                    StatusName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Availability = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BatteryLevel = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    MapCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NodeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MissionCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Robots", x => x.RobotId);
                });

            migrationBuilder.CreateTable(
                name: "Missions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MissionCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    RequestId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RobotId = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    MissionType = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    StartNode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    TargetNode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ContainerCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Priority = table.Column<short>(type: "smallint", nullable: false),
                    StatusCode = table.Column<short>(type: "smallint", nullable: false),
                    StatusName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Missions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Missions_Robots_RobotId",
                        column: x => x.RobotId,
                        principalTable: "Robots",
                        principalColumn: "RobotId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "RobotStatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RobotId = table.Column<string>(type: "character varying(120)", nullable: false),
                    RobotType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    StatusCode = table.Column<short>(type: "smallint", nullable: false),
                    StatusName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Availability = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    BatteryLevel = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    MapCode = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    NodeCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MissionCode = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: true),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RobotStatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RobotStatusHistories_Robots_RobotId",
                        column: x => x.RobotId,
                        principalTable: "Robots",
                        principalColumn: "RobotId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "StatusHistories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MissionId = table.Column<int>(type: "integer", nullable: false),
                    StatusCode = table.Column<short>(type: "smallint", nullable: false),
                    StatusName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    ValidFrom = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ValidTo = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StatusHistories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StatusHistories_Missions_MissionId",
                        column: x => x.MissionId,
                        principalTable: "Missions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Missions_MissionCode",
                table: "Missions",
                column: "MissionCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Missions_RequestId",
                table: "Missions",
                column: "RequestId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Missions_RobotId",
                table: "Missions",
                column: "RobotId");

            migrationBuilder.CreateIndex(
                name: "IX_RobotStatusHistories_RobotId_ValidTo",
                table: "RobotStatusHistories",
                columns: new[] { "RobotId", "ValidTo" });

            migrationBuilder.CreateIndex(
                name: "IX_StatusHistories_MissionId_ValidFrom",
                table: "StatusHistories",
                columns: new[] { "MissionId", "ValidFrom" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RobotStatusHistories");

            migrationBuilder.DropTable(
                name: "StatusHistories");

            migrationBuilder.DropTable(
                name: "Missions");

            migrationBuilder.DropTable(
                name: "Robots");
        }
    }
}
