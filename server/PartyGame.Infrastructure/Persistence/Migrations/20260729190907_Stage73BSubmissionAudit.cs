using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Stage73BSubmissionAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SubmissionAuditEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientSubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    Result = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionAuditEntries", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SubmissionReceipts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoomId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ActionType = table.Column<int>(type: "INTEGER", nullable: false),
                    ClientSubmissionId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PayloadFingerprint = table.Column<string>(type: "TEXT", fixedLength: true, maxLength: 64, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SubmissionReceipts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionAuditEntries_RoomId_CreatedAtUtc",
                table: "SubmissionAuditEntries",
                columns: new[] { "RoomId", "CreatedAtUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionAuditEntries_RoomId_PlayerId_QuestionInstanceId_ActionType_ClientSubmissionId",
                table: "SubmissionAuditEntries",
                columns: new[] { "RoomId", "PlayerId", "QuestionInstanceId", "ActionType", "ClientSubmissionId" });

            migrationBuilder.CreateIndex(
                name: "IX_SubmissionReceipts_RoomId_PlayerId_QuestionInstanceId_ActionType_ClientSubmissionId",
                table: "SubmissionReceipts",
                columns: new[] { "RoomId", "PlayerId", "QuestionInstanceId", "ActionType", "ClientSubmissionId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SubmissionAuditEntries");

            migrationBuilder.DropTable(
                name: "SubmissionReceipts");
        }
    }
}
