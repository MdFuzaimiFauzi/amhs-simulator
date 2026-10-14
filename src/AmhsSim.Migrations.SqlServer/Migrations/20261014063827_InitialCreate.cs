using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AmhsSim.Migrations.SqlServer.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Alarms",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Source = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Code = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Message = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RaisedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ClearedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Alarms", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Jobs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CarrierId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    SourcePort = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    DestPort = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Priority = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    VehicleName = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    FinishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Jobs", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JobEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TransferJobId = table.Column<int>(type: "int", nullable: false),
                    From = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    To = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Trigger = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JobEvents_Jobs_TransferJobId",
                        column: x => x.TransferJobId,
                        principalTable: "Jobs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Alarms_Source_ClearedAt",
                table: "Alarms",
                columns: new[] { "Source", "ClearedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_JobEvents_TransferJobId",
                table: "JobEvents",
                column: "TransferJobId");

            migrationBuilder.CreateIndex(
                name: "IX_Jobs_State",
                table: "Jobs",
                column: "State");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Alarms");

            migrationBuilder.DropTable(
                name: "JobEvents");

            migrationBuilder.DropTable(
                name: "Jobs");
        }
    }
}
