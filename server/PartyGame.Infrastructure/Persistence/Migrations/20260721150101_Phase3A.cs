using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PartyGame.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase3A : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EnabledQuestionTypes",
                table: "GameRooms",
                type: "TEXT",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<Guid>(
                name: "SubjectPlayerId",
                table: "GameQuestionInstances",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TextAnswerEligiblePlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextAnswerEligiblePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextAnswerEligiblePlayers_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextAnswerSubmissions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AuthorPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RevealOrder = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextAnswerSubmissions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextAnswerSubmissions_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextAnswerVoteEligiblePlayers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PlayerId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextAnswerVoteEligiblePlayers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextAnswerVoteEligiblePlayers_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TextAnswerVotes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    QuestionInstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    VoterPlayerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SelectedTextAnswerId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubmittedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TextAnswerVotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TextAnswerVotes_GameQuestionInstances_QuestionInstanceId",
                        column: x => x.QuestionInstanceId,
                        principalTable: "GameQuestionInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TextAnswerEligiblePlayers_QuestionInstanceId_PlayerId",
                table: "TextAnswerEligiblePlayers",
                columns: new[] { "QuestionInstanceId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TextAnswerSubmissions_QuestionInstanceId_AuthorPlayerId",
                table: "TextAnswerSubmissions",
                columns: new[] { "QuestionInstanceId", "AuthorPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TextAnswerSubmissions_QuestionInstanceId_RevealOrder",
                table: "TextAnswerSubmissions",
                columns: new[] { "QuestionInstanceId", "RevealOrder" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TextAnswerVoteEligiblePlayers_QuestionInstanceId_PlayerId",
                table: "TextAnswerVoteEligiblePlayers",
                columns: new[] { "QuestionInstanceId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TextAnswerVotes_QuestionInstanceId_VoterPlayerId",
                table: "TextAnswerVotes",
                columns: new[] { "QuestionInstanceId", "VoterPlayerId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TextAnswerEligiblePlayers");

            migrationBuilder.DropTable(
                name: "TextAnswerSubmissions");

            migrationBuilder.DropTable(
                name: "TextAnswerVoteEligiblePlayers");

            migrationBuilder.DropTable(
                name: "TextAnswerVotes");

            migrationBuilder.DropColumn(
                name: "EnabledQuestionTypes",
                table: "GameRooms");

            migrationBuilder.DropColumn(
                name: "SubjectPlayerId",
                table: "GameQuestionInstances");
        }
    }
}
